using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Tags;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Which tags the remote a tag is pushed to has, as it last said — which is how a tag's badge knows
/// whether the tag was pushed.
/// </summary>
/// <remarks>
/// <para>
/// git keeps no remote-tracking tags, so this is the one place that asks the remote: when a repository
/// has opened and its references are read, after every automatic or requested refresh whose fetch ran
/// — the network was just reachable, and someone else may have pushed or deleted a tag — and when asked
/// (<see cref="RefreshAsync"/>), after the reader pushed or deleted one. Never on a refresh of what
/// changed on disk: nothing there can change what the remote has.
/// </para>
/// <para>
/// The remote is the one a tag's push goes to (<see cref="SyncOperations.TagRemote"/>), so the mark and
/// the badge's push and delete agree. A repository without that remote is not asked.
/// </para>
/// <para>
/// Quiet, like the automatic fetch: a read that fails is logged and keeps what the same remote said
/// last — a pushed tag stays pushed while the laptop is offline, as a remote-tracking branch does. A
/// repository the remote never answered for is unknown, which no badge marks.
/// </para>
/// </remarks>
public interface IRemoteTags
{
    /// <summary>
    /// Gets the remote that answered last, or <see langword="null"/> while unknown.
    /// </summary>
    string? Remote { get; }

    /// <summary>
    /// Gets each tag the remote has, by name, with the commit it points at; <see langword="null"/> while
    /// unknown.
    /// </summary>
    IReadOnlyDictionary<string, string>? Tags { get; }

    /// <summary>
    /// Raised, on the UI thread, when what the remote says has changed — or has become unknown.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Says whether the remote has a tag on a commit.
    /// </summary>
    /// <param name="tag">The tag's name.</param>
    /// <param name="commit">The commit it points at here.</param>
    /// <returns>
    /// The remote's name when it has the tag on that commit; <see langword="null"/> when it has not,
    /// has it on another commit, or is unknown.
    /// </returns>
    string? RemoteOf(string tag, string commit);

    /// <summary>
    /// Asks the remote again.
    /// </summary>
    /// <returns>A task that completes once the answer is in, or the read failed or was overtaken.</returns>
    Task RefreshAsync();
}

/// <summary>
/// Default <see cref="IRemoteTags"/>.
/// </summary>
public sealed class RemoteTags : IRemoteTags, IDisposable
{
    private readonly IRepositoryContext _context;
    private readonly IAutoRefreshService _autoRefresh;
    private readonly ITagService _tags;
    private readonly IRemoteReader _remotes;
    private readonly ILogger<RemoteTags> _logger;

    // A repository has opened and its references are not read yet: the first reading names the remote.
    private bool _awaitingReferences;

    // Bumped by every read and every repository change, so an answer that was overtaken is dropped.
    private int _generation;

    private bool _disposed;

    /// <summary>
    /// Initialises a new instance, and asks the remote at once if a repository is already open and read.
    /// </summary>
    /// <param name="context">The open repository and its references.</param>
    /// <param name="autoRefresh">Says when a fetch ran.</param>
    /// <param name="tags">Reads the remote's tags.</param>
    /// <param name="remotes">Says whether the remote exists at all.</param>
    /// <param name="logger">Receives a read that failed, which nobody is shown.</param>
    public RemoteTags(
        IRepositoryContext context,
        IAutoRefreshService autoRefresh,
        ITagService tags,
        IRemoteReader remotes,
        ILogger<RemoteTags> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(autoRefresh);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(remotes);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _autoRefresh = autoRefresh;
        _tags = tags;
        _remotes = remotes;
        _logger = logger;

        _context.RepositoryChanged += OnRepositoryChanged;
        _context.StateRefreshed += OnStateRefreshed;
        _autoRefresh.Refreshed += OnRefreshed;

        if (_context.IsRepositoryOpen)
        {
            _awaitingReferences = _context.Head is null;

            if (!_awaitingReferences)
            {
                RefreshAsync().Forget();
            }
        }
    }

    /// <inheritdoc />
    public string? Remote { get; private set; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string>? Tags { get; private set; }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public string? RemoteOf(string tag, string commit)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(commit);

        return Remote is { } remote
            && Tags is { } tags
            && tags.TryGetValue(tag, out string? there)
            && string.Equals(there, commit, StringComparison.OrdinalIgnoreCase)
                ? remote
                : null;
    }

    /// <inheritdoc />
    public async Task RefreshAsync()
    {
        if (_disposed || _context.Repository is not { } repository)
        {
            return;
        }

        int generation = ++_generation;
        CancellationToken lifetime = _context.RepositoryLifetime;
        string remote = SyncOperations.TagRemote(_context.Refs);

        try
        {
            IReadOnlyList<GitRemote> configured = await _remotes.GetRemotesAsync(repository, lifetime).ConfigureAwait(true);

            if (!IsCurrent(generation, repository))
            {
                return;
            }

            if (!configured.Any(candidate => string.Equals(candidate.Name, remote, StringComparison.Ordinal)))
            {
                Publish(null, null);
                return;
            }

            IReadOnlyDictionary<string, string> tags = await _tags.ListRemoteAsync(repository, remote, lifetime).ConfigureAwait(true);

            if (IsCurrent(generation, repository))
            {
                Publish(remote, tags);
            }
        }
        catch (OperationCanceledException)
        {
            // The repository changed or closed under the read: its answer is nobody's any more.
        }
        catch (GitCommandException exception) when (IsCurrent(generation, repository))
        {
            // Debug, not warning: an offline laptop fails this after every fetch it tries.
            _logger.LogDebug(exception, "The tags of {Remote} could not be read", remote);

            // What the same remote said last still stands; what another one said says nothing here.
            if (!string.Equals(Remote, remote, StringComparison.Ordinal))
            {
                Publish(null, null);
            }
        }
        catch (GitCommandException)
        {
            // Overtaken by a newer read or another repository: not this read's to report.
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
        _generation++;

        _context.RepositoryChanged -= OnRepositoryChanged;
        _context.StateRefreshed -= OnStateRefreshed;
        _autoRefresh.Refreshed -= OnRefreshed;
    }

    private bool IsCurrent(int generation, RepositoryHandle repository)
        => !_disposed && generation == _generation && ReferenceEquals(repository, _context.Repository);

    private void OnRepositoryChanged(object? sender, RepositoryChangedEventArgs e)
    {
        // Whatever was in flight was about the repository that is gone.
        _generation++;
        _awaitingReferences = e.Current is not null;

        Publish(null, null);
    }

    private void OnStateRefreshed(object? sender, EventArgs e)
    {
        if (!_awaitingReferences)
        {
            return;
        }

        _awaitingReferences = false;
        RefreshAsync().Forget();
    }

    private void OnRefreshed(object? sender, AutoRefreshResult e)
    {
        if (e.Fetch == QuietFetchResult.Fetched)
        {
            RefreshAsync().Forget();
        }
    }

    private void Publish(string? remote, IReadOnlyDictionary<string, string>? tags)
    {
        if (string.Equals(remote, Remote, StringComparison.Ordinal) && SameTags(tags, Tags))
        {
            return;
        }

        Remote = remote;
        Tags = tags;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool SameTags(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        foreach ((string name, string commit) in left)
        {
            if (!right.TryGetValue(name, out string? other) || !string.Equals(commit, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
