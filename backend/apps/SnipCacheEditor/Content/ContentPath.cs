using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace SnipCacheEditor;

/// <summary>
/// Normalizes and validates content repository paths coming from the editor and the bot.
/// Every path must be relative, use <c>/</c> separators, be in Unicode NFC, and have no
/// empty segment, no segment with a leading dot and no control characters. The dot rule
/// rejects <c>.</c>, <c>..</c>, <c>.github</c> and the dotfiles that prepare-content
/// ignores, so the editor can never touch something the site skips.
/// <para>
/// Names the editor <em>creates</em> must also match <c>^[\w .\-()]+$</c> without surrounding
/// whitespace. Existing files are not held to that pattern, because content added through
/// git may use any name (e.g. commas), and it must still be possible to open and save it.
/// </para>
/// </summary>
public static partial class ContentPath
{
    /// <summary>Upper bound for a whole path, well inside GitHub's limits.</summary>
    public const int MaxLength = 400;

    [GeneratedRegex(@"^[\w .\-()]+$")]
    private static partial Regex SegmentPattern();

    /// <summary>
    /// Normalizes <paramref name="path"/> (NFC, <c>\</c> to <c>/</c>, trims whitespace and a
    /// trailing <c>/</c>) and validates the result. Returns false with a user-facing
    /// <paramref name="error"/> when the path is not acceptable. Set <paramref name="newName"/>
    /// when the path is about to be created, to apply the stricter name pattern.
    /// </summary>
    public static bool TryNormalize(
        string? path,
        [NotNullWhen(true)] out string? normalized,
        [NotNullWhen(false)] out string? error,
        bool newName = false)
    {
        normalized = null;

        var candidate = (path ?? string.Empty).Normalize(NormalizationForm.FormC).Replace('\\', '/').Trim();
        if (candidate.Length == 0)
        {
            error = "The path is required.";
            return false;
        }
        if (candidate.StartsWith('/'))
        {
            error = "The path must be relative.";
            return false;
        }
        candidate = candidate.TrimEnd('/');
        if (candidate.Length > MaxLength)
        {
            error = $"The path must not be longer than {MaxLength} characters.";
            return false;
        }

        foreach (var segment in candidate.Split('/'))
        {
            if (segment.Length == 0)
            {
                error = "The path must not contain empty segments.";
                return false;
            }
            if (segment.StartsWith('.'))
            {
                error = $"'{segment}': names must not start with a dot.";
                return false;
            }
            if (segment.Any(char.IsControl))
            {
                error = $"'{segment}': names must not contain control characters.";
                return false;
            }
            if (newName && !IsValidNewName(segment, out error))
                return false;
        }

        normalized = candidate;
        error = null;
        return true;
    }

    /// <summary>
    /// The stricter rule for a name the editor creates, applied to one segment that already
    /// passed <see cref="TryNormalize"/>: no surrounding whitespace, only <c>^[\w .\-()]+$</c>.
    /// </summary>
    public static bool IsValidNewName(string segment, [NotNullWhen(false)] out string? error)
    {
        if (segment != segment.Trim())
        {
            error = $"'{segment}': names must not start or end with whitespace.";
            return false;
        }
        if (!SegmentPattern().IsMatch(segment))
        {
            error = $"'{segment}': only letters, digits, spaces and _ . - ( ) are allowed.";
            return false;
        }
        error = null;
        return true;
    }

    /// <summary>
    /// True when no segment of a repository path starts with a dot, i.e. the file is part
    /// of the site (same rule as prepare-content). Used to filter the tree.
    /// </summary>
    public static bool IsVisible(string path) =>
        !path.Split('/').Any(segment => segment.StartsWith('.'));

    /// <summary>The last segment of a normalized path.</summary>
    public static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];
}
