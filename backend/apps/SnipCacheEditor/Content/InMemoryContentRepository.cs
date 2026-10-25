using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SnipCacheEditor;

/// <summary>
/// <see cref="IContentRepository"/> held in memory, for tests and for local development
/// without GitHub (<c>GitHub:UseFake=true</c>). It follows the GitHub semantics the editor
/// relies on: git blob SHAs, create-must-not-exist, update-needs-the-current-SHA.
/// </summary>
public sealed class InMemoryContentRepository(IOptions<GitHubOptions> options) : IContentRepository
{
    private readonly GitHubOptions github = options.Value;
    private readonly Lock gate = new();
    private readonly SortedDictionary<string, string> files = new(StringComparer.Ordinal);
    private readonly List<FakeCommit> commits = [];

    /// <summary>A commit recorded by <see cref="PutFileAsync"/>.</summary>
    public sealed record FakeCommit(string Sha, string Path, string Message, CommitIdentity Author);

    /// <summary>Commits made so far, oldest first.</summary>
    public IReadOnlyList<FakeCommit> Commits
    {
        get { lock (gate) return commits.ToList(); }
    }

    /// <summary>Adds or replaces a file without a commit.</summary>
    public void Seed(string path, string content)
    {
        lock (gate) files[path] = content;
    }

    /// <summary>Seeds every visible UTF-8 file below <paramref name="directory"/>.</summary>
    public void SeedFromDirectory(string directory)
    {
        var strictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (!ContentPath.IsVisible(path)) continue;
            try
            {
                Seed(path, strictUtf8.GetString(File.ReadAllBytes(file)));
            }
            catch (DecoderFallbackException)
            {
                // Binary files are not editable; leave them out.
            }
        }
    }

    /// <summary>Git blob SHA-1 of <paramref name="content"/>, as GitHub reports it.</summary>
    public static string BlobSha(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var header = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        return Convert.ToHexStringLower(SHA1.HashData([.. header, .. bytes]));
    }

    public Task<IReadOnlyList<TreeNode>> GetTreeAsync(CancellationToken ct)
    {
        lock (gate)
        {
            var nodes = new SortedDictionary<string, TreeNode>(StringComparer.Ordinal);
            foreach (var (path, content) in files)
            {
                if (!ContentPath.IsVisible(path)) continue;
                nodes[path] = new TreeNode(path, NodeType.File, Encoding.UTF8.GetByteCount(content));
                for (var slash = path.IndexOf('/'); slash > 0; slash = path.IndexOf('/', slash + 1))
                    nodes.TryAdd(path[..slash], new TreeNode(path[..slash], NodeType.Dir, null));
            }
            return Task.FromResult<IReadOnlyList<TreeNode>>(nodes.Values.ToList());
        }
    }

    public Task<ContentFile?> GetFileAsync(string path, CancellationToken ct)
    {
        lock (gate)
        {
            if (!files.TryGetValue(path, out var content))
                return Task.FromResult<ContentFile?>(null);

            var size = Encoding.UTF8.GetByteCount(content);
            if (size > ContentLimits.MaxFileBytes)
                throw new ContentTooLargeException(path, size);

            return Task.FromResult<ContentFile?>(new ContentFile(path, BlobSha(content), content));
        }
    }

    public Task<SaveResult> PutFileAsync(
        string path, string content, string? sha, string message, CommitIdentity author, CancellationToken ct)
    {
        lock (gate)
        {
            var exists = files.TryGetValue(path, out var current);
            if (sha is null && exists)
                throw new ContentConflictException($"{path} already exists.");
            if (sha is not null && (!exists || BlobSha(current!) != sha))
                throw new ContentConflictException($"{path} changed on GitHub since it was opened.");
            if (files.Keys.Any(existing => existing.StartsWith(path + "/", StringComparison.Ordinal))
                || ParentPaths(path).Any(files.ContainsKey))
                throw new ContentConflictException($"{path} conflicts with an existing file or folder.");

            files[path] = content;
            var commitSha = Convert.ToHexStringLower(
                SHA1.HashData(Encoding.UTF8.GetBytes($"{commits.Count}\n{path}\n{message}")));
            commits.Add(new FakeCommit(commitSha, path, message, author));
            return Task.FromResult(new SaveResult(path, BlobSha(content), commitSha, github.CommitUrl(commitSha), Created: sha is null));
        }
    }

    private static IEnumerable<string> ParentPaths(string path)
    {
        for (var slash = path.IndexOf('/'); slash > 0; slash = path.IndexOf('/', slash + 1))
            yield return path[..slash];
    }
}
