using Microsoft.Extensions.Options;

namespace SnipCacheEditor.Tests;

// The fake stands in for GitHub in the endpoint tests and in local development, so its
// semantics (blob SHAs, create/update conflicts, implied folders) must match GitHub's.
public class InMemoryContentRepositoryTests
{
    private static readonly CommitIdentity Author = new("Ada", "ada@example.org");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static InMemoryContentRepository NewRepository() =>
        new(Options.Create(new GitHubOptions { Owner = "owner", Repository = "content" }));

    [Theory]
    [InlineData("", "e69de29bb2d1d6434b8b29ae775ad8c2e48c5391")]
    [InlineData("hello\n", "ce013625030ba8dba906f756967f9e9ca394464a")]
    public void BlobSha_MatchesGit(string content, string sha) =>
        Assert.Equal(sha, InMemoryContentRepository.BlobSha(content));

    [Fact]
    public async Task Tree_ListsImpliedFoldersAndHidesDotfiles()
    {
        var repository = NewRepository();
        repository.Seed("README.md", "# Home\n");
        repository.Seed("SQL/Postgres/count.sql", "select 1;");
        repository.Seed(".github/workflows/deploy.yml", "name: Deploy SnipCache");

        var tree = await repository.GetTreeAsync(Ct);

        Assert.Equal(
            [
                new TreeNode("README.md", NodeType.File, 7),
                new TreeNode("SQL", NodeType.Dir, null),
                new TreeNode("SQL/Postgres", NodeType.Dir, null),
                new TreeNode("SQL/Postgres/count.sql", NodeType.File, 9),
            ],
            tree);
    }

    [Fact]
    public async Task GetFile_ReturnsContentAndSha_OrNullForFoldersAndMissingFiles()
    {
        var repository = NewRepository();
        repository.Seed("a/b.md", "hello\n");

        var file = await repository.GetFileAsync("a/b.md", Ct);

        Assert.Equal(new ContentFile("a/b.md", "ce013625030ba8dba906f756967f9e9ca394464a", "hello\n"), file);
        Assert.Null(await repository.GetFileAsync("a", Ct));
        Assert.Null(await repository.GetFileAsync("missing.md", Ct));
    }

    [Fact]
    public async Task GetFile_AboveOneMegabyte_Throws()
    {
        var repository = NewRepository();
        repository.Seed("big.txt", new string('x', (int)ContentLimits.MaxFileBytes + 1));

        await Assert.ThrowsAsync<ContentTooLargeException>(() => repository.GetFileAsync("big.txt", Ct));
    }

    [Fact]
    public async Task Put_CreateThenUpdate_RecordsCommits()
    {
        var repository = NewRepository();

        var created = await repository.PutFileAsync("a.md", "v1", sha: null, "Add a.md", Author, Ct);
        var updated = await repository.PutFileAsync("a.md", "v2", created.Sha, "Edit a.md", Author, Ct);

        Assert.True(created.Created);
        Assert.False(updated.Created);
        Assert.Equal(InMemoryContentRepository.BlobSha("v2"), updated.Sha);
        Assert.Equal($"https://github.com/owner/content/commit/{updated.CommitSha}", updated.CommitUrl);
        Assert.Equal(["Add a.md", "Edit a.md"], repository.Commits.Select(c => c.Message));
        Assert.All(repository.Commits, c => Assert.Equal(Author, c.Author));
    }

    [Fact]
    public async Task Put_CreateOverExistingFile_Conflicts()
    {
        var repository = NewRepository();
        repository.Seed("a.md", "v1");

        await Assert.ThrowsAsync<ContentConflictException>(() =>
            repository.PutFileAsync("a.md", "v2", sha: null, "Add a.md", Author, Ct));
    }

    [Theory]
    [InlineData("a.md")]       // stale SHA
    [InlineData("deleted.md")] // file vanished
    public async Task Put_UpdateWithStaleSha_Conflicts(string path)
    {
        var repository = NewRepository();
        repository.Seed("a.md", "changed elsewhere");

        await Assert.ThrowsAsync<ContentConflictException>(() =>
            repository.PutFileAsync(path, "mine", InMemoryContentRepository.BlobSha("original"), "Edit", Author, Ct));
        Assert.Empty(repository.Commits);
    }

    [Theory]
    [InlineData("a.md/b.md")] // below a file
    [InlineData("dir")]       // over a folder
    public async Task Put_ClashingWithFileOrFolder_Conflicts(string path)
    {
        var repository = NewRepository();
        repository.Seed("a.md", "file");
        repository.Seed("dir/x.md", "x");

        await Assert.ThrowsAsync<ContentConflictException>(() =>
            repository.PutFileAsync(path, "content", sha: null, "Add", Author, Ct));
    }

    [Fact]
    public async Task SeedFromDirectory_SkipsDotfilesAndBinaries()
    {
        var root = Directory.CreateTempSubdirectory("snipcache-seed-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "SQL"));
            Directory.CreateDirectory(Path.Combine(root, ".github"));
            File.WriteAllText(Path.Combine(root, "SQL", "count.sql"), "select 1;");
            File.WriteAllText(Path.Combine(root, ".github", "x.yml"), "x");
            File.WriteAllBytes(Path.Combine(root, "image.png"), [0x89, 0x50, 0xFF, 0xFE, 0x00]);
            var repository = NewRepository();

            repository.SeedFromDirectory(root);

            var paths = (await repository.GetTreeAsync(Ct)).Select(n => n.Path);
            Assert.Equal(["SQL", "SQL/count.sql"], paths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
