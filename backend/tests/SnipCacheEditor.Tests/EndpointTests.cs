using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace SnipCacheEditor.Tests;

// The /api surface end to end over the in-memory repository: auth and CSRF gates, status
// code mapping (400, 404, 409, 413) and what ends up in the commit.
public sealed class EndpointTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly EditorFactory factory = new();

    public EndpointTests()
    {
        factory.Content.Seed("README.md", "# Home\n");
        factory.Content.Seed("SQL/count.sql", "select count(*) from t;\n");
        factory.Content.Seed("KI-Chats/Codeblöcke in Markdown.md", "# Codeblöcke\n");
        factory.Content.Seed(".github/workflows/deploy.yml", "name: Deploy SnipCache\n");
        factory.Content.Seed("AI/WSL2, VS Code.md", "# WSL2\n");
    }

    public void Dispose() => factory.Dispose();

    [Theory]
    [InlineData("/api/config")]
    [InlineData("/api/tree")]
    [InlineData("/api/files/README.md")]
    public async Task Anonymous_Gets401(string url)
    {
        var response = await factory.CreateClient().GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_IsAnonymous()
    {
        var response = await factory.CreateClient().GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Config_ReportsSiteAndRepository()
    {
        var config = await factory.SignedIn().GetFromJsonAsync<ConfigResponse>("/api/config", Ct);

        Assert.Equal(new ConfigResponse("https://owner.github.io/content", "owner/content", "main"), config);
    }

    [Fact]
    public async Task Tree_IsFlatSortedAndHidesDotfiles()
    {
        var json = await factory.SignedIn().GetStringAsync("/api/tree", Ct);

        Assert.Equal(
            """{"nodes":[{"path":"AI","type":"dir","size":null},{"path":"AI/WSL2, VS Code.md","type":"file","size":7},{"path":"KI-Chats","type":"dir","size":null},{"path":"KI-Chats/Codeblöcke in Markdown.md","type":"file","size":14},{"path":"README.md","type":"file","size":7},{"path":"SQL","type":"dir","size":null},{"path":"SQL/count.sql","type":"file","size":24}]}""",
            json);
    }

    [Fact]
    public async Task GetFile_ReturnsContentAndSha()
    {
        var file = await factory.SignedIn().GetFromJsonAsync<FileResponse>("/api/files/SQL/count.sql", Ct);

        Assert.Equal(new FileResponse("SQL/count.sql", InMemoryContentRepository.BlobSha("select count(*) from t;\n"),
            "select count(*) from t;\n", "utf-8"), file);
    }

    [Fact]
    public async Task GetFile_DecodesEscapedPaths()
    {
        var file = await factory.SignedIn().GetFromJsonAsync<FileResponse>(
            "/api/files/KI-Chats/Codebl%C3%B6cke%20in%20Markdown.md", Ct);

        Assert.Equal("# Codeblöcke\n", file!.Content);
    }

    [Fact]
    public async Task GetFile_EmptyPath_Is400()
    {
        var response = await factory.SignedIn().GetAsync("/api/files/", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExistingFileWithNonStrictName_CanBeOpenedAndSaved()
    {
        var client = factory.SignedIn();
        var file = await client.GetFromJsonAsync<FileResponse>("/api/files/AI/WSL2%2C%20VS%20Code.md", Ct);

        var response = await client.PutAsJsonAsync("/api/files/AI/WSL2%2C%20VS%20Code.md",
            new SaveFileRequest("# WSL2\nmore\n", file!.Sha, null), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Edit AI/WSL2, VS Code.md", Assert.Single(factory.Content.Commits).Message);
    }

    [Fact]
    public async Task CreateFileWithNonStrictName_Is400()
    {
        var response = await factory.SignedIn().PutAsJsonAsync("/api/files/AI/a, b.md",
            new SaveFileRequest("x", null, null), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Content.Commits);
    }

    [Theory]
    [InlineData("/api/files/missing.md")]
    [InlineData("/api/files/SQL")]
    public async Task GetFile_MissingOrFolder_Is404(string url)
    {
        var response = await factory.SignedIn().GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFile_Dotfile_Is400()
    {
        var response = await factory.SignedIn().GetAsync("/api/files/.github/workflows/deploy.yml", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);
        Assert.Contains("path", problem!.Errors.Keys);
    }

    [Fact]
    public async Task GetFile_TooLarge_Is413()
    {
        factory.Content.Seed("big.txt", new string('x', (int)ContentLimits.MaxFileBytes + 1));

        var response = await factory.SignedIn().GetAsync("/api/files/big.txt", Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Save_WithoutCsrfHeader_Is403()
    {
        var client = factory.SignedIn();
        client.DefaultRequestHeaders.Remove("X-CSRF");

        var response = await client.PutAsJsonAsync("/api/files/new.md", new SaveFileRequest("x", null, null), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Content.Commits);
    }

    [Fact]
    public async Task Save_UpdateWithCurrentSha_CommitsAsTheUser()
    {
        var client = factory.SignedIn();
        var file = await client.GetFromJsonAsync<FileResponse>("/api/files/SQL/count.sql", Ct);

        var response = await client.PutAsJsonAsync("/api/files/SQL/count.sql",
            new SaveFileRequest("select 1;\n", file!.Sha, null), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<SaveFileResponse>(Ct);
        Assert.Equal(InMemoryContentRepository.BlobSha("select 1;\n"), saved!.Sha);
        var commit = Assert.Single(factory.Content.Commits);
        Assert.Equal(saved.CommitSha, commit.Sha);
        Assert.Equal($"https://github.com/owner/content/commit/{commit.Sha}", saved.CommitUrl);
        Assert.Equal("Edit SQL/count.sql", commit.Message);
        Assert.Equal(new CommitIdentity("ada (display)", "ada@example.org"), commit.Author);
    }

    [Fact]
    public async Task Save_UserWithoutEmail_UsesCommitterAddress()
    {
        var response = await factory.SignedIn(email: null).PutAsJsonAsync("/api/files/new.md",
            new SaveFileRequest("x", null, "  My message "), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var commit = Assert.Single(factory.Content.Commits);
        Assert.Equal("My message", commit.Message);
        Assert.Equal(new CommitIdentity("ada (display)", "noreply@snipcache.test"), commit.Author);
    }

    [Fact]
    public async Task Save_WithStaleSha_Is409()
    {
        var response = await factory.SignedIn().PutAsJsonAsync("/api/files/SQL/count.sql",
            new SaveFileRequest("mine", InMemoryContentRepository.BlobSha("what I opened"), null), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.Equal("SQL/count.sql changed on GitHub since it was opened.", problem!.Detail);
        Assert.Empty(factory.Content.Commits);
    }

    [Fact]
    public async Task Save_CreateNewFile_Is201WithLocation()
    {
        var response = await factory.SignedIn().PutAsJsonAsync("/api/files/Shell/Neue Datei.sh",
            new SaveFileRequest("echo hi\n", null, null), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/files/Shell/Neue%20Datei.sh", response.Headers.Location!.OriginalString);
        Assert.Equal("Add Shell/Neue Datei.sh", Assert.Single(factory.Content.Commits).Message);
        Assert.Contains(await factory.Content.GetTreeAsync(Ct), n => n is { Path: "Shell", Type: NodeType.Dir });
    }

    [Fact]
    public async Task Save_CreateOverExistingFile_Is409()
    {
        var response = await factory.SignedIn().PutAsJsonAsync("/api/files/README.md",
            new SaveFileRequest("overwrite", null, null), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/files/.github/workflows/deploy.yml")]
    [InlineData("/api/files/a/b%3F.md")]
    public async Task Save_InvalidPath_Is400(string url)
    {
        var response = await factory.SignedIn().PutAsJsonAsync(url, new SaveFileRequest("x", null, null), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Content.Commits);
    }

    [Fact]
    public async Task Save_MissingContent_Is400()
    {
        var response = await factory.SignedIn().PutAsJsonAsync("/api/files/a.md", new { sha = (string?)null }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Save_TooLarge_Is413()
    {
        var response = await factory.SignedIn().PutAsJsonAsync("/api/files/big.txt",
            new SaveFileRequest(new string('x', (int)ContentLimits.MaxFileBytes + 1), null, null), Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task CreateFolder_AddsReadmeWithTitle()
    {
        var response = await factory.SignedIn().PostAsJsonAsync("/api/folders",
            new CreateFolderRequest("SQL/Postgres", "Postgres\r\nrecipes"), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<SaveFileResponse>(Ct);
        Assert.Equal("SQL/Postgres/README.md", saved!.Path);
        Assert.Equal("# Postgres recipes\n", (await factory.Content.GetFileAsync("SQL/Postgres/README.md", Ct))!.Content);
        Assert.Equal("Add folder SQL/Postgres", Assert.Single(factory.Content.Commits).Message);
    }

    [Fact]
    public async Task CreateFolder_WithoutTitle_UsesFolderName()
    {
        await factory.SignedIn().PostAsJsonAsync("/api/folders", new CreateFolderRequest("Docker/Compose/", null), Ct);

        Assert.Equal("# Compose\n", (await factory.Content.GetFileAsync("Docker/Compose/README.md", Ct))!.Content);
    }

    [Fact]
    public async Task CreateFolder_WithExistingReadme_Is409()
    {
        factory.Content.Seed("SQL/README.md", "# SQL\n");

        var response = await factory.SignedIn().PostAsJsonAsync("/api/folders", new CreateFolderRequest("SQL", null), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateFolder_InvalidPath_Is400()
    {
        var response = await factory.SignedIn().PostAsJsonAsync("/api/folders", new CreateFolderRequest("../x", null), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

// Auth:RequiredRole restricts the editor to users holding the role in the roles claim.
public sealed class RequiredRoleTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly EditorFactory factory = new(requiredRole: "snipcache-editor");

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task UserWithoutRole_Gets403()
    {
        var response = await factory.SignedIn(roles: "other-app").GetAsync("/api/tree", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserWithRole_GetsIn()
    {
        var response = await factory.SignedIn(roles: "other-app,snipcache-editor").GetAsync("/api/tree", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
