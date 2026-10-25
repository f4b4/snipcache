using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SnipCacheEditor;

/// <summary>
/// The <c>Bot</c> configuration section. <see cref="ApiKey"/> is a secret (environment file
/// <c>Bot__ApiKey</c> in production, user-secrets in development); without it every
/// <c>/bot</c> call gets 401.
/// </summary>
public sealed class BotOptions
{
    public const string SectionName = "Bot";

    /// <summary>Shortest accepted key; <c>openssl rand -base64 32</c> gives 44 characters.</summary>
    public const int MinKeyLength = 32;

    /// <summary>The key bots send in <c>X-Api-Key</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Commit author name of bot saves; the email is the committer address.</summary>
    public string AuthorName { get; set; } = "SnipCache Bot";
}

/// <summary>
/// The policy every <c>/bot</c> endpoint requires: a caller authenticated by
/// <see cref="ApiKeyAuthenticationHandler"/>. A browser session cookie does not count.
/// </summary>
public static class BotAuthentication
{
    public const string SchemeName = "BotApiKey";

    public const string PolicyName = "SnipCacheBot";

    public const string HeaderName = "X-Api-Key";

    public static IServiceCollection AddBotAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BotOptions>()
            .Bind(configuration.GetSection(BotOptions.SectionName))
            .Validate(o => string.IsNullOrEmpty(o.ApiKey) || o.ApiKey.Length >= BotOptions.MinKeyLength,
                $"Bot:ApiKey must have at least {BotOptions.MinKeyLength} characters (e.g. openssl rand -base64 32).")
            .ValidateOnStart();

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(SchemeName, null);
        services.AddAuthorizationBuilder().AddPolicy(PolicyName, policy => policy
            .AddAuthenticationSchemes(SchemeName)
            .RequireAuthenticatedUser());
        return services;
    }

    /// <summary>Logs at startup that <c>/bot</c> rejects every call because no key is configured.</summary>
    public static WebApplication WarnIfBotApiKeyMissing(this WebApplication app)
    {
        if (string.IsNullOrEmpty(app.Services.GetRequiredService<IOptions<BotOptions>>().Value.ApiKey))
            app.Logger.LogWarning("Bot:ApiKey is not set; every /bot call is rejected with 401");
        return app;
    }
}

/// <summary>
/// Authenticates a bot by the <c>X-Api-Key</c> header, compared in constant time with
/// <see cref="BotOptions.ApiKey"/>. No header means no result (401 on challenge); a wrong
/// key fails. The principal carries a <c>name</c> claim with <see cref="BotOptions.AuthorName"/>
/// so <see cref="CommitAuthors.FromPrincipal"/> records it as the commit author.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<BotOptions> bot)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(BotAuthentication.HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        var configured = bot.Value.ApiKey;
        if (string.IsNullOrEmpty(configured))
            return Task.FromResult(AuthenticateResult.Fail("Bot:ApiKey is not set."));
        if (values is not [{ } presented] || !KeysEqual(presented, configured))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var identity = new ClaimsIdentity(
            [new Claim("sub", "bot"), new Claim("name", bot.Value.AuthorName)],
            BotAuthentication.SchemeName, "name", "roles");
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), BotAuthentication.SchemeName)));
    }

    // Hashing first gives equal-length inputs, so neither the content nor the length of the
    // configured key leaks through the comparison time.
    private static bool KeysEqual(string presented, string configured) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
            SHA256.HashData(Encoding.UTF8.GetBytes(configured)));
}
