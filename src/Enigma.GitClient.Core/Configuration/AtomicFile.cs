using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Enigma.GitClient.Core.Configuration;

/// <summary>
/// Replaces a file's contents in one step, so that a reader sees either the old document or the new
/// one and never half of either.
/// </summary>
/// <remarks>
/// <para>
/// Several instances of the application can run at once, and they share the same configuration
/// files. A plain <c>File.WriteAllText</c> truncates the file and then fills it; another instance
/// reading in between reads a partial document, takes it for a corrupt one and moves it aside —
/// which is how a recent list gets lost.
/// </para>
/// <para>
/// The new contents are written to a temporary file beside the target, in the same directory so the
/// final move is a rename on the same volume, and then moved over it.
/// </para>
/// <para>
/// Windows refuses that move while another handle holds the target open for reading — another
/// instance loading the same file, for the microseconds it takes. The move is retried briefly then,
/// rather than losing the write; elsewhere a rename over an open file simply succeeds.
/// </para>
/// </remarks>
public static class AtomicFile
{
    // Measured against a reader re-opening the file in a tight loop: the worst write took 16 attempts.
    private const int ReplaceAttempts = 50;

    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Writes a text file atomically.
    /// </summary>
    /// <param name="path">The file to replace or create.</param>
    /// <param name="contents">What it must contain.</param>
    /// <param name="encoding">The encoding to write it in; UTF-8 without a byte-order mark when omitted.</param>
    /// <param name="unixCreateMode">
    /// The mode the file is created with outside Windows — <c>0600</c> for a file that holds or names
    /// secrets — or <see langword="null"/> for the process's default. Ignored on Windows.
    /// </param>
    public static void WriteAllText(string path, string contents, Encoding? encoding = null, UnixFileMode? unixCreateMode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        WriteAllBytes(path, Encode(contents, encoding), unixCreateMode);
    }

    /// <summary>
    /// Writes a binary file atomically.
    /// </summary>
    /// <param name="path">The file to replace or create.</param>
    /// <param name="contents">What it must contain.</param>
    /// <param name="unixCreateMode">
    /// The mode the file is created with outside Windows, or <see langword="null"/> for the process's
    /// default. Ignored on Windows.
    /// </param>
    /// <remarks>
    /// The mode is the temporary file's from the moment it exists, so the file that is moved into place
    /// is never readable by anyone else — not even for the instant between creating it and tightening
    /// it.
    /// </remarks>
    public static void WriteAllBytes(string path, byte[] contents, UnixFileMode? unixCreateMode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        string temporary = TemporaryPathFor(path);

        try
        {
            using (FileStream stream = new(temporary, TemporaryOptions(unixCreateMode, FileOptions.None)))
            {
                stream.Write(contents);
            }

            Replace(temporary, path);
        }
        finally
        {
            DeleteQuietly(temporary);
        }
    }

    /// <summary>
    /// Writes a text file atomically.
    /// </summary>
    /// <param name="path">The file to replace or create.</param>
    /// <param name="contents">What it must contain.</param>
    /// <param name="encoding">The encoding to write it in; UTF-8 without a byte-order mark when omitted.</param>
    /// <param name="cancellationToken">Cancels the write before the file is replaced.</param>
    /// <returns>A task that completes once the file has been replaced.</returns>
    public static async Task WriteAllTextAsync(
        string path,
        string contents,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        string temporary = TemporaryPathFor(path);

        try
        {
            FileStream stream = new(temporary, TemporaryOptions(unixCreateMode: null, FileOptions.Asynchronous));

            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(Encode(contents, encoding), cancellationToken).ConfigureAwait(false);
            }

            await ReplaceAsync(temporary, path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DeleteQuietly(temporary);
        }
    }

    private static void Replace(string temporary, string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (Exception exception) when (attempt < ReplaceAttempts && IsHeldOpen(exception))
            {
                Thread.Sleep(ReplaceRetryDelay);
            }
        }
    }

    private static async Task ReplaceAsync(string temporary, string path, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (Exception exception) when (attempt < ReplaceAttempts && IsHeldOpen(exception))
            {
                await Task.Delay(ReplaceRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Whether a refused move is Windows saying another handle holds the target open — the only
    /// refusal worth waiting out. Anything else is a real error.
    /// </summary>
    private static bool IsHeldOpen(Exception exception)
        => OperatingSystem.IsWindows()
            && exception is UnauthorizedAccessException or IOException { HResult: SharingViolation or LockViolation };

    /// <summary>
    /// The text's bytes, with the encoding's byte-order mark when it has one — what
    /// <see cref="File.WriteAllText(string, string, Encoding)"/> would have written.
    /// </summary>
    private static byte[] Encode(string contents, Encoding? encoding)
    {
        Encoding chosen = encoding ?? new UTF8Encoding(false);
        byte[] preamble = chosen.GetPreamble();
        byte[] text = chosen.GetBytes(contents);

        return preamble.Length == 0 ? text : [.. preamble, .. text];
    }

    /// <summary>
    /// How the temporary file is opened: created new, by this writer alone, with the mode it must end
    /// up with.
    /// </summary>
    private static FileStreamOptions TemporaryOptions(UnixFileMode? unixCreateMode, FileOptions options)
    {
        FileStreamOptions created = new()
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = options,
        };

        if (!OperatingSystem.IsWindows() && unixCreateMode is { } mode)
        {
            created.UnixCreateMode = mode;
        }

        return created;
    }

    /// <summary>
    /// A name beside the target that no other writer — in this process or another — will pick.
    /// </summary>
    private static string TemporaryPathFor(string path)
        => $"{path}.{Guid.NewGuid():N}.tmp";

    /// <summary>
    /// Removes the temporary file when the move did not consume it; a leftover is not worth an error.
    /// </summary>
    private static void DeleteQuietly(string temporary)
    {
        try
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        catch (IOException)
        {
            // Nothing a caller could do about it.
        }
        catch (UnauthorizedAccessException)
        {
            // Likewise.
        }
    }
}
