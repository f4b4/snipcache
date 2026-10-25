using System.Text.Json;

namespace SnipCacheEditor.Tests;

// The C# port of the slug rule against the cases @snipcache/content-rules tests too
// (frontend/shared/content-rules/src/slug-cases.json, copied next to the test assembly).
public sealed class PageSlugsTests
{
    private static readonly JsonElement Cases =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "slug-cases.json"))).RootElement;

    public static TheoryData<string, string> Segments => Pairs("segments");

    public static TheoryData<string, string> Pages => Pairs("pages");

    public static TheoryData<string> InvalidSegments =>
        new(Cases.GetProperty("invalidSegments").EnumerateArray().Select(e => e.GetString()!));

    public static TheoryData<string, string, string> Urls
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var c in Cases.GetProperty("urls").EnumerateArray())
                data.Add(c[0].GetString()!, c[1].GetString()!, c[2].GetString()!);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Segments))]
    public void SlugifySegment_MatchesSharedCases(string input, string expected) =>
        Assert.Equal(expected, PageSlugs.SlugifySegment(input));

    [Theory]
    [MemberData(nameof(InvalidSegments))]
    public void SlugifySegment_NothingLeft_IsNull(string input) =>
        Assert.Null(PageSlugs.SlugifySegment(input));

    [Theory]
    [MemberData(nameof(Pages))]
    public void PageSlug_MatchesSharedCases(string input, string expected) =>
        Assert.Equal(expected, PageSlugs.PageSlug(input));

    [Theory]
    [MemberData(nameof(Urls))]
    public void PageUrl_MatchesSharedCases(string baseUrl, string path, string expected) =>
        Assert.Equal(expected, PageSlugs.PageUrl(baseUrl, path));

    [Theory]
    [InlineData("!!!/note.md")]
    [InlineData("a/().md")]
    public void PageSlug_SegmentWithoutSlug_IsNull(string path)
    {
        Assert.Null(PageSlugs.PageSlug(path));
        Assert.Null(PageSlugs.PageUrl("https://example.org", path));
    }

    private static TheoryData<string, string> Pairs(string name)
    {
        var data = new TheoryData<string, string>();
        foreach (var c in Cases.GetProperty(name).EnumerateArray())
            data.Add(c[0].GetString()!, c[1].GetString()!);
        return data;
    }
}
