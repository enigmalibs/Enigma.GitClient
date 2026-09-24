using System;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// What one automatic refresh came to.
/// </summary>
/// <param name="Fetch">What the fetch did.</param>
/// <param name="Changed">
/// Whether HEAD or any reference is somewhere else than before the refresh — which is what decides
/// whether the history has anything new to draw.
/// </param>
/// <param name="Requested">
/// Whether the reader asked for this refresh with the toolbar's refresh button, rather than the
/// interval running out. A requested refresh is a refresh of everything: what only the reader's
/// asking justifies — redrawing a history in which nothing moved, calling a hosting API — follows it
/// and not the periodic one.
/// </param>
public sealed record AutoRefreshResult(QuietFetchResult Fetch, bool Changed, bool Requested = false)
{
    /// <summary>A refresh that did not run: another one was still going, or no repository was open.</summary>
    public static readonly AutoRefreshResult NotRun = new(QuietFetchResult.Skipped, false);
}

/// <summary>
/// Fetches and refreshes the open repository on its own, at the interval the settings name.
/// </summary>
/// <remarks>
/// <para>
/// Quiet on purpose: it never shows an overlay or a notification, whatever happens, because nobody
/// asked for it. A failed fetch is logged and the next tick tries again; a tick that finds the
/// repository busy with the reader's own work does nothing rather than wait behind it.
/// </para>
/// <para>
/// It runs while a repository is open and stops when it closes, and it follows the setting live: a
/// new interval takes effect at once, and zero stops it.
/// </para>
/// </remarks>
public interface IAutoRefreshService
{
    /// <summary>
    /// Gets a value indicating whether the periodic refresh is scheduled.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Raised after every refresh that ran, on the thread the refresh ran on.
    /// </summary>
    event EventHandler<AutoRefreshResult>? Refreshed;

    /// <summary>
    /// Runs one refresh now, unless one is already running.
    /// </summary>
    /// <param name="cancellationToken">Cancels it.</param>
    /// <returns>What it came to.</returns>
    Task<AutoRefreshResult> RefreshNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs one refresh now because the reader asked for it — the same fetch and re-read as the
    /// periodic one, published with <see cref="AutoRefreshResult.Requested"/> set — unless one is
    /// already running, in which case nothing more runs and the running one publishes what it found.
    /// </summary>
    /// <param name="cancellationToken">Cancels it.</param>
    /// <returns>What it came to.</returns>
    Task<AutoRefreshResult> RequestRefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IAutoRefreshService"/>.
/// </summary>
public sealed class AutoRefreshService : IAutoRefreshService, IDisposable
{
    private readonly IRepositoryContext _context;
    private readonly ISyncOperations _sync;
    private readonly ISettingsService _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<AutoRefreshService> _logger;

    private CancellationTokenSource? _loop;
    private TimeSpan _interval;
    private int _refreshing;
    private bool _disposed;

    /// <summary>
    /// Initialises a new instance, and starts at once if a repository is already open.
    /// </summary>
    /// <param name="context">The repository to refresh.</param>
    /// <param name="sync">Fetches quietly.</param>
    /// <param name="settings">Says how often, and follows the reader's changes to it.</param>
    /// <param name="time">The clock the interval is measured on.</param>
    /// <param name="logger">Receives what went wrong, which nobody is shown.</param>
    public AutoRefreshService(
        IRepositoryContext context,
        ISyncOperations sync,
        ISettingsService settings,
        TimeProvider time,
        ILogger<AutoRefreshService> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _sync = sync;
        _settings = settings;
        _time = time;
        _logger = logger;

        _context.RepositoryChanged += OnRepositoryChanged;
        _settings.Changed += OnSettingsChanged;

        Reschedule();
    }

    /// <inheritdoc />
    public bool IsRunning => _loop is not null;

    /// <inheritdoc />
    public event EventHandler<AutoRefreshResult>? Refreshed;

    /// <inheritdoc />
    public Task<AutoRefreshResult> RefreshNowAsync(CancellationToken cancellationToken = default)
        => RefreshAsync(requested: false, cancellationToken);

    /// <inheritdoc />
    public Task<AutoRefreshResult> RequestRefreshAsync(CancellationToken cancellationToken = default)
        => RefreshAsync(requested: true, cancellationToken);

    private async Task<AutoRefreshResult> RefreshAsync(bool requested, CancellationToken cancellationToken)
    {
        if (!_context.IsRepositoryOpen || Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return AutoRefreshResult.NotRun;
        }

        try
        {
            RepositoryStateStamp before = RepositoryStateStamp.Of(_context);

            QuietFetchResult fetch = await _sync.FetchQuietlyAsync(cancellationToken).ConfigureAwait(true);

            if (fetch == QuietFetchResult.Skipped)
            {
                // The reader is doing something; the next tick will do.
                return AutoRefreshResult.NotRun;
            }

            // A fetch that failed — offline, say — refreshed nothing, and what changed locally (a
            // commit made in a terminal) is still worth reading.
            if (fetch == QuietFetchResult.Failed)
            {
                await _context.RefreshAsync(cancellationToken).ConfigureAwait(true);
            }

            AutoRefreshResult result = new(fetch, before != RepositoryStateStamp.Of(_context), requested);
            Refreshed?.Invoke(this, result);

            return result;
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
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

        _context.RepositoryChanged -= OnRepositoryChanged;
        _settings.Changed -= OnSettingsChanged;
        Stop();
    }

    private void OnRepositoryChanged(object? sender, RepositoryChangedEventArgs e) => Reschedule();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (IntervalOf(e.Settings) != _interval || !IsRunning)
        {
            Reschedule();
        }
    }

    private static TimeSpan IntervalOf(AppSettings settings)
        => TimeSpan.FromSeconds(Math.Max(0, settings.AutoRefreshSeconds));

    /// <summary>
    /// Starts, restarts or stops the loop to match the repository and the setting.
    /// </summary>
    private void Reschedule()
    {
        Stop();

        _interval = IntervalOf(_settings.Current);

        if (_disposed || _interval <= TimeSpan.Zero || !_context.IsRepositoryOpen)
        {
            return;
        }

        CancellationTokenSource loop = new();
        _loop = loop;

        _ = RunAsync(_interval, loop);
    }

    private void Stop()
    {
        CancellationTokenSource? loop = _loop;
        _loop = null;

        if (loop is null)
        {
            return;
        }

        // Cancelled, not disposed: the loop still holds its token, and disposes the source itself
        // once it has let go of it.
        loop.Cancel();
    }

    /// <summary>
    /// The loop itself: one refresh per tick, on the context the loop was started from — the UI
    /// thread, in the application — so everything it publishes lands where bindings expect it.
    /// </summary>
    private async Task RunAsync(TimeSpan interval, CancellationTokenSource loop)
    {
        CancellationToken cancellationToken = loop.Token;

        try
        {
            using PeriodicTimer timer = new(interval, _time);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true))
            {
                await RefreshNowAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped: the repository closed, the interval changed, or the application is leaving.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Nothing awaits this loop, so an exception here would vanish; it is logged instead, and
            // the next change of repository or setting starts a fresh loop.
            _logger.LogError(exception, "The automatic refresh stopped");

            if (ReferenceEquals(_loop, loop))
            {
                _loop = null;
            }
        }
        finally
        {
            loop.Dispose();
        }
    }
}
