using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Octokit;
using Octokit.Internal;

namespace SnipCacheEditor.Tests;

// Runs the real Octokit client against canned GitHub responses (FakeGitHubHttp), so the
// tests cover what GitHub actually returns: status codes for conflicts and bad tokens,
// base64 file bodies, the recursive tree, and the request bodies we send.
public class GitHubContentRepositoryTests
{
    private static readonly CommitIdentity Author = new("Ada Lovelace", "ada@example.org");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeGitHubHttp http = new();
    private readonly GitHubContentRepository repository;

    public GitHubContentRepositoryTests()
    {
        var connection = new Connection(
            new Octokit.ProductHeaderValue("SnipCache.Tests"), GitHubClient.GitHubApiUrl,
            new InMemoryCredentialStore(new Credentials("test-token")), http, new SimpleJsonSerializer());
        repository = new GitHubContentRepository(
            new GitHubClient(connection),
            Options.Create(new GitHubOptions { Owner = "owner", Repository = "content", Branch = "main" }),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<GitHubContentRepository>.Instance);
    }

    [Fact]
    public async Task Tree_MapsBlobsAndTrees_HidesDotfilesAndSubmodules_AndIsCached()
    {
        http.Respond(HttpStatusCode.OK, """
            {"sha":"t1","truncated":false,"tree":[
              {"path":"SQL","mode":"040000","type":"tree","sha":"s1"},
              {"path":"README.md","mode":"100644","type":"blob","sha":"s2","size":7},
              {"path":".github","mode":"040000","type":"tree","sha":"s3"},
              {"path":".github/workflows/deploy.yml","mode":"100644","type":"blob","sha":"s4","size":3},
              {"path":"SQL/count.sql","mode":"100644","type":"blob","sha":"s5","size":9},
              {"path":"vendor","mode":"160000","type":"commit","sha":"s6"}
            ]}
            """);

        var tree = await repository.GetTreeAsync(Ct);
        await repository.GetTreeAsync(Ct);

        Assert.Equal(
            [
                new TreeNode("README.md", NodeType.File, 7),
                new TreeNode("SQL", NodeType.Dir, null),
                new TreeNode("SQL/count.sql", NodeType.File, 9),
            ],
            tree);
        var request = Assert.Single(http.Requests);
        Assert.Equal("repos/owner/content/git/trees/main?recursive=1", request.Endpoint.OriginalString);
    }

    [Fact]
    public async Task Write_InvalidatesTheTreeCache()
    {
        http.Respond(HttpStatusCode.OK, """{"sha":"t1","truncated":false,"tree":[]}""");
        await repository.GetTreeAsync(Ct);
        http.Respond(HttpStatusCode.Created, PutResponse("a.md"));
        await repository.PutFileAsync("a.md", "x", sha: null, "Add a.md", Author, Ct);
        http.Respond(HttpStatusCode.OK, """{"sha":"t2","truncated":false,"tree":[]}""");

        await repository.GetTreeAsync(Ct);

        Assert.Equal(3, http.Requests.Count);
    }

    [Fact]
    public async Task GetFile_DecodesBase64Content()
    {
        http.Respond(HttpStatusCode.OK, FileResponse("KI-Chats/Codeblöcke in Markdown.md", "Grüße\n"));

        var file = await repository.GetFileAsync("KI-Chats/Codeblöcke in Markdown.md", Ct);

        Assert.Equal(new ContentFile("KI-Chats/Codeblöcke in Markdown.md", "blobsha", "Grüße\n"), file);
        var request = Assert.Single(http.Requests);
        Assert.Equal("https://api.github.com/repos/owner/content/contents/KI-Chats/Codebl%C3%B6cke%20in%20Markdown.md?ref=main",
            new Uri(GitHubClient.GitHubApiUrl, request.Endpoint).AbsoluteUri);
    }

    [Fact]
    public async Task GetFile_NotFound_ReturnsNull()
    {
        http.Respond(HttpStatusCode.NotFound, """{"message":"Not Found"}""");

        Assert.Null(await repository.GetFileAsync("missing.md", Ct));
    }

    [Fact]
    public async Task GetFile_ForAFolder_ReturnsNull()
    {
        http.Respond(HttpStatusCode.OK, """
            [{"type":"file","name":"x.md","path":"dir/x.md","sha":"a","size":1},
             {"type":"file","name":"y.md","path":"dir/y.md","sha":"b","size":1}]
            """);

        Assert.Null(await repository.GetFileAsync("dir", Ct));
    }

    [Fact]
    public async Task GetFile_AboveOneMegabyte_Throws()
    {
        http.Respond(HttpStatusCode.OK, """
            {"type":"file","encoding":"none","name":"big.sql","path":"big.sql","sha":"a","size":2000000,"content":""}
            """);

        await Assert.ThrowsAsync<ContentTooLargeException>(() => repository.GetFileAsync("big.sql", Ct));
    }

