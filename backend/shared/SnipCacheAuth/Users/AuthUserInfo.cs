namespace SnipCacheAuth;

/// <summary>
/// The minimal identity the library derives from the signed-in
/// <see cref="System.Security.Claims.ClaimsPrincipal"/> (or the dev-fallback subject) and
/// hands to <see cref="IAuthUserStore{TUser}.CreateAsync"/> when a user is seen for the
/// first time. The app turns this into its own entity.
/// </summary>
/// <param name="Sub">Stable Keycloak subject identifier (the <c>sub</c> claim).</param>
/// <param name="DisplayName">
/// Best-effort display name (<c>preferred_username</c> → name claim → <c>"dev"</c>).
/// </param>
public sealed record AuthUserInfo(string Sub, string? DisplayName);
