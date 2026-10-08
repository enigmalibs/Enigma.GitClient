using System;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Watches the open repository's files, and brings the application up to date within a second of a
/// change made outside it — a file saved, a commit made in a terminal — rather than at the next
/// automatic refresh.
/// </summary>
/// <remarks>
/// <para>
/// A trigger on top of <see cref="IAutoRefreshService"/>, not a replacement for it: the periodic
/// refresh still fetches, and still notices whatever the operating system never reported — on a
/// network share, say.
/// </para>
/// <para>
/// It runs while a repository is open, follows <see cref="AppSettings.WatchFileSystem"/> live, and
/// never shows anything, whatever happens: a watch the operating system refuses falls back on the
/// periodic refresh, and the log says so.
/// </para>
/// </remarks>
public interface IRepositoryWatcher
{
    /// <summary>
    /// Gets a value indicating whether the open repository's files are being watched right now.
    /// </summary>
    bool IsWatching { get; }
}

/// <summary>
/// Default <see cref="IRepositoryWatcher"/>.
/// </summary>
/// <remarks>
/// <para>
/// An event only marks what changed; nothing runs per event. The refresh waits for
/// <see cref="QuietPeriod"/> without a change, or <see cref="MaximumWait"/> since the first one — so a
/// long build still refreshes every two seconds — and one refresh runs at a time: changes that arrive
/// during it make exactly one more afterwards.
/// </para>
/// <para>
/// The application's own writes are not changes to report: they refresh by themselves. Everything
/// seen while one runs, and for <see cref="WriteGracePeriod"/> after it, is dropped.
/// </para>
/// <para>
/// The operating system reports on its own threads, which touch nothing here but interlocked state.
/// The loop that refreshes runs on the context the watch was started from — the UI thread, in the
/// application — exactly as <see cref="AutoRefreshService"/>'s does, so everything it publishes lands
/// where bindings expect it.
/// </para>
/// </remarks>
public sealed class RepositoryWatcher : IRepositoryWatcher, IDisposable
{
    /// <summary>How long nothing must change before a refresh runs.</summary>
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(300);

    /// <summary>The longest a refresh waits for things to settle, counted from the first change.</summary>
    public static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long after one of the application's own writes what the operating system reports is still
    /// taken for that write's: its events can arrive after it has finished.
    /// </summary>
    public static readonly TimeSpan WriteGracePeriod = TimeSpan.FromMilliseconds(500);

    private readonly IRepositoryContext _context;
    private readonly IAutoRefreshService _refresh;
    private readonly ISettingsService _settings;
    private readonly IRepositoryEventSource _source;
    private readonly TimeProvider _time;
    private readonly ILogger<RepositoryWatcher> _logger;

    private Session? _session;
    private bool _enabled;
    private bool _disposed;

    // When the last of the application's own writes ended, as a TimeProvider timestamp; zero before
    // the first one.
    private long _lastWriteEnded;

    // Set once a failure has been logged as a warning: a machine at its inotify limit fails on every
    // repository, and says so once.
    private int _warned;

    /// <summary>
    /// Initialises a new instance, and starts watching at once if a repository is already open.
    /// </summary>
    /// <param name="context">The repository to watch, and the one place its writes are made.</param>
    /// <param name="refresh">Refreshes what changed, one refresh at a time with the periodic one.</param>
    /// <param name="settings">Says whether to watch at all, and follows the reader's changes to it.</param>
    /// <param name="source">Watches the files themselves.</param>
    /// <param name="time">The clock the waits are measured on.</param>
    /// <param name="logger">Receives what went wrong, which nobody is shown.</param>
    public RepositoryWatcher(
        IRepositoryContext context,
        IAutoRefreshService refresh,
        ISettingsService settings,
        IRepositoryEventSource source,
        TimeProvider time,
        ILogger<RepositoryWatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _refresh = refresh;
        _settings = settings;
        _source = source;
        _time = time;
        _logger = logger;

        _enabled = settings.Current.WatchFileSystem;

        _context.RepositoryChanged += OnRepositoryChanged;
        _context.WriteEnded += OnWriteEnded;
        _settings.Changed += OnSettingsChanged;

        Reschedule();
    }

    /// <inheritdoc />
    public bool IsWatching => _session?.IsWatching == true;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _context.RepositoryChanged -= OnRepositoryChanged;
        _context.WriteEnded -= OnWriteEnded;
        _settings.Changed -= OnSettingsChanged;