    [Fact]
    public async Task GetFile_NotUtf8_Throws()
    {
        var base64 = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0xFF, 0xFE]);
        http.Respond(HttpStatusCode.OK, $$"""
            {"type":"file","encoding":"base64","name":"x.png","path":"x.png","sha":"a","size":6,"content":"{{base64}}"}
            """);

        await Assert.ThrowsAsync<UnsupportedContentException>(() => repository.GetFileAsync("x.png", Ct));
    }

    [Fact]
    public async Task Create_SendsAuthorAndCommitter_WithoutSha()
    {
        http.Respond(HttpStatusCode.Created, PutResponse("SQL/count.sql"));

        var result = await repository.PutFileAsync("SQL/count.sql", "select 1;", sha: null, "Add SQL/count.sql", Author, Ct);

        Assert.Equal(new SaveResult("SQL/count.sql", "newblob", "commit1",
            "https://github.com/owner/content/commit/commit1", Created: true), result);
        var request = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("repos/owner/content/contents/SQL/count.sql", request.Endpoint.OriginalString);
        var body = Assert.IsType<string>(request.Body);
        Assert.Contains("\"message\":\"Add SQL/count.sql\"", body);
        Assert.Contains("\"branch\":\"main\"", body);
        Assert.Contains("\"author\":{\"name\":\"Ada Lovelace\",\"email\":\"ada@example.org\"", body);
        Assert.Contains("\"committer\":{\"name\":\"SnipCache\",\"email\":\"noreply@snipcache.freeddns.org\"", body);
        Assert.Contains($"\"content\":\"{Convert.ToBase64String(Encoding.UTF8.GetBytes("select 1;"))}\"", body);
        Assert.DoesNotContain("\"sha\"", body);
    }

    [Fact]
    public async Task Update_SendsSha()
    {
        http.Respond(HttpStatusCode.OK, PutResponse("a.md"));

        var result = await repository.PutFileAsync("a.md", "v2", "oldblob", "Edit a.md", Author, Ct);

        Assert.False(result.Created);
        Assert.Contains("\"sha\":\"oldblob\"", Assert.IsType<string>(Assert.Single(http.Requests).Body));
    }

    [Fact]
    public async Task Create_OverExistingFile_IsConflict()
    {
        // GitHub answers a create on an existing path with 422 "sha wasn't supplied".
        http.Respond(HttpStatusCode.UnprocessableEntity, """{"message":"Invalid request.\n\n\"sha\" wasn't supplied."}""");

        var ex = await Assert.ThrowsAsync<ContentConflictException>(() =>
            repository.PutFileAsync("a.md", "x", sha: null, "Add a.md", Author, Ct));
        Assert.Equal("a.md already exists.", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]            // stale SHA
    [InlineData(HttpStatusCode.NotFound)]            // file deleted meanwhile
    [InlineData(HttpStatusCode.UnprocessableEntity)] // SHA of no current file
    public async Task Update_WithStaleSha_IsConflict(HttpStatusCode status)
    {
        http.Respond(status, """{"message":"a.md does not match oldblob"}""");

        var ex = await Assert.ThrowsAsync<ContentConflictException>(() =>
            repository.PutFileAsync("a.md", "x", "oldblob", "Edit a.md", Author, Ct));
        Assert.Equal("a.md changed on GitHub since it was opened.", ex.Message);
    }

    [Fact]
    public async Task BadToken_IsBackendErrorNamingTheToken()
    {
        http.Respond(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""");

        var ex = await Assert.ThrowsAsync<ContentBackendException>(() => repository.GetTreeAsync(Ct));
        Assert.Contains("GitHub:Token", ex.Message);
    }

    [Fact]
    public async Task ServerError_IsBackendError()
    {
        http.Respond(HttpStatusCode.InternalServerError, """{"message":"boom"}""");

        var ex = await Assert.ThrowsAsync<ContentBackendException>(() =>
            repository.PutFileAsync("a.md", "x", "oldblob", "Edit a.md", Author, Ct));
        Assert.Contains("500", ex.Message);
    }

    private static string FileResponse(string path, string content) => $$"""
        {"type":"file","encoding":"base64","name":"{{Path.GetFileName(path)}}","path":"{{path}}","sha":"blobsha",
         "size":{{Encoding.UTF8.GetByteCount(content)}},"content":"{{Convert.ToBase64String(Encoding.UTF8.GetBytes(content))}}\n"}
        """;

    private static string PutResponse(string path) => $$$"""
        {"content":{"type":"file","name":"{{{Path.GetFileName(path)}}}","path":"{{{path}}}","sha":"newblob","size":1},
         "commit":{"sha":"commit1","message":"m"}}
        """;
}

/// <summary>Octokit HTTP seam that records requests and answers with the next canned response.</summary>
internal sealed class FakeGitHubHttp : IHttpClient
{
    private (HttpStatusCode Status, string Body) next = (HttpStatusCode.InternalServerError, "{}");

    public List<IRequest> Requests { get; } = [];

    public void Respond(HttpStatusCode status, string body) => next = (status, body);

    public Task<IResponse> Send(IRequest request, CancellationToken cancellationToken, Func<object, object>? preprocessResponseBody = null)
    {
        Requests.Add(request);
        return Task.FromResult<IResponse>(new CannedResponse(next.Status, next.Body));
    }

    public void SetRequestTimeout(TimeSpan timeout)
    {
    }

    public void Dispose()
    {
    }

    private sealed record CannedResponse(HttpStatusCode StatusCode, object Body) : IResponse
    {
        public IReadOnlyDictionary<string, string> Headers { get; } = new Dictionary<string, string>();

        public ApiInfo ApiInfo { get; } = new(
            new Dictionary<string, Uri>(), [], [], "etag", new RateLimit(5000, 4999, 0), TimeSpan.Zero);

        public string ContentType => "application/json";
    }
}
