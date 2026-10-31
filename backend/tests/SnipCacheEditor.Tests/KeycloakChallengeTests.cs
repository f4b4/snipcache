using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SnipCacheEditor.Tests;

// With Keycloak configured the default challenge is the OIDC redirect. /api must still answer
// 401 (the SPA starts the sign-in itself; a fetch cannot follow a 302 to the IdP). The
// metadata is never fetched, so no Keycloak is needed.
public sealed class KeycloakChallengeTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly KeycloakFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Api_WithoutSession_Is401NotARedirect()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/api/tree", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class KeycloakFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Keycloak:Authority", "https://keycloak.invalid/realms/test");
            builder.UseSetting("GitHub:UseFake", "true");
            builder.UseSetting("GitHub:FakeSeedDirectory", string.Empty);
            builder.UseSetting("DataProtection:KeysPath", string.Empty);
            builder.UseSetting("App:DataDirectory", Path.Combine(Path.GetTempPath(), "snipcache-editor-tests"));
            builder.UseSetting("Serilog:MinimumLevel:Default", "Error");
            builder.UseSetting("Serilog:MinimumLevel:Override:Microsoft.Hosting.Lifetime", "Error");
            builder.UseSetting("Serilog:MinimumLevel:Override:Microsoft.AspNetCore", "Error");
        }
    }
}
