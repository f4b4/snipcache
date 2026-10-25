using System.Security.Claims;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SnipCacheAuth;

/// <summary>
/// Resolves the application user from the incoming <see cref="ClaimsPrincipal"/>, lazy-creating
/// the record on first sight via the app's <see cref="IAuthUserStore{TUser}"/>.
/// <para>
/// When no <c>sub</c> claim is present (no token) the resolver falls back to a configurable dev
/// subject so early work can proceed without Keycloak. Outside Development a missing <c>sub</c>
/// throws — by then an authenticated principal is expected (enforced by <c>[Authorize]</c>
/// before the resolver runs). Behavior is ported from BlondiApp's <c>UserResolver</c>, with the
/// EF Core dependency replaced by <see cref="IAuthUserStore{TUser}"/>.
/// </para>
/// </summary>
/// <typeparam name="TUser">The application's user entity, owned and stored by the app.</typeparam>
public sealed class UserResolver<TUser>(
    IAuthUserStore<TUser> store,
    IOptions<SnipCacheAuthOptions> options,
    IHostEnvironment env)
{
    /// <summary>
    /// Returns the app user for <paramref name="principal"/>, creating it on first sight.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown outside Development when the principal carries no <c>sub</c> claim.
    /// </exception>
    public async Task<TUser> ResolveAsync(ClaimsPrincipal? principal, CancellationToken ct)
    {
        var sub = principal?.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(sub))
        {
            if (!env.IsDevelopment())
                throw new UnauthorizedAccessException("Missing 'sub' claim.");

            sub = options.Value.DevFallbackSub;
        }

        var existing = await store.FindBySubAsync(sub, ct);
        if (existing is not null) return existing;

        var displayName = principal?.FindFirstValue("preferred_username")
                          ?? principal?.FindFirstValue(ClaimTypes.Name)
                          ?? "dev";

        return await store.CreateAsync(new AuthUserInfo(sub, displayName), ct);
    }
}
