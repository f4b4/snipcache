using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Octokit;

namespace SnipCacheEditor;

/// <summary>
/// <see cref="IContentRepository"/> over the GitHub REST API (Octokit.net). Reads use the
/// Git Trees and Contents APIs; every write is one Contents API commit with the user as
/// author and SnipCache as committer. The tree is cached for 30 seconds and dropped after
/// every write attempt.
/// </summary>
public sealed class GitHubContentRepository(
    IGitHubClient client,
    IOptions<GitHubOptions> options,
    IMemoryCache cache,
    ILogger<GitHubContentRepository> logger) : IContentRepository
{
    private static readonly TimeSpan TreeCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly GitHubOptions github = options.Value;
    private readonly object treeCacheKey = new();

    public async Task<IReadOnlyList<TreeNode>> GetTreeAsync(CancellationToken ct)
    {
        var tree = await cache.GetOrCreateAsync(treeCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TreeCacheDuration;
            return await LoadTreeAsync();
        });
        return tree!;
    }

    private async Task<IReadOnlyList<TreeNode>> LoadTreeAsync()
    {
        var response = await Call(() => client.Git.Tree.GetRecursive(github.Owner, github.Repository, github.Branch));
        if (response.Truncated)
            logger.LogWarning("The tree of {Repository} is truncated by GitHub; some files are not listed", github.FullName);

        return response.Tree
            .Where(item => ContentPath.IsVisible(item.Path))
            .Select(item => item.Type.Value switch
            {
                TreeType.Blob => new TreeNode(item.Path, NodeType.File, item.Size),
                TreeType.Tree => new TreeNode(item.Path, NodeType.Dir, null),
                _ => null, // submodules
            })
            .OfType<TreeNode>()
            .OrderBy(node => node.Path, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<ContentFile?> GetFileAsync(string path, CancellationToken ct)
    {
        IReadOnlyList<RepositoryContent> items;
        try
        {
            items = await Call(() => client.Repository.Content.GetAllContentsByRef(
                github.Owner, github.Repository, path, github.Branch));
        }
        catch (ContentBackendException ex) when (ex.InnerException is NotFoundException)
        {
            return null;
        }

        // A folder returns its children; a file returns exactly itself.
        if (items is not [var item] || item.Type.Value != ContentType.File || item.Path != path)
            return null;

        if (item.Size > ContentLimits.MaxFileBytes)
            throw new ContentTooLargeException(path, item.Size);

        string content;
        try
        {
            content = StrictUtf8.GetString(Convert.FromBase64String(item.EncodedContent ?? string.Empty));
        }
        catch (Exception ex) when (ex is DecoderFallbackException or FormatException)
        {
            throw new UnsupportedContentException(path);
        }
        return new ContentFile(path, item.Sha, content);
    }

    public async Task<SaveResult> PutFileAsync(
        string path, string content, string? sha, string message, CommitIdentity author, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var commitAuthor = new Committer(author.Name, author.Email, now);
        var committer = new Committer(github.CommitterName, github.CommitterEmail, now);

        try
        {
            var changes = sha is null
                ? await Call(() => client.Repository.Content.CreateFile(
                    github.Owner, github.Repository, path,
                    new CreateFileRequest(message, content, github.Branch) { Author = commitAuthor, Committer = committer }))
                : await Call(() => client.Repository.Content.UpdateFile(
                    github.Owner, github.Repository, path,
                    new UpdateFileRequest(message, content, sha, github.Branch) { Author = commitAuthor, Committer = committer }));

            logger.LogInformation("Committed {Path} to {Repository} as {Commit} by {Author}",
                path, github.FullName, changes.Commit.Sha, author.Name);
            return new SaveResult(path, changes.Content.Sha, changes.Commit.Sha,
                github.CommitUrl(changes.Commit.Sha), Created: sha is null);
        }
        catch (ContentBackendException ex) when (IsConflict(ex.InnerException))
        {
            // Create on an existing path: 422 "sha wasn't supplied". Update with a stale SHA:
            // 409. Update of a file deleted in the meantime: 404 or 422.
            throw new ContentConflictException(sha is null
                ? $"{path} already exists."
                : $"{path} changed on GitHub since it was opened.", ex.InnerException);
        }
        finally
        {
            cache.Remove(treeCacheKey);
        }
    }

    private static bool IsConflict(Exception? ex) =>
        ex is ApiValidationException or NotFoundException
        || ex is ApiException { StatusCode: HttpStatusCode.Conflict };

    // Wraps every Octokit failure into a ContentBackendException with a message fit for the
    // editor; callers that expect a specific status inspect InnerException.
    private static async Task<T> Call<T>(Func<Task<T>> request)
    {
        try
        {
            return await request();
        }
        catch (AuthorizationException ex)
        {
            throw new ContentBackendException(
                "GitHub rejected the access token (401). It may have expired; renew GitHub:Token.", ex);
        }
        catch (RateLimitExceededException ex)
        {
            throw new ContentBackendException($"GitHub rate limit exceeded; it resets at {ex.Reset:u}.", ex);
        }
        catch (ApiException ex)
        {
            throw new ContentBackendException($"GitHub request failed ({(int)ex.StatusCode}): {ex.Message}", ex);
        }
    }
}
