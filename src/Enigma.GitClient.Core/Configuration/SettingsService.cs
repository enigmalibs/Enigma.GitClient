using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Core.Configuration;

/// <summary>
/// Carries the settings as they now are.
/// </summary>
/// <param name="Settings">The new settings.</param>
public sealed record SettingsChangedEventArgs(AppSettings Settings);

/// <summary>
/// The application's preferences, and the file they live in.
/// </summary>
/// <remarks>
/// Every consumer reads <see cref="Current"/> and listens to <see cref="Changed"/>, so a preference
/// takes effect where it is used rather than being re-plumbed through every page that shows it.
/// </remarks>
public interface ISettingsService
{
    /// <summary>Gets the settings as they now are.</summary>
    AppSettings Current { get; }

    /// <summary>Raised after the settings change, on the thread that changed them.</summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <summary>
    /// Reads the settings from disk, once, at startup.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The settings that were read.</returns>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the settings and schedules a save.
    /// </summary>
    /// <param name="change">Produces the new settings from the current ones.</param>
    /// <returns>The new settings.</returns>
    AppSettings Update(Func<AppSettings, AppSettings> change);

    /// <summary>
    /// Writes anything still pending, immediately.
    /// </summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the file is up to date.</returns>
    Task FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts every preference back to its default.
    /// </summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The defaults.</returns>
    Task<AppSettings> ResetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ISettingsService"/>: a versioned JSON file in the user's configuration
/// directory, written a short while after the last change rather than on every keystroke.
/// </summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    /// <summary>The file's name inside the configuration directory.</summary>
    public const string FileName = "settings.json";

    /// <summary>
    /// How long a change waits for the next one before the file is written.
    /// </summary>
    /// <remarks>
    /// Dragging a slider or typing in a box produces a change per event; without this the settings
    /// file is rewritten dozens of times a second for a preference nobody has finished choosing.
    /// </remarks>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(400);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<SettingsService> _logger;
    private readonly TimeSpan _debounce;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _pending = new();

    private CancellationTokenSource? _scheduled;
    private bool _dirty;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="paths">Where the configuration directory is.</param>
    /// <param name="logger">Receives a corrupt-file report.</param>
    /// <param name="debounce">
    /// How long a change waits for the next one. A test passes a short window, or zero to write
    /// immediately.
    /// </param>
    public SettingsService(IAppPaths paths, ILogger<SettingsService> logger, TimeSpan? debounce = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        _paths = paths;
        _logger = logger;
        _debounce = debounce ?? DefaultDebounce;
    }

    /// <inheritdoc />
    public AppSettings Current { get; private set; } = AppSettings.Defaults;

    /// <inheritdoc />
    public event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <summary>
    /// Gets how many times the file has been written, which is what a test asserts the debouncing
    /// with.
    /// </summary>
    public int Writes { get; private set; }

    /// <summary>
    /// Gets the path the settings file was backed up to when it could not be read, or
    /// <see langword="null"/> when that has not happened.
    /// </summary>
    public string? BackupPath { get; private set; }

