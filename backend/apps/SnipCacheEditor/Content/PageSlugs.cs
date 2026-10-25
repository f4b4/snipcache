using System.Globalization;
using System.Text;

namespace SnipCacheEditor;

/// <summary>
/// C# port of the slug rule in <c>@snipcache/content-rules</c> (<c>slug.ts</c>): how a content
/// file path maps to the address of its page on the site. The bot needs it for the page URL
/// it returns and to refuse names whose page would collide with an existing one (which fails
/// the site build). Both implementations run the test cases in <c>slug-cases.json</c>; change
/// the rule there first.
/// </summary>
public static class PageSlugs
{
    /// <summary>True for files rendered as Markdown pages; every other file becomes a code page.</summary>
    public static bool IsMarkdownPath(string path) => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for a folder's <c>README.md</c>, which becomes the folder's index page.</summary>
    public static bool IsReadmePath(string path) => ContentPath.Name(path).Equals("README.md", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Slug for one path segment: lower-cased, umlauts transliterated, other accents stripped,
    /// dots and whitespace turned into <c>-</c>, everything except letters, digits, <c>-</c> and
    /// <c>_</c> dropped. Null when nothing is left.
    /// </summary>
    public static string? SlugifySegment(string segment)
    {
        var lower = segment.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        var transliterated = new StringBuilder(lower.Length + 8);
        foreach (var c in lower)
        {
            transliterated.Append(c switch
            {
                'ä' => "ae",
                'ö' => "oe",
                'ü' => "ue",
                'ß' => "ss",
                _ => c.ToString(),
            });
        }

        // Runes, not chars, so letters outside the BMP count as letters (as in JS with /u).
        var slug = new StringBuilder(transliterated.Length);
        var dash = false;
        foreach (var rune in transliterated.ToString().Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (IsMark(category))
                continue;
            if (rune.Value is '.' or '-' || IsJsWhitespace(rune, category))
            {
                dash = true;
                continue;
            }
            if (!IsLetterOrNumber(category) && rune.Value != '_')
                continue;
            if (dash && slug.Length > 0)
                slug.Append('-');
            dash = false;
            slug.Append(rune.ToString());
        }
        return slug.Length == 0 ? null : slug.ToString();
    }

    /// <summary>
    /// Slug of the page generated for a content file, e.g. <c>a/b/README.md</c> → <c>a/b</c>,
    /// <c>a/b/note.md</c> → <c>a/b/note</c>, <c>a/b/count.sql</c> → <c>a/b/count-sql</c>. The root
    /// <c>README.md</c> maps to the empty string (the home page). Null when a segment has no slug.
    /// </summary>
    public static string? PageSlug(string sourcePath)
    {
        var segments = sourcePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;

        var fileName = segments[^1];
        var names = segments[..^1].ToList();
        if (!IsReadmePath(fileName))
            names.Add(IsMarkdownPath(fileName) ? fileName[..^".md".Length] : fileName);

        var parts = new List<string>(names.Count);
        foreach (var name in names)
        {
            if (SlugifySegment(name) is not { } slug)
                return null;
            parts.Add(slug);
        }
        return string.Join('/', parts);
    }

    /// <summary>Absolute URL of a content file's page, given the site's base URL. Null when the path has no slug.</summary>
    public static string? PageUrl(string siteBaseUrl, string sourcePath)
    {
        if (PageSlug(sourcePath) is not { } slug)
            return null;
        var path = slug.Length == 0 ? string.Empty : string.Join('/', slug.Split('/').Select(Uri.EscapeDataString)) + "/";
        return $"{siteBaseUrl.TrimEnd('/')}/{path}";
    }

    private static bool IsMark(UnicodeCategory category) =>
        category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static bool IsLetterOrNumber(UnicodeCategory category) =>
        category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;

    // JavaScript's \s: ASCII whitespace, U+FEFF and the space, line and paragraph separators.
    private static bool IsJsWhitespace(Rune rune, UnicodeCategory category) =>
        rune.Value is '\t' or '\n' or '\v' or '\f' or '\r' or '﻿'
        || category is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
