namespace SnipCacheEditor;

/// <summary>
/// The <c>GitHub</c> configuration section: which repository the editor writes to and how.
/// <see cref="Token"/> is a secret (environment file in production, user-secrets in development).
/// </summary>
public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public string Owner { get; set; } = string.Empty;

    public string Repository { get; set; } = string.Empty;

    public string Branch { get; set; } = "main";

    /// <summary>Fine-grained personal access token with Contents read/write on <see cref="Repository"/>.</summary>
    public string? Token { get; set; }

    /// <summary>Optional expiry date of <see cref="Token"/>; a warning is logged 14 days ahead.</summary>
    public DateOnly? TokenExpires { get; set; }

    /// <summary>Use the in-memory repository instead of GitHub (tests, local development).</summary>
    public bool UseFake { get; set; }

    /// <summary>
    /// With <see cref="UseFake"/>: directory whose files seed the in-memory repository, relative
    /// to the content root (e.g. the site's sample content).
    /// </summary>
    public string? FakeSeedDirectory { get; set; }

    public string CommitterName { get; set; } = "SnipCache";

    public string CommitterEmail { get; set; } = "noreply@snipcache.freeddns.org";

    /// <summary><c>owner/repository</c>.</summary>
    public string FullName => $"{Owner}/{Repository}";

    /// <summary>Web URL of a commit in the content repository.</summary>
    public string CommitUrl(string commitSha) => $"https://github.com/{Owner}/{Repository}/commit/{commitSha}";
}

/// <summary>The <c>Site</c> configuration section: where the content repository is published.</summary>
public sealed class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>Pages URL of the content site, e.g. <c>https://f4b4.github.io/snipcache-content-f4b4</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}
