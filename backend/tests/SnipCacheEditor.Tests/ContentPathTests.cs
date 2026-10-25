namespace SnipCacheEditor.Tests;

// ContentPath is the only gate between user input and a GitHub write; these tests pin
// the normalization and every rejection rule.
public class ContentPathTests
{
    [Theory]
    [InlineData("notes.md", "notes.md")]
    [InlineData("  SQL/count.sql  ", "SQL/count.sql")] // outer whitespace is trimmed
    [InlineData(@"Scripts\Windows\clean.ps1", "Scripts/Windows/clean.ps1")]
    [InlineData("Folder/Sub/", "Folder/Sub")]
    [InlineData("KI-Chats/Codeblöcke in Markdown.md", "KI-Chats/Codeblöcke in Markdown.md")]
    [InlineData("a/b (copy)/x_y-z.txt", "a/b (copy)/x_y-z.txt")]
    public void AcceptsAndNormalizes(string input, string expected)
    {
        Assert.True(ContentPath.TryNormalize(input, out var normalized, out var error), error);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void NormalizesToNfc()
    {
        var decomposed = "Knöpfe.md"; // o + combining diaeresis

        Assert.True(ContentPath.TryNormalize(decomposed, out var normalized, out _));
        Assert.Equal("Knöpfe.md", normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/etc/passwd")]
    [InlineData("../outside.md")]
    [InlineData("a/../b.md")]
    [InlineData("./a.md")]
    [InlineData(".github/workflows/deploy.yml")]
    [InlineData(".gitignore")]
    [InlineData("a/.hidden/b.md")]
    [InlineData("a//b.md")]
    [InlineData("tab\there.md")]
    [InlineData("new\nline.md")]
    public void Rejects(string? input)
    {
        foreach (var newName in new[] { false, true })
        {
            Assert.False(ContentPath.TryNormalize(input, out var normalized, out var error, newName));
            Assert.Null(normalized);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    // Files that already exist (added through git) may use any name; only names the
    // editor creates follow the strict pattern.
    [Theory]
    [InlineData("AI/WSL2, VS Code, Visual Studio.md")]
    [InlineData("a /b.md")]
    [InlineData("a/b?.md")]
    [InlineData("a/b:c.md")]
    [InlineData("a/b*.md")]
    [InlineData("a/<b>.md")]
    [InlineData("a/50% off.md")]
    public void AcceptsExistingNames_RejectsThemAsNewNames(string input)
    {
        Assert.True(ContentPath.TryNormalize(input, out var normalized, out var error), error);
        Assert.Equal(input, normalized);

        Assert.False(ContentPath.TryNormalize(input, out _, out error, newName: true));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("KI-Chats/Codeblöcke in Markdown.md")]
    [InlineData("a/b (copy)/x_y-z.txt")]
    public void AcceptsStrictNamesAsNewNames(string input) =>
        Assert.True(ContentPath.TryNormalize(input, out _, out var error, newName: true), error);

    [Fact]
    public void RejectsOverlongPaths()
    {
        var path = string.Join('/', Enumerable.Repeat("segment", 60));

        Assert.False(ContentPath.TryNormalize(path, out _, out var error));
        Assert.Contains("400", error);
    }

    [Theory]
    [InlineData("a/b.md", true)]
    [InlineData(".github/workflows/deploy.yml", false)]
    [InlineData("a/.draft.md", false)]
    public void IsVisible_HidesDotSegments(string path, bool visible) =>
        Assert.Equal(visible, ContentPath.IsVisible(path));

    [Theory]
    [InlineData("a/b/c.md", "c.md")]
    [InlineData("top", "top")]
    public void Name_IsLastSegment(string path, string name) => Assert.Equal(name, ContentPath.Name(path));
}
