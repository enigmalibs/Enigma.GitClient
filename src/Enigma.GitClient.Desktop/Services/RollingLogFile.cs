using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// The log file of one day in the log directory: appended to in whole batches, replaced by the next
/// day's file when the day changes, with the files of all but the most recent days deleted.
/// </summary>
/// <remarks>
/// <para>
/// Used from one thread only, the writer of <see cref="FileLoggerProvider"/>.
/// </para>
/// <para>
/// Another instance of the application writes to the same day's file. The file is shared for
/// reading and writing, and each batch is appended in one write at the end of the file, under a lock
/// named after the directory that every process shares. Two writers never find the same end of file,
/// so the lines of two instances interleave by batch and never overwrite each other. A user can open
/// the file while the application runs.
/// </para>
/// </remarks>
internal sealed partial class RollingLogFile : IDisposable
{
    private const string DayFormat = "yyyyMMdd";

    // Long enough for any other writer's batch; past it, this batch is written without the lock
    // rather than lost.
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(1);

    private readonly Func<string> _locate;
    private readonly int _retainedDays;

    private string? _directory;
    private Mutex? _appendLock;
    private FileStream? _stream;
    private DateOnly _day;

    /// <summary>
    /// Initialises a new instance. Nothing is read or created on disk before the first append.
    /// </summary>
    /// <param name="locate">
    /// Finds the directory that holds the log files. Called on the first append, where a failure to
    /// reach it is the append's failure.
    /// </param>
    /// <param name="retainedDays">How many days of files are kept, the current day included.</param>
    public RollingLogFile(Func<string> locate, int retainedDays)
    {
        ArgumentNullException.ThrowIfNull(locate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retainedDays);

        _locate = locate;
        _retainedDays = retainedDays;
    }

    /// <summary>
    /// Gets the name of the log file of a day.
    /// </summary>
    /// <param name="day">The day, in UTC.</param>
    /// <returns>The file name, without a directory.</returns>
    public static string FileNameOf(DateOnly day)
        => "enigma-gitclient-" + day.ToString(DayFormat, CultureInfo.InvariantCulture) + ".log";

    /// <summary>
    /// Appends text to the file of a day, opening that file first when it is not the current one.
    /// </summary>
    /// <param name="day">The day the text belongs to, in UTC.</param>
    /// <param name="bytes">The encoded text: whole lines.</param>
    /// <exception cref="IOException">The file cannot be opened or written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file or its directory cannot be written.</exception>
    public void Append(DateOnly day, ReadOnlySpan<byte> bytes)
    {
        try
        {
            FileStream stream = _stream is { } open && day == _day ? open : Open(day);
            bool locked = Enter(_appendLock);

            try
            {
                // At the end as it is now, not where this instance last wrote: another instance may
                // have appended in between.
                stream.Seek(0, SeekOrigin.End);
                stream.Write(bytes);
                stream.Flush();
            }
            finally
            {
                if (locked)
                {
                    _appendLock?.ReleaseMutex();
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Opened again on the next append, which is how a full disk or a lost handle recovers.
            Close();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Close();
        _appendLock?.Dispose();
        _appendLock = null;
    }

    private FileStream Open(DateOnly day)
    {
        Close();

        string directory = _directory ??= _locate();
        Directory.CreateDirectory(directory);
        _appendLock ??= CreateAppendLock(directory);

        // No buffer: a batch is one write, so another instance's lines never land inside it.
        FileStream stream = new(
            Path.Combine(directory, FileNameOf(day)),
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 0);
        _stream = stream;
        _day = day;

        DeleteOldDays(directory);

        return stream;
    }

    private void Close()
    {
        _stream?.Dispose();
        _stream = null;
    }

    /// <summary>
    /// Creates the lock every writer of a directory shares, in this process or another, or returns
    /// <see langword="null"/> when the system refuses one: the log is then written without it.
    /// </summary>
    private static Mutex? CreateAppendLock(string directory)
    {
        // A name the system accepts whatever the path holds, and the same for the same directory.
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory)));
        string name = "Enigma.GitClient.Log." + Convert.ToHexString(hash, 0, 8);

        try
        {
            return new Mutex(initiallyOwned: false, name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            return null;
        }
    }

    private static bool Enter(Mutex? appendLock)
    {
        if (appendLock is null)
        {
            return false;
        }

        try
        {
            return appendLock.WaitOne(LockTimeout);
        }
        catch (AbandonedMutexException)
        {
            // Its owner ended while holding it: this writer holds it now.
            return true;
        }
    }

    /// <summary>
    /// Deletes the files of every day but the most recent ones. Only files named as this class names
    /// them are touched, and never the current day's.
    /// </summary>
    private void DeleteOldDays(string directory)
    {
        Dictionary<DateOnly, List<string>> otherDays = [];

        foreach (string path in Directory.EnumerateFiles(directory, "enigma-gitclient-*.log"))
        {
            if (DayOf(path) is not { } day || day == _day)
            {
                continue;
            }

            if (!otherDays.TryGetValue(day, out List<string>? paths))
            {
                paths = [];
                otherDays.Add(day, paths);
            }

            paths.Add(path);
        }

        // The current day counts as one of the days kept.
        foreach (DateOnly day in otherDays.Keys.OrderDescending().Skip(_retainedDays - 1))
        {
            foreach (string path in otherDays[day])
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Held open by another program: deleted when the next day's file opens.
                }
            }
        }
    }

    private static DateOnly? DayOf(string path)
    {
        Match match = LogFileNamePattern().Match(Path.GetFileName(path));

        return match.Success
            && DateOnly.TryParseExact(match.Groups["day"].Value, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
                ? day
                : null;
    }

    [GeneratedRegex(@"^enigma-gitclient-(?<day>\d{8})\.log$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant)]
    private static partial Regex LogFileNamePattern();
}
