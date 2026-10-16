using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Worktrees;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Default <see cref="IRepositoryContext"/>. One instance for the whole application.
/// </summary>
public sealed class RepositoryContext : ObservableObject, IRepositoryContext, IDisposable
{
    private static readonly IReadOnlyList<GitWorktree> NoWorktrees = [];

    private readonly IRefReader _refReader;
    private readonly IWorktreeService _worktreeService;
    private readonly ILogger<RepositoryContext> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private CancellationTokenSource _lifetime = new();
    private int _writes;
    private bool _disposed;

    // The other working trees by the branch each has checked out, as of the last read.
    private Dictionary<string, GitWorktree> _worktreesByBranch = new(StringComparer.Ordinal);

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="refReader">Reads the reference state after every change.</param>
    /// <param name="worktreeService">Lists the repository's working trees with the references.</param>
    /// <param name="logger">Receives failures that must not take the shell down.</param>
    public RepositoryContext(IRefReader refReader, IWorktreeService worktreeService, ILogger<RepositoryContext> logger)
    {
        ArgumentNullException.ThrowIfNull(refReader);
        ArgumentNullException.ThrowIfNull(worktreeService);
        ArgumentNullException.ThrowIfNull(logger);

        _refReader = refReader;
        _worktreeService = worktreeService;
        _logger = logger;
    }

