using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// The repository the whole application is looking at, and the single place its write operations
/// are serialised.
/// </summary>
/// <remarks>
/// Every page observes this rather than holding its own repository handle, so opening a different
/// repository updates the entire shell at once and cancels whatever the previous one had in flight.
/// </remarks>
public interface IRepositoryContext : INotifyPropertyChanged
{
    /// <summary>
    /// Gets the open repository, or <see langword="null"/> when none is open.
    /// </summary>
    RepositoryHandle? Repository { get; }

    /// <summary>
    /// Gets a value indicating whether a repository is open.
    /// </summary>
    bool IsRepositoryOpen { get; }

    /// <summary>
    /// Gets where HEAD is, or <see langword="null"/> when no repository is open.
    /// </summary>
    HeadState? Head { get; }

    /// <summary>
    /// Gets every reference in the open repository.
    /// </summary>
    RefCollection Refs { get; }

    /// <summary>
    /// Gets the commit-to-references index the graph draws badges from.
    /// </summary>
    RefDecorationIndex Decorations { get; }

    /// <summary>
    /// Gets or sets the commit the history view has selected, which the details panel follows.
    /// </summary>
    GitCommit? SelectedCommit { get; set; }

    /// <summary>
    /// Gets a token cancelled whenever the open repository changes or the application shuts down.
    /// Pass it to any work whose result would be meaningless against a different repository.
    /// </summary>
    CancellationToken RepositoryLifetime { get; }

    /// <summary>
    /// Raised after a different repository has been opened or the current one closed.
    /// </summary>
    event EventHandler<RepositoryChangedEventArgs>? RepositoryChanged;

    /// <summary>
    /// Raised after the reference state has been re-read, so views can refresh without polling.
    /// </summary>
    event EventHandler? StateRefreshed;

    /// <summary>
    /// Opens a repository, replacing whatever was open.
    /// </summary>
    /// <param name="repository">The repository to open.</param>
    /// <param name="cancellationToken">Cancels the initial state read.</param>
    /// <returns>A task that completes once the first state read has finished.</returns>
    Task OpenAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the open repository, if any.
    /// </summary>
    void Close();

    /// <summary>
    /// Re-reads the reference state of the open repository.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes once the state has been refreshed.</returns>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs an operation that writes to the repository, serialised against every other write.
    /// </summary>
    /// <param name="operation">The operation, given a token linked to the repository's lifetime.</param>
    /// <param name="refreshAfter">Whether to re-read the reference state when the operation finishes.</param>
    /// <param name="cancellationToken">Cancels waiting for the lock and the operation itself.</param>
    /// <returns>A task that completes when the operation and any refresh have finished.</returns>
    Task RunExclusiveAsync(
        Func<RepositoryHandle, CancellationToken, Task> operation,
        bool refreshAfter = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs an operation that writes to the repository and returns a result, serialised against
    /// every other write.
    /// </summary>
    /// <typeparam name="TResult">The operation's result type.</typeparam>
    /// <param name="operation">The operation, given a token linked to the repository's lifetime.</param>
    /// <param name="refreshAfter">Whether to re-read the reference state when the operation finishes.</param>
    /// <param name="cancellationToken">Cancels waiting for the lock and the operation itself.</param>
    /// <returns>The operation's result.</returns>
    Task<TResult> RunExclusiveAsync<TResult>(
        Func<RepositoryHandle, CancellationToken, Task<TResult>> operation,
        bool refreshAfter = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a write the way <see cref="RunExclusiveAsync"/> does — but only if no other write is in
    /// progress; otherwise it does nothing at all.
    /// </summary>
    /// <param name="operation">The operation, given a token linked to the repository's lifetime.</param>
    /// <param name="refreshAfter">Whether to re-read the reference state when the operation finishes.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// <see langword="true"/> when the operation ran; <see langword="false"/> when another write held
    /// the repository, or none is open.
    /// </returns>
    /// <remarks>
    /// For work nobody asked for — the automatic refresh — which must never queue up behind the reader's
    /// own operations: when they are busy, the next tick will do.
    /// </remarks>
    Task<bool> TryRunExclusiveAsync(
        Func<RepositoryHandle, CancellationToken, Task> operation,
        bool refreshAfter = true,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Carries which repository the context moved to.
/// </summary>
/// <param name="Previous">The repository that was open before, if any.</param>
/// <param name="Current">The repository that is open now, or <see langword="null"/> after a close.</param>
public sealed record RepositoryChangedEventArgs(RepositoryHandle? Previous, RepositoryHandle? Current);
