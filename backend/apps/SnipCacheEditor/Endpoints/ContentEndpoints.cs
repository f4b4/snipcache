using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;

namespace SnipCacheEditor;

/// <summary>
/// The editor's content API: the repository tree, reading and saving files, creating
/// folders. Mapped on the authorized <c>/api</c> group. Conflicts, oversized and binary
/// files and GitHub failures surface as problem responses via <see cref="ContentExceptionHandler"/>.
/// </summary>
internal static class ContentEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/tree", async (IContentRepository repository, CancellationToken ct) =>
                TypedResults.Ok(new TreeResponse(await repository.GetTreeAsync(ct))))
            .WithName("GetTree")
            .ProducesProblem(StatusCodes.Status502BadGateway);

        api.MapGet("/files/{**path}", GetFile)
            .WithName("GetFile")
            .Produces<FileResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        api.MapPut("/files/{**path}", SaveFile)
            .WithName("SaveFile")
            .Produces<SaveFileResponse>()
            .Produces<SaveFileResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        api.MapPost("/folders", CreateFolder)
            .WithName("CreateFolder")
            .Produces<SaveFileResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }

    private static async Task<IResult> GetFile(string? path, IContentRepository repository, CancellationToken ct)
    {
        if (!ContentPath.TryNormalize(path, out var normalized, out var error))
            return PathProblem(error);

        var file = await repository.GetFileAsync(normalized, ct);
        return file is null
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: $"{normalized} does not exist.")
            : TypedResults.Ok(new FileResponse(file.Path, file.Sha, file.Content, FileResponse.Utf8));
    }

    private static async Task<IResult> SaveFile(
        string? path, SaveFileRequest request, ClaimsPrincipal user,
        IContentRepository repository, IOptions<GitHubOptions> github, CancellationToken ct)
    {
        // Without a SHA the file is created, so its name must follow the strict pattern.
        var sha = string.IsNullOrWhiteSpace(request.Sha) ? null : request.Sha.Trim();
        if (!ContentPath.TryNormalize(path, out var normalized, out var error, newName: sha is null))
            return PathProblem(error);
        if (request.Content is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["content"] = ["The content is required."] });

        var size = Encoding.UTF8.GetByteCount(request.Content);
        if (size > ContentLimits.MaxFileBytes)
            throw new ContentTooLargeException(normalized, size);

        var message = CommitMessages.OrDefault(request.Message,
            sha is null ? CommitMessages.Add(normalized) : CommitMessages.Edit(normalized));
        var author = CommitAuthors.FromPrincipal(user, github.Value.CommitterEmail);

        var result = await repository.PutFileAsync(normalized, request.Content, sha, message, author, ct);
        var response = SaveFileResponse.From(result);
        return result.Created
            ? TypedResults.Created(FileLocation(result.Path), response)
            : TypedResults.Ok(response);
    }

    private static async Task<IResult> CreateFolder(
        CreateFolderRequest request, ClaimsPrincipal user,
        IContentRepository repository, IOptions<GitHubOptions> github, CancellationToken ct)
    {
        if (!ContentPath.TryNormalize(request.Path, out var folder, out var error, newName: true))
            return PathProblem(error);

        // Git has no empty folders: the folder is its README.md, which becomes the folder's
        // index page on the site.
        var title = string.Join(' ', (request.Title ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (title.Length == 0) title = ContentPath.Name(folder);

        var author = CommitAuthors.FromPrincipal(user, github.Value.CommitterEmail);
        var result = await repository.PutFileAsync(
            $"{folder}/README.md", $"# {title}\n", sha: null, CommitMessages.AddFolder(folder), author, ct);
        return TypedResults.Created(FileLocation(result.Path), SaveFileResponse.From(result));
    }

    private static IResult PathProblem(string error) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["path"] = [error] });

    private static string FileLocation(string path) =>
        "/api/files/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}

/// <summary>The flat list of visible folders and files, sorted by path.</summary>
public sealed record TreeResponse(IReadOnlyList<TreeNode> Nodes);

/// <summary>A file opened for editing.</summary>
/// <param name="Sha">Blob SHA to send back with <see cref="SaveFileRequest.Sha"/>.</param>
/// <param name="Encoding">Always <c>utf-8</c>.</param>
public sealed record FileResponse(string Path, string Sha, string Content, string Encoding)
{
    public const string Utf8 = "utf-8";
}

/// <summary>Body of <c>PUT /api/files/{path}</c>.</summary>
/// <param name="Content">The full new file content.</param>
/// <param name="Sha">SHA of the version that was edited; omit to create a new file.</param>
/// <param name="Message">Commit message; defaults to <c>Edit &lt;path&gt;</c> or <c>Add &lt;path&gt;</c>.</param>
public sealed record SaveFileRequest(string Content, string? Sha, string? Message);

/// <summary>Body of <c>POST /api/folders</c>.</summary>
/// <param name="Path">Folder path, e.g. <c>SQL/Postgres</c>.</param>
/// <param name="Title">H1 of the folder's README.md; defaults to the folder name.</param>
public sealed record CreateFolderRequest(string Path, string? Title);

/// <summary>Result of a save: the file's new SHA and the commit it is in.</summary>
public sealed record SaveFileResponse(string Path, string Sha, string CommitSha, string CommitUrl)
{
    public static SaveFileResponse From(SaveResult result) =>
        new(result.Path, result.Sha, result.CommitSha, result.CommitUrl);
}
