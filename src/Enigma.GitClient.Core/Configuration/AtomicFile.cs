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
/// </remarks>
public static class AtomicFile
{
    /// <summary>
    /// Writes a text file atomically.
    /// </summary>
    /// <param name="path">The file to replace or create.</param>
    /// <param name="contents">What it must contain.</param>
    /// <param name="encoding">The encoding to write it in; UTF-8 without a byte-order mark when omitted.</param>
    public static void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        string temporary = TemporaryPathFor(path);

        try
        {
            File.WriteAllText(temporary, contents, encoding ?? new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
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
            await File.WriteAllTextAsync(temporary, contents, encoding ?? new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            DeleteQuietly(temporary);
        }
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