        Stop();
    }

    private void OnRepositoryChanged(object? sender, RepositoryChangedEventArgs e) => Reschedule();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Settings.WatchFileSystem == _enabled)
        {
            return;
        }

        _enabled = e.Settings.WatchFileSystem;
        Reschedule();
    }

    private void OnWriteEnded(object? sender, EventArgs e) => Interlocked.Exchange(ref _lastWriteEnded, _time.GetTimestamp());

    /// <summary>
    /// Whether what is reported now belongs to one of the application's own writes: one running, or
    /// one that has only just finished.
    /// </summary>
    private bool IsOwnWrite()
    {
        if (_context.IsWriting)
        {
            return true;
        }

        long ended = Interlocked.Read(ref _lastWriteEnded);

        return ended != 0 && _time.GetElapsedTime(ended) < WriteGracePeriod;
    }

    /// <summary>
    /// Starts, restarts or stops the watch to match the open repository and the setting.
    /// </summary>
    private void Reschedule()
    {
        Stop();

        if (_disposed || !_enabled || _context.Repository is not { } repository)
        {
            return;
        }

        Session session = new(this, repository);
        _session = session;

        _ = session.RunAsync();
    }

    private void Stop()
    {
        Session? session = _session;
        _session = null;

        session?.Stop();
    }

    /// <summary>
    /// Brings the application up to date with what changed, and lets nothing escape: nobody awaits the
    /// loop that calls this.
    /// </summary>
    private async Task RefreshAsync(RepositoryHandle repository, RepositoryChanges changes, CancellationToken cancellationToken)
    {
        try
        {
            await _refresh.RefreshLocallyAsync(changes, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.LogWarning(exception, "Refreshing {Repository} after a change on disk failed", repository.WorkTreePath);
        }
    }

    /// <summary>
    /// Logs a watch that could not start or stopped working: as a warning the first time, at debug
    /// level afterwards.
    /// </summary>
    private void ReportFailure(RepositoryHandle repository, Exception error)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            _logger.LogWarning(
                error,
                "The files of {Repository} cannot be watched, so changes made outside the application show at "
                + "the next automatic refresh instead. On Linux, raising fs.inotify.max_user_watches or "
                + "fs.inotify.max_user_instances lifts the limit",
                repository.WorkTreePath);
        }
        else
        {
            _logger.LogDebug(error, "The files of {Repository} cannot be watched", repository.WorkTreePath);
        }
    }

    /// <summary>
    /// One repository's watch: the operating system's, what it has reported since the last refresh,
    /// and the loop that turns that into refreshes. A new repository is a new session, so nothing the
    /// previous one saw can be published against it.
    /// </summary>
    private sealed class Session(RepositoryWatcher owner, RepositoryHandle repository) : IRepositoryEventSink
    {
        private readonly CancellationTokenSource _stop = new();

        // What the loop sleeps on while there is nothing to do, completed by the first change marked
        // after it went to sleep. Not RunContinuationsAsynchronously: completed on the operating system's
        // thread, the loop's continuation is posted to the context it awaited on, and nothing is gained
        // by a hop through the thread pool first.
        private TaskCompletionSource? _wake;

        private IDisposable? _watch;

        // The RepositoryChanges marked since the last refresh took them.
        private int _pending;

        // Set once the watch has failed: it reports nothing reliable any more.
        private int _failed;

        // The times of the first and of the latest change of the pending batch, and when the loop last
        // took a batch, as TimeProvider timestamps: a monotonic clock, which a change of the system's
        // time cannot stall.
        private long _firstChange;
        private long _lastChange;
        private long _taken;

        /// <summary>Gets a value indicating whether the operating system's watch is running.</summary>
        public bool IsWatching { get; private set; }

        /// <summary>
        /// Starts the operating system's watch, off this thread — on a large tree that alone takes a
        /// while — then refreshes as changes come, until stopped.
        /// </summary>
        public async Task RunAsync()
        {
            CancellationToken cancellationToken = _stop.Token;

            try
            {
                IDisposable watch;

                try
                {
                    watch = await Task.Run(() => owner._source.Watch(repository, this), cancellationToken)
                        .ConfigureAwait(true);
                }
                catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
                {
                    // Nothing was watched, so nothing was missed: the periodic refresh carries on alone.
                    owner.ReportFailure(repository, exception);
                    return;
                }

                // Stopped while the watch was starting: it never belonged to anything.
                if (cancellationToken.IsCancellationRequested)
                {
                    watch.Dispose();
                    return;
                }

                _watch = watch;
                IsWatching = true;
                owner._logger.LogDebug("Watching the files of {Repository}", repository.WorkTreePath);

                await LoopAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Stopped: another repository, the setting turned off, or the application leaving.
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Nothing awaits this loop, so an exception here would vanish; it is logged instead, and
                // the next change of repository or setting starts a fresh session.
                owner._logger.LogError(exception, "Watching the files of {Repository} stopped", repository.WorkTreePath);
            }
            finally
            {
                Release();
            }
        }

        /// <summary>
        /// Stops the session: a refresh it was still waiting to run never runs, and the operating
        /// system's watch is let go of at once.
        /// </summary>
        public void Stop()
        {
            _stop.Cancel();
            Release();
        }

        /// <inheritdoc />
        public void OnChanged(RepositoryChanges changes)
        {
            if (changes == RepositoryChanges.None || owner.IsOwnWrite())
            {
                return;
            }

            Mark(changes);
        }

        /// <inheritdoc />
        public void OnFaulted(Exception error, bool eventsLost)
        {
            if (eventsLost)
            {
                // A buffer overflowed: what was lost is unknown, so everything is read again — and the
                // watch itself goes on.
                owner._logger.LogDebug(error, "Changes to {Repository} were lost; everything is read again", repository.WorkTreePath);
                OnChanged(RepositoryChanges.Everything);
                return;
            }

            if (Interlocked.Exchange(ref _failed, 1) == 1)
            {
                return;
            }

            owner.ReportFailure(repository, error);

            // One full refresh, whatever was running: what the watch missed before it failed is unknown,
            // and after it nothing more comes from here.
            Mark(RepositoryChanges.Everything);
        }

        private void Mark(RepositoryChanges changes)
        {
            long now = owner._time.GetTimestamp();

            Interlocked.Exchange(ref _lastChange, now);

            if (Interlocked.Or(ref _pending, (int)changes) == 0)
            {
                Interlocked.Exchange(ref _firstChange, now);
                Interlocked.Exchange(ref _wake, null)?.TrySetResult();
            }
        }

        /// <summary>
        /// Waits for changes, lets them settle, and refreshes them — one refresh at a time, every
        /// refresh awaited, so whatever arrives during one is taken by the next.
        /// </summary>
        private async Task LoopAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Volatile.Read(ref _pending) == 0)
                {
                    TaskCompletionSource wake = new();
                    Volatile.Write(ref _wake, wake);

                    // A change marked between the first look and the line above found nobody to wake.
                    if (Volatile.Read(ref _pending) == 0)
                    {
                        await wake.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
                    }

                    continue;
                }

                await SettleAsync(cancellationToken).ConfigureAwait(true);

                RepositoryChanges changes = (RepositoryChanges)Interlocked.Exchange(ref _pending, 0);
                Interlocked.Exchange(ref _taken, owner._time.GetTimestamp());

                if (changes != RepositoryChanges.None)
                {
                    await owner.RefreshAsync(repository, changes, cancellationToken).ConfigureAwait(true);
                }

                // A failed watch has had its one full refresh, and has nothing more to say.
                if (Volatile.Read(ref _failed) == 1)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Waits until nothing has changed for the quiet period, or until the first change of the
        /// batch is as old as the maximum wait.
        /// </summary>
        private async Task SettleAsync(CancellationToken cancellationToken)
        {
            while (Volatile.Read(ref _failed) == 0)
            {
                long now = owner._time.GetTimestamp();
                long last = Interlocked.Read(ref _lastChange);
                long first = Interlocked.Read(ref _firstChange);

                // A change marked as the previous batch was being taken can be seen here before its own
                // first-change time is: it is a batch of its own, and starts with its latest change.
                if (first < Interlocked.Read(ref _taken))
                {
                    first = last;
                }

                TimeSpan quietFor = owner._time.GetElapsedTime(last, now);
                TimeSpan waitedFor = owner._time.GetElapsedTime(first, now);

                if (quietFor >= QuietPeriod || waitedFor >= MaximumWait)
                {
                    return;
                }

                TimeSpan wait = QuietPeriod - quietFor < MaximumWait - waitedFor
                    ? QuietPeriod - quietFor
                    : MaximumWait - waitedFor;

                await Task.Delay(wait, owner._time, cancellationToken).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Lets go of the operating system's watch, once, whichever of the session's ends gets here first.
        /// </summary>
        private void Release()
        {
            IsWatching = false;
            Interlocked.Exchange(ref _watch, null)?.Dispose();
        }
    }
}
