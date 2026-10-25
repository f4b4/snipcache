using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace SnipCacheAuth;

/// <summary>
/// Service-registration entry points for the Backend-for-Frontend (BFF) auth library.
/// </summary>
public static class SnipCacheAuthServiceCollectionExtensions
{
    /// <summary>
    /// Wires the whole BFF auth stack in one call:
    /// <list type="bullet">
    ///   <item>the cookie scheme (HttpOnly, SameSite=Lax, Secure=Always, and 401/403 on
    ///   challenge/forbid instead of IdP redirects so SPA fetches get a status code);</item>
    ///   <item>the public-PKCE OpenIdConnect scheme (ResponseType=Code, CallbackPath
    ///   <c>/auth/signin-oidc</c>, claims from the userinfo endpoint, MapInboundClaims=false,
    ///   name=<c>preferred_username</c>, role=<c>roles</c>, PAR disabled) with redirect_uri /
    ///   post-logout overrides driven by <see cref="SnipCacheAuthOptions.BaseAddress"/>;</item>
    ///   <item>silent refresh of the cookie's access token (offline_access + SaveTokens);</item>
    ///   <item>authorization, a CORS policy for the SPA origin(s), and Data Protection
    ///   (key isolation + optional persistence so logins survive restarts).</item>
    /// </list>
    /// When <c>Keycloak:Authority</c> is unset the OpenIdConnect scheme and <c>/auth</c>
    /// endpoints are skipped and challenges fall back to the cookie scheme (401) — the
    /// graceful-degradation path matching BlondiApp's <c>authConfigured</c> branch.
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <param name="configuration">Source of the keys in <see cref="SnipCacheAuthConfigKeys"/>.</param>
    /// <param name="environment">Used by the (optional) <see cref="UserResolver{TUser}"/> for the dev fallback.</param>
    /// <param name="configure">Optional code override of any resolved option (e.g. per-app cookie name).</param>
    public static IServiceCollection AddSnipCacheAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        Action<SnipCacheAuthOptions>? configure = null)
    {
        _ = environment; // reserved for parity with the resolver's dev-fallback contract

        // 1. Resolve options from configuration, then let the app override in code.
        var options = new SnipCacheAuthOptions
        {
            Authority = configuration[SnipCacheAuthConfigKeys.Authority],
            ClientId = configuration[SnipCacheAuthConfigKeys.ClientId]
                       ?? configuration[SnipCacheAuthConfigKeys.Audience],
            BaseAddress = configuration[SnipCacheAuthConfigKeys.BaseAddress] ?? string.Empty,
            CookieName = configuration[SnipCacheAuthConfigKeys.CookieName] ?? SnipCacheAuthDefaults.DefaultCookieName,
            DevFallbackSub = configuration[SnipCacheAuthConfigKeys.DevFallbackSub] ?? SnipCacheAuthDefaults.DefaultDevFallbackSub,
            ApplicationName = configuration[SnipCacheAuthConfigKeys.DataProtectionApplicationName] ?? SnipCacheAuthDefaults.DefaultApplicationName,
            DataProtectionKeysPath = configuration[SnipCacheAuthConfigKeys.DataProtectionKeysPath],
        };

        var corsOrigins = configuration[SnipCacheAuthConfigKeys.CorsAllowedOrigins];
        if (!string.IsNullOrWhiteSpace(corsOrigins))
            options.CorsAllowedOrigins = corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        configure?.Invoke(options);

        // Normalize the public origin so redirect_uri / post-logout never double-slash,
        // regardless of whether it came from config or the callback.
        options.BaseAddress = options.BaseAddress.TrimEnd('/');

        // Expose the resolved options (raw + IOptions) for the endpoints and the resolver.
        services.AddSingleton(options);
        services.AddSingleton<IOptions<SnipCacheAuthOptions>>(Options.Create(options));

        var authConfigured = options.AuthConfigured;

        // 2. CORS for the SPA origin(s).
        services.AddCors(cors => cors.AddPolicy(SnipCacheAuthDefaults.CorsPolicyName, policy =>
            policy.WithOrigins(options.CorsAllowedOrigins).AllowAnyHeader().AllowAnyMethod()));

        // 3. Cookie + OIDC BFF schemes.
        var authBuilder = services
            .AddAuthentication(authOptions =>
            {
                authOptions.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                authOptions.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                // Challenge via Keycloak when configured; otherwise fall back to the
                // cookie scheme (which we make return 401 instead of redirecting).
                authOptions.DefaultChallengeScheme = authConfigured
                    ? OpenIdConnectDefaults.AuthenticationScheme
                    : CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, cookieOptions =>
            {
                cookieOptions.Cookie.Name = options.CookieName;
                cookieOptions.Cookie.IsEssential = true;
                cookieOptions.Cookie.HttpOnly = true;
                cookieOptions.Cookie.SameSite = SameSiteMode.Lax;
                cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                cookieOptions.SlidingExpiration = true;
                // API clients want a status code, not a 302 to the IdP — the SPA decides
                // when to start the login flow itself.
                cookieOptions.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                cookieOptions.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        if (authConfigured)
        {
            authBuilder.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, oidcOptions =>
            {
                oidcOptions.Authority = options.Authority;
                oidcOptions.ClientId = options.ClientId;
                // Public client: no ClientSecret. PKCE protects the code exchange.
                oidcOptions.UsePkce = true;
                oidcOptions.ResponseType = OpenIdConnectResponseType.Code;
                oidcOptions.ResponseMode = OpenIdConnectResponseMode.Query;
                oidcOptions.CallbackPath = "/auth/signin-oidc";
                oidcOptions.SignedOutCallbackPath = "/auth/signout-callback-oidc";

                oidcOptions.Scope.Add("email");
                oidcOptions.GetClaimsFromUserInfoEndpoint = true;
                oidcOptions.SaveTokens = true;

                oidcOptions.MapInboundClaims = false;
                oidcOptions.TokenValidationParameters.NameClaimType = "preferred_username";
                oidcOptions.TokenValidationParameters.RoleClaimType = "roles";

                oidcOptions.RequireHttpsMetadata = true;
                // Keycloak public clients generally don't advertise PAR support.
                oidcOptions.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;

                // Behind the dev Vite proxy (and any reverse proxy) the browser-facing
                // origin differs from the server's own URL. Force redirect_uri to the
                // configured public BaseAddress so it matches the Keycloak client config.
                if (!string.IsNullOrEmpty(options.BaseAddress))
                {
                    oidcOptions.Events.OnRedirectToIdentityProvider = context =>
                    {
                        context.ProtocolMessage.RedirectUri = options.BaseAddress + oidcOptions.CallbackPath;
                        return Task.CompletedTask;
                    };
                }

                oidcOptions.Events.OnRedirectToIdentityProviderForSignOut = context =>
                {
                    context.ProtocolMessage.PostLogoutRedirectUri = options.BaseAddress;
                    return Task.CompletedTask;
                };

                oidcOptions.Events.OnTicketReceived = context =>
                {
                    // The "remember me" logic
                    context.Properties!.IsPersistent = true;
                    return Task.CompletedTask;
                };

                // After email verification the OIDC state cookie is typically gone,
                // so Keycloak's redirect back hits a "state is null" error. Recover
                // by starting a fresh login flow — the user now has a verified account.
                oidcOptions.Events.OnRemoteFailure = context =>
                {
                    context.Response.Redirect("/auth/login");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });

            // Wire silent refresh of the cookie's access token + request offline_access.
            services.ConfigureCookieOidc(
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme);
        }

        // 4. Authorization, so the bundled UseAuthorization in UseSnipCacheAuth is valid.
        services.AddAuthorization();

        // 5. Data Protection: isolate this app's key ring and (optionally) persist the
        // keys so the auth-cookie encryption keys — and thus existing logins — survive a restart.
        var dataProtection = services.AddDataProtection().SetApplicationName(options.ApplicationName);
        if (!string.IsNullOrWhiteSpace(options.DataProtectionKeysPath))
        {
            Directory.CreateDirectory(options.DataProtectionKeysPath);
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(options.DataProtectionKeysPath));
        }

        return services;
    }

    /// <summary>
    /// Registers the app's <see cref="IAuthUserStore{TUser}"/> and the generic
    /// <see cref="UserResolver{TUser}"/> (both scoped). Only needed by apps that persist
    /// a user record; the library never references EF Core / Npgsql or the app's entity type.
    /// </summary>
    /// <typeparam name="TUser">The application's user entity.</typeparam>
    /// <typeparam name="TStore">The app's persistence implementation.</typeparam>
    public static IServiceCollection AddSnipCacheUserResolver<TUser, TStore>(this IServiceCollection services)
        where TStore : class, IAuthUserStore<TUser>
    {
        services.AddScoped<IAuthUserStore<TUser>, TStore>();
        services.AddScoped<UserResolver<TUser>>();
        return services;
    }
}
