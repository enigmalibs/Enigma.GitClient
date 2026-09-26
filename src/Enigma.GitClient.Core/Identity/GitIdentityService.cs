using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Identity;

/// <summary>
/// The git configuration scopes an identity is read from and written to.
/// </summary>
/// <remarks>
/// The system scope is left out on purpose: it belongs to whoever administers the machine, and
/// writing it needs rights the client should never ask for.
/// </remarks>
public enum GitConfigScope
{
    /// <summary>The user's own configuration, <c>git config --global</c>.</summary>
    Global,

    /// <summary>One repository's configuration, <c>git config --local</c>.</summary>
    Local,
}

/// <summary>
/// Reads and writes the name and email git records on a commit — the user's global identity, and a
/// repository's own.
/// </summary>
/// <remarks>
/// git does the file work: it knows where the global file is (<c>GIT_CONFIG_GLOBAL</c>, the XDG
/// location, <c>~/.gitconfig</c>), it keeps the rest of the file as it was, and it locks the file
/// while it writes. This service only decides what to ask for.
/// </remarks>
public interface IGitIdentityService
{
    /// <summary>
    /// Reads the global identity.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The identity, with empty values for what is not set.</returns>
    Task<GitIdentity> GetGlobalAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the global identity.
    /// </summary>
    /// <param name="identity">The identity; both values are required and are trimmed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once git has written both values.</returns>
    /// <exception cref="ArgumentException">The identity is incomplete or invalid; nothing was written.</exception>
    Task SetGlobalAsync(GitIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a repository's own identity — only what its local configuration sets, not what it
    /// inherits.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The identity, with empty values for what is not set.</returns>
    Task<GitIdentity> GetLocalAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a repository's own identity, which its commits then use whatever the global one is.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="identity">The identity; both values are required and are trimmed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once git has written both values.</returns>
    /// <exception cref="ArgumentException">The identity is incomplete or invalid; nothing was written.</exception>
    Task SetLocalAsync(RepositoryHandle repository, GitIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a repository's own identity, so its commits use the global one again.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once neither value is set locally; removing nothing succeeds.</returns>
    Task RemoveLocalAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IGitIdentityService"/>, driving <c>git config</c>.
/// </summary>
public sealed class GitIdentityService : IGitIdentityService
{
    /// <summary>The configuration key holding the name.</summary>
    public const string NameKey = "user.name";

    /// <summary>The configuration key holding the email.</summary>
    public const string EmailKey = "user.email";

    /// <summary>
    /// The pattern a read asks for: the two keys and nothing else — not <c>user.signingkey</c>, not
    /// <c>user.useConfigOnly</c>.
    /// </summary>
    public const string KeyPattern = @"^user\.(name|email)$";

    /// <summary>Both keys, in the order they are written and removed.</summary>
    private static readonly string[] Keys = [NameKey, EmailKey];

    /// <summary>What <c>git config --get-regexp</c> exits with when nothing matches.</summary>
    private const int NothingFound = 1;

    /// <summary>What <c>git config --unset-all</c> exits with when the key is not set.</summary>
    private const int NothingToUnset = 5;

    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    public GitIdentityService(IGitProcessRunner runner, IGitCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);

        _runner = runner;
        _commandFactory = commandFactory;
    }

    /// <inheritdoc />
    public Task<GitIdentity> GetGlobalAsync(CancellationToken cancellationToken = default)
        => ReadAsync(GlobalWorkingDirectory, GitConfigScope.Global, cancellationToken);

    /// <inheritdoc />
    public Task SetGlobalAsync(GitIdentity identity, CancellationToken cancellationToken = default)
        => WriteAsync(GlobalWorkingDirectory, GitConfigScope.Global, identity, cancellationToken);

    /// <inheritdoc />
    public Task<GitIdentity> GetLocalAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return ReadAsync(repository.WorkTreePath, GitConfigScope.Local, cancellationToken);
    }

    /// <inheritdoc />
    public Task SetLocalAsync(
        RepositoryHandle repository,
        GitIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return WriteAsync(repository.WorkTreePath, GitConfigScope.Local, identity, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveLocalAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        foreach (string key in Keys)
        {
            GitCommand command = _commandFactory.Create(repository.WorkTreePath, BuildRemoveArguments(key));
            GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
                .ConfigureAwait(false);

            // Already absent is what was asked for.
            if (!result.IsSuccess && result.ExitCode != NothingToUnset)
            {
                throw new GitCommandException(command, result.ExitCode, result.StandardError, result.StandardOutput);
            }
        }
    }

    /// <summary>
    /// Builds the argument vector that reads both keys of a scope.
    /// </summary>
    /// <param name="scope">The scope to read.</param>
    /// <returns>The arguments.</returns>
    /// <remarks>
    /// <c>-z</c> separates each key from its value with a newline and ends each entry with a NUL, so a
    /// name with spaces in it is never split in the wrong place.
    /// </remarks>
    public static List<string> BuildReadArguments(GitConfigScope scope)
        => ["config", "-z", ScopeFlag(scope), "--get-regexp", KeyPattern];

    /// <summary>
    /// Builds the argument vector that sets one key in a scope.
    /// </summary>
    /// <param name="scope">The scope to write.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, already validated.</param>
    /// <returns>The arguments.</returns>
    /// <remarks>
    /// <c>git config</c> stops reading options at the key, so a value that begins with a dash is still
    /// written as a value.
    /// </remarks>
    public static List<string> BuildWriteArguments(GitConfigScope scope, string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        return ["config", ScopeFlag(scope), key, value];
    }

    /// <summary>
    /// Builds the argument vector that removes one key from a repository's configuration.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The arguments.</returns>
    /// <remarks>
    /// <c>--unset-all</c> rather than <c>--unset</c>: a hand-edited file that sets the key twice would
    /// make <c>--unset</c> refuse, and leave the identity half removed.
    /// </remarks>
    public static List<string> BuildRemoveArguments(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return ["config", ScopeFlag(GitConfigScope.Local), "--unset-all", key];
    }

    /// <summary>
    /// Reads what <c>git config -z --get-regexp</c> printed.
    /// </summary>
    /// <param name="output">The standard output.</param>
    /// <returns>The identity; a key set more than once takes its last value, as git itself does.</returns>
    public static GitIdentity Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        string name = string.Empty;
        string email = string.Empty;

        foreach (string entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = entry.IndexOf('\n', StringComparison.Ordinal);

            // A key with no value at all ("[user] name") is printed without a newline; it sets nothing.
            string key = separator < 0 ? entry : entry[..separator];
            string value = separator < 0 ? string.Empty : entry[(separator + 1)..];

            if (string.Equals(key, NameKey, StringComparison.OrdinalIgnoreCase))
            {
                name = value;
            }
            else if (string.Equals(key, EmailKey, StringComparison.OrdinalIgnoreCase))
            {
                email = value;
            }
        }

        return new GitIdentity(name, email);
    }

    /// <summary>
    /// Where a global read or write runs: a directory that always exists, as the environment probe
    /// uses. The global scope never looks at the repository around it.
    /// </summary>
    private static string GlobalWorkingDirectory => AppContext.BaseDirectory;

    private static string ScopeFlag(GitConfigScope scope)
        => scope switch
        {
            GitConfigScope.Global => "--global",
            GitConfigScope.Local => "--local",
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Not a configuration scope."),
        };

    private async Task<GitIdentity> ReadAsync(
        string workingDirectory,
        GitConfigScope scope,
        CancellationToken cancellationToken)
    {
        GitCommand command = _commandFactory.Create(workingDirectory, BuildReadArguments(scope));
        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode == NothingFound)
        {
            return GitIdentity.Empty;
        }

        if (!result.IsSuccess)
        {
            throw new GitCommandException(command, result.ExitCode, result.StandardError, result.StandardOutput);
        }

        return Parse(result.StandardOutput);
    }

    private async Task WriteAsync(
        string workingDirectory,
        GitConfigScope scope,
        GitIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        GitIdentity trimmed = identity.Normalised();

        if (GitIdentityRules.Validate(trimmed) is { } problem)
        {
            throw new ArgumentException(problem, nameof(identity));
        }

        (string Key, string Value)[] values = [(NameKey, trimmed.Name), (EmailKey, trimmed.Email)];

        foreach ((string key, string value) in values)
        {
            GitCommand command = _commandFactory.Create(workingDirectory, BuildWriteArguments(scope, key, value));

            await _runner.RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);
        }
    }
}
