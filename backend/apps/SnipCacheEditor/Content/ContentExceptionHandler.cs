using Microsoft.AspNetCore.Diagnostics;

namespace SnipCacheEditor;

/// <summary>
/// Turns <see cref="ContentException"/>s into problem responses (409, 413, 415, 502) so the
/// endpoints stay free of try/catch. Other exceptions fall through to the default handling.
/// </summary>
internal sealed class ContentExceptionHandler(IProblemDetailsService problemDetails, ILogger<ContentExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            ContentConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            ContentTooLargeException => (StatusCodes.Status413PayloadTooLarge, "File too large"),
            UnsupportedContentException => (StatusCodes.Status415UnsupportedMediaType, "Not a text file"),
            ContentBackendException => (StatusCodes.Status502BadGateway, "GitHub request failed"),
            _ => (0, null),
        };
        if (status == 0) return false;

        if (exception is ContentBackendException)
            logger.LogWarning(exception, "GitHub request failed: {Message}", exception.Message);

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Detail = exception.Message },
        });
    }
}
