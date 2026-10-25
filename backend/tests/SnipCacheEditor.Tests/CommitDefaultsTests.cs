using System.Security.Claims;

namespace SnipCacheEditor.Tests;

public class CommitDefaultsTests
{
    private const string Fallback = "noreply@snipcache.test";

    [Fact]
    public void DefaultMessages()
    {
        Assert.Equal("Edit SQL/count.sql", CommitMessages.Edit("SQL/count.sql"));
        Assert.Equal("Add SQL/count.sql", CommitMessages.Add("SQL/count.sql"));
        Assert.Equal("Add folder SQL", CommitMessages.AddFolder("SQL"));
        Assert.Equal("Bot: add KI-Chats/x.md", CommitMessages.BotAdd("KI-Chats/x.md"));
    }

    [Theory]
    [InlineData(null, "Edit a.md")]
    [InlineData("", "Edit a.md")]
    [InlineData("   ", "Edit a.md")]
    [InlineData("  Fix typo ", "Fix typo")]
    public void OrDefault_UsesFallbackOnlyForBlankMessages(string? message, string expected) =>
        Assert.Equal(expected, CommitMessages.OrDefault(message, "Edit a.md"));

    [Fact]
    public void Author_PrefersNameAndEmailClaims()
    {
        var author = CommitAuthors.FromPrincipal(
            Principal(("name", "Ada Lovelace"), ("preferred_username", "ada"), ("email", "ada@example.org")), Fallback);

        Assert.Equal(new CommitIdentity("Ada Lovelace", "ada@example.org"), author);
    }

    [Fact]
    public void Author_FallsBackToPreferredUsernameAndCommitterEmail()
    {
        var author = CommitAuthors.FromPrincipal(Principal(("preferred_username", "ada")), Fallback);

        Assert.Equal(new CommitIdentity("ada", Fallback), author);
    }

    [Fact]
    public void Author_WithoutAnyNameClaim_UsesGenericName()
    {
        var author = CommitAuthors.FromPrincipal(Principal(("name", "  ")), Fallback);

        Assert.Equal(new CommitIdentity(CommitAuthors.FallbackName, Fallback), author);
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));
}
