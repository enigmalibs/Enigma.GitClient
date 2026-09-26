using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Checkout;

/// <summary>
/// What to do with the local changes standing between the user and a checkout.
/// </summary>
public enum DirtyTreeResolution
{
    /// <summary>Do nothing; git will refuse if the changes are in the way.</summary>
    Keep,

    /// <summary>Put the changes on the stash first, so they can be brought back.</summary>
    Stash,

    /// <summary>Throw the changes away.</summary>
    Discard,

    /// <summary>Do not check anything out.</summary>
    Cancel,
}

/// <summary>
/// How a checkout is performed.
/// </summary>
public sealed record CheckoutOptions
{
    /// <summary>
    /// The options an ordinary checkout uses.
    /// </summary>
    public static readonly CheckoutOptions Default = new();

    /// <summary>
    /// Gets what to do with local changes.
    /// </summary>
    public DirtyTreeResolution Dirty { get; init; } = DirtyTreeResolution.Keep;

    /// <summary>
    /// Gets a value indicating whether to detach HEAD even when the revision names a branch.
    /// </summary>
    public bool Detach { get; init; }

    /// <summary>
    /// Gets the message put on the stash, when the changes are stashed.
    /// </summary>
    public string? StashMessage { get; init; }
}

/// <summary>
/// What a checkout did.
/// </summary>
/// <param name="Head">Where HEAD is now.</param>
/// <param name="Stashed">Whether local changes were put on the stash to make room.</param>
/// <param name="Discarded">Whether local changes were thrown away.</param>
public sealed record CheckoutResult(HeadState Head, bool Stashed, bool Discarded)
{
    /// <summary>
    /// Gets a value indicating whether the repository is now on a detached HEAD.
    /// </summary>
    public bool IsDetached => Head.IsDetached;
}

/// <summary>
/// Checks out anything a repository can be pointed at: a branch, a tag, a remote branch or a raw
/// commit.
/// </summary>
/// <remarks>
/// git will refuse a checkout that would overwrite local changes, and that refusal is the right
/// default — but "refused" is not an answer a user can act on. This service makes the three real
/// options explicit: keep the changes and let git decide, put them on the stash, or throw them
/// away. Which one is chosen belongs to the caller, because only the UI can ask.
/// </remarks>
public interface ICheckoutService
{
    /// <summary>
    /// Checks a revision out.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="revision">What to check out.</param>
    /// <param name="options">How to perform it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the checkout did.</returns>
    Task<CheckoutResult> CheckoutAsync(
        RepositoryHandle repository,
        string revision,
        CheckoutOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers whether checking out would detach HEAD — the revision is not a local branch.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="revision">The revision to check out.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true"/> when HEAD would end up detached.</returns>
    Task<bool> WouldDetachAsync(
        RepositoryHandle repository,
        string revision,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the work tree's changes on the stash.
    /// </summary>
    /// <param name="repository">The repository to write to.</param>
    /// <param name="message">The message the stash entry carries.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when something was stashed.</returns>
    Task<bool> StashAsync(
        RepositoryHandle repository,
        string? message = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ICheckoutService"/>, driving the real git executable.
/// </summary>
public sealed class CheckoutService : ICheckoutService
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitCommandFactory _commandFactory;
    private readonly IRefReader _refReader;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="runner">Runs the commands.</param>
    /// <param name="commandFactory">Builds the commands.</param>
    /// <param name="refReader">Reads where HEAD ended up.</param>
    public CheckoutService(IGitProcessRunner runner, IGitCommandFactory commandFactory, IRefReader refReader)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(refReader);

        _runner = runner;
        _commandFactory = commandFactory;
        _refReader = refReader;
    }

    /// <inheritdoc />
    public async Task<CheckoutResult> CheckoutAsync(
        RepositoryHandle repository,
        string revision,
        CheckoutOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        CheckoutOptions effective = options ?? CheckoutOptions.Default;

        if (effective.Dirty == DirtyTreeResolution.Cancel)
        {
            throw new GitOperationRefusedException("The checkout was cancelled.");
        }

        bool stashed = false;

        if (effective.Dirty == DirtyTreeResolution.Stash)
        {
            stashed = await StashAsync(repository, effective.StashMessage, cancellationToken).ConfigureAwait(false);
        }

        List<string> arguments = ["checkout"];

        if (effective.Dirty == DirtyTreeResolution.Discard)
        {
            // --force is what discards: it overwrites the work tree from the revision being checked
            // out. It is only ever reached from a confirmation that named the files at risk.
            arguments.Add("--force");
        }

        if (effective.Detach)
        {
            arguments.Add("--detach");
        }

        arguments.Add(revision);

        await RunAsync(repository, arguments, cancellationToken).ConfigureAwait(false);

        HeadState head = await _refReader.GetHeadStateAsync(repository, cancellationToken).ConfigureAwait(false);

        return new CheckoutResult(head, stashed, effective.Dirty == DirtyTreeResolution.Discard);
    }

    /// <inheritdoc />
    public async Task<bool> WouldDetachAsync(
        RepositoryHandle repository,
        string revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        GitCommand command = _commandFactory.Create(
            repository.WorkTreePath,
            ["show-ref", "--verify", "--quiet", $"{GitBranch.LocalPrefix}{revision}"]);

        GitResult result = await _runner.RunAsync(command, throwOnError: false, cancellationToken)
            .ConfigureAwait(false);

        // Only a local branch keeps HEAD attached. A tag, a remote branch or a raw sha all detach,
        // and that is worth saying out loud before it happens rather than afterwards.
        return result.ExitCode != 0;
    }

    /// <inheritdoc />
    public async Task<bool> StashAsync(
        RepositoryHandle repository,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        List<string> arguments = ["stash", "push", "--include-untracked"];

        if (message is { Length: > 0 })
        {
            arguments.Add("--message");
            arguments.Add(message);
        }

        GitCommand command = _commandFactory.Create(repository.WorkTreePath, arguments);
        GitResult result = await _runner.RunAsync(command, throwOnError: true, cancellationToken)
            .ConfigureAwait(false);

        // git says so rather than failing when there was nothing to stash, and the caller needs to
        // know, because "your changes are on the stash" is a lie if nothing was.
        return !result.StandardOutput.Contains("No local changes to save", StringComparison.Ordinal);
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
