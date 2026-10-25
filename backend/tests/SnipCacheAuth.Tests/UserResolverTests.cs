using System.Security.Claims;
using SnipCacheAuth;
using Microsoft.Extensions.Options;

namespace SnipCacheAuth.Tests;

// UserResolver owns claim extraction + the dev-fallback policy. These tests pin both:
// the lazy-create contract (find first, create on a miss), the display-name fallback
// chain, and the environment-gated behavior when no 'sub' is present.
public class UserResolverTests
{
    private const string DevFallbackSub = "local-dev";

    private static UserResolver<TestUser> Resolver(
        FakeUserStore store, string environmentName = "Development") =>
        new(store,
            Options.Create(new SnipCacheAuthOptions { DevFallbackSub = DevFallbackSub }),
            new FakeHostEnvironment(environmentName));

    [Fact]
    public async Task ReturnsExistingUser_WithoutCreating()
    {
        var existing = new TestUser("sub-1", "Existing");
        var store = new FakeUserStore(existing);

        var resolved = await Resolver(store)
            .ResolveAsync(Principals.With(("sub", "sub-1")), TestContext.Current.CancellationToken);

        Assert.Same(existing, resolved);
        Assert.Empty(store.Created);
        Assert.Equal(1, store.FindCalls);
    }

    [Fact]
    public async Task CreatesUserOnFirstSight_UsingPreferredUsername()
    {
        var store = new FakeUserStore();

        var resolved = await Resolver(store).ResolveAsync(
            Principals.With(("sub", "sub-2"), ("preferred_username", "ada")),
            TestContext.Current.CancellationToken);

        var created = Assert.Single(store.Created);
        Assert.Equal("sub-2", created.Sub);
        Assert.Equal("ada", created.DisplayName);
        Assert.Equal(new TestUser("sub-2", "ada"), resolved);
    }

    [Fact]
    public async Task DisplayName_FallsBackToNameClaim_WhenNoPreferredUsername()
    {
        var store = new FakeUserStore();

        await Resolver(store).ResolveAsync(
            Principals.With(("sub", "sub-3"), (ClaimTypes.Name, "Ada Lovelace")),
            TestContext.Current.CancellationToken);

        Assert.Equal("Ada Lovelace", Assert.Single(store.Created).DisplayName);
    }

    [Fact]
    public async Task DisplayName_FallsBackToDev_WhenNoNameClaims()
    {
        var store = new FakeUserStore();

        await Resolver(store)
            .ResolveAsync(Principals.With(("sub", "sub-4")), TestContext.Current.CancellationToken);

        Assert.Equal("dev", Assert.Single(store.Created).DisplayName);
    }

    [Fact]
    public async Task UsesConfiguredDevFallbackSub_WhenNoSubInDevelopment()
    {
        var store = new FakeUserStore();

        var resolved = await Resolver(store, "Development")
            .ResolveAsync(Principals.With(), TestContext.Current.CancellationToken);

        Assert.Equal(DevFallbackSub, resolved.Sub);
        Assert.Equal(DevFallbackSub, Assert.Single(store.Created).Sub);
    }

    [Fact]
    public async Task NullPrincipalInDevelopment_UsesDevFallback()
    {
        var store = new FakeUserStore();

        var resolved = await Resolver(store, "Development")
            .ResolveAsync(principal: null, TestContext.Current.CancellationToken);

        Assert.Equal(DevFallbackSub, resolved.Sub);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task ThrowsOutsideDevelopment_WhenNoSub(string environmentName)
    {
        var store = new FakeUserStore();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Resolver(store, environmentName).ResolveAsync(Principals.With(), TestContext.Current.CancellationToken));

        Assert.Empty(store.Created);
    }
}
