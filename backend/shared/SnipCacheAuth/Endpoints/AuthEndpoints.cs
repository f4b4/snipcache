using System.Security.Claims;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SnipCacheAuth;

// The BFF auth surface the SPA talks to. The browser never sees a token: /auth/login
// starts the OIDC code flow, the resulting tokens live in an encrypted cookie, and
// /auth/me reports the signed-in user from that cookie. Ported from BlondiApp's
// AuthEndpoints; reads the authority / base address from SnipCacheAuthOptions rather than
// raw IConfiguration so the keys stay in one strongly-typed place.
internal static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder app, SnipCacheAuthOptions options)
    {
        var group = app.MapGroup("/auth");

        // Kicks off the OIDC challenge (redirect to Keycloak). returnUrl is a
        // relative app path to land on after sign-in; anything else is ignored.
        group.MapGet("/login", (string? returnUrl) =>
        {
            var baseAddress = options.BaseAddress;
            returnUrl = HttpUtility.UrlDecode(returnUrl);
            if (string.IsNullOrEmpty(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
                returnUrl = "/";

            var returnUri = $"{baseAddress}/{returnUrl.TrimStart('/')}";

            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = returnUri },
                [OpenIdConnectDefaults.AuthenticationScheme]);
        }).AllowAnonymous();

        // Clears the local cookie and returns Keycloak's end-session URL as JSON.
        // We return the URL instead of redirecting so the SPA can do a top-level
        // navigation to it (a 302 to Keycloak from a fetch() would CORS-fail).
        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            var authority = options.Authority;
            var baseAddress = options.BaseAddress;

            // Grab the id_token before clearing the cookie (logout hint for Keycloak).
            var idToken = await ctx.GetTokenAsync("id_token");

            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            var endSessionUrl = $"{authority}/protocol/openid-connect/logout";
            var postLogoutUri = Uri.EscapeDataString($"{baseAddress}/logged-out");
            var logoutUrl = string.IsNullOrEmpty(idToken)
                ? $"{endSessionUrl}?post_logout_redirect_uri={postLogoutUri}"
                : $"{endSessionUrl}?id_token_hint={idToken}&post_logout_redirect_uri={postLogoutUri}";

            return Results.Ok(new { logoutUrl });
        }).RequireAuthorization();

        // Returns the current user (or null). The client calls this on startup to
        // decide whether to show the app or the sign-in landing page.
        group.MapGet("/me", (ClaimsPrincipal principal) =>
        {
            if (principal.Identity?.IsAuthenticated != true)
                return Results.Ok((object?)null);

            var claims = principal.Claims
                .GroupBy(c => c.Type)
                .ToDictionary(g => g.Key, g => g.Select(c => c.Value).ToArray());

            return Results.Ok(new
            {
                sub = principal.FindFirstValue("sub"),
                name = principal.FindFirstValue("preferred_username"),
                email = principal.FindFirstValue("email"),
                roles = principal.FindAll("roles").Select(c => c.Value),
                claims,
            });
        }).AllowAnonymous();
    }
}
