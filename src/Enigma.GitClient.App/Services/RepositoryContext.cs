using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// Default <see cref="IRepositoryContext"/>. One instance for the whole application.
/// </summary>
public sealed class RepositoryContext : ObservableObject, IRepositoryContext, IDisposable
{
    private readonly IRefReader _refReader;
    private readonly ILogger<RepositoryContext> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private CancellationTokenSource _lifetime = new();
    private bool _disposed;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="refReader">Reads the reference state after every change.</param>
    /// <param name="logger">Receives failures that must not take the shell down.</param>
    public RepositoryContext(IRefReader refReader, ILogger<RepositoryContext> logger)
    {
        ArgumentNullException.ThrowIfNull(refReader);
        ArgumentNullException.ThrowIfNull(logger);

        _refReader = refReader;
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
    public GitCommit? SelectedCommit { get; set => SetProperty(ref field, value); }

    /// <inheritdoc />
    public CancellationToken RepositoryLifetime => _lifetime.Token;

    /// <inheritdoc />
    public event EventHandler<RepositoryChangedEventArgs>? RepositoryChanged;

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
            RepositoryRefState state = await _refReader.GetStateAsync(repository, linked.Token).ConfigureAwait(true);

            // The repository may have been swapped while the read was in flight; a stale answer must
            // never be published.
            if (!ReferenceEquals(repository, Repository))
            {
                return;
            }

            Refs = state.Refs;
            Head = state.Head;
            Decorations = state.Decorations;

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
            _writeLock.Release();
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

    private void ReplaceLifetime()
    {
        CancellationTokenSource previous = _lifetime;
        _lifetime = new CancellationTokenSource();

        previous.Cancel();
        previous.Dispose();
    }
}
