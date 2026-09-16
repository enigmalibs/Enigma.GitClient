using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Removes credentials from strings before they reach a log, an error message or a completion
/// record. Every git invocation and every failure passes through here.
/// </summary>
public static partial class ArgumentRedactor
{
    /// <summary>
    /// The text substituted for a redacted secret.
    /// </summary>
    public const string Placeholder = "***";

    /// <summary>
    /// Redacts the credentials in a single value.
    /// </summary>
    /// <param name="value">The value to redact.</param>
    /// <returns>The value with any credential replaced by <see cref="Placeholder"/>.</returns>
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        string result = UrlUserInfoPattern().Replace(value, match =>
            $"{match.Groups["scheme"].Value}{match.Groups["user"].Value}:{Placeholder}@");

        result = UrlSingleTokenPattern().Replace(result, match =>
            $"{match.Groups["scheme"].Value}{Placeholder}@");

        result = QueryTokenPattern().Replace(result, match =>
            $"{match.Groups["prefix"].Value}{Placeholder}");

        result = HeaderTokenPattern().Replace(result, match =>
            $"{match.Groups["prefix"].Value}{Placeholder}");

        result = OptionTokenPattern().Replace(result, match =>
            $"{match.Groups["prefix"].Value}{Placeholder}");

        return result;
    }

    /// <summary>
    /// Redacts the credentials in every value of a sequence.
    /// </summary>
    /// <param name="values">The values to redact.</param>
    /// <returns>A new list holding the redacted values.</returns>
    public static IReadOnlyList<string> RedactAll(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        List<string> redacted = [];
        foreach (string value in values)
        {
            redacted.Add(Redact(value));
        }

        return redacted;
    }

    /// <summary>
    /// Joins a redacted argument list into a single loggable command line.
    /// </summary>
    /// <param name="arguments">The arguments to render.</param>
    /// <returns>A space-separated, redacted command line.</returns>
    public static string RedactCommandLine(IEnumerable<string> arguments)
        => string.Join(' ', RedactAll(arguments));

    // scheme://user:secret@host  ->  scheme://user:***@host
    [GeneratedRegex(@"(?<scheme>[a-zA-Z][a-zA-Z0-9+.\-]*://)(?<user>[^/:@\s]+):[^/@\s]*@", RegexOptions.ExplicitCapture)]
    private static partial Regex UrlUserInfoPattern();

    // scheme://secret@host  ->  scheme://***@host  (a bare token with no user name).
    // Deliberately requires at least 20 characters: real tokens are long, whereas the short
    // user names that legitimately appear in a remote URL (ssh://git@host, ssh://hg@host) are
    // not secrets and stay readable in the log.
    [GeneratedRegex(@"(?<scheme>[a-zA-Z][a-zA-Z0-9+.\-]*://)[^/:@\s]{20,}@", RegexOptions.ExplicitCapture)]
    private static partial Regex UrlSingleTokenPattern();

    // ?private_token=... / &access_token=... / ?token=...
    [GeneratedRegex(@"(?<prefix>[?&](?:private_token|access_token|token|api_key)=)[^&\s]+",
        RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase)]
    private static partial Regex QueryTokenPattern();

    // Authorization: Bearer ... / PRIVATE-TOKEN: ... / X-Auth-Token: ...
    [GeneratedRegex(@"(?<prefix>(?:Authorization|PRIVATE-TOKEN|X-Auth-Token)\s*:\s*(?:Bearer\s+|Basic\s+|token\s+)?)\S+",
        RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase)]
    private static partial Regex HeaderTokenPattern();

    // --password=... / --token=... / --access-token=...
    [GeneratedRegex(@"(?<prefix>--(?:password|token|access-token|private-token)=)\S+",
        RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase)]
    private static partial Regex OptionTokenPattern();
}
