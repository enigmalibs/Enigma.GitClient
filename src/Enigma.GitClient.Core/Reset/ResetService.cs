using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Reset;

/// <summary>
/// What a reset does to the work that sits between the branch's old tip and its new one.
/// </summary>
/// <remarks>
/// git's third mode, <c>--mixed</c>, is left out on purpose: nothing offers it, and it is one member
/// away when something does.
/// </remarks>
public enum ResetMode
{
    /// <summary>
    /// Moves the branch and nothing else: the index and the work tree stay as they were, so everything
    /// the commits after the new tip changed shows as staged, ready to be committed again.
    /// </summary>
    Soft,

    /// <summary>
    /// Moves the branch and makes the index and every tracked file the new tip's. Uncommitted changes
    /// to tracked files are gone for good; untracked files are left where they are.
    /// </summary>
    Hard,
}

/// <summary>
/// Moves the branch HEAD is on to another commit.
/// </summary>
/// <remarks>
/// Whether the reset should happen at all — HEAD detached, a merge in progress, work about to be
/// thrown away — is the caller's question, because only the UI can ask it. This service does what it
/// is told, and refuses only what could never mean what was intended.
/// </remarks>
public interface IResetService
{
    /// <summary>
    /// Resets the current branch to a commit.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="revision">The commit to move to — the history passes a full sha.</param>
    /// <param name="mode">What happens to the index and the work tree.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once git has moved the branch.</returns>
    Task ResetAsync(
        RepositoryHandle repository,
        string revision,
        ResetMode mode,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IResetService"/>, driving the real git executable.
/// </summary>
public sealed class ResetService : IResetService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the command.</param>
    /// <param name="commandFactory">Builds the command.</param>
    public ResetService(IGitProcessRunner runner, IGitCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);

        _runner = runner;
        _commandFactory = commandFactory;
    }

    /// <inheritdoc />
    public async Task ResetAsync(
        RepositoryHandle repository,
        string revision,
        ResetMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitCommand command = _commandFactory.Create(repository.WorkTreePath, BuildArguments(revision, mode));

        await _runner.RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the argument vector for a reset.
    /// </summary>
    /// <param name="revision">The commit to move to.</param>
    /// <param name="mode">What happens to the index and the work tree.</param>
    /// <returns>The arguments.</returns>
    /// <remarks>
    /// The trailing <c>--</c> ends the revisions, so a name that is also a file's is still read as
    /// the commit. A revision that starts with a dash would be read as an option before it, and
    /// <c>--end-of-options</c>, which would prevent that, needs git 2.24 — above the minimum this
    /// client supports — so such a revision is refused instead.
    /// </remarks>
    /// <exception cref="ArgumentException">The revision is blank, or starts with a dash.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not one of <see cref="ResetMode"/>'s.</exception>
    public static List<string> BuildArguments(string revision, ResetMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        if (revision.StartsWith('-'))
        {
            throw new ArgumentException("A revision to reset to cannot start with a dash.", nameof(revision));
        }

        string flag = mode switch
        {
            ResetMode.Soft => "--soft",
            ResetMode.Hard => "--hard",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a reset mode."),
        };

        return ["reset", flag, revision, "--"];
    }
}
