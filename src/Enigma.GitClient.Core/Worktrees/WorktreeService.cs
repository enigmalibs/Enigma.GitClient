using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Branches;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Worktrees;

/// <summary>
/// Creates, lists and removes worktrees: more working directories of the same repository, each on a
/// branch of its own.
/// </summary>
public interface IWorktreeService
{
    /// <summary>
    /// Lists every working tree of a repository.
    /// </summary>
    /// <param name="repository">The repository, opened from any of its working trees.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The working trees, the main one first, then the linked ones in git's order.</returns>
    /// <remarks>
    /// <c>git worktree list --porcelain</c>, which every git from the 2.20 floor reads. A folder that is
    /// gone is <see cref="GitWorktree.IsMissing"/> whether or not git says so — git before 2.31 does
    /// not.
    /// </remarks>
    Task<IReadOnlyList<GitWorktree>> ListAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the uncommitted changes in a working tree that would stop git removing it.
    /// </summary>
    /// <param name="worktree">The working tree.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// How many entries <c>git status --porcelain</c> lists there — modified, staged and untracked
    /// files, and changed submodules; 0 for a folder that is gone. Ignored files do not count: git
    /// deletes them with the folder.
    /// </returns>
    /// <exception cref="GitCommandException">git could not read the working tree.</exception>
    Task<int> CountChangesAsync(GitWorktree worktree, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a linked worktree: its folder and git's record of it.
    /// </summary>
    /// <param name="repository">The repository, opened from another of its working trees.</param>
    /// <param name="worktreePath">The worktree's folder, as a full path.</param>
    /// <param name="force">
    /// Whether to remove it with uncommitted changes in it (<see cref="CountChangesAsync"/>), which are
    /// lost.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the worktree is gone.</returns>
    /// <remarks>
    /// The branch the worktree had checked out is left alone: deleting it is a separate step. A worktree
    /// whose folder is already gone is removed all the same, which clears git's record of it.
    /// </remarks>
    /// <exception cref="GitOperationRefusedException">
    /// The folder is not a worktree of the repository, is its main working tree, is the working tree the
    /// repository was opened from, or is locked. Every refusal comes before git runs.
    /// </exception>
    Task RemoveAsync(
        RepositoryHandle repository,
        string worktreePath,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a worktree on a new branch.
    /// </summary>
    /// <param name="repository">The repository the worktree belongs to.</param>
    /// <param name="path">
    /// The folder it goes in: one that does not exist yet, or an empty one, outside the repository
    /// (<see cref="WorktreePaths.Validate"/>).
    /// </param>
    /// <param name="branch">The branch to create and check out there, without <c>refs/heads/</c>.</param>
    /// <param name="startPoint">What the branch starts from — a sha, a branch, a tag.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the worktree exists, on the new branch.</returns>
    /// <remarks>
    /// The repository's own HEAD and working tree are left alone. A branch started from a remote
    /// branch tracks it, as git sets one up by default.
    /// </remarks>
    /// <exception cref="GitOperationRefusedException">
    /// The branch name is not valid, the branch exists already, or the folder cannot take a worktree.
    /// </exception>
    Task AddAsync(
        RepositoryHandle repository,
        string path,
        string branch,
        string startPoint,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IWorktreeService"/>, driving the real git executable.
/// </summary>
public sealed class WorktreeService : IWorktreeService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly IBranchService _branches;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="branches">Answers whether the branch exists, before and after git runs.</param>
    public WorktreeService(IGitProcessRunner runner, IGitCommandFactory commandFactory, IBranchService branches)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(branches);

        _runner = runner;
        _commandFactory = commandFactory;
        _branches = branches;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitWorktree>> ListAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        GitCommand command = _commandFactory.Create(repository.WorkTreePath, "worktree", "list", "--porcelain");
        GitResult result = await _runner.RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<GitWorktree> listed = WorktreeListParser.Parse(result.StandardOutput);
        List<GitWorktree> worktrees = new(listed.Count);

        foreach (GitWorktree worktree in listed)
        {
            worktrees.Add(worktree.IsMissing || Directory.Exists(worktree.Path)
                ? worktree
                : worktree with { IsMissing = true });
        }

        return worktrees;
    }

    /// <inheritdoc />
    public async Task<int> CountChangesAsync(GitWorktree worktree, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(worktree);

        if (worktree.IsMissing || worktree.IsBare || !Directory.Exists(worktree.Path))
        {
            return 0;
        }

        // The very status git worktree remove runs before it refuses a worktree that is not clean.
        GitCommand command = _commandFactory.Create(
            worktree.Path,
            "status",
            "--porcelain",
            "--ignore-submodules=none");

        GitResult result = await _runner.RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);

        int count = 0;

        foreach (string line in result.SplitOutput())
        {
            if (line.Length > 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(
        RepositoryHandle repository,
        string worktreePath,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);

        IReadOnlyList<GitWorktree> worktrees = await ListAsync(repository, cancellationToken).ConfigureAwait(false);
        GitWorktree worktree = FindRemovable(repository, worktrees, worktreePath);

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            BuildRemoveArguments(worktree.Path, force));

        await _runner.RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the worktree a removal is for, and refuses one that cannot be removed.
    /// </summary>
    /// <param name="repository">The repository, opened from one of its working trees.</param>
    /// <param name="worktrees">Its working trees, as just listed.</param>
    /// <param name="worktreePath">The folder asked for.</param>
    /// <returns>The worktree to remove.</returns>
    /// <exception cref="GitOperationRefusedException">
    /// The folder is not one of the worktrees, or is one that cannot be removed.
    /// </exception>
    public static GitWorktree FindRemovable(
        RepositoryHandle repository,
        IReadOnlyList<GitWorktree> worktrees,
        string worktreePath)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(worktrees);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);

        string full = Path.IsPathFullyQualified(worktreePath) ? WorktreeListParser.Normalise(worktreePath) : worktreePath;
        GitWorktree? worktree = null;

        foreach (GitWorktree candidate in worktrees)
        {
            if (string.Equals(candidate.Path, full, PathComparison))
            {
                worktree = candidate;
                break;
            }
        }

        if (worktree is null)
        {
            throw new GitOperationRefusedException($"\"{worktreePath}\" is not a worktree of this repository.");
        }

        if (worktree.IsMain)
        {
            throw new GitOperationRefusedException(
                $"\"{worktree.Path}\" is the repository's main working tree, which cannot be removed.");
        }

        if (string.Equals(worktree.Path, repository.WorkTreePath, PathComparison))
        {
            throw new GitOperationRefusedException(
                $"\"{worktree.Path}\" is the working tree the repository is open in. Remove it from another working tree of the repository.");
        }

        if (worktree.IsLocked)
        {
            string reason = worktree.LockReason.Length > 0 ? $" ({worktree.LockReason})" : string.Empty;

            throw new GitOperationRefusedException(
                $"The worktree at \"{worktree.Path}\" is locked{reason}. Unlock it with git worktree unlock first.");
        }

        return worktree;
    }

    /// <inheritdoc />
    public async Task AddAsync(
        RepositoryHandle repository,
        string path,
        string branch,
        string startPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(startPoint);

        RefNameValidation name = RefNameValidator.ValidateBranch(branch);

        if (!name.IsValid)
        {
            throw new GitOperationRefusedException(name.Message);
        }

        if (await _branches.ExistsAsync(repository, branch, cancellationToken).ConfigureAwait(false))
        {
            throw new GitOperationRefusedException($"A branch called \"{branch}\" already exists.");
        }

        string folder = WorktreePaths.Validate(repository.WorkTreePath, path);

        if (folder.Length > 0)
        {
            throw new GitOperationRefusedException(folder);
        }

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            BuildAddArguments(Path.GetFullPath(path), branch, startPoint));

        try
        {
            await _runner.RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);
        }
        catch (GitCommandException)
        {
            await RemoveLeftBehindBranchAsync(repository, branch).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Builds the argument vector that creates a worktree on a new branch.
    /// </summary>
    /// <param name="path">The worktree's folder, as a full path.</param>
    /// <param name="branch">The branch to create.</param>
    /// <param name="startPoint">What the branch starts from.</param>
    /// <returns>The arguments.</returns>
    /// <remarks>
    /// The <c>--</c> ends the options, so the folder and the start point are only ever read as what
    /// they are. A branch or a start point starting with a dash is refused all the same: neither is a
    /// name git would accept, and refusing says so before git does.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// An argument is blank, the folder is not a full path, or the branch or the start point starts
    /// with a dash.
    /// </exception>
    public static List<string> BuildAddArguments(string path, string branch, string startPoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        ArgumentException.ThrowIfNullOrWhiteSpace(startPoint);

        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A worktree's folder must be a full path.", nameof(path));
        }

        if (branch.StartsWith('-'))
        {
            throw new ArgumentException("A branch name cannot start with a dash.", nameof(branch));
        }

        if (startPoint.StartsWith('-'))
        {
            throw new ArgumentException("A start point cannot start with a dash.", nameof(startPoint));
        }

        return ["worktree", "add", "-b", branch, "--", path, startPoint];
    }

