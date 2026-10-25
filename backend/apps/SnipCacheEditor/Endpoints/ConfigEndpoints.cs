using Microsoft.Extensions.Options;

namespace SnipCacheEditor;

/// <summary>What the editor SPA needs to know about the content repository and its site.</summary>
internal static class ConfigEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/config", (IOptions<GitHubOptions> github, IOptions<SiteOptions> site) =>
                TypedResults.Ok(new ConfigResponse(
                    site.Value.BaseUrl.TrimEnd('/'), github.Value.FullName, github.Value.Branch)))
            .WithName("GetConfig");
    }
}

/// <param name="SiteBaseUrl">Pages URL of the content site, without a trailing slash.</param>
/// <param name="Repository"><c>owner/repository</c>.</param>
/// <param name="Branch">The branch the editor commits to.</param>
public sealed record ConfigResponse(string SiteBaseUrl, string Repository, string Branch);
