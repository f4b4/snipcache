using System.Reflection;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Octokit;

namespace SnipCacheEditor;

public static class ContentServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="GitHubOptions"/> and <see cref="SiteOptions"/> and registers the
    /// content repository: the in-memory fake (seeded from <c>GitHub:FakeSeedDirectory</c>)
    /// when <c>GitHub:UseFake</c> is set, otherwise <see cref="GitHubContentRepository"/>.
    /// Missing GitHub settings fail at startup, not on the first request.
    /// </summary>
    public static IServiceCollection AddContentRepository(this IServiceCollection services, IConfiguration configuration)
    {
        var github = services.AddOptions<GitHubOptions>()
            .Bind(configuration.GetSection(GitHubOptions.SectionName))
            .Validate(o => o.UseFake || !string.IsNullOrWhiteSpace(o.Owner) && !string.IsNullOrWhiteSpace(o.Repository),
                "GitHub:Owner and GitHub:Repository are required.")
            .Validate(o => o.UseFake || !string.IsNullOrWhiteSpace(o.Token),
                "GitHub:Token is required (environment variable GitHub__Token, or user-secrets in development), or set GitHub:UseFake=true.");
        if (!IsOpenApiGeneration)
            github.ValidateOnStart();
        services.AddOptions<SiteOptions>().Bind(configuration.GetSection(SiteOptions.SectionName));

        var useFake = configuration.GetValue<bool>($"{GitHubOptions.SectionName}:{nameof(GitHubOptions.UseFake)}");
        if (useFake)
        {
            services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<GitHubOptions>>();
                var repository = new InMemoryContentRepository(options);
                if (!string.IsNullOrWhiteSpace(options.Value.FakeSeedDirectory))
                {
                    var root = sp.GetRequiredService<IHostEnvironment>().ContentRootPath;
                    repository.SeedFromDirectory(Path.GetFullPath(options.Value.FakeSeedDirectory, root));
                }
                return repository;
            });
            services.AddSingleton<IContentRepository>(sp => sp.GetRequiredService<InMemoryContentRepository>());
        }
        else
        {
            services.AddMemoryCache();
            services.AddSingleton<IGitHubClient>(sp => new GitHubClient(new Octokit.ProductHeaderValue("SnipCache"))
            {
                Credentials = new Credentials(sp.GetRequiredService<IOptions<GitHubOptions>>().Value.Token),
            });
            services.AddSingleton<IContentRepository, GitHubContentRepository>();
        }
        return services;
    }

    /// <summary>
    /// Once the host has started, logs a warning when <c>GitHub:TokenExpires</c> is less than
    /// 14 days away (or past): an expired token silently breaks every save. Deferred to
    /// ApplicationStarted because build-time OpenAPI generation runs Program without a token.
    /// </summary>
    public static WebApplication WarnIfGitHubTokenExpiresSoon(this WebApplication app)
    {
        if (IsOpenApiGeneration) return app;

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var github = app.Services.GetRequiredService<IOptions<GitHubOptions>>().Value;
            if (github.UseFake || github.TokenExpires is not { } expires) return;

            var daysLeft = expires.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
            if (daysLeft < 14)
                app.Logger.LogWarning("The GitHub token expires on {Expires} ({DaysLeft} days left); renew GitHub:Token", expires, daysLeft);
        });
        return app;
    }

    // Build-time OpenAPI generation starts the host without secrets, so the GitHub
    // settings are not checked there.
    private static bool IsOpenApiGeneration =>
        Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
}
