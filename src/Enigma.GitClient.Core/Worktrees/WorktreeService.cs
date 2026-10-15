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
/// Creates worktrees: more working directories of the same repository, each on a branch of its own.
/// </summary>
public interface IWorktreeService
{
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
