using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Branches;

/// <summary>
/// Creates, renames, deletes and checks out branches.
/// </summary>
/// <remarks>
/// Every method that could lose work refuses first and asks for an explicit <c>force</c> second.
/// Losing commits silently is the one thing a git client must never do, and "the user clicked the
/// button" is not consent when the button did not say what it would destroy.
/// </remarks>
public interface IBranchService
{
    /// <summary>
    /// Creates a branch.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="name">The new branch's name, without <c>refs/heads/</c>.</param>
    /// <param name="startPoint">
    /// What the branch points at — a sha, a branch, a tag. <see langword="null"/> means HEAD.
    /// </param>
    /// <param name="checkout">Whether to check the new branch out.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the branch exists.</returns>
    Task CreateAsync(
        RepositoryHandle repository,
        string name,
        string? startPoint = null,
        bool checkout = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a branch.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="oldName">The branch's current name.</param>
    /// <param name="newName">The name it should have.</param>
    /// <param name="force">Whether to overwrite an existing branch of the new name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the branch is renamed.</returns>
    Task RenameAsync(
        RepositoryHandle repository,
        string oldName,
        string newName,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a local branch.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="name">The branch to delete.</param>
    /// <param name="force">Whether to delete it even if it holds unmerged commits.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the branch is gone.</returns>
    Task DeleteAsync(
        RepositoryHandle repository,
        string name,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a branch on a remote.
    /// </summary>
    /// <param name="repository">The repository to write from.</param>
    /// <param name="remote">The remote's name.</param>
    /// <param name="name">The branch's name on the remote, without <c>refs/heads/</c>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the remote has accepted the deletion.</returns>
    Task DeleteRemoteAsync(
        RepositoryHandle repository,
        string remote,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Points a branch at an upstream, or clears the one it has.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="name">The branch to change.</param>
    /// <param name="upstream">
    /// The upstream, as <c>remote/branch</c>. <see langword="null"/> clears it.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the upstream is set.</returns>
    Task SetUpstreamAsync(
        RepositoryHandle repository,
        string name,
        string? upstream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a local branch out.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="name">The branch to check out.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once HEAD points at the branch.</returns>
    Task CheckoutAsync(RepositoryHandle repository, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a remote branch out by creating a local branch that tracks it.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="remoteBranch">The remote branch, as <c>remote/branch</c>.</param>
    /// <param name="localName">
    /// The local branch's name. <see langword="null"/> uses the remote branch's own name.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The local branch's name.</returns>
    Task<string> CheckoutRemoteAsync(
        RepositoryHandle repository,
        string remoteBranch,
        string? localName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers whether every commit on one branch is already on another.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="branch">The branch that might be merged.</param>
    /// <param name="into">The branch it might be merged into. <see langword="null"/> means HEAD.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true"/> when nothing would be lost by deleting the branch.</returns>
    Task<bool> IsMergedAsync(
        RepositoryHandle repository,
        string branch,
        string? into = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the commits a branch holds that another does not, newest first.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="branch">The branch that might hold unmerged work.</param>
    /// <param name="into">The branch to compare against. <see langword="null"/> means HEAD.</param>
    /// <param name="limit">The most subjects to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The subjects of the commits that would be lost, newest first.</returns>
    Task<IReadOnlyList<string>> GetUnmergedCommitsAsync(
        RepositoryHandle repository,
        string branch,
        string? into = null,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers whether a local branch already exists.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="name">The branch's name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true"/> when the branch exists.</returns>
    Task<bool> ExistsAsync(RepositoryHandle repository, string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IBranchService"/>, driving the real git executable.
/// </summary>
public sealed class BranchService : IBranchService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly IRefReader _refReader;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="refReader">Reads the current head, for the guards.</param>
    public BranchService(IGitProcessRunner runner, IGitCommandFactory commandFactory, IRefReader refReader)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(refReader);

        _runner = runner;
        _commandFactory = commandFactory;
        _refReader = refReader;
    }

    /// <inheritdoc />
    public async Task CreateAsync(
        RepositoryHandle repository,
        string name,
        string? startPoint = null,
        bool checkout = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        Require(RefNameValidator.ValidateBranch(name));

        if (await ExistsAsync(repository, name, cancellationToken).ConfigureAwait(false))
        {
            throw new GitOperationRefusedException($"A branch called \"{name}\" already exists.");
        }

        List<string> arguments = checkout ? ["checkout", "-b", name] : ["branch", name];

        if (startPoint is { Length: > 0 })
        {
            arguments.Add(startPoint);
        }

        await RunAsync(repository, arguments, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RenameAsync(
        RepositoryHandle repository,
        string oldName,
        string newName,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);

        Require(RefNameValidator.ValidateBranch(newName));

        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return;
        }

        if (!force && await ExistsAsync(repository, newName, cancellationToken).ConfigureAwait(false))
        {
            throw new GitOperationRefusedException(
                $"A branch called \"{newName}\" already exists. Choose another name, or replace it deliberately.");
        }

        await RunAsync(repository, ["branch", force ? "-M" : "-m", oldName, newName], cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        RepositoryHandle repository,
        string name,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        HeadState head = await _refReader.GetHeadStateAsync(repository, cancellationToken).ConfigureAwait(false);

        // Deleting the branch you are standing on leaves the repository on a detached HEAD with no
        // way back to the name. git refuses it too; refusing here says why.
        if (string.Equals(head.BranchName, name, StringComparison.Ordinal))
        {
            throw new GitOperationRefusedException(
                $"\"{name}\" is the branch you have checked out. Switch to another branch first.");
        }

        if (!force && !await IsMergedAsync(repository, name, into: null, cancellationToken).ConfigureAwait(false))
        {
            throw new GitOperationRefusedException(
                $"\"{name}\" holds commits that are not on the current branch. Deleting it would lose them.");
        }

        await RunAsync(repository, ["branch", force ? "-D" : "-d", name], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteRemoteAsync(
        RepositoryHandle repository,
        string remote,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(remote);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await RunAsync(repository, ["push", remote, "--delete", name], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetUpstreamAsync(
        RepositoryHandle repository,
        string name,
        string? upstream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        List<string> arguments = upstream is { Length: > 0 }
            ? ["branch", $"--set-upstream-to={upstream}", name]
            : ["branch", "--unset-upstream", name];

        await RunAsync(repository, arguments, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CheckoutAsync(
        RepositoryHandle repository,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await RunAsync(repository, ["checkout", name], cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> CheckoutRemoteAsync(
        RepositoryHandle repository,
        string remoteBranch,
        string? localName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteBranch);

        string local = localName is { Length: > 0 } ? localName : LocalNameFor(remoteBranch);

        Require(RefNameValidator.ValidateBranch(local));

        if (await ExistsAsync(repository, local, cancellationToken).ConfigureAwait(false))
        {
            throw new GitOperationRefusedException(
                $"A branch called \"{local}\" already exists. Check it out, or give the new branch another name.");
        }

        await RunAsync(repository, ["checkout", "-b", local, "--track", remoteBranch], cancellationToken)
            .ConfigureAwait(false);

        return local;
    }

    /// <inheritdoc />
    public async Task<bool> IsMergedAsync(
        RepositoryHandle repository,
        string branch,
        string? into = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            ["merge-base", "--is-ancestor", branch, into is { Length: > 0 } ? into : "HEAD"]);

        // Exit status is the answer here, not an error: 0 means "already contained", 1 means "not".
        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetUnmergedCommitsAsync(
        RepositoryHandle repository,
        string branch,
        string? into = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        string target = into is { Length: > 0 } ? into : "HEAD";

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            [
                "log",
                "--format=%s",
                $"--max-count={limit.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"{target}..{branch}",
            ]);

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? result.SplitOutput() : [];
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        RepositoryHandle repository,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            ["show-ref", "--verify", "--quiet", $"{GitBranch.LocalPrefix}{name}"]);

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    /// <summary>
    /// Strips the remote from a remote branch's name, so <c>origin/topic</c> becomes <c>topic</c>.
    /// </summary>
    /// <param name="remoteBranch">The remote branch, as <c>remote/branch</c>.</param>
    /// <returns>The local branch name it maps onto.</returns>
    public static string LocalNameFor(string remoteBranch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteBranch);

        int separator = remoteBranch.IndexOf('/', StringComparison.Ordinal);

        return separator < 0 || separator == remoteBranch.Length - 1
            ? remoteBranch
            : remoteBranch[(separator + 1)..];
    }

    private static void Require(RefNameValidation validation)
    {
        if (!validation.IsValid)
        {
            throw new GitOperationRefusedException(validation.Message);
        }
    }

    private async Task RunAsync(
        RepositoryHandle repository,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        GitCommand command = _commandFactory.Create(repository.WorkTreePath, arguments);

        await _runner.RunAsync(command, throwOnError: true, cancellationToken).ConfigureAwait(false);
    }
}
