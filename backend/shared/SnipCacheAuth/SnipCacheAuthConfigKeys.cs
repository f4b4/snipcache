namespace SnipCacheAuth;

/// <summary>
/// The configuration keys the BFF auth library reads. They map onto
/// <see cref="SnipCacheAuthOptions"/>; every per-app value (cookie name, client id,
/// application name) comes from config or the code-callback — nothing is hard-coded,
/// so one Keycloak realm can serve many public clients.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="Authority"/> — OIDC authority (realm URL). Unset ⇒ auth degrades
///   to cookie-only and challenges return 401 instead of redirecting.</item>
///   <item><see cref="ClientId"/> / <see cref="Audience"/> — public PKCE client id for
///   THIS app (no secret); <see cref="Audience"/> is the fallback.</item>
///   <item><see cref="BaseAddress"/> — public origin used to force redirect_uri and
///   post-logout URIs so they match the Keycloak client behind a dev/reverse proxy.</item>
///   <item><see cref="CookieName"/> — per-app session cookie name.</item>
///   <item><see cref="DevFallbackSub"/> — dev-only subject when no token is present.</item>
///   <item><see cref="CorsAllowedOrigins"/> — comma-separated SPA origins for the
///   library's CORS policy.</item>
///   <item><see cref="DataProtectionApplicationName"/> / <see cref="DataProtectionKeysPath"/>
///   — isolate and persist the auth-cookie encryption keys so logins survive restarts.</item>
/// </list>
/// </remarks>
public static class SnipCacheAuthConfigKeys
{
    /// <summary>OIDC authority (Keycloak realm URL).</summary>
    public const string Authority = "Keycloak:Authority";

    /// <summary>Public PKCE client id for this app.</summary>
    public const string ClientId = "Keycloak:ClientId";

    /// <summary>Fallback for <see cref="ClientId"/>.</summary>
    public const string Audience = "Keycloak:Audience";

    /// <summary>Public origin (scheme+host[:port]) of the app.</summary>
    public const string BaseAddress = "App:BaseAddress";

    /// <summary>Per-app session cookie name.</summary>
    public const string CookieName = "Auth:CookieName";

    /// <summary>Dev-only fallback subject.</summary>
    public const string DevFallbackSub = "Auth:DevFallbackSub";

    /// <summary>Comma-separated SPA origins allowed by the CORS policy.</summary>
    public const string CorsAllowedOrigins = "Cors:AllowedOrigins";

    /// <summary>Data Protection application name (isolates the key ring).</summary>
    public const string DataProtectionApplicationName = "DataProtection:ApplicationName";

    /// <summary>Directory to persist Data Protection keys (cookie survives restarts).</summary>
    public const string DataProtectionKeysPath = "DataProtection:KeysPath";
}
