namespace SnipCacheAuth;

/// <summary>
/// Strongly-typed surface for the Backend-for-Frontend (BFF) auth wiring.
/// <para>
/// In the BFF model the browser never holds a token: the app backend runs the OIDC
/// Authorization Code flow (PKCE, public client — no secret) against Keycloak and stores
/// the resulting tokens in an encrypted, HttpOnly cookie. These options drive that wiring.
/// </para>
/// <para>
/// Values are read from configuration (see <see cref="SnipCacheAuthConfigKeys"/>) and may be
/// overridden in code via the <c>configure</c> callback on <c>AddSnipCacheAuth</c>. Per-app
/// values (cookie name, client id, application name) live here so one Keycloak realm can
/// serve many public clients without anything app-specific being hard-coded.
/// </para>
/// </summary>
public sealed class SnipCacheAuthOptions
{
    /// <summary>
    /// OIDC authority (Keycloak realm URL). When null/empty the library wires cookie-only
    /// auth and challenges return 401 instead of redirecting — the graceful-degradation path
    /// that lets app work proceed before Keycloak is provisioned.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>Public PKCE client id for THIS app (no secret). Falls back to the audience.</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Public origin (scheme+host[:port]) used to force the OIDC redirect_uri and post-logout
    /// URIs so they match the Keycloak client config behind a dev Vite proxy / reverse proxy.
    /// Trailing slashes are normalized away.
    /// </summary>
    public string BaseAddress { get; set; } = string.Empty;

    /// <summary>Per-app session cookie name. Defaults to <see cref="SnipCacheAuthDefaults.DefaultCookieName"/>.</summary>
    public string CookieName { get; set; } = SnipCacheAuthDefaults.DefaultCookieName;

    /// <summary>
    /// Dev-only subject used by <see cref="UserResolver{TUser}"/> when no token is present.
    /// Ignored outside the Development environment (a missing subject throws there).
    /// </summary>
    public string DevFallbackSub { get; set; } = SnipCacheAuthDefaults.DefaultDevFallbackSub;

    /// <summary>SPA origins allowed by the CORS policy the library registers.</summary>
    public string[] CorsAllowedOrigins { get; set; } = [SnipCacheAuthDefaults.DefaultCorsOrigin];

    /// <summary>
    /// Data Protection application name; isolates this app's key ring so cookies issued by one
    /// app can't be read by another. Defaults to <see cref="SnipCacheAuthDefaults.DefaultApplicationName"/>.
    /// </summary>
    public string ApplicationName { get; set; } = SnipCacheAuthDefaults.DefaultApplicationName;

    /// <summary>
    /// Directory to persist Data Protection keys so the auth-cookie encryption keys (and thus
    /// existing logins) survive an app restart. Null/empty ⇒ in-memory (ephemeral) keys.
    /// </summary>
    public string? DataProtectionKeysPath { get; set; }

    /// <summary>
    /// True when <see cref="Authority"/> is configured. When false the OpenIdConnect scheme and
    /// the <c>/auth</c> endpoints are not wired (cookie-only graceful degradation).
    /// </summary>
    public bool AuthConfigured => !string.IsNullOrWhiteSpace(Authority);
}
