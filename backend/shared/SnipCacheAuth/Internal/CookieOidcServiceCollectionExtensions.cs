using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace SnipCacheAuth;

// Wires the cookie/OIDC pair so the access token stored in the auth cookie is
// silently refreshed before it expires (OnValidatePrincipal -> CookieOidcRefresher),
// and the OIDC handler always asks Keycloak for a refresh_token. Ported from the
// ASP.NET BFF sample; unchanged for a public (PKCE) client — the refresh request
// itself omits the client_secret (see CookieOidcRefresher).
internal static class CookieOidcServiceCollectionExtensions
{
    public static IServiceCollection ConfigureCookieOidc(
        this IServiceCollection services, string cookieScheme, string oidcScheme)
    {
        services.AddSingleton<CookieOidcRefresher>();
        services.AddOptions<CookieAuthenticationOptions>(cookieScheme)
            .Configure<CookieOidcRefresher>((cookieOptions, refresher) =>
            {
                cookieOptions.Events.OnValidatePrincipal = context =>
                    refresher.ValidateOrRefreshCookieAsync(context, oidcScheme);
            });
        services.AddOptions<OpenIdConnectOptions>(oidcScheme).Configure(oidcOptions =>
        {
            // Request a refresh_token...
            oidcOptions.Scope.Add(OpenIdConnectScope.OfflineAccess);
            // ...and store it (plus access_token/id_token) in the auth cookie.
            oidcOptions.SaveTokens = true;
        });
        return services;
    }
}
