using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Status;

namespace Enigma.GitClient.Core.Revert;

/// <summary>
/// What a revert did.
/// </summary>
public enum RevertResultKind
{
    /// <summary>A commit undoing the reverted one was recorded on the current branch.</summary>
    Reverted,

    /// <summary>What the commit changed is already undone; nothing was recorded.</summary>
    NothingToRevert,

    /// <summary>
    /// Undoing the commit conflicts with what changed since. The revert was abandoned, and the
    /// repository is exactly as it was.
    /// </summary>
    Conflicted,

    /// <summary>git refused, for a reason it explained; nothing was recorded.</summary>
    Failed,
}

/// <summary>
/// The result of a revert.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="ConflictedPaths">The files that conflicted, empty unless it conflicted.</param>
/// <param name="Message">What to tell the user.</param>
/// <param name="Detail">Everything git wrote, kept for the log.</param>
public sealed record RevertOutcome(
    RevertResultKind Kind,
    IReadOnlyList<string> ConflictedPaths,
    string Message,
    string Detail)
{
    /// <summary>
    /// Gets the full sha of the commit the revert recorded, empty unless <see cref="Kind"/> is
    /// <see cref="RevertResultKind.Reverted"/>.
    /// </summary>
    public string CommitSha { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether a commit was recorded.</summary>
    public bool CommittedAnything => Kind == RevertResultKind.Reverted;
}

/// <summary>
/// Records a commit that undoes another one.
/// </summary>
/// <remarks>
/// A revert never stops half-way here: the client has no page to finish or abandon one, and a
/// repository left in the reverting state refuses resets, merges and the next revert alike. A revert
/// that conflicts is abandoned on the spot, and says which files it could not undo.
/// </remarks>
public interface IRevertService
{
    /// <summary>
    /// Reverts a commit onto the branch HEAD is on, committing the result with git's own message.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="revision">The commit to undo — the history passes a full sha.</param>
    /// <param name="mainline">
    /// For a merge commit, the parent whose side is kept (1 is the branch it was merged into);
    /// <see langword="null"/> for any other commit.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What happened.</returns>
    Task<RevertOutcome> RevertAsync(
        RepositoryHandle repository,
        string revision,
        int? mainline = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IRevertService"/>, driving the real git executable.
/// </summary>
public sealed class RevertService : IRevertService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly IStatusService _status;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="status">Lists what a conflicting revert could not undo.</param>
    public RevertService(IGitProcessRunner runner, IGitCommandFactory commandFactory, IStatusService status)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(status);

        _runner = runner;
        _commandFactory = commandFactory;
        _status = status;
    }

    /// <summary>
    /// Builds the argument vector for a revert.
    /// </summary>
    /// <param name="revision">The commit to undo.</param>
    /// <param name="mainline">For a merge, the parent whose side is kept; otherwise <see langword="null"/>.</param>
    /// <returns>The arguments.</returns>
    /// <remarks>
    /// <c>--no-edit</c> commits with the message git writes — <c>Revert "subject"</c> and the sha it
    /// undoes — without an editor nobody would see. The trailing <c>--</c> ends the revisions, and a
    /// revision that starts with a dash, which would be read as an option before it, is refused:
    /// <c>--end-of-options</c> needs git 2.24, above the minimum this client supports.
    /// </remarks>
    /// <exception cref="ArgumentException">The revision is blank, or starts with a dash.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The mainline is below 1.</exception>
    public static List<string> BuildArguments(string revision, int? mainline = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        if (revision.StartsWith('-'))
        {
            throw new ArgumentException("A revision to revert cannot start with a dash.", nameof(revision));
        }

        List<string> arguments = ["revert", "--no-edit"];

        if (mainline is { } parent)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(parent, 1, nameof(mainline));

            arguments.Add("--mainline");
            arguments.Add(parent.ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add(revision);
        arguments.Add("--");

        return arguments;
    }

    /// <summary>
    /// Works out what a revert did, from git's exit code and what it wrote.
    /// </summary>
    /// <param name="exitCode">git's exit code.</param>
    /// <param name="standardOutput">Everything git wrote to standard output.</param>
    /// <param name="standardError">Everything git wrote to standard error.</param>
    /// <returns>The outcome, with no conflict list — the caller fills that from the status.</returns>
    /// <remarks>
    /// A conflict, a revert with nothing left to undo and a refusal all exit non-zero, and only the
    /// first leaves anything behind; git's wording is what tells them apart, as it is for a merge.
    /// </remarks>
    public static RevertOutcome Classify(int exitCode, string standardOutput, string standardError)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        string detail = $"{standardOutput}\n{standardError}".Trim();

        if (exitCode == 0)
        {
            return new RevertOutcome(RevertResultKind.Reverted, [], "The revert was committed.", detail);
        }

        if (Contains(detail, "CONFLICT (", "after resolving the conflicts"))
        {
            return new RevertOutcome(
                RevertResultKind.Conflicted,
                [],
                "Undoing it conflicts with what changed since, so the revert was abandoned and nothing changed.",
                detail);
        }

        if (Contains(detail, "nothing to commit", "nothing added to commit"))
        {
            return new RevertOutcome(
                RevertResultKind.NothingToRevert,
                [],
                "What it changed is already undone, so there was nothing to commit.",
                detail);
        }

        if (Contains(detail, "local changes", "would be overwritten", "commit your changes or stash them"))
        {
            return new RevertOutcome(
                RevertResultKind.Failed,
                [],
                "Uncommitted work is in the way. Commit it or stash it, then revert.",
                detail);
        }

        if (Contains(detail, "is a merge but no -m option"))
        {
            return new RevertOutcome(
                RevertResultKind.Failed,
                [],
                "It is a merge, so git needs to know which side to keep.",
                detail);
        }

        return new RevertOutcome(
            RevertResultKind.Failed,
            [],
            FirstLine(standardError.Trim().Length > 0 ? standardError : standardOutput),
            detail);
    }

    /// <inheritdoc />
    public async Task<RevertOutcome> RevertAsync(
        RepositoryHandle repository,
        string revision,
        int? mainline = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitCommand command = _commandFactory.Create(repository.WorkTreePath, BuildArguments(revision, mainline));

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        RevertOutcome outcome = Classify(result.ExitCode, result.StandardOutput, result.StandardError);

        if (outcome.Kind == RevertResultKind.Reverted)
        {
            GitCommand head = _commandFactory.Create(repository.WorkTreePath, ["rev-parse", "HEAD"]);
            GitResult resolved = await _runner.RunAsync(head, throwOnError: true, cancellationToken)
                .ConfigureAwait(false);

            return outcome with { CommitSha = resolved.TrimmedOutput };
        }

        if (outcome.Kind == RevertResultKind.Conflicted)
        {
            // Read before the abort puts the files back: afterwards there is nothing left to name.
            outcome = outcome with { ConflictedPaths = await ReadConflictedPathsAsync(repository, cancellationToken).ConfigureAwait(false) };
        }

        // Whatever stopped it, a revert that left its state behind is abandoned here, so the
        // repository is never left in the middle of one. --abort keeps unrelated local edits, as
        // git's own merge-safe reset does.
        if (File.Exists(repository.GetGitPath("REVERT_HEAD")))
        {
            GitCommand abort = _commandFactory.Create(repository.WorkTreePath, ["revert", "--abort"]);

            await _runner.RunAsync(abort, throwOnError: true, cancellationToken).ConfigureAwait(false);
        }

        return outcome;
    }

    private async Task<IReadOnlyList<string>> ReadConflictedPathsAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken)
    {
        WorkingTreeStatus status = await _status
            .GetStatusAsync(repository, includeIgnored: false, cancellationToken)
            .ConfigureAwait(false);

        List<string> paths = [];

        foreach (ChangedFile file in status.Conflicted)
        {
            paths.Add(file.Path);
        }

        return paths;
    }

    private static bool Contains(string text, params string[] fragments)
    {
        foreach (string fragment in fragments)
        {
            if (text.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FirstLine(string text)
    {
        string trimmed = text.Trim();

        if (trimmed.Length == 0)
        {
            return "git reported no reason.";
        }

        int newline = trimmed.IndexOf('\n', StringComparison.Ordinal);

        return newline < 0 ? trimmed : trimmed[..newline].TrimEnd('\r');
    }
}
