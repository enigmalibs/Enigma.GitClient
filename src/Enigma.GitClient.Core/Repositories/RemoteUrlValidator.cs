using System;
using System.IO;

namespace Enigma.GitClient.Core.Repositories;

/// <summary>
/// Why a clone URL was rejected.
/// </summary>
/// <param name="IsValid">Whether the URL is one git can clone from.</param>
/// <param name="Message">A sentence explaining the rejection, empty when valid.</param>
/// <param name="NormalisedUrl">The URL with surrounding whitespace removed.</param>
public sealed record RemoteUrlValidation(bool IsValid, string Message, string NormalisedUrl)
{
    /// <summary>
    /// Creates a successful validation.
    /// </summary>
    /// <param name="url">The normalised URL.</param>
    /// <returns>The validation.</returns>
    public static RemoteUrlValidation Valid(string url) => new(true, string.Empty, url);

    /// <summary>
    /// Creates a failed validation.
    /// </summary>
    /// <param name="message">Why the URL was rejected.</param>
    /// <param name="url">The normalised URL.</param>
    /// <returns>The validation.</returns>
    public static RemoteUrlValidation Invalid(string message, string url) => new(false, message, url);
}

/// <summary>
/// Checks that a clone URL is a shape git understands, before a process is started for it.
/// </summary>
/// <remarks>
/// <para>
/// This is a usability check, not a security boundary — arguments are passed to git as a vector, so
/// nothing a user types can be interpreted as anything but a URL. Its job is to turn a typo into a
/// sentence rather than into git's exit code 128.
/// </para>
/// <para>
/// The order of the checks matters and is not the obvious one. <c>Uri.TryCreate</c> cannot be asked
/// first: a URI scheme may legally contain dots, so <c>gitlab.example.com:group/project.git</c>
/// parses as a URI with the scheme <c>gitlab.example.com</c> rather than as git's scp syntax; and on
/// Unix an absolute path parses as a <c>file:</c> URI, so <c>/tmp/does-not-exist</c> would be
/// accepted without ever checking whether it exists.
/// </para>
/// </remarks>
public static class RemoteUrlValidator
{
    private static readonly string[] SupportedSchemes =
    [
        "https", "http", "ssh", "git", "ftp", "ftps",
    ];

    /// <summary>
    /// Validates a clone URL.
    /// </summary>
    /// <param name="url">What the user typed.</param>
    /// <returns>The validation result.</returns>
    public static RemoteUrlValidation Validate(string? url)
    {
        string trimmed = (url ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return RemoteUrlValidation.Invalid("Enter a repository URL or a local path.", trimmed);
        }

        if (trimmed.StartsWith('-'))
        {
            // Not a security issue — arguments never reach a shell — but git would read it as an
            // option and fail confusingly.
            return RemoteUrlValidation.Invalid("A repository URL cannot start with '-'.", trimmed);
        }

        if (LooksLikeLocalPath(trimmed))
        {
            return ValidateLocalPath(trimmed);
        }

        if (IsScpLike(trimmed))
        {
            return RemoteUrlValidation.Valid(trimmed);
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            foreach (string scheme in SupportedSchemes)
            {
                if (string.Equals(uri.Scheme, scheme, StringComparison.OrdinalIgnoreCase))
                {
                    return RemoteUrlValidation.Valid(trimmed);
                }
            }

            if (string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
            {
                // A file: URL still names a directory on this machine, so it is held to the same
                // "does it exist?" standard as a bare path.
                return Directory.Exists(uri.LocalPath)
                    ? RemoteUrlValidation.Valid(trimmed)
                    : RemoteUrlValidation.Invalid($"'{uri.LocalPath}' does not exist.", trimmed);
            }

            return RemoteUrlValidation.Invalid(
                $"'{uri.Scheme}' is not a protocol git can clone from. Use https, ssh, git, file, or a local path.",
                trimmed);
        }

        return ValidateLocalPath(trimmed);
    }

    /// <summary>
    /// Checks for git's scp-like syntax, <c>[user@]host:path</c>.
    /// </summary>
    /// <param name="value">The candidate URL.</param>
    /// <returns><see langword="true"/> when the value is scp-shaped.</returns>
    public static bool IsScpLike(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // Anything carrying a scheme separator is a URL, not scp syntax.
        if (value.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        int colon = value.IndexOf(':');

        if (colon <= 0 || colon == value.Length - 1)
        {
            return false;
        }

        // A single letter before the colon is a Windows drive, not a host.
        if (colon == 1)
        {
            return false;
        }

        string host = value.Substring(0, colon);
        int at = host.IndexOf('@');

        if (at >= 0)
        {
            host = host.Substring(at + 1);
        }

        return host.Length > 0 &&
               !host.Contains('/', StringComparison.Ordinal) &&
               !host.Contains('\\', StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks whether a value is written as a filesystem path rather than as a URL.
    /// </summary>
    /// <param name="value">The candidate.</param>
    /// <returns><see langword="true"/> when it looks like a path.</returns>
    public static bool LooksLikeLocalPath(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length == 0)
        {
            return false;
        }

        if (value[0] is '/' or '~' or '.')
        {
            return true;
        }

        // Windows: a drive-qualified path or a UNC share.
        return (value.Length >= 2 && value[1] == ':' && char.IsAsciiLetter(value[0])) ||
               value.StartsWith(@"\\", StringComparison.Ordinal);
    }

    private static RemoteUrlValidation ValidateLocalPath(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);

            return Directory.Exists(full)
                ? RemoteUrlValidation.Valid(path)
                : RemoteUrlValidation.Invalid(
                    $"'{path}' is not a URL git understands, and no such directory exists.",
                    path);
        }
        catch (ArgumentException)
        {
            return RemoteUrlValidation.Invalid($"'{path}' is not a usable repository URL or path.", path);
        }
        catch (NotSupportedException)
        {
            return RemoteUrlValidation.Invalid($"'{path}' is not a usable repository URL or path.", path);
        }
        catch (PathTooLongException)
        {
            return RemoteUrlValidation.Invalid("That path is too long.", path);
        }
    }
}
