namespace SnipCacheAuth;

/// <summary>
/// Stable names the BFF auth library uses internally and exposes for app wiring.
/// </summary>
public static class SnipCacheAuthDefaults
{
    /// <summary>
    /// Name of the CORS policy registered by <c>AddSnipCacheAuth</c> and applied by
    /// <c>UseSnipCacheAuth</c>. Apps don't need to reference this unless they apply the
    /// policy themselves.
    /// </summary>
    public const string CorsPolicyName = "SnipCacheAuthFrontend";

    /// <summary>Per-app session cookie name used when <c>Auth:CookieName</c> is unset.</summary>
    public const string DefaultCookieName = "snipcache-auth";

    /// <summary>
    /// Dev-only fallback subject used by <see cref="UserResolver{TUser}"/> when no token
    /// is present and <c>Auth:DevFallbackSub</c> is unset.
    /// </summary>
    public const string DefaultDevFallbackSub = "dev-user";

    /// <summary>
    /// Data Protection application name used when <c>DataProtection:ApplicationName</c>
    /// is unset. Override per app so each app isolates its key ring.
    /// </summary>
    public const string DefaultApplicationName = "SnipCache";

    /// <summary>Default SPA origin allowed by the CORS policy when none is configured.</summary>
    public const string DefaultCorsOrigin = "https://localhost:5173";
}
