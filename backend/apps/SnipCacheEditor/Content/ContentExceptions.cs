namespace SnipCacheEditor;

/// <summary>
/// Base class of the content errors that <see cref="ContentExceptionHandler"/> turns into
/// problem responses. The message is shown to the user.
/// </summary>
public abstract class ContentException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The write does not match the repository state: the file exists although it was to be
/// created, or it changed (or vanished) since it was read. HTTP 409.
/// </summary>
public sealed class ContentConflictException(string message, Exception? inner = null) : ContentException(message, inner);

/// <summary>The file is larger than <see cref="ContentLimits.MaxFileBytes"/>. HTTP 413.</summary>
public sealed class ContentTooLargeException(string path, long size)
    : ContentException($"{path} has {size:N0} bytes; files above {ContentLimits.MaxFileBytes:N0} bytes cannot be edited.");

/// <summary>The file is not valid UTF-8 text. HTTP 415.</summary>
public sealed class UnsupportedContentException(string path)
    : ContentException($"{path} is not a UTF-8 text file.");

/// <summary>GitHub rejected or failed the request (token, rate limit, outage). HTTP 502.</summary>
public sealed class ContentBackendException(string message, Exception? inner = null) : ContentException(message, inner);
