using System.Security.Claims;
using SnipCacheAuth;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace SnipCacheAuth.Tests;

/// <summary>A stand-in for the app's user entity (the library is generic over it).</summary>
internal sealed record TestUser(string Sub, string? DisplayName);

/// <summary>
/// In-memory <see cref="IAuthUserStore{TUser}"/> that records its calls so tests can assert
/// the resolver's lazy-create contract (find first, create only on a miss).
/// </summary>
internal sealed class FakeUserStore : IAuthUserStore<TestUser>
{
    private readonly Dictionary<string, TestUser> bySub;

    public FakeUserStore(params TestUser[] seed) =>
        bySub = seed.ToDictionary(u => u.Sub);

    /// <summary>The <see cref="AuthUserInfo"/> values passed to <see cref="CreateAsync"/>, in order.</summary>
    public List<AuthUserInfo> Created { get; } = [];

    public int FindCalls { get; private set; }

    public Task<TestUser?> FindBySubAsync(string sub, CancellationToken ct)
    {
        FindCalls++;
        return Task.FromResult(bySub.TryGetValue(sub, out var user) ? user : null);
    }

    public Task<TestUser> CreateAsync(AuthUserInfo info, CancellationToken ct)
    {
        Created.Add(info);
        var user = new TestUser(info.Sub, info.DisplayName);
        bySub[info.Sub] = user;
        return Task.FromResult(user);
    }
}

/// <summary>Minimal <see cref="IHostEnvironment"/> whose only meaningful axis is the environment name.</summary>
internal sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "SnipCacheAuth.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal static class Principals
{
    /// <summary>An authenticated principal carrying the given claims.</summary>
    public static ClaimsPrincipal With(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), authenticationType: "test"));
}
