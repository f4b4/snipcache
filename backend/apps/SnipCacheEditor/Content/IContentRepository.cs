namespace SnipCacheEditor;

/// <summary>
/// Reads and writes the content repository. Paths passed in are already normalized with
/// <see cref="ContentPath.TryNormalize"/>. Implementations throw the
/// <see cref="ContentException"/> subtypes for conflicts, oversized or binary files and
/// backend failures.
/// </summary>
public interface IContentRepository
{
    /// <summary>All visible folders and files of the branch, sorted by path.</summary>
    Task<IReadOnlyList<TreeNode>> GetTreeAsync(CancellationToken ct);

    /// <summary>The file at <paramref name="path"/>, or null when there is no file there.</summary>
    Task<ContentFile?> GetFileAsync(string path, CancellationToken ct);

    /// <summary>
    /// Writes <paramref name="content"/> in one commit. With <paramref name="sha"/> null the file
    /// is created and must not exist; otherwise it is updated and <paramref name="sha"/> must be
    /// its current blob SHA. Either mismatch throws <see cref="ContentConflictException"/>.
    /// </summary>
    Task<SaveResult> PutFileAsync(
        string path, string content, string? sha, string message, CommitIdentity author, CancellationToken ct);
}