    /// <summary>
    /// Builds the argument vector that removes a worktree.
    /// </summary>
    /// <param name="path">The worktree's folder, as a full path.</param>
    /// <param name="force">Whether to remove it with uncommitted changes in it.</param>
    /// <returns>The arguments.</returns>
    /// <remarks>
    /// One <c>--force</c> removes a worktree that is not clean. A locked one would take two, which is
    /// never passed: a locked worktree is refused instead.
    /// </remarks>
    /// <exception cref="ArgumentException">The folder is blank or not a full path.</exception>
    public static List<string> BuildRemoveArguments(string path, bool force)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A worktree's folder must be a full path.", nameof(path));
        }

        return force ? ["worktree", "remove", "--force", "--", path] : ["worktree", "remove", "--", path];
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Deletes the branch a failed <c>worktree add</c> created anyway.
    /// </summary>
    /// <remarks>
    /// git creates the branch before the folder, so a folder it cannot make — under a path that runs
    /// through a file, on a drive that is not there — leaves the branch behind, at the start point with
    /// nothing on it. The branch did not exist before (<see cref="AddAsync"/> refused otherwise), so
    /// deleting it loses nothing. Not cancellable, and never thrown from: the failure the caller hears
    /// about is git's first one, and a branch git did check out somewhere is refused by <c>-D</c> on its
    /// own.
    /// </remarks>
    private async Task RemoveLeftBehindBranchAsync(RepositoryHandle repository, string branch)
    {
        if (!await _branches.ExistsAsync(repository, branch, CancellationToken.None).ConfigureAwait(false))
        {
            return;
        }

        GitCommand delete = _commandFactory.Create(repository.WorkTreePath, ["branch", "-D", branch]);

        await _runner.RunAsync(delete, throwOnError: false, CancellationToken.None).ConfigureAwait(false);
    }
}
