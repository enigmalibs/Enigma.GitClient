using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Status;

namespace Enigma.GitClient.Core.Merging;

/// <summary>
/// How a merge treats a branch that is simply ahead.
/// </summary>
public enum FastForwardMode
{
    /// <summary>Fast-forward when possible, which is git's own default.</summary>
    WhenPossible,

    /// <summary>Always record a merge commit, even when a fast-forward would do.</summary>
    Never,

    /// <summary>Refuse anything that is not a fast-forward.</summary>
    Only,
}

/// <summary>
/// What to merge, and how.
/// </summary>
public sealed record MergeRequest
{
    /// <summary>
    /// Gets the revision to merge in — a branch, a tag or a commit.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets how the merge treats a branch that is simply ahead.
    /// </summary>
    public FastForwardMode FastForward { get; init; } = FastForwardMode.WhenPossible;

    /// <summary>
    /// Gets the commit message, or <see langword="null"/> to let git write one.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets a value indicating whether the merge stops before committing.
    /// </summary>
    public bool NoCommit { get; init; }

    /// <summary>
    /// Gets a value indicating whether the changes are staged without recording a merge at all.
    /// </summary>
    public bool Squash { get; init; }
}

/// <summary>
/// What a merge did.
/// </summary>
public enum MergeResultKind
{
    /// <summary>The branch was already contained; nothing happened.</summary>
    AlreadyUpToDate,

    /// <summary>HEAD moved forward without a merge commit.</summary>
    FastForward,

    /// <summary>A merge commit was recorded.</summary>
    Merged,

    /// <summary>The changes were staged without a merge being recorded.</summary>
    Squashed,

    /// <summary>The merge stopped, leaving conflicts to resolve.</summary>
    Conflicted,

    /// <summary>The merge stopped before committing, as it was asked to.</summary>
    Stopped,

    /// <summary>git refused, for a reason it explained.</summary>
    Failed,
}

/// <summary>
/// The result of a merge.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="ConflictedPaths">The files left to resolve, empty unless it conflicted.</param>
/// <param name="Message">What to tell the user.</param>
/// <param name="Detail">Everything git wrote, kept for the log.</param>
public sealed record MergeOutcome(
    MergeResultKind Kind,
    IReadOnlyList<string> ConflictedPaths,
    string Message,
    string Detail)
{
    /// <summary>Gets a value indicating whether the merge left work to do.</summary>
    public bool NeedsResolution => Kind == MergeResultKind.Conflicted;

    /// <summary>Gets a value indicating whether anything at all changed.</summary>
    public bool ChangedAnything => Kind is MergeResultKind.FastForward
        or MergeResultKind.Merged
        or MergeResultKind.Squashed
        or MergeResultKind.Conflicted
        or MergeResultKind.Stopped;
}

/// <summary>
/// Where a merge in progress came from.
/// </summary>
/// <param name="Ours">The commit HEAD was on.</param>
/// <param name="Theirs">The commit being merged in.</param>
/// <param name="OursName">What to call our side, empty when it has no name.</param>
/// <param name="TheirsName">What to call their side, empty when it has no name.</param>
public sealed record MergeHeads(string Ours, string Theirs, string OursName, string TheirsName)
{
    /// <summary>
    /// A repository with no merge in progress.
    /// </summary>
    public static readonly MergeHeads None = new(string.Empty, string.Empty, string.Empty, string.Empty);

    /// <summary>Gets a value indicating whether there is a merge in progress at all.</summary>
    public bool Exists => Theirs.Length > 0;
}

