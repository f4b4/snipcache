namespace SnipCacheAuth;

/// <summary>
/// App-owned persistence for auth users, kept deliberately tiny so the library never
/// references EF Core / Npgsql or any app's DbContext. The LIBRARY owns claim extraction
/// and the dev-fallback policy (see <see cref="UserResolver{TUser}"/>); the APP owns the
/// entity type <typeparamref name="TUser"/> and how it's stored.
/// </summary>
/// <typeparam name="TUser">The application's user entity.</typeparam>
public interface IAuthUserStore<TUser>
{
    /// <summary>Finds a user by Keycloak subject, or returns <see langword="null"/> if not yet persisted.</summary>
    Task<TUser?> FindBySubAsync(string sub, CancellationToken ct);

    /// <summary>Creates and persists a new user from the first-sight identity info.</summary>
    Task<TUser> CreateAsync(AuthUserInfo info, CancellationToken ct);
}
