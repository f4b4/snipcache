using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;

namespace SnipCacheEditor;

/// <summary>
/// The bot API: save a snippet with an API key (<see cref="BotAuthentication"/>). Lives under
/// <c>/bot</c>, outside the CSRF guard of the cookie-authenticated <c>/api</c>, and commits
/// through the same <see cref="IContentRepository"/> as the editor.
/// </summary>
internal static class BotEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var bot = app.MapGroup("/bot")
            .RequireAuthorization(BotAuthentication.PolicyName)
            .WithTags("Bot")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        bot.MapPost("/snippets", SaveSnippet)
            .WithName("SaveSnippet")
            .Produces<SaveSnippetResponse>()
            .Produces<SaveSnippetResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }

    private static async Task<IResult> SaveSnippet(
        SaveSnippetRequest request, ClaimsPrincipal user, IContentRepository repository,
        IOptions<GitHubOptions> github, IOptions<SiteOptions> site, CancellationToken ct)
    {
        if (!TryCombine(request.Folder, request.FileName, out var path, out var field, out var error))
            return Problem(field, error);
        var segments = path.Split('/');
        if (PageSlugs.PageSlug(path) is not { } slug)
            return Problem(segments[..^1].Any(s => PageSlugs.SlugifySegment(s) is null) ? "folder" : "fileName",
                "Every name needs at least one letter or digit.");
        if (request.Content is null)
            return Problem("content", "The content is required.");

        var size = Encoding.UTF8.GetByteCount(request.Content);
        if (size > ContentLimits.MaxFileBytes)
            throw new ContentTooLargeException(path, size);

        // Names that already exist are kept as they are (content added through git may use
        // any name); only the folders and the file the bot creates follow the strict pattern.
        var tree = await repository.GetTreeAsync(ct);
        var types = tree.ToDictionary(node => node.Path, node => node.Type, StringComparer.Ordinal);
        for (var i = 0; i < segments.Length; i++)
        {
            var isFile = i == segments.Length - 1;
            var prefix = string.Join('/', segments[..(i + 1)]);
            if (types.TryGetValue(prefix, out var type))
            {
                if (!isFile && type == NodeType.File)
                    return Conflict($"{prefix} is a file, not a folder.");
                if (isFile && type == NodeType.Dir)
                    return Conflict($"{path} is a folder.");
            }
            else if (!ContentPath.IsValidNewName(segments[i], out error))
            {
                return Problem(isFile ? "fileName" : "folder", error);
            }
        }

        // Read directly rather than from the (cached) tree: it is fresh and has the SHA.
        var existing = await repository.GetFileAsync(path, ct);
        if (existing is not null && !request.Overwrite)
            return Conflict($"{path} already exists; set overwrite to replace it.");

        if (existing is null)
        {
            // Two files with one page address fail the site build (same check as the SPA).
            var clash = tree.FirstOrDefault(node =>
                node.Type == NodeType.File && node.Path != path && PageSlugs.PageSlug(node.Path) == slug);
            if (clash is not null)
                return Conflict($"The page of {path} would get the same address as {clash.Path}.");
        }

        var message = CommitMessages.OrDefault(request.Message,
            existing is null ? CommitMessages.BotAdd(path) : CommitMessages.BotUpdate(path));
        var author = CommitAuthors.FromPrincipal(user, github.Value.CommitterEmail);
        var result = await repository.PutFileAsync(path, request.Content, existing?.Sha, message, author, ct);

        var pageUrl = PageSlugs.PageUrl(site.Value.BaseUrl, path)!;
        var response = new SaveSnippetResponse(result.Path, result.Sha, result.CommitUrl, pageUrl);
        return result.Created ? TypedResults.Created(pageUrl, response) : TypedResults.Ok(response);
    }

    // Validates folder and file name separately so the problem names the offending field,
    // then the joined path (length limit).
    private static bool TryCombine(
        string? folder, string? fileName, out string path, out string field, out string error)
    {
        path = field = error = string.Empty;

        var folderPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(folder) && !ContentPath.TryNormalize(folder, out folderPath, out var folderError))
        {
            (field, error) = ("folder", folderError);
            return false;
        }

        field = "fileName";
        if (!ContentPath.TryNormalize(fileName, out var name, out var nameError))
        {
            error = nameError;
            return false;
        }
        if (name.Contains('/'))
        {
            error = "The file name must not contain a folder; put that into folder.";
            return false;
        }

        if (!ContentPath.TryNormalize(folderPath.Length == 0 ? name : $"{folderPath}/{name}", out var joined, out var pathError))
        {
            error = pathError;
            return false;
        }
        path = joined;
        return true;
    }

    private static IResult Problem(string field, string error) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [error] });

    private static IResult Conflict(string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: detail);
}

/// <summary>Body of <c>POST /bot/snippets</c>.</summary>
/// <param name="FileName">File name with extension, e.g. <c>Docker cleanup.md</c>. Only <c>.md</c> is rendered as Markdown.</param>
/// <param name="Content">The full file content (UTF-8 text, at most 1 MB).</param>
/// <param name="Folder">Folder path, e.g. <c>KI-Chats</c> or <c>SQL/Postgres</c>; empty for the repository root. Missing folders are created.</param>
/// <param name="Message">Commit message; defaults to <c>Bot: add &lt;path&gt;</c> or <c>Bot: update &lt;path&gt;</c>.</param>
/// <param name="Overwrite">Replace the file if it exists; otherwise an existing file is a 409.</param>
public sealed record SaveSnippetRequest(
    string FileName, string Content, string? Folder = null, string? Message = null, bool Overwrite = false);

/// <summary>Result of a bot save.</summary>
/// <param name="Path">Repository path of the saved file.</param>
/// <param name="Sha">New blob SHA of the file.</param>
/// <param name="CommitUrl">Web URL of the commit.</param>
/// <param name="PageUrl">Where the page appears on the site once "Deploy SnipCache" has run (one to two minutes).</param>
public sealed record SaveSnippetResponse(string Path, string Sha, string CommitUrl, string PageUrl);
