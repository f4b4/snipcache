using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SnipCacheAuth;

/// <summary>
/// Pipeline + endpoint entry points for the BFF auth library.
/// </summary>
public static class SnipCacheAuthApplicationBuilderExtensions
{
    /// <summary>
    /// Applies the BFF middleware in the one correct order:
    /// CORS → <c>UseAuthentication</c> → <c>UseAuthorization</c> → <see cref="CsrfGuardMiddleware"/>.
    /// All four are registered by <c>AddSnipCacheAuth</c>. Call this after any middleware that
    /// must run first (e.g. request logging) and before mapping endpoints. If an app needs
    /// custom middleware between these steps, call the four equivalents explicitly instead.
    /// </summary>
    public static IApplicationBuilder UseSnipCacheAuth(this IApplicationBuilder app)
    {
        app.UseCors(SnipCacheAuthDefaults.CorsPolicyName);
        app.UseAuthentication();
        app.UseAuthorization();
        // Blocks mutating /api requests that lack the X-CSRF header (form-POST defense
        // for the cookie session). Runs after auth so the 403 reason is unambiguous.
        app.UseMiddleware<CsrfGuardMiddleware>();
        return app;
    }

    /// <summary>
    /// Maps the BFF <c>/auth</c> endpoints (<c>login</c>, <c>logout</c>, <c>me</c>).
    /// No-ops when Keycloak isn't configured — mirrors BlondiApp guarding the endpoints
    /// behind <c>authConfigured</c> so a dev-fallback run doesn't expose a broken challenge.
    /// </summary>
    public static IEndpointRouteBuilder MapSnipCacheAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<SnipCacheAuthOptions>();
        if (options.AuthConfigured)
            AuthEndpoints.Map(endpoints, options);
        return endpoints;
    }
}
