using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace SnipCacheEditor.Tests;

// POST /bot/snippets end to end over the in-memory repository: the API-key gate, create,
// conflict and overwrite, name and slug checks, and what ends up in the commit.
public sealed class BotEndpointTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly EditorFactory factory = new();

    public BotEndpointTests()
    {
        factory.Content.Seed("README.md", "# Home\n");
        factory.Content.Seed("SQL/count.sql", "select count(*) from t;\n");
        factory.Content.Seed("KI-Chats/Codeblöcke in Markdown.md", "# Codeblöcke\n");
        factory.Content.Seed("AI/WSL2, VS Code.md", "# WSL2\n");
    }

    public void Dispose() => factory.Dispose();

    private static SaveSnippetRequest Snippet(
        string? folder, string fileName, string content = "# Snippet\n", string? message = null, bool overwrite = false) =>
        new(fileName, content, folder, message, overwrite);

    private Task<HttpResponseMessage> Post(SaveSnippetRequest request, string? apiKey = EditorFactory.BotApiKey) =>
        factory.Bot(apiKey).PostAsJsonAsync("/bot/snippets", request, Ct);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key-0123456789abcdefghijklmnopqrs")]
    [InlineData(EditorFactory.BotApiKey + "x")]
    public async Task MissingOrWrongKey_Is401(string? apiKey)
    {
        var response = await Post(Snippet("KI-Chats", "new.md"), apiKey);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.Content.Commits);
    }

    [Fact]
    public async Task SignedInUserWithoutKey_Is401()
    {
        var response = await factory.SignedIn().PostAsJsonAsync("/bot/snippets", Snippet("KI-Chats", "new.md"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NoKeyConfigured_RejectsEveryKey()
    {
        using var unconfigured = new EditorFactory(botApiKey: null);

        var response = await unconfigured.Bot().PostAsJsonAsync("/bot/snippets", Snippet("KI-Chats", "new.md"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void ShortKey_FailsAtStartup()
    {
        using var misconfigured = new EditorFactory(botApiKey: "too-short");

        var ex = Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());
        Assert.Contains("Bot:ApiKey", ex.Message);
    }

    [Fact]
    public async Task ValidKey_DoesNotNeedCsrfHeader()
    {
        var response = await Post(Snippet("KI-Chats", "new.md"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_CommitsAsBotAndReturnsPageUrl()
    {
        var response = await Post(Snippet("KI-Chats", "Docker aufräumen.md", "# Docker\n\n`docker system prune`\n"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<SaveSnippetResponse>(Ct);
        var commit = Assert.Single(factory.Content.Commits);
        Assert.Equal(new SaveSnippetResponse(
            "KI-Chats/Docker aufräumen.md",
            InMemoryContentRepository.BlobSha("# Docker\n\n`docker system prune`\n"),
            $"https://github.com/owner/content/commit/{commit.Sha}",
            "https://owner.github.io/content/ki-chats/docker-aufraeumen/"), saved);
        Assert.Equal(saved!.PageUrl, response.Headers.Location!.OriginalString);
        Assert.Equal("Bot: add KI-Chats/Docker aufräumen.md", commit.Message);
        Assert.Equal(new CommitIdentity("SnipCache Bot", "noreply@snipcache.test"), commit.Author);
        Assert.Equal("# Docker\n\n`docker system prune`\n",
            (await factory.Content.GetFileAsync("KI-Chats/Docker aufräumen.md", Ct))!.Content);
    }

    [Fact]
    public async Task Create_InNewNestedFolder()
    {
        var response = await Post(Snippet(@"Shell\Linux/Backup ", "home.sh", "tar czf home.tgz ~\n", message: " Backup script "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<SaveSnippetResponse>(Ct);
        Assert.Equal("Shell/Linux/Backup/home.sh", saved!.Path);
        Assert.Equal("https://owner.github.io/content/shell/linux/backup/home-sh/", saved.PageUrl);
        Assert.Equal("Backup script", Assert.Single(factory.Content.Commits).Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Create_WithoutFolder_GoesToTheRoot(string? folder)
    {
        var response = await Post(Snippet(folder, "Notes.md"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Notes.md", (await response.Content.ReadFromJsonAsync<SaveSnippetResponse>(Ct))!.Path);
    }

    [Fact]
    public async Task Create_InExistingFolderWithNonStrictName()
    {
        var response = await Post(Snippet("AI", "Second.md"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ExistingFile_WithoutOverwrite_Is409()
    {
        var response = await Post(Snippet("SQL", "count.sql", "select 1;\n"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.Equal("SQL/count.sql already exists; set overwrite to replace it.", problem!.Detail);
        Assert.Empty(factory.Content.Commits);
        Assert.Equal("select count(*) from t;\n", (await factory.Content.GetFileAsync("SQL/count.sql", Ct))!.Content);
    }

    [Fact]
    public async Task ExistingFile_WithOverwrite_ReplacesIt()
    {
        var response = await Post(Snippet("SQL", "count.sql", "select 1;\n", overwrite: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<SaveSnippetResponse>(Ct);
        Assert.Equal(InMemoryContentRepository.BlobSha("select 1;\n"), saved!.Sha);
        Assert.Equal("https://owner.github.io/content/sql/count-sql/", saved.PageUrl);
        Assert.Equal("Bot: update SQL/count.sql", Assert.Single(factory.Content.Commits).Message);
        Assert.Equal("select 1;\n", (await factory.Content.GetFileAsync("SQL/count.sql", Ct))!.Content);
    }

    [Fact]
    public async Task ExistingFileWithNonStrictName_CanBeOverwritten()
    {
        var response = await Post(Snippet("AI", "WSL2, VS Code.md", "# WSL2\nnew\n", overwrite: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://owner.github.io/content/ai/wsl2-vs-code/",
            (await response.Content.ReadFromJsonAsync<SaveSnippetResponse>(Ct))!.PageUrl);
    }

    [Fact]
    public async Task Overwrite_OfMissingFile_Creates()
    {
        var response = await Post(Snippet("SQL", "new.sql", overwrite: true));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Bot: add SQL/new.sql", Assert.Single(factory.Content.Commits).Message);
    }

    [Fact]
    public async Task SameAddressAsExistingPage_Is409()
    {
        // SQL/Count.sql and SQL/count.sql both become /sql/count-sql/, which fails the site build.
        var response = await Post(Snippet("SQL", "Count.sql"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.Equal("The page of SQL/Count.sql would get the same address as SQL/count.sql.", problem!.Detail);
        Assert.Empty(factory.Content.Commits);
    }

    [Theory]
    [InlineData("SQL/count.sql", "x.md", "SQL/count.sql is a file, not a folder.")]
    [InlineData("", "SQL", "SQL is a folder.")]
    public async Task PathClashesWithTree_Is409(string folder, string fileName, string detail)
    {
        var response = await Post(Snippet(folder, fileName));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(detail, (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Detail);
    }

    [Theory]
    [InlineData("AI", "a, b.md", "fileName")]       // new file: strict name pattern
    [InlineData("New, Folder", "a.md", "folder")]   // new folder: strict name pattern
    [InlineData("KI-Chats", "().md", "fileName")]   // no letter or digit, so no page address
    [InlineData("()", "a.md", "folder")]
    [InlineData(".github/workflows", "x.yml", "folder")]
    [InlineData("/abs", "x.md", "folder")]
    [InlineData("KI-Chats", "sub/x.md", "fileName")]
    [InlineData("KI-Chats", "", "fileName")]
    [InlineData("KI-Chats", ".env", "fileName")]
    public async Task InvalidName_Is400(string folder, string fileName, string field)
    {
        var response = await Post(Snippet(folder, fileName));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);
        Assert.Equal([field], problem!.Errors.Keys);
        Assert.Empty(factory.Content.Commits);
    }

    [Fact]
    public async Task MissingContent_Is400()
    {
        var response = await factory.Bot().PostAsJsonAsync("/bot/snippets",
            new { folder = "KI-Chats", fileName = "x.md" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);
        Assert.Equal(["content"], problem!.Errors.Keys);
    }

    [Fact]
    public async Task TooLarge_Is413()
    {
        var response = await Post(Snippet("KI-Chats", "big.md", new string('x', (int)ContentLimits.MaxFileBytes + 1)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(factory.Content.Commits);
    }
}