/// <summary>
/// Merges branches, and gets out of a merge that went wrong.
/// </summary>
/// <remarks>
/// Rebase is forbidden product-wide, which makes merge the integration operation this client has,
/// so it has to report what actually happened rather than only whether git exited zero: a merge
/// that conflicts exits non-zero and has still done something.
/// </remarks>
public interface IMergeService
{
    /// <summary>
    /// Merges a revision into the current branch.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="request">What to merge, and how.</param>
    /// <param name="cancellationToken">Cancels the merge.</param>
    /// <returns>What happened.</returns>
    Task<MergeOutcome> MergeAsync(
        RepositoryHandle repository,
        MergeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes a merge in progress, putting the repository back exactly as it was.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the merge is gone.</returns>
    Task AbortAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the merge commit once every conflict has been resolved.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="message">The commit message, or <see langword="null"/> for the one git prepared.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The merge commit's full SHA.</returns>
    Task<string> ContinueAsync(
        RepositoryHandle repository,
        string? message = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers whether a merge is waiting to be finished.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true"/> when there is a merge in progress.</returns>
    Task<bool> IsMergeInProgressAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads where a merge in progress came from.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The two sides, or <see cref="MergeHeads.None"/> when no merge is in progress.</returns>
    Task<MergeHeads> GetMergeHeadsAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IMergeService"/>.
/// </summary>
public sealed class MergeService : IMergeService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly IStatusService _status;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="status">Lists what a conflicted merge left behind.</param>
    public MergeService(IGitProcessRunner runner, IGitCommandFactory commandFactory, IStatusService status)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(status);

        _runner = runner;
        _commandFactory = commandFactory;
        _status = status;
    }

    /// <summary>
    /// Builds the argument vector for a merge.
    /// </summary>
    /// <param name="request">What to merge, and how.</param>
    /// <returns>The arguments.</returns>
    public static List<string> BuildArguments(MergeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<string> arguments = ["merge"];

        switch (request.FastForward)
        {
            case FastForwardMode.Never:
                arguments.Add("--no-ff");
                break;

            case FastForwardMode.Only:
                arguments.Add("--ff-only");
                break;

            case FastForwardMode.WhenPossible:
            default:
                break;
        }

        if (request.Squash)
        {
            arguments.Add("--squash");
        }

        if (request.NoCommit && !request.Squash)
        {
            arguments.Add("--no-commit");
        }

        if (request.Message is { Length: > 0 })
        {
            arguments.Add("--message");
            arguments.Add(request.Message);
        }

        arguments.Add(request.Source);

        return arguments;
    }

    /// <summary>
    /// Works out what a merge did, from git's exit code and what it wrote.
    /// </summary>
    /// <param name="request">What was asked for.</param>
    /// <param name="exitCode">git's exit code.</param>
    /// <param name="standardOutput">Everything git wrote to standard output.</param>
    /// <param name="standardError">Everything git wrote to standard error.</param>
    /// <returns>The outcome, with no conflict list — the caller fills that from the status.</returns>
    /// <remarks>
    /// An exit code alone cannot tell a conflicted merge from a refused one: both are non-zero, and
    /// one of them has changed the repository. The wording git uses is stable enough to tell them
    /// apart, and the caller confirms a conflict against the real status afterwards.
    /// </remarks>
    public static MergeOutcome Classify(
        MergeRequest request,
        int exitCode,
        string standardOutput,
        string standardError)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        string text = $"{standardOutput}\n{standardError}";

        if (Contains(text, "Already up to date", "Already up-to-date"))
        {
            return new MergeOutcome(
                MergeResultKind.AlreadyUpToDate,
                [],
                $"\"{request.Source}\" is already contained in this branch.",
                standardOutput);
        }

        if (exitCode != 0)
        {
            if (Contains(text, "Automatic merge failed", "CONFLICT ("))
            {
                return new MergeOutcome(
                    MergeResultKind.Conflicted,
                    [],
                    "The merge stopped on conflicts. Resolve them, then commit the merge.",
                    standardOutput.Length > 0 ? standardOutput : standardError);
            }

            if (Contains(text, "Your local changes", "would be overwritten", "commit your changes or stash them"))
            {
                return new MergeOutcome(
                    MergeResultKind.Failed,
                    [],
                    "Uncommitted work is in the way. Commit it or stash it, then merge.",
                    standardError);
            }

            if (Contains(text, "Not possible to fast-forward", "not something we can merge"))
            {
                return new MergeOutcome(
                    MergeResultKind.Failed,
                    [],
                    Contains(text, "Not possible to fast-forward")
                        ? "The branches have diverged, so this cannot be a fast-forward."
                        : $"\"{request.Source}\" is not something git can merge.",
                    standardError);
            }

            return new MergeOutcome(MergeResultKind.Failed, [], FirstLine(standardError), standardError);
        }

        if (request.Squash)
        {
            return new MergeOutcome(
                MergeResultKind.Squashed,
                [],
                $"The changes from \"{request.Source}\" are staged, with no merge recorded.",
                standardOutput);
        }

        if (Contains(text, "Fast-forward"))
        {
            return new MergeOutcome(
                MergeResultKind.FastForward,
                [],
                $"The branch moved forward onto \"{request.Source}\".",
                standardOutput);
        }

        if (request.NoCommit)
        {
            return new MergeOutcome(
                MergeResultKind.Stopped,
                [],
                "The merge is staged and waiting to be committed.",
                standardOutput);
        }

        return new MergeOutcome(
            MergeResultKind.Merged,
            [],
            $"\"{request.Source}\" was merged in.",
            standardOutput);
    }

    /// <inheritdoc />
    public async Task<MergeOutcome> MergeAsync(
        RepositoryHandle repository,
        MergeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source);