    /// <inheritdoc />
    public RepositoryHandle? Repository
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsRepositoryOpen));
            }
        }
    }

    /// <inheritdoc />
    public bool IsRepositoryOpen => Repository is not null;

    /// <inheritdoc />
    public HeadState? Head { get; private set => SetProperty(ref field, value); }

    /// <inheritdoc />
    public RefCollection Refs { get; private set => SetProperty(ref field, value); } = RefCollection.Empty;

    /// <inheritdoc />
    public RefDecorationIndex Decorations { get; private set => SetProperty(ref field, value); }
        = RefDecorationIndex.Empty;

    /// <inheritdoc />
    public IReadOnlyList<GitWorktree> Worktrees
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                _worktreesByBranch = IndexByBranch(value, Repository);
                OnPropertyChanged(nameof(IsLinkedWorktree));
                OnPropertyChanged(nameof(MainWorktreePath));
            }
        }
    } = NoWorktrees;

    /// <inheritdoc />
    public bool IsLinkedWorktree
        => Repository is { } repository
            && MainWorktreePath is { } main
            && !SamePath(main, repository.WorkTreePath);

    /// <inheritdoc />
    public string? MainWorktreePath => Worktrees is [{ IsMain: true } main, ..] ? main.Path : null;

    /// <inheritdoc />
    public GitWorktree? WorktreeOf(string branch)
    {
        ArgumentNullException.ThrowIfNull(branch);

        return _worktreesByBranch.GetValueOrDefault(branch);
    }

    /// <inheritdoc />
    public GitCommit? SelectedCommit { get; set => SetProperty(ref field, value); }

    /// <inheritdoc />
    public CancellationToken RepositoryLifetime => _lifetime.Token;

    /// <inheritdoc />
    public bool IsWriting => Volatile.Read(ref _writes) > 0;

    /// <inheritdoc />
    public event EventHandler<RepositoryChangedEventArgs>? RepositoryChanged;

    /// <inheritdoc />
    public event EventHandler? WriteEnded;

    /// <inheritdoc />
    public event EventHandler? StateRefreshed;

    /// <inheritdoc />
    public async Task OpenAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        RepositoryHandle? previous = Repository;

        if (previous is not null && previous.Equals(repository))
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        ReplaceLifetime();

        Repository = repository;
        SelectedCommit = null;
        Refs = RefCollection.Empty;
        Decorations = RefDecorationIndex.Empty;
        Worktrees = NoWorktrees;
        Head = null;

        RepositoryChanged?.Invoke(this, new RepositoryChangedEventArgs(previous, repository));

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public void Close()
    {
        if (Repository is null)
        {
            return;
        }

        RepositoryHandle previous = Repository;

        ReplaceLifetime();

        Repository = null;
        SelectedCommit = null;
        Head = null;
        Refs = RefCollection.Empty;
        Decorations = RefDecorationIndex.Empty;
        Worktrees = NoWorktrees;

        RepositoryChanged?.Invoke(this, new RepositoryChangedEventArgs(previous, null));
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        RepositoryHandle? repository = Repository;

        if (repository is null)
        {
            return;
        }

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

        try
        {
            // Both reads at once: the worktrees are one more git process, and never worth waiting for
            // the references to finish first.
            Task<RepositoryRefState> reading = _refReader.GetStateAsync(repository, linked.Token);
            Task<IReadOnlyList<GitWorktree>> listing = ReadWorktreesAsync(repository, linked.Token);

            RepositoryRefState state = await reading.ConfigureAwait(true);
            IReadOnlyList<GitWorktree> worktrees = await listing.ConfigureAwait(true);

            // The repository may have been swapped while the read was in flight; a stale answer must
            // never be published.
            if (!ReferenceEquals(repository, Repository))
            {
                return;
            }

            Refs = state.Refs;
            Head = state.Head;
            Decorations = state.Decorations;
            Worktrees = worktrees;

            StateRefreshed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository is swapped or the application is shutting down.
        }
        catch (Exception exception)
        {
            // A repository that cannot be read must not take the shell down; the page that asked
            // for the refresh reports it, and the state simply stays as it was.
            _logger.LogWarning(exception, "Reading the reference state of {Repository} failed", repository.WorkTreePath);
        }
    }

    /// <inheritdoc />
    public async Task RunExclusiveAsync(
        Func<RepositoryHandle, CancellationToken, Task> operation,
        bool refreshAfter = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await RunExclusiveAsync<object?>(
                async (repository, token) =>
                {
                    await operation(repository, token).ConfigureAwait(true);
                    return null;
                },
                refreshAfter,
                cancellationToken)
            .ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<TResult> RunExclusiveAsync<TResult>(
        Func<RepositoryHandle, CancellationToken, Task<TResult>> operation,
        bool refreshAfter = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        RepositoryHandle repository = Repository
            ?? throw new InvalidOperationException("No repository is open.");

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

        await _writeLock.WaitAsync(linked.Token).ConfigureAwait(true);
        BeginWrite();

        try
        {
            TResult result = await operation(repository, linked.Token).ConfigureAwait(true);

            if (refreshAfter)
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
            }

            return result;
        }
        finally
        {
            EndWrite();
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryRunExclusiveAsync(
        Func<RepositoryHandle, CancellationToken, Task> operation,
        bool refreshAfter = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (Repository is not { } repository || !_writeLock.Wait(0, CancellationToken.None))
        {
            return false;
        }

        BeginWrite();

        try
        {
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

            await operation(repository, linked.Token).ConfigureAwait(true);

            if (refreshAfter)
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
            }

            return true;
        }
        finally
        {
            EndWrite();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _lifetime.Cancel();
        _lifetime.Dispose();
        _writeLock.Dispose();
    }

    /// <summary>
    /// Marks a write as running, once the lock is held.
    /// </summary>
    private void BeginWrite() => Interlocked.Increment(ref _writes);

    /// <summary>
    /// Marks the write as over, lets the lock go and says so — whether the write succeeded or not.
    /// </summary>
    private void EndWrite()
    {
        Interlocked.Decrement(ref _writes);
        _writeLock.Release();

        WriteEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Lists the working trees, which is never worth failing a refresh for: a list that cannot be read
    /// is logged and left empty.
    /// </summary>
    private async Task<IReadOnlyList<GitWorktree>> ReadWorktreesAsync(RepositoryHandle repository, CancellationToken cancellationToken)
    {
        try
        {
            return await _worktreeService.ListAsync(repository, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Listing the worktrees of {Repository} failed", repository.WorkTreePath);
            return NoWorktrees;
        }
    }

    /// <summary>
    /// Indexes the working trees other than the open one by the branch each has checked out.
    /// </summary>
    private static Dictionary<string, GitWorktree> IndexByBranch(IReadOnlyList<GitWorktree> worktrees, RepositoryHandle? repository)
    {
        Dictionary<string, GitWorktree> byBranch = new(StringComparer.Ordinal);

        if (repository is null)
        {
            return byBranch;
        }

        foreach (GitWorktree worktree in worktrees)
        {
            if (worktree.Branch is { Length: > 0 } branch && !SamePath(worktree.Path, repository.WorkTreePath))
            {
                byBranch.TryAdd(branch, worktree);
            }
        }

        return byBranch;
    }

    private static bool SamePath(string first, string second)
        => string.Equals(
            first,
            second,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void ReplaceLifetime()
    {
        CancellationTokenSource previous = _lifetime;
        _lifetime = new CancellationTokenSource();

        previous.Cancel();
        previous.Dispose();
    }
}
