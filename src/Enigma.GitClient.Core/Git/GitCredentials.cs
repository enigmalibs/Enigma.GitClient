using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Enigma.GitClient.Core.Security;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// A login git may answer with on one origin: a user name and a token.
/// </summary>
/// <remarks>
/// An origin is a scheme and a server — <c>https://github.com</c>, <c>https://git.example.com:8443</c> —
/// which is exactly what git's credential context carries for an HTTP remote, since git leaves the path
/// out of it by default. Only a value that cannot bend git's credential protocol or its configuration
/// key is accepted, so a stored token with a newline in it can never forge a line of its own.
/// </remarks>
public sealed partial class GitHostCredential
{
    private GitHostCredential(string origin, string userName, SecretString token)
    {
        Origin = origin;
        UserName = userName;
        Token = token;
    }

    /// <summary>Gets the origin the login is for, such as <c>https://github.com</c>.</summary>
    public string Origin { get; }

    /// <summary>Gets the user name git sends with the token.</summary>
    public string UserName { get; }

    /// <summary>Gets the token.</summary>
    public SecretString Token { get; }

    /// <summary>
    /// Builds a login, unless one of its values could not be handed to git safely.
    /// </summary>
    /// <param name="location">Any address on the origin: an instance root or a remote URL.</param>
    /// <param name="userName">The user name git sends with the token.</param>
    /// <param name="token">The token.</param>
    /// <param name="credential">The login, or <see langword="null"/> when it was refused.</param>
    /// <returns>
    /// <see langword="true"/> when the address is HTTP(S) on an ordinary host and neither the user name
    /// nor the token is empty or holds a line break.
    /// </returns>
    public static bool TryCreate(Uri location, string userName, SecretString token, out GitHostCredential? credential)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(token);

        credential = null;

        if (!TryGetOrigin(location, out string? origin) || origin is null
            || !IsSafeValue(userName)
            || !IsSafeValue(token.Reveal()))
        {
            return false;
        }

