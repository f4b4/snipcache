using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace SnipCacheEditor;

/// <summary>
/// Development only, and only while <c>Keycloak:Authority</c> is unset: issues a session
/// cookie for a made-up user so the API can be exercised with curl or the SPA without
/// Keycloak. Never mapped in other environments.
/// </summary>
internal static class DevAuthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/dev-login", async (HttpContext ctx, string? name, string? email, string? roles) =>
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

            var identity = new ClaimsIdentity(claims, "dev", "preferred_username", "roles");
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Ok(new { name, email, roles = claims.Where(c => c.Type == "roles").Select(c => c.Value) });
        }).ExcludeFromDescription();
    }
}