        GitCommand command = _commandFactory.Create(repository.WorkTreePath, BuildArguments(request));

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        MergeOutcome outcome = Classify(request, result.ExitCode, result.StandardOutput, result.StandardError);

        if (outcome.Kind != MergeResultKind.Conflicted)
        {
            return outcome;
        }

        // The conflict list comes from the real status rather than from git's message: the message
        // names the files it noticed, and the status is what the user has to work through.
        WorkingTreeStatus status = await _status
            .GetStatusAsync(repository, includeIgnored: false, cancellationToken)
            .ConfigureAwait(false);

        List<string> paths = [];

        foreach (Files.ChangedFile file in status.Conflicted)
        {
            paths.Add(file.Path);
        }

        return outcome with { ConflictedPaths = paths };
    }

    /// <inheritdoc />
    public async Task AbortAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitCommand command = _commandFactory.Create(repository.WorkTreePath, ["merge", "--abort"]);

        await _runner.RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ContinueAsync(
        RepositoryHandle repository,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        WorkingTreeStatus status = await _status
            .GetStatusAsync(repository, includeIgnored: false, cancellationToken)
            .ConfigureAwait(false);

        if (status.HasConflicts)
        {
            throw new GitOperationRefusedException(
                $"{status.Conflicted.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)} "
                + "file(s) still have conflicts. Resolve them before committing the merge.");
        }

        string? messageFile = null;

        try
        {
            List<string> arguments = ["commit", "--no-edit"];

            if (message is { Length: > 0 })
            {
                // Through a file, for the same reason an ordinary commit's message is: an argument
                // vector has a length limit and mangles some encodings.
                messageFile = Path.Combine(Path.GetTempPath(), $"enigma-merge-{Guid.NewGuid():N}.txt");

                await File
                    .WriteAllTextAsync(messageFile, message.TrimEnd('\n', '\r') + "\n", new UTF8Encoding(false), cancellationToken)
                    .ConfigureAwait(false);

                arguments = ["commit", "--file", messageFile];
            }

            GitCommand command = _commandFactory.Create(repository.WorkTreePath, arguments);

            await _runner.RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(messageFile);
        }

        GitCommand head = _commandFactory.Create(repository.WorkTreePath, ["rev-parse", "HEAD"]);
        GitResult resolved = await _runner.RunAsync(head, throwOnError: true, cancellationToken)
            .ConfigureAwait(false);

        return resolved.TrimmedOutput;
    }

    /// <inheritdoc />
    public Task<bool> IsMergeInProgressAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        // MERGE_HEAD is what git itself checks; there is nothing to ask a process about.
        return Task.FromResult(File.Exists(repository.GetGitPath("MERGE_HEAD")));
    }

    /// <inheritdoc />
    public async Task<MergeHeads> GetMergeHeadsAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (!await IsMergeInProgressAsync(repository, cancellationToken).ConfigureAwait(false))
        {
            return MergeHeads.None;
        }

        string theirs = (await File
            .ReadAllTextAsync(repository.GetGitPath("MERGE_HEAD"), cancellationToken)
            .ConfigureAwait(false))
            .Trim();

        string ours = await ResolveAsync(repository, "HEAD", cancellationToken).ConfigureAwait(false);

        return new MergeHeads(
            ours,
            theirs,
            await DescribeAsync(repository, "HEAD", cancellationToken).ConfigureAwait(false),
            await DescribeAsync(repository, theirs, cancellationToken).ConfigureAwait(false));
    }

    private async Task<string> ResolveAsync(
        RepositoryHandle repository,
        string revision,
        CancellationToken cancellationToken)
    {
        GitCommand command = _commandFactory.Create(repository.WorkTreePath, ["rev-parse", revision]);
        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? result.TrimmedOutput : string.Empty;
    }

    /// <summary>
    /// Finds the friendliest name for a commit: the branch or tag pointing at it, or its short hash.
    /// </summary>
    private async Task<string> DescribeAsync(
        RepositoryHandle repository,
        string revision,
        CancellationToken cancellationToken)
    {
        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            ["name-rev", "--name-only", "--refs=refs/heads/*", "--refs=refs/tags/*", revision]);

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        string name = result.IsSuccess ? result.TrimmedOutput : string.Empty;

        if (name.Length == 0 || string.Equals(name, "undefined", StringComparison.Ordinal))
        {
            string sha = await ResolveAsync(repository, revision, cancellationToken).ConfigureAwait(false);
            return sha.Length >= 7 ? sha[..7] : sha;
        }

        return name;
    }

    private static bool Contains(string text, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FirstLine(string text)
    {
        foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return "git reported no reason.";
    }

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A temporary file that outlives the process is not worth failing a merge over.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }
}
