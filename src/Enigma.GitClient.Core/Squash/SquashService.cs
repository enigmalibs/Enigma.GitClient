using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Branches;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Squash;

/// <summary>
/// A merge inside a squash's range that brings in a branch from outside it.
/// </summary>
/// <param name="Sha">The merge commit's full sha.</param>
/// <param name="Subject">Its subject, which usually names the branch it merged.</param>
public sealed record SquashOutsideMerge(string Sha, string Subject);

/// <summary>
/// What a squash between two commits would replace, read before anything is written.
/// </summary>
/// <param name="BaseSha">The base, in full: the squash commit's parent.</param>
/// <param name="TipSha">The tip, in full: the squash commit takes its files.</param>
/// <param name="CommitCount">
/// The commits the squash replaces on the new branch: those that descend from the base and lead to
/// the tip, the tip included.
/// </param>
/// <param name="MergeCount">How many of them are merges.</param>
/// <param name="Subjects">The subjects of the ones that are not merges, oldest first.</param>
/// <param name="OutsideMerges">The merges that bring in a branch from outside the range.</param>
/// <param name="OutsideCommitCount">
/// The commits those merges bring in: reachable from the tip, not from the base, and not among the
/// replaced ones.
/// </param>
/// <param name="ChangesNothing">Whether the tip's files are exactly the base's.</param>
public sealed record SquashPreview(
    string BaseSha,
    string TipSha,
    int CommitCount,
    int MergeCount,
    IReadOnlyList<string> Subjects,
    IReadOnlyList<SquashOutsideMerge> OutsideMerges,
    int OutsideCommitCount,
    bool ChangesNothing);

/// <summary>
/// A squash to perform.
/// </summary>
/// <param name="BaseSha">The commit the squash commit sits on.</param>
/// <param name="TipSha">The commit whose files the squash commit records.</param>
/// <param name="BranchName">The new branch, which must not exist yet.</param>
/// <param name="Message">The squash commit's message; its first line is the title.</param>
/// <param name="Checkout">Whether to switch to the new branch once it exists.</param>
public sealed record SquashRequest(string BaseSha, string TipSha, string BranchName, string Message, bool Checkout);

/// <summary>
/// What a squash created.
/// </summary>
/// <param name="CommitSha">The squash commit's full sha.</param>
/// <param name="BranchName">The new branch, which points at it.</param>
/// <param name="CheckedOut">Whether HEAD is now on the new branch.</param>
public sealed record SquashOutcome(string CommitSha, string BranchName, bool CheckedOut);

