using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Keeps the image a repository's tile shows in place of the folder glyph, one per repository.
/// </summary>
/// <remarks>
/// The icon belongs to the repository, not to a profile's list: every profile that lists the
/// repository shows it, and adding the repository to a list again does not lose it.
/// </remarks>
public interface IRepositoryIconStore
{
    /// <summary>
    /// Finds the icon stored for a repository.
    /// </summary>
    /// <param name="repositoryPath">The repository's work-tree path.</param>
    /// <returns>The stored icon's absolute path, or <see langword="null"/> when it has none.</returns>
    string? Find(string repositoryPath);

    /// <summary>
    /// Stores a copy of an image as a repository's icon, replacing the one it had.
    /// </summary>
    /// <param name="repositoryPath">The repository's work-tree path.</param>
    /// <param name="sourceFile">The image the user chose.</param>
    /// <param name="cancellationToken">Cancels the read before anything is written.</param>
    /// <returns>The stored copy's absolute path.</returns>
    /// <exception cref="RepositoryIconException">
    /// The file is not one the client takes as an icon: not there, of another kind, too large, or not
    /// readable as an image. Nothing is stored and the previous icon stays.
    /// </exception>
    Task<string> SetAsync(string repositoryPath, string sourceFile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets a repository's icon, so its tile shows the folder glyph again.
    /// </summary>
    /// <param name="repositoryPath">The repository's work-tree path.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes once the icon is gone.</returns>
    Task ResetAsync(string repositoryPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// A file refused as a repository's icon, with the reason in words a user can read.
/// </summary>
public sealed class RepositoryIconException : Exception
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public RepositoryIconException()
    {
    }

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">Why the file was refused.</param>
    public RepositoryIconException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">Why the file was refused.</param>
    /// <param name="innerException">What failed underneath.</param>
    public RepositoryIconException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Stores each repository's icon as a copy in the <c>repository-icons</c> folder of the user's
/// configuration directory, named after the repository's path.
/// </summary>
/// <remarks>
/// <para>
/// A copy rather than the path of the file the user chose: the original can be moved, edited or
/// deleted without the tile changing. The name is a hash of the repository's path, so no part of a
/// path ever becomes part of a file name, and the extension is the original's, which says how to read
/// it.
/// </para>
/// <para>
/// A new icon is written beside the old one under a temporary name and moved into place, then the old
/// one (of another extension) is removed: an instance reading the folder meanwhile finds one or the
/// other, never half a file.
/// </para>
/// </remarks>
public sealed class RepositoryIconStore : IRepositoryIconStore
{
    /// <summary>
    /// The folder's name inside the configuration directory.
    /// </summary>
    public const string DirectoryName = "repository-icons";

    /// <summary>
    /// The largest picture taken, in bytes: an icon is small, and this keeps a photograph picked by
    /// mistake from being copied.
    /// </summary>
    public const long MaxImageBytes = 10L * 1024 * 1024;

    /// <summary>
    /// The largest SVG taken, in bytes: the parser's own limit.
    /// </summary>
    public const long MaxSvgBytes = 1024L * 1024;

    /// <summary>
    /// The kinds of file taken, by extension: what the desktop's image decoder reads, and SVG.
    /// </summary>
    public static readonly IReadOnlyList<string> Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".webp", ".svg"];

    private readonly IAppPaths _paths;
    private readonly ILogger<RepositoryIconStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="paths">Where the configuration directory is.</param>
    /// <param name="logger">Receives what failed underneath a refusal.</param>
    public RepositoryIconStore(IAppPaths paths, ILogger<RepositoryIconStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        _paths = paths;
        _logger = logger;
    }

    private string Folder => Path.Combine(_paths.ConfigurationDirectory, DirectoryName);

    /// <summary>
    /// The stem every icon of a repository is named with: the first 16 hexadecimal digits of the
    /// SHA-256 of its full path, without a trailing separator — upper-cased first on Windows, where
    /// two spellings of a path in different cases are one folder.
    /// </summary>
    /// <param name="repositoryPath">The repository's work-tree path.</param>
    /// <returns>The stem.</returns>
    public static string KeyOf(string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        string full = Path.GetFullPath(repositoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (OperatingSystem.IsWindows())
        {
            full = full.ToUpperInvariant();
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)), 0, 8);
    }

    /// <summary>
    /// Whether a file is of a kind taken as an icon, by its extension.
    /// </summary>
    /// <param name="file">The file's path or name.</param>
    /// <returns><see langword="true"/> for one of <see cref="Extensions"/>, in any case.</returns>
    public static bool IsIconFile(string file)
        => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string? Find(string repositoryPath)
    {
        string folder = Folder;

        if (!Directory.Exists(folder))
        {
            return null;
        }

        return StoredFiles(folder, KeyOf(repositoryPath)).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<string> SetAsync(
        string repositoryPath,
        string sourceFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);

        string name = Path.GetFileName(sourceFile);
        string extension = Path.GetExtension(sourceFile).ToLowerInvariant();

        if (!IsIconFile(sourceFile))
        {
            throw new RepositoryIconException(
                $"'{name}' is not a kind of image the client takes as an icon. Choose a PNG, JPEG, BMP, GIF, ICO, WebP or SVG file.");
        }

        FileInfo source = new(sourceFile);

        if (!source.Exists)
        {
            throw new RepositoryIconException($"'{sourceFile}' does not exist.");
        }

        long limit = extension == ".svg" ? MaxSvgBytes : MaxImageBytes;

        if (source.Length > limit)
        {
            throw new RepositoryIconException(
                $"'{name}' is {Megabytes(source.Length)}; an icon can be {Megabytes(limit)} at most.");
        }

        byte[] bytes;

        try
        {
            bytes = await File.ReadAllBytesAsync(sourceFile, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Reading {File} as a repository icon failed", sourceFile);
            throw new RepositoryIconException($"'{name}' could not be read: {exception.Message}", exception);
        }

        // Read as the tile will read it: a file the tile could not draw is not stored.
        if (!RepositoryIconImages.CanDecode(bytes, extension, out Exception? failure))
        {
            _logger.LogWarning(failure, "{File} is not readable as a repository icon", sourceFile);
            throw new RepositoryIconException(
                $"'{name}' is not an image the client can read. It may be damaged, or use a feature this client does not draw.",
                failure ?? new InvalidDataException(name));
        }

        string key = KeyOf(repositoryPath);
        string folder = Folder;
        string target = Path.Combine(folder, key + extension);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);

        try
        {
            Directory.CreateDirectory(folder);

            string temporary = Path.Combine(folder, $"{key}.{Guid.NewGuid():N}.tmp");

            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, CancellationToken.None).ConfigureAwait(true);
                File.Move(temporary, target, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                TryDelete(temporary);
                _logger.LogWarning(exception, "Storing the icon of {Repository} failed", repositoryPath);
                throw new RepositoryIconException($"The icon could not be stored: {exception.Message}", exception);
            }

            foreach (string previous in StoredFiles(folder, key))
            {
                if (!string.Equals(previous, target, StringComparison.Ordinal))
                {
                    TryDelete(previous);
                }
            }

            return target;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ResetAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        string folder = Folder;

        if (!Directory.Exists(folder))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);

        try
        {
            foreach (string stored in StoredFiles(folder, KeyOf(repositoryPath)))
            {
                TryDelete(stored);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The repository's icons in the folder: its stem with one of the taken extensions — normally one
    /// file, briefly two while a new icon of another kind replaces the old.
    /// </summary>
    private static IEnumerable<string> StoredFiles(string folder, string key)
        => Directory.EnumerateFiles(folder, key + ".*")
            .Where(file => string.Equals(Path.GetFileNameWithoutExtension(file), key, StringComparison.OrdinalIgnoreCase)
                && IsIconFile(file))
            .Order(StringComparer.Ordinal);

    private void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Removing the repository icon {File} failed", file);
        }
    }

    private static string Megabytes(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / (1024.0 * 1024.0):0.#} MB"
            : $"{Math.Max(1, bytes / 1024)} KB";
}
