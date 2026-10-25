using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SnipCacheEditor.Tests;

/// <summary>
/// Hosts the editor in memory with the fake content repository and <see cref="TestAuthHandler"/>
/// in place of the cookie session. <paramref name="requiredRole"/> sets <c>Auth:RequiredRole</c>.
/// </summary>
public sealed class EditorFactory(string? requiredRole = null) : WebApplicationFactory<Program>
{
    public InMemoryContentRepository Content => Services.GetRequiredService<InMemoryContentRepository>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("GitHub:UseFake", "true");
        builder.UseSetting("GitHub:Owner", "owner");
        builder.UseSetting("GitHub:Repository", "content");
        builder.UseSetting("GitHub:CommitterEmail", "noreply@snipcache.test");
        builder.UseSetting("Site:BaseUrl", "https://owner.github.io/content/");
        builder.UseSetting("Auth:RequiredRole", requiredRole ?? string.Empty);
        builder.UseSetting("DataProtection:KeysPath", string.Empty);
        builder.UseSetting("App:DataDirectory", Path.Combine(Path.GetTempPath(), "snipcache-editor-tests"));
        builder.UseSetting("Serilog:MinimumLevel:Default", "Error");
        builder.UseSetting("Serilog:MinimumLevel:Override:Microsoft.Hosting.Lifetime", "Error");
        builder.UseSetting("Serilog:MinimumLevel:Override:Microsoft.AspNetCore", "Error");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                o.DefaultForbidScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    /// <summary>A client signed in as <paramref name="name"/>, sending the CSRF header.</summary>
    public HttpClient SignedIn(string name = "ada", string? email = "ada@example.org", string? roles = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, name);
        if (email is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        if (roles is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        client.DefaultRequestHeaders.Add("X-CSRF", "1");
        return client;
    }
}

/// <summary>
/// Authenticates from test headers (user, email, comma-separated roles) with the claim
/// types Keycloak delivers; no user header means anonymous.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string EmailHeader = "X-Test-Email";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new("sub", $"sub-{user}"),
            new("preferred_username", user.ToString()),
            new("name", $"{user} (display)"),
        };
        if (Request.Headers.TryGetValue(EmailHeader, out var email)) claims.Add(new("email", email.ToString()));
        if (Request.Headers.TryGetValue(RolesHeader, out var roles))
            claims.AddRange(roles.ToString().Split(',').Select(role => new Claim("roles", role)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName, "preferred_username", "roles"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