        credential = new GitHostCredential(origin, userName, token);
        return true;
    }

    /// <summary>
    /// Works out the origin of an HTTP(S) address — its scheme, host and any port that is not the
    /// scheme's default, lower-cased.
    /// </summary>
    /// <param name="location">The address.</param>
    /// <param name="origin">The origin, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the address is absolute, HTTP or HTTPS, and on a host git's
    /// configuration can name.
    /// </returns>
    public static bool TryGetOrigin(Uri location, out string? origin)
    {
        ArgumentNullException.ThrowIfNull(location);

        origin = null;

        if (!location.IsAbsoluteUri
            || (location.Scheme != Uri.UriSchemeHttps && location.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        string host = location.Host.ToLowerInvariant();

        if (!SafeHostPattern().IsMatch(host))
        {
            return false;
        }

        origin = location.IsDefaultPort
            ? $"{location.Scheme}://{host}"
            : $"{location.Scheme}://{host}:{location.Port.ToString(CultureInfo.InvariantCulture)}";

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => $"{UserName} on {Origin}";

    private static bool IsSafeValue(string value)
        => value.Length > 0 && value.IndexOfAny(['\r', '\n', '\0']) < 0;

    // A DNS name or a bracketed IPv6 literal: nothing that could end the configuration key early.
    [GeneratedRegex(@"^(?:[a-z0-9](?:[a-z0-9.\-]*[a-z0-9])?|\[[0-9a-f:.]+\])$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeHostPattern();
}

/// <summary>
/// The logins one git invocation signs in with, as the configuration and environment that hand them
/// over.
/// </summary>
/// <remarks>
/// <para>
/// Each origin gets two configuration overrides, placed before the sub-command:
/// <c>credential.&lt;origin&gt;.helper=</c>, whose empty value empties the helper list so the user's own
/// helpers neither answer first nor store the token, and then a shell helper that answers git's
/// <c>get</c> with the user name and token read from two environment variables — and ignores
/// <c>store</c> and <c>erase</c>.
/// </para>
/// <para>
/// The token never appears in the argument vector, which other users of the machine can read; it lives
/// only in the environment of the one process, which they cannot. Nothing is written to disk or to the
/// repository's configuration. Every git from 2.9 understands the empty helper, and helpers starting with
/// <c>!</c> run through <c>sh</c>, which Git for Windows ships.
/// </para>
/// </remarks>
public sealed class GitCredentials
{
    /// <summary>The prefix of the variables holding the user names, followed by the origin's index.</summary>
    public const string UserNameVariablePrefix = "ENIGMA_GIT_USERNAME_";

    /// <summary>The prefix of the variables holding the tokens, followed by the origin's index.</summary>
    public const string PasswordVariablePrefix = "ENIGMA_GIT_PASSWORD_";

    private readonly List<GitHostCredential> _credentials;

    private GitCredentials(List<GitHostCredential> credentials) => _credentials = credentials;

    /// <summary>Gets the credentials that sign in with nothing: git keeps its own.</summary>
    public static GitCredentials None { get; } = new([]);

    /// <summary>Gets a value indicating whether there is no login to hand over.</summary>
    public bool IsEmpty => _credentials.Count == 0;

    /// <summary>Gets the logins, one per origin.</summary>
    public IReadOnlyList<GitHostCredential> Credentials => _credentials;

    /// <summary>
    /// Gathers logins, keeping the first one given for each origin.
    /// </summary>
    /// <param name="credentials">The logins, in order of preference.</param>
    /// <returns>The credentials, or <see cref="None"/> when there were none.</returns>
    public static GitCredentials For(IEnumerable<GitHostCredential> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        List<GitHostCredential> kept = [];
        HashSet<string> origins = new(StringComparer.Ordinal);

        foreach (GitHostCredential credential in credentials)
        {
            ArgumentNullException.ThrowIfNull(credential);

            if (origins.Add(credential.Origin))
            {
                kept.Add(credential);
            }
        }

        return kept.Count == 0 ? None : new GitCredentials(kept);
    }

    /// <summary>
    /// Builds the configuration overrides — <c>-c</c> and its value, twice per origin — to place before
    /// the sub-command.
    /// </summary>
    /// <returns>The arguments; empty when there is no login.</returns>
    public IReadOnlyList<string> BuildConfigArguments()
    {
        List<string> arguments = [];

        for (int index = 0; index < _credentials.Count; index++)
        {
            string key = $"credential.{_credentials[index].Origin}.helper";

            arguments.Add("-c");
            arguments.Add(key + "=");
            arguments.Add("-c");
            arguments.Add(key + "=" + BuildHelper(index));
        }

        return arguments;
    }

    /// <summary>
    /// Builds the environment variables the helpers read. This is where the tokens are revealed, and it
    /// is only ever handed to the child process.
    /// </summary>
    /// <returns>The variables; empty when there is no login.</returns>
    public IReadOnlyDictionary<string, string> BuildEnvironment()
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal);

        for (int index = 0; index < _credentials.Count; index++)
        {
            string suffix = index.ToString(CultureInfo.InvariantCulture);

            environment[UserNameVariablePrefix + suffix] = _credentials[index].UserName;
            environment[PasswordVariablePrefix + suffix] = _credentials[index].Token.Reveal();
        }

        return environment;
    }

    /// <inheritdoc />
    public override string ToString()
        => IsEmpty ? "no credentials" : string.Join(", ", _credentials);

    /// <summary>
    /// Builds the helper for one origin.
    /// </summary>
    /// <remarks>
    /// git runs it as <c>sh -c '&lt;helper&gt; &lt;operation&gt;'</c>, so the function receives
    /// <c>get</c>, <c>store</c> or <c>erase</c> as <c>$1</c>. <c>printf '%s'</c> rather than
    /// <c>echo</c>, which some shells let interpret a backslash.
    /// </remarks>
    private static string BuildHelper(int index)
    {
        string suffix = index.ToString(CultureInfo.InvariantCulture);

        return "!f() { test \"$1\" = get || exit 0; "
            + "printf 'username=%s\\npassword=%s\\n' "
            + $"\"${UserNameVariablePrefix}{suffix}\" \"${PasswordVariablePrefix}{suffix}\"; }}; f";
    }
}
