using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Git;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Writes the log to files a user can attach to a bug report: one file a day, in <c>logs</c> under
/// the configuration directory, with the last <see cref="RetainedDays"/> days kept.
/// </summary>
/// <remarks>
/// <para>
/// Logging never waits for the disk. An entry is queued and written by a single background writer.
/// When the queue is full, the entry is dropped and counted, and the count is written once the writer
/// catches up.
/// </para>
/// <para>
/// An entry is one line: the UTC time, the level, the category and the message. The lines of a
/// multi-line message and of an exception follow it, indented. Everything passes through
/// <see cref="ArgumentRedactor"/> before it reaches the disk.
/// </para>
/// <para>
/// Its lifetime is the logger factory's, not the host's. It also records the host's own start and
/// stop, so its writer starts with the provider and drains when the container disposes it.
/// </para>
/// <para>
/// The levels come from the host configuration's <c>Logging</c> section, where <c>File</c> names this
/// provider.
/// </para>
/// </remarks>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// How many days of log files are kept, the current day included.
    /// </summary>
    public const int RetainedDays = 7;

    // A burst far beyond anything the application logs; beyond it, entries are dropped and counted.
    private const int QueueCapacity = 4096;

    private const string Indent = "    ";

    // Bounded, so that closing the application never waits on a disk that stopped answering.
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IAppPaths _paths;
    private readonly TimeProvider _time;
    private readonly Channel<LogEntry> _queue;
    private readonly Task _writer;

    private long _lost;
    private int _disposed;

    /// <summary>
    /// Initialises a new instance and starts its writer. Nothing is written to disk before the first
    /// entry.
    /// </summary>
    /// <param name="paths">Where the configuration directory is.</param>
    /// <param name="time">Stamps each entry.</param>
    public FileLoggerProvider(IAppPaths paths, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(time);

        _paths = paths;
        _time = time;
        _queue = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
            },
            _ => Interlocked.Increment(ref _lost));

        // On the thread pool, never on the UI thread that builds the host.
        _writer = Task.Run(WriteAsync);
    }

    /// <summary>
    /// Gets the directory that holds the log files.
    /// </summary>
    /// <param name="paths">Where the configuration directory is.</param>
    /// <returns>The <c>logs</c> directory under the configuration directory.</returns>
    public static string DirectoryIn(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths.ConfigurationDirectory, "logs");
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        return new FileLogger(this, categoryName);
    }

    /// <summary>
    /// Writes what is still queued, waiting a few seconds at most, and closes the file. Entries logged
    /// from here on are dropped.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.Writer.TryComplete();

        // At the process edge, from the thread that disposes the container. The writer runs on the
        // thread pool, so waiting for it here cannot deadlock.
        _writer.Wait(DrainTimeout);
    }

    private void Enqueue(LogLevel level, string category, string message, Exception? exception)
        => _queue.Writer.TryWrite(new LogEntry(_time.GetUtcNow(), level, category, message, exception?.ToString()));

    private async Task WriteAsync()
    {
        ChannelReader<LogEntry> reader = _queue.Reader;
        StringBuilder text = new();

        // The configuration directory is read on the writer, the first time there is something to
        // write.
        using RollingLogFile file = new(() => DirectoryIn(_paths), RetainedDays);

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            WriteBatch(reader, file, text);
        }
    }

    /// <summary>
    /// Writes everything queued now: one write per day the entries fall on, then the count of the
    /// entries lost since the last batch.
    /// </summary>
    private void WriteBatch(ChannelReader<LogEntry> reader, RollingLogFile file, StringBuilder text)
    {
        DateOnly day = default;
        int entries = 0;

        while (reader.TryRead(out LogEntry entry))
        {
            DateOnly entryDay = DateOnly.FromDateTime(entry.Timestamp.UtcDateTime);

            if (entries > 0 && entryDay != day)
            {
                Write(file, day, text, entries);
                entries = 0;
            }

            day = entryDay;
            Format(entry, text);
            entries++;
        }

        if (entries > 0)
        {
            Write(file, day, text, entries);
        }

        long lost = Interlocked.Exchange(ref _lost, 0);

        if (lost > 0)
        {
            LogEntry notice = new(
                _time.GetUtcNow(),
                LogLevel.Warning,
                typeof(FileLoggerProvider).FullName ?? nameof(FileLoggerProvider),
                $"{lost.ToString(CultureInfo.InvariantCulture)} earlier log entries could not be written: the queue was full or the file could not be written.",
                null);

            Format(notice, text);

            // The notice itself is not counted if it is lost too: the next one would only grow by it.
            Write(file, DateOnly.FromDateTime(notice.Timestamp.UtcDateTime), text, entries: 0);
        }
    }

    private void Write(RollingLogFile file, DateOnly day, StringBuilder text, int entries)
    {
        try
        {
            file.Append(day, Utf8.GetBytes(text.ToString()));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The log has nowhere to report its own failure: it counts what it lost and says so once
            // a write succeeds again.
            Interlocked.Add(ref _lost, entries);
        }
        finally
        {
            text.Clear();
        }
    }

    private static void Format(in LogEntry entry, StringBuilder text)
    {
        text.Append(entry.Timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(LevelOf(entry.Level))
            .Append(' ')
            .Append(entry.Category)
            .Append(": ");

        AppendLines(text, ArgumentRedactor.Redact(entry.Message), indentFirst: false);

        if (entry.Exception is { } exception)
        {
            AppendLines(text, ArgumentRedactor.Redact(exception), indentFirst: true);
        }
    }

    /// <summary>
    /// Appends text line by line, every line but the entry's first indented, so that each entry starts
    /// at the beginning of a line with its time and nothing else does.
    /// </summary>
    private static void AppendLines(StringBuilder text, string value, bool indentFirst)
    {
        bool first = true;

        foreach (ReadOnlySpan<char> line in value.AsSpan().TrimEnd().EnumerateLines())
        {
            if (!first || indentFirst)
            {
                text.Append(Indent);
            }

            text.Append(line.TrimEnd()).Append(Environment.NewLine);
            first = false;
        }

        if (first && !indentFirst)
        {
            // An empty message still ends its line.
            text.Append(Environment.NewLine);
        }
    }

    private static string LevelOf(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT ",
        _ => "NONE ",
    };

    private readonly record struct LogEntry(
        DateTimeOffset Timestamp,
        LogLevel Level,
        string Category,
        string Message,
        string? Exception);

    /// <summary>
    /// The logger of one category. The logger factory applies the configured levels before it is
    /// called.
    /// </summary>
    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            // Formatted here, on the caller's thread: the state is only valid during this call.
            provider.Enqueue(logLevel, category, formatter(state, exception), exception);
        }
    }
}
