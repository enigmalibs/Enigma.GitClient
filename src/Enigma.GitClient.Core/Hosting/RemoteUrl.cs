using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Enigma.GitClient.Core.Hosting;

/// <summary>
/// A git remote address, parsed far enough to say which host it points at and what it points at
/// there.
/// </summary>
/// <remarks>
/// <para>
/// git accepts four shapes for the same remote — <c>https://host/owner/repo.git</c>,
/// <c>ssh://git@host:22/owner/repo.git</c>, the scp-like <c>git@host:owner/repo.git</c>, and
/// <c>git://host/owner/repo</c> — and a hosting integration has to recognise its own repositories
/// whichever one the user cloned with.
/// </para>
/// <para>
/// Parsing never throws for a value that is simply not a remote URL: a local path is an answer of
/// "no host", not a failure. Any credentials in the address are dropped rather than carried around.
/// </para>
/// </remarks>
public sealed partial class RemoteUrl
{
    private RemoteUrl(string original, string host, int port, string path, bool isSsh, string? userName)
    {
        Original = original;
        Host = host;
        Port = port;
        Path = path;
        IsSsh = isSsh;
        UserName = userName;
        Segments = Split(path);
    }

    /// <summary>Gets the address exactly as git holds it, minus nothing.</summary>
    public string Original { get; }

    /// <summary>Gets the host name, lower-cased and without any credentials.</summary>
    public string Host { get; }

    /// <summary>Gets the port, or zero when the address did not name one.</summary>
    public int Port { get; }

    /// <summary>
    /// Gets the path on the host, without a leading slash and without a trailing <c>.git</c>.
    /// </summary>
    public string Path { get; }

    /// <summary>Gets a value indicating whether the address is an SSH one.</summary>
    public bool IsSsh { get; }

    /// <summary>
    /// Gets the user name the address carried, normally <c>git</c> for an SSH remote. A password or
    /// token is never kept.
    /// </summary>
    public string? UserName { get; }

    /// <summary>Gets <see cref="Path"/> split on slashes.</summary>
    public IReadOnlyList<string> Segments { get; }

    /// <summary>Gets the last path segment, which is normally the repository's name.</summary>
    public string Name => Segments.Count > 0 ? Segments[^1] : string.Empty;

    /// <summary>
    /// Gets the segment before the name, which is the owner on the hosts that have one.
    /// </summary>
    public string Owner => Segments.Count > 1 ? Segments[^2] : string.Empty;

    /// <summary>
    /// Answers whether the address points at a host.
    /// </summary>
    /// <param name="host">The host name to compare with, case-insensitively.</param>
    /// <returns><see langword="true"/> when the hosts are the same.</returns>
    public bool IsHost(string host)
        => !string.IsNullOrEmpty(host) && string.Equals(Host, host, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Answers whether the address points at a host or at a sub-domain of it.
    /// </summary>
    /// <param name="domain">The domain to compare with, case-insensitively.</param>
    /// <returns><see langword="true"/> when the host is that domain or sits under it.</returns>
    public bool IsUnder(string domain)
    {
        if (string.IsNullOrEmpty(domain))
        {
            return false;
        }

        return IsHost(domain)
            || Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses a remote address.
    /// </summary>
    /// <param name="value">The address.</param>
    /// <param name="remote">The parsed address, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the value named a host.</returns>
    public static bool TryParse(string? value, out RemoteUrl? remote)
    {
        remote = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();

        if (SchemePattern().IsMatch(trimmed))
        {
            return TryParseUri(trimmed, out remote);
        }

        Match scp = ScpPattern().Match(trimmed);

        if (!scp.Success)
        {
            // A local path, a Windows drive, or something that is not an address at all.
            return false;
        }

        string host = scp.Groups["host"].Value;

        // A single letter before the colon is a Windows drive, not a host.
        if (host.Length <= 1)
        {
            return false;
        }

        remote = new RemoteUrl(
            trimmed,
            host.ToLowerInvariant(),
            0,
            Normalise(scp.Groups["path"].Value),
            true,
            NullIfEmpty(scp.Groups["user"].Value));

        return true;
    }

    /// <summary>
    /// Parses a remote address, or throws.
    /// </summary>
    /// <param name="value">The address.</param>
    /// <returns>The parsed address.</returns>
    /// <exception cref="FormatException">The value does not name a host.</exception>
    public static RemoteUrl Parse(string value)
        => TryParse(value, out RemoteUrl? remote) && remote is not null
            ? remote
            : throw new FormatException($"'{value}' is not a remote URL this client recognises.");

    /// <inheritdoc />
    public override string ToString() => Path.Length > 0 ? $"{Host}/{Path}" : Host;

    private static bool TryParseUri(string value, out RemoteUrl? remote)
    {
        remote = null;

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Host.Length == 0)
        {
            return false;
        }

        string? user = null;

        if (uri.UserInfo.Length > 0)
        {
            int colon = uri.UserInfo.IndexOf(':', StringComparison.Ordinal);

            // Anything after the colon is a password or a token, and is dropped here rather than
            // carried through the application waiting to be logged.
            user = NullIfEmpty(colon < 0 ? uri.UserInfo : uri.UserInfo[..colon]);
        }

        bool isSsh = uri.Scheme.Equals("ssh", StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("git+ssh", StringComparison.OrdinalIgnoreCase);

        remote = new RemoteUrl(
            value,
            uri.Host.ToLowerInvariant(),
            uri.IsDefaultPort ? 0 : uri.Port,
            Normalise(uri.AbsolutePath),
            isSsh,
            user);

        return true;
    }

    private static string Normalise(string path)
    {
        string trimmed = path.Replace('\\', '/').Trim('/');

        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4].TrimEnd('/');
        }

        return Uri.UnescapeDataString(trimmed);
    }

    private static IReadOnlyList<string> Split(string path)
    {
        if (path.Length == 0)
        {
            return [];
        }

        List<string> segments = [];

        foreach (string segment in path.Split('/'))
        {
            if (segment.Length > 0)
            {
                segments.Add(segment);
            }
        }

        return segments;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*://", RegexOptions.ExplicitCapture)]
    private static partial Regex SchemePattern();

    // git@host:owner/repo.git — the scp-like form, which has no scheme and one colon.
    [GeneratedRegex(@"^(?:(?<user>[^@/\\:]+)@)?(?<host>[^:/\\@]+):(?<path>[^\s].*)$", RegexOptions.ExplicitCapture)]
    private static partial Regex ScpPattern();
}
