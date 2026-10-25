namespace SnipCacheEditor;

/// <summary>
/// The policy every <c>/api</c> endpoint requires: a signed-in user and, when
/// <c>Auth:RequiredRole</c> is set, that role in the <c>roles</c> claim (the Keycloak realm
/// may serve other apps and users).
/// </summary>
public static class EditorAuthorization
{
    public const string PolicyName = "SnipCacheEditor";

    public const string RequiredRoleKey = "Auth:RequiredRole";

    public static IServiceCollection AddEditorAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        var requiredRole = configuration[RequiredRoleKey];
        services.AddAuthorizationBuilder().AddPolicy(PolicyName, policy =>
        {
            policy.RequireAuthenticatedUser();
            if (!string.IsNullOrWhiteSpace(requiredRole))
                policy.RequireClaim("roles", requiredRole.Trim());
        });
        return services;
    }
}
