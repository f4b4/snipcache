using System.Text.Json.Serialization;

namespace SnipCacheEditor;

/// <summary>Limits the editor enforces on content files.</summary>
public static class ContentLimits
{
    /// <summary>
    /// 1 MB. The GitHub Contents API does not return the content of larger files, so they
    /// can be neither opened nor saved in v1.
    /// </summary>
    public const long MaxFileBytes = 1024 * 1024;
}

/// <summary>Kind of a tree entry.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NodeType>))]
public enum NodeType
{
    [JsonStringEnumMemberName("dir")]
    Dir,

    [JsonStringEnumMemberName("file")]
    File,
}

/// <summary>One entry of the flat repository tree; the client builds the hierarchy.</summary>
/// <param name="Path">Repository path with <c>/</c> separators.</param>
/// <param name="Type">Folder or file.</param>
/// <param name="Size">File size in bytes; null for folders.</param>
public sealed record TreeNode(string Path, NodeType Type, long? Size);

/// <summary>A text file read from the content repository.</summary>
/// <param name="Path">Repository path.</param>
/// <param name="Sha">Git blob SHA, sent back on save for conflict detection.</param>
/// <param name="Content">Decoded UTF-8 content.</param>
public sealed record ContentFile(string Path, string Sha, string Content);

/// <summary>Name and email recorded on a commit.</summary>
public sealed record CommitIdentity(string Name, string Email);

/// <summary>Outcome of a successful write (one commit).</summary>
/// <param name="Path">Repository path of the written file.</param>
/// <param name="Sha">New blob SHA of the file.</param>
/// <param name="CommitSha">SHA of the commit.</param>
/// <param name="CommitUrl">Web URL of the commit.</param>
/// <param name="Created">True when the file did not exist before.</param>
public sealed record SaveResult(string Path, string Sha, string CommitSha, string CommitUrl, bool Created);
