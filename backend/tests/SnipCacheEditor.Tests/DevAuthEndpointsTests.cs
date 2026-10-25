using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SnipCacheEditor.Tests;

// The Development stand-ins for the BFF endpoints when Keycloak is not configured: the SPA
// signs in, reads /auth/me and signs out through them exactly as it would with Keycloak.
public sealed class DevAuthEndpointsTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly DevFactory factory = new();

    public void Dispose() => factory.Dispose();

    // https, because the session cookie is Secure; redirects are inspected, not followed.
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    [Fact]
    public async Task Me_IsEmptyWithoutSession()
    {
        var me = await Client().GetStringAsync("/auth/me", Ct);

        Assert.Equal(string.Empty, me);
    }

    [Fact]
    public async Task Login_StartsSessionAndReturnsToTheRequestedPath()
    {
        var client = Client();

        var login = await client.GetAsync("/auth/login?returnUrl=%2Fedit%2FSQL%2Fcount.sql", Ct);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/edit/SQL/count.sql", login.Headers.Location?.OriginalString);
        var me = await client.GetFromJsonAsync<JsonElement>("/auth/me", Ct);
        Assert.Equal("dev", me.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tree", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    [InlineData("")]
    public async Task Login_IgnoresNonLocalReturnUrls(string returnUrl)
    {
        var login = await Client().GetAsync($"/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}", Ct);

        Assert.Equal("/", login.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Logout_EndsSessionAndPointsToLoggedOut()
    {
        var client = Client();
        await client.GetAsync("/auth/login", Ct);

        var logout = await client.PostAsync("/auth/logout", null, Ct);

        var body = await logout.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("/logged-out", body.GetProperty("logoutUrl").GetString());
        Assert.Equal(string.Empty, await client.GetStringAsync("/auth/me", Ct));
    }

    private sealed class DevFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Keycloak:Authority", string.Empty);
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