/// <summary>
/// Replaces the commits between a base and a tip with one commit, on a new branch.
/// </summary>
/// <remarks>
/// Nothing that exists is rewritten or moved: <c>git commit-tree</c> writes the squash commit without
/// touching a ref, the index or the work tree, and the new branch is then created on it. The original
/// branch, its commits, its tags and its upstream stay exactly as they were. Rebase is out of the
/// question in this client (<see cref="ForbiddenGitOperations"/>), and nothing here cherry-picks either.
/// </remarks>
public interface ISquashService
{
    /// <summary>
    /// Reads what a squash would replace.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="baseSha">The base — the history passes a full sha.</param>
    /// <param name="tipSha">The tip — the history passes a full sha.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The range's description.</returns>
    /// <exception cref="GitOperationRefusedException">
    /// The base is the tip, or is not on the tip's first-parent line.
    /// </exception>
    Task<SquashPreview> PreviewAsync(
        RepositoryHandle repository,
        string baseSha,
        string tipSha,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the squash commit and creates the new branch on it.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="request">The squash.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What was created.</returns>
    /// <exception cref="GitOperationRefusedException">
    /// The range, the title or the branch name is refused, or uncommitted changes are in the way of
    /// the switch. Nothing was created.
    /// </exception>
    Task<SquashOutcome> SquashAsync(
        RepositoryHandle repository,
        SquashRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ISquashService"/>, driving the real git executable.
/// </summary>
public sealed class SquashService : ISquashService
{
    private const char FieldSeparator = '\u001F';
    private const char RecordSeparator = '\u0000';

    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly IBranchService _branches;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="branches">Creates the new branch, and says whether its name is taken.</param>
    public SquashService(IGitProcessRunner runner, IGitCommandFactory commandFactory, IBranchService branches)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(branches);

        _runner = runner;
        _commandFactory = commandFactory;
        _branches = branches;
    }

    /// <summary>
    /// Builds the argument vector that writes the squash commit.
    /// </summary>
    /// <param name="tip">The commit whose files are recorded.</param>
    /// <param name="baseRevision">The squash commit's only parent.</param>
    /// <param name="messageFile">The file holding the message.</param>
    /// <returns>The arguments.</returns>
    /// <remarks>
    /// A revision that starts with a dash would be read as an option, and <c>--end-of-options</c>
    /// needs git 2.24, above the minimum this client supports — so such a revision is refused.
    /// </remarks>
    /// <exception cref="ArgumentException">A revision is blank or starts with a dash, or the file is blank.</exception>
    public static List<string> BuildCommitTreeArguments(string tip, string baseRevision, string messageFile)
    {
        RequireRevision(tip, nameof(tip));
        RequireRevision(baseRevision, nameof(baseRevision));
        ArgumentException.ThrowIfNullOrWhiteSpace(messageFile);

        return ["commit-tree", $"{tip}^{{tree}}", "-p", baseRevision, "-F", messageFile];
    }

    /// <summary>
    /// Writes the message the squash dialog starts from: an empty title for the reader to type, then
    /// one bullet per replaced commit that is not a merge.
    /// </summary>
    /// <param name="subjects">The subjects, oldest first.</param>
    /// <returns>The message, or an empty string when there is no subject to list.</returns>
    public static string ComposeDefaultMessage(IReadOnlyList<string> subjects)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        if (subjects.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder message = new("\n");

        foreach (string subject in subjects)
        {
            message.Append("\n- ").Append(subject);
        }

        return message.ToString();
    }

    /// <summary>
    /// Cleans a message up the way <c>git commit</c> does by default and <c>git commit-tree</c> does
    /// not: line endings as LF, no trailing spaces, no run of blank lines, no blank lines at the end,
    /// and exactly one newline after the last line.
    /// </summary>
    /// <param name="message">The message as it was typed.</param>
    /// <returns>The message to record.</returns>
    public static string CleanUpMessage(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        string[] lines = message.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        StringBuilder cleaned = new();
        bool blankPending = false;

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd();

            if (line.Length == 0)
            {
                blankPending = cleaned.Length > 0;
                continue;
            }

            if (blankPending)
            {
                cleaned.Append('\n');
                blankPending = false;
            }

            cleaned.Append(line).Append('\n');
        }

        return cleaned.ToString();
    }

    /// <summary>
    /// Reads the title of a message: its first line, as typed.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The first line without its surrounding spaces; empty when it is blank.</returns>
    public static string TitleOf(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        int newline = message.IndexOfAny(['\r', '\n']);

        return (newline < 0 ? message : message[..newline]).Trim();
    }

    /// <inheritdoc />
    public async Task<SquashPreview> PreviewAsync(
        RepositoryHandle repository,
        string baseSha,
        string tipSha,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        (string baseCommit, string tipCommit) = await ResolveRangeAsync(repository, baseSha, tipSha, cancellationToken)
            .ConfigureAwait(false);

        List<RangeCommit> path = await ReadAncestryPathAsync(repository, baseCommit, tipCommit, cancellationToken)
            .ConfigureAwait(false);

        HashSet<string> onPath = new(StringComparer.Ordinal);

        foreach (RangeCommit commit in path)
        {
            onPath.Add(commit.Sha);
        }

        int merges = 0;
        List<string> subjects = [];
        List<SquashOutsideMerge> outside = [];

        // Newest first from git; the subjects read oldest first, as the work was done.
        for (int index = path.Count - 1; index >= 0; index--)
        {
            RangeCommit commit = path[index];

            if (commit.Parents.Count < 2)
            {
                subjects.Add(commit.Subject);
                continue;
            }

            merges++;

            // A parent that neither is the base nor descends from it is a branch from elsewhere:
            // the squash keeps its files and loses the link to it.
            foreach (string parent in commit.Parents)
            {
                if (!onPath.Contains(parent) && !string.Equals(parent, baseCommit, StringComparison.Ordinal))
                {
                    outside.Add(new SquashOutsideMerge(commit.Sha, commit.Subject));
                    break;
                }
            }
        }

        int reachable = await CountAsync(repository, $"{baseCommit}..{tipCommit}", cancellationToken).ConfigureAwait(false);

        GitResult trees = await RunAsync(
            repository,
            ["rev-parse", $"{baseCommit}^{{tree}}", $"{tipCommit}^{{tree}}"],
            throwOnError: true,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<string> treeShas = trees.SplitOutput();
        bool changesNothing = treeShas.Count == 2 && string.Equals(treeShas[0], treeShas[1], StringComparison.Ordinal);

        return new SquashPreview(
            baseCommit,
            tipCommit,
            path.Count,
            merges,
            subjects,
            outside,
            Math.Max(0, reachable - path.Count),
            changesNothing);
    }

    /// <inheritdoc />
    public async Task<SquashOutcome> SquashAsync(
        RepositoryHandle repository,
        SquashRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Message);

        // Every refusal comes before anything is written, so a refused squash leaves not even an
        // unreferenced commit behind.
        if (TitleOf(request.Message).Length == 0)
        {
            throw new GitOperationRefusedException("Write a title for the squash commit first.");
        }

        RefNameValidation name = RefNameValidator.ValidateBranch(request.BranchName);

        if (!name.IsValid)
        {
            throw new GitOperationRefusedException(name.Message);
        }

        (string baseCommit, string tipCommit) = await ResolveRangeAsync(repository, request.BaseSha, request.TipSha, cancellationToken)
            .ConfigureAwait(false);

        if (await _branches.ExistsAsync(repository, request.BranchName, cancellationToken).ConfigureAwait(false))
        {
            throw new GitOperationRefusedException($"A branch called \"{request.BranchName}\" already exists.");
        }

        string commit = await CommitTreeAsync(repository, baseCommit, tipCommit, request.Message, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            // "checkout -b" checks the work tree before it creates the branch: when the switch is
            // refused, no branch exists, and the squash commit is an unreferenced object git
            // collects in time.
            await _branches.CreateAsync(repository, request.BranchName, commit, request.Checkout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitCommandException exception) when (request.Checkout && IsBlockedSwitch(exception))
        {
            throw new GitOperationRefusedException(DescribeBlockedSwitch(exception.StandardError), exception);
        }

        return new SquashOutcome(commit, request.BranchName, request.Checkout);
    }

    /// <summary>
    /// Says what stopped the switch to the new branch, naming the files git listed.
    /// </summary>
    /// <param name="standardError">What git wrote when it refused.</param>
    /// <returns>The sentence to show.</returns>
    public static string DescribeBlockedSwitch(string standardError)
    {
        ArgumentNullException.ThrowIfNull(standardError);

        List<string> files = [];

        foreach (string line in standardError.Split('\n'))
        {
            // git lists the files in its refusal one per line, indented with a tab.
            if (line.StartsWith('\t') && line.Trim().Length > 0)
            {
                files.Add(line.Trim());
            }
        }

        string what = files.Count == 0
            ? "Uncommitted changes are"
            : $"Uncommitted changes to {string.Join(", ", files)} are";

        return $"{what} in the way of switching to the new branch, so nothing was created. "
            + "Commit or stash them, or create the branch without switching to it.";
    }

    private static bool IsBlockedSwitch(GitCommandException exception)
        => exception.StandardError.Contains("would be overwritten", StringComparison.OrdinalIgnoreCase);

    private static void RequireRevision(string revision, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision, parameterName);

        if (revision.StartsWith('-'))
        {
            throw new ArgumentException("A revision to squash cannot start with a dash.", parameterName);
        }
    }

    private static string Short(string sha) => sha[..Math.Min(7, sha.Length)];

    /// <summary>
    /// Resolves both ends to full shas and checks that the base is on the tip's first-parent line —
    /// the one rule that keeps the meaning obvious: "everything the branch did since the base".
    /// </summary>
    private async Task<(string Base, string Tip)> ResolveRangeAsync(
        RepositoryHandle repository,
        string baseRevision,
        string tipRevision,
        CancellationToken cancellationToken)
    {
        RequireRevision(baseRevision, nameof(baseRevision));
        RequireRevision(tipRevision, nameof(tipRevision));

        string baseCommit = await ResolveCommitAsync(repository, baseRevision, cancellationToken).ConfigureAwait(false);
        string tipCommit = await ResolveCommitAsync(repository, tipRevision, cancellationToken).ConfigureAwait(false);

        if (string.Equals(baseCommit, tipCommit, StringComparison.Ordinal))
        {
            throw new GitOperationRefusedException(
                $"The base and the tip are both {Short(tipCommit)}, so there is nothing to squash.");
        }

        GitResult line = await RunAsync(
            repository,
            ["rev-list", "--first-parent", "--parents", $"{baseCommit}..{tipCommit}"],
            throwOnError: true,
            cancellationToken).ConfigureAwait(false);

        // Following first parents down from the tip reaches the base exactly when one of the commits
        // on that line has the base as its first parent. The output's order is not relied on.
        foreach (string entry in line.SplitOutput())
        {
            string[] shas = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (shas.Length > 1 && string.Equals(shas[1], baseCommit, StringComparison.Ordinal))
            {
                return (baseCommit, tipCommit);
            }
        }

        GitResult ancestor = await RunAsync(
            repository,
            ["merge-base", "--is-ancestor", baseCommit, tipCommit],
            throwOnError: false,
            cancellationToken).ConfigureAwait(false);

        throw new GitOperationRefusedException(ancestor.ExitCode == 0
            ? $"{Short(baseCommit)} is not on {Short(tipCommit)}'s main line — it came in through a merge. Choose a base on the line of first parents."
            : $"{Short(tipCommit)} does not descend from {Short(baseCommit)}, so there is nothing between them to squash.");
    }

    private async Task<string> ResolveCommitAsync(
        RepositoryHandle repository,
        string revision,
        CancellationToken cancellationToken)
    {
        GitResult result = await RunAsync(
            repository,
            ["rev-parse", "--verify", "--quiet", $"{revision}^{{commit}}"],
            throwOnError: false,
            cancellationToken).ConfigureAwait(false);

        string sha = result.TrimmedOutput.Trim();

        if (!result.IsSuccess || sha.Length == 0)
        {
            throw new GitOperationRefusedException($"\"{revision}\" is not a commit of this repository.");
        }

        return sha;
    }

    /// <summary>
    /// Reads the commits that descend from the base and lead to the tip, newest first, with their
    /// parents and subjects.
    /// </summary>
    private async Task<List<RangeCommit>> ReadAncestryPathAsync(
        RepositoryHandle repository,
        string baseCommit,
        string tipCommit,
        CancellationToken cancellationToken)
    {
        // log.showSignature would put PGP output into every record, as it would for the history.
        GitResult result = await RunAsync(
            repository,
            [
                "log",
                "--no-show-signature",
                "--date-order",
                "--ancestry-path",
                "--format=%H%x1f%P%x1f%s%x00",
                $"{baseCommit}..{tipCommit}",
            ],
            throwOnError: true,
            cancellationToken).ConfigureAwait(false);

        List<RangeCommit> commits = [];

        foreach (string record in result.StandardOutput.Split(RecordSeparator))
        {
            string trimmed = record.Trim('\n', '\r');

            if (trimmed.Length == 0)
            {
                continue;
            }

            string[] fields = trimmed.Split(FieldSeparator);

            if (fields.Length < 3)
            {
                continue;
            }

            commits.Add(new RangeCommit(
                fields[0],
                fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                string.Join(FieldSeparator, fields, 2, fields.Length - 2)));
        }

        return commits;
    }

    private async Task<int> CountAsync(RepositoryHandle repository, string range, CancellationToken cancellationToken)
    {
        GitResult result = await RunAsync(repository, ["rev-list", "--count", range], throwOnError: true, cancellationToken)
            .ConfigureAwait(false);

        return int.TryParse(result.TrimmedOutput.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            ? count
            : 0;
    }

    /// <summary>
    /// Writes the squash commit: the tip's tree, the base as its only parent, the message through a
    /// file — an argument vector has a length limit, and a file keeps every byte as it was typed.
    /// </summary>
    private async Task<string> CommitTreeAsync(
        RepositoryHandle repository,
        string baseCommit,
        string tipCommit,
        string message,
        CancellationToken cancellationToken)
    {
        string messageFile = Path.Combine(Path.GetTempPath(), $"enigma-squash-{Guid.NewGuid():N}.txt");

        try
        {
            // UTF-8 with no byte-order mark: a BOM would become the first characters of the title.
            await File.WriteAllTextAsync(messageFile, CleanUpMessage(message), new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);

            GitResult result = await RunAsync(
                repository,
                BuildCommitTreeArguments(tipCommit, baseCommit, messageFile),
                throwOnError: true,
                cancellationToken).ConfigureAwait(false);

            return result.TrimmedOutput.Trim();
        }
        finally
        {
            TryDelete(messageFile);
        }
    }

    private async Task<GitResult> RunAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> arguments,
        bool throwOnError,
        CancellationToken cancellationToken)
    {
        GitCommand command = _commandFactory.Create(repository.WorkTreePath, arguments);

        return await _runner.RunAsync(command, throwOnError, cancellationToken).ConfigureAwait(false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A temporary file that outlives the process is not worth failing a squash over.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }

    private sealed record RangeCommit(string Sha, IReadOnlyList<string> Parents, string Subject);
}
