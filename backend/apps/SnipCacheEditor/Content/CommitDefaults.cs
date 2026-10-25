using System.Security.Claims;

namespace SnipCacheEditor;

/// <summary>Default commit messages, used when the caller sends none.</summary>
public static class CommitMessages
{
    public static string Edit(string path) => $"Edit {path}";

    public static string Add(string path) => $"Add {path}";

    public static string AddFolder(string path) => $"Add folder {path}";

    public static string BotAdd(string path) => $"Bot: add {path}";

    public static string BotUpdate(string path) => $"Bot: update {path}";

    /// <summary>The trimmed <paramref name="message"/>, or <paramref name="fallback"/> when it is blank.</summary>
    public static string OrDefault(string? message, string fallback) =>
        string.IsNullOrWhiteSpace(message) ? fallback : message.Trim();
}

/// <summary>Maps the signed-in Keycloak user to the commit author.</summary>
public static class CommitAuthors
{
    /// <summary>Author name when the principal carries neither <c>name</c> nor <c>preferred_username</c>.</summary>
    public const string FallbackName = "SnipCache user";

    /// <summary>
    /// Name from <c>name</c>, else <c>preferred_username</c>; email from <c>email</c>, else
    /// <paramref name="fallbackEmail"/> (the committer address), because GitHub requires both.
    /// </summary>
    public static CommitIdentity FromPrincipal(ClaimsPrincipal principal, string fallbackEmail)
    {
        var name = NonBlank(principal.FindFirstValue("name"))
                   ?? NonBlank(principal.FindFirstValue("preferred_username"))
                   ?? FallbackName;
        var email = NonBlank(principal.FindFirstValue("email")) ?? fallbackEmail;
        return new CommitIdentity(name, email);
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
