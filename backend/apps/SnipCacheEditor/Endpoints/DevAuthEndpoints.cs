using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace SnipCacheEditor;

/// <summary>
/// Development only, and only while <c>Keycloak:Authority</c> is unset: issues a session
/// cookie for a made-up user so the API can be exercised with curl or the SPA without
/// Keycloak. Never mapped in other environments.
/// <para>
/// <c>/auth/login</c>, <c>/auth/me</c> and <c>/auth/logout</c> stand in for the BFF
/// endpoints of SnipCacheAuth (which are not mapped without Keycloak) with the same
/// contract, so the SPA's sign-in and sign-out flow works unchanged.
/// </para>
/// </summary>
internal static class DevAuthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/dev-login", async (HttpContext ctx, string? name, string? email, string? roles) =>
        {
            var principal = await SignInAsync(ctx, name, email, roles);
            return Results.Ok(new
            {
                name = principal.Identity!.Name,
                email = principal.FindFirstValue("email"),
                roles = principal.FindAll("roles").Select(c => c.Value),
            });
        }).ExcludeFromDescription();

        // Signs in as "dev" (unless a session exists) and returns to the relative returnUrl.
        app.MapGet("/auth/login", async (HttpContext ctx, string? returnUrl) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
                await SignInAsync(ctx, name: null, email: null, roles: null);
            return Results.Redirect(IsLocalPath(returnUrl) ? returnUrl! : "/");
        }).ExcludeFromDescription();

        app.MapPost("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { logoutUrl = "/logged-out" });
        }).ExcludeFromDescription();

        // Same shape as SnipCacheAuth's /auth/me.
        app.MapGet("/auth/me", (ClaimsPrincipal principal) =>
        {
            if (principal.Identity?.IsAuthenticated != true)
                return Results.Ok((object?)null);

            return Results.Ok(new
            {
                sub = principal.FindFirstValue("sub"),
                name = principal.FindFirstValue("preferred_username"),
                email = principal.FindFirstValue("email"),
                roles = principal.FindAll("roles").Select(c => c.Value),
                claims = principal.Claims
                    .GroupBy(c => c.Type)
                    .ToDictionary(g => g.Key, g => g.Select(c => c.Value).ToArray()),
            });
        }).ExcludeFromDescription();
    }

    private static async Task<ClaimsPrincipal> SignInAsync(HttpContext ctx, string? name, string? email, string? roles)
    {
        name = string.IsNullOrWhiteSpace(name) ? "dev" : name.Trim();
        var claims = new List<Claim>
        {
            new("sub", $"dev-{name}"),
            new("preferred_username", name),
            new("name", name),
        };
        if (!string.IsNullOrWhiteSpace(email)) claims.Add(new("email", email.Trim()));
        foreach (var role in (roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            claims.Add(new("roles", role));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "dev", "preferred_username", "roles"));
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return principal;
    }

    // An app-relative path ("/edit/x"), not "//host" or "/\host", which browsers treat as
    // another origin.
    private static bool IsLocalPath(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' &&
        (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
