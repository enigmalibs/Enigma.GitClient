using System;

namespace Enigma.GitClient.Core.Refs;

/// <summary>
/// A configured remote.
/// </summary>
/// <param name="Name">The remote's name, for example <c>origin</c>.</param>
/// <param name="FetchUrl">The URL fetches read from.</param>
/// <param name="PushUrl">
/// The URL pushes write to. Equal to <paramref name="FetchUrl"/> unless the repository configures a
/// separate <c>pushurl</c>.
/// </param>
public sealed record GitRemote(string Name, string FetchUrl, string PushUrl)
{
    /// <summary>
    /// The conventional name of the default remote.
    /// </summary>
    public const string DefaultName = "origin";

    /// <summary>
    /// Gets a value indicating whether fetches and pushes use different URLs.
    /// </summary>
    public bool HasSeparatePushUrl => !string.Equals(FetchUrl, PushUrl, StringComparison.Ordinal);

    /// <summary>
    /// Gets the host of the fetch URL, for both HTTPS and <c>git@host:path</c> style URLs, or
    /// <see langword="null"/> when the URL is a local path.
    /// </summary>
    public string? Host => ExtractHost(FetchUrl);

    /// <summary>
    /// Extracts the host from a remote URL, understanding both URI-shaped and scp-shaped forms.
    /// </summary>
    /// <param name="url">The remote URL.</param>
    /// <returns>The host, or <see langword="null"/> for a local path.</returns>
    public static string? ExtractHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && !uri.IsFile && uri.Host.Length > 0)
        {
            return uri.Host;
        }

        // scp-like syntax: [user@]host:path — note that a Windows path such as C:\src also matches
        // "something:something", so a single-character prefix is treated as a drive letter.
        int at = url.IndexOf('@');
        int colon = url.IndexOf(':', at + 1);

        if (colon <= 0)
        {
            return null;
        }

        string host = url.Substring(at + 1, colon - at - 1);
        return host.Length > 1 && host.Contains('.', StringComparison.Ordinal) ? host : null;
    }
}