    /// <inheritdoc />
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Current = Read();
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, new SettingsChangedEventArgs(Current));

        return Current;
    }

    /// <inheritdoc />
    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        AppSettings updated = change(Current).Normalised() with { Version = AppSettings.CurrentVersion };

        if (updated == Current)
        {
            // Nothing actually changed: a setter that re-assigns the same value should not cost a
            // file write or wake every listener.
            return Current;
        }

        Current = updated;

        Schedule();

        Changed?.Invoke(this, new SettingsChangedEventArgs(Current));

        return Current;
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? scheduled;

        lock (_pending)
        {
            scheduled = _scheduled;
            _scheduled = null;
        }

        scheduled?.Cancel();
        scheduled?.Dispose();

        await WriteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AppSettings> ResetAsync(CancellationToken cancellationToken = default)
    {
        Update(_ => AppSettings.Defaults);

        await FlushAsync(cancellationToken).ConfigureAwait(false);

        return Current;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_pending)
        {
            _scheduled?.Cancel();
            _scheduled?.Dispose();
            _scheduled = null;
        }

        _gate.Dispose();
    }

    private void Schedule()
    {
        lock (_pending)
        {
            _dirty = true;

            _scheduled?.Cancel();
            _scheduled?.Dispose();

            if (_debounce <= TimeSpan.Zero)
            {
                _scheduled = null;

                // No window at all: write now. This is what a test asks for when it wants the file
                // to exist by the time the next line runs.
                _ = WriteAsync(CancellationToken.None);
                return;
            }

            CancellationTokenSource source = new();
            _scheduled = source;

            _ = DelayThenWriteAsync(source.Token);
        }
    }

    private async Task DelayThenWriteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_debounce, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Another change arrived inside the window, and it scheduled its own write.
            return;
        }

        await WriteAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task WriteAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            lock (_pending)
            {
                if (!_dirty)
                {
                    return;
                }

                _dirty = false;
            }

            string file = _paths.GetConfigurationFile(FileName);
            string json = JsonSerializer.Serialize(Current, SerializerOptions);

            await File.WriteAllTextAsync(file, json, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);

            Writes++;
        }
        catch (IOException exception)
        {
            // A settings file that cannot be written is not worth taking the application down for;
            // the preference is still live in this session.
            _logger.LogWarning(exception, "The settings could not be saved");
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "The settings could not be saved");
        }
        finally
        {
            _gate.Release();
        }
    }

    private AppSettings Read()
    {
        string file = _paths.GetConfigurationFile(FileName);

        if (!File.Exists(file))
        {
            return AppSettings.Defaults;
        }

        try
        {
            AppSettings? stored = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), SerializerOptions);

            if (stored is null)
            {
                return AppSettings.Defaults;
            }

            if (stored.Version > AppSettings.CurrentVersion)
            {
                // Written by a newer build. Everything this build understands was read; anything it
                // does not is left in the file until that build writes it again.
                _logger.LogInformation(
                    "The settings file is version {Version}; this build understands {Current}",
                    stored.Version,
                    AppSettings.CurrentVersion);

                return stored.Normalised();
            }

            return Migrate(stored).Normalised();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The settings file could not be read; defaults will be used");

            Backup(file);

            return AppSettings.Defaults;
        }
    }

    /// <summary>
    /// Brings a file written by an older build up to date.
    /// </summary>
    /// <param name="stored">What was read.</param>
    /// <returns>The migrated settings.</returns>
    /// <remarks>
    /// <para>
    /// Every case here reads the same way: the value an older build shipped as <em>its</em> default
    /// is a value nobody chose, so it moves to the new one. Anything else was picked on the settings
    /// page and is kept — a client that overwrites what someone chose is worse than one that never
    /// changes anything.
    /// </para>
    /// <para>
    /// Version 2 raised the default row height from 26 to 36. Version 3 moved the diff to the
    /// side-by-side rendering, so a file written before it that still says <c>Unified</c> is moved
    /// across; one that says <c>SideBySide</c> is already where version 3 would put it, and a
    /// version 3 file is left alone entirely, because from version 3 on <c>Unified</c> is a
    /// preference like any other. Version 4 widened the graph lane from 16 to 20, so that two
    /// parallel lines read as two.
    /// </para>
    /// <para>
    /// The cases compose: a version 1 file goes through all three of them in one read.
    /// </para>
    /// </remarks>
    private static AppSettings Migrate(AppSettings stored)
    {
        AppSettings migrated = stored;

        if (stored.Version < 2 && stored.GraphRowHeight == AppSettings.LegacyGraphRowHeight)
        {
            migrated = migrated with { GraphRowHeight = AppSettings.Defaults.GraphRowHeight };
        }

        if (stored.Version < 3 && stored.DiffView == AppSettings.LegacyDiffView)
        {
            migrated = migrated with { DiffView = AppSettings.Defaults.DiffView };
        }

        if (stored.Version < 4 && stored.GraphLaneWidth == AppSettings.LegacyGraphLaneWidth)
        {
            migrated = migrated with { GraphLaneWidth = AppSettings.Defaults.GraphLaneWidth };
        }

        return migrated with { Version = AppSettings.CurrentVersion };
    }

    /// <summary>
    /// Moves an unreadable settings file aside rather than overwriting it.
    /// </summary>
    private void Backup(string file)
    {
        try
        {
            string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string backup = Path.Combine(
                Path.GetDirectoryName(file) ?? string.Empty,
                $"settings.corrupt-{stamp}.json");

            File.Move(file, backup, overwrite: true);

            BackupPath = backup;

            _logger.LogInformation("The unreadable settings file was kept as {Backup}", backup);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing more to do: the defaults are already what this session runs on.
            _logger.LogWarning(exception, "The unreadable settings file could not be moved aside");
        }
    }
}
