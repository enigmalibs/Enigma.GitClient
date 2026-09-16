using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// A repository the user has opened before.
/// </summary>
/// <param name="Path">The absolute work-tree path.</param>
/// <param name="Name">The display name, normally the last path segment.</param>
/// <param name="LastOpenedUtc">When it was last opened.</param>
/// <param name="IsPinned">Whether the user pinned it to the top of the list.</param>
public sealed record RecentRepository(string Path, string Name, DateTimeOffset LastOpenedUtc, bool IsPinned = false)
{
    /// <summary>
    /// Gets a value indicating whether the path still exists on disk. A repository that has been
    /// moved or deleted is kept in the list and flagged, rather than silently disappearing.
    /// </summary>
    [JsonIgnore]
    public bool Exists => Directory.Exists(Path);
}

/// <summary>
/// Remembers the repositories the user has opened.
/// </summary>
public interface IRecentRepositoryStore
{
    /// <summary>
    /// Reads the list, most recently opened first, with pinned entries above the rest.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The remembered repositories.</returns>
    Task<IReadOnlyList<RecentRepository>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a repository has just been opened.
    /// </summary>
    /// <param name="path">The repository's work-tree path.</param>
    /// <param name="name">The repository's display name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The updated list.</returns>
    Task<IReadOnlyList<RecentRepository>> TouchAsync(
        string path,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets a repository.
    /// </summary>
    /// <param name="path">The repository's work-tree path.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The updated list.</returns>
    Task<IReadOnlyList<RecentRepository>> RemoveAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pins or unpins a repository, which keeps it at the top of the list whatever its age.
    /// </summary>
    /// <param name="path">The repository's work-tree path.</param>
    /// <param name="isPinned">Whether it should be pinned.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The updated list.</returns>
    Task<IReadOnlyList<RecentRepository>> SetPinnedAsync(
        string path,
        bool isPinned,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores the recent repositories as a versioned JSON file in the user's configuration directory.
/// </summary>
public sealed class RecentRepositoryStore : IRecentRepositoryStore
{
    /// <summary>
    /// The file's name inside the configuration directory.
    /// </summary>
    public const string FileName = "recent-repositories.json";

    /// <summary>
    /// The schema version written into the file, so a future format change can migrate rather than
    /// guess.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// How many repositories are remembered. Beyond this the oldest unpinned entries are dropped.
    /// </summary>
    public const int Capacity = 20;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<RecentRepositoryStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="paths">Where the configuration directory is.</param>
    /// <param name="logger">Receives a corrupt-file report.</param>
    public RecentRepositoryStore(IAppPaths paths, ILogger<RecentRepositoryStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecentRepository>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return Read();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RecentRepository>> TouchAsync(
        string path,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return MutateAsync(
            entries =>
            {
                string full = Path.GetFullPath(path);
                bool wasPinned = entries.Any(entry => SamePath(entry.Path, full) && entry.IsPinned);

                entries.RemoveAll(entry => SamePath(entry.Path, full));
                entries.Insert(
                    0,
                    new RecentRepository(
                        full,
                        string.IsNullOrWhiteSpace(name) ? Path.GetFileName(full) : name,
                        DateTimeOffset.UtcNow,
                        wasPinned));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RecentRepository>> RemoveAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return MutateAsync(
            entries => entries.RemoveAll(entry => SamePath(entry.Path, Path.GetFullPath(path))),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RecentRepository>> SetPinnedAsync(
        string path,
        bool isPinned,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return MutateAsync(
            entries =>
            {
                string full = Path.GetFullPath(path);

                for (int index = 0; index < entries.Count; index++)
                {
                    if (SamePath(entries[index].Path, full))
                    {
                        entries[index] = entries[index] with { IsPinned = isPinned };
                    }
                }
            },
            cancellationToken);
    }

    private async Task<IReadOnlyList<RecentRepository>> MutateAsync(
        Action<List<RecentRepository>> mutate,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            List<RecentRepository> entries = [.. Read()];
            mutate(entries);

            List<RecentRepository> ordered = Order(entries);

            // Pinned entries never fall off the end: the cap applies to the unpinned tail.
            if (ordered.Count > Capacity)
            {
                List<RecentRepository> pinned = [.. ordered.Where(entry => entry.IsPinned)];
                List<RecentRepository> rest = [.. ordered.Where(entry => !entry.IsPinned)];

                int room = Math.Max(0, Capacity - pinned.Count);
                ordered = Order([.. pinned, .. rest.Take(room)]);
            }

            Write(ordered);
            return ordered;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static List<RecentRepository> Order(IEnumerable<RecentRepository> entries)
        => [.. entries
            .OrderByDescending(entry => entry.IsPinned)
            .ThenByDescending(entry => entry.LastOpenedUtc)];

    private static bool SamePath(string left, string right)
        => string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private IReadOnlyList<RecentRepository> Read()
    {
        string file = _paths.GetConfigurationFile(FileName);

        if (!File.Exists(file))
        {
            return [];
        }

        try
        {
            StoredDocument? document = JsonSerializer.Deserialize<StoredDocument>(
                File.ReadAllText(file),
                SerializerOptions);

            if (document?.Repositories is null)
            {
                return [];
            }

            if (document.Version > CurrentVersion)
            {
                // Written by a newer build. Reading it is still the best available answer: the
                // entries we understand are shown, and the file is only rewritten when the user
                // changes something.
                _logger.LogInformation(
                    "The recent-repository file is version {Version}; this build understands {Current}",
                    document.Version,
                    CurrentVersion);
            }

            return Order(document.Repositories.Where(entry => !string.IsNullOrWhiteSpace(entry.Path)));
        }
        catch (JsonException exception)
        {
            BackUpCorruptFile(file, exception);
            return [];
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "The recent-repository file could not be read");
            return [];
        }
    }

    private void Write(IReadOnlyList<RecentRepository> entries)
    {
        string file = _paths.GetConfigurationFile(FileName);

        try
        {
            File.WriteAllText(
                file,
                JsonSerializer.Serialize(new StoredDocument(CurrentVersion, [.. entries]), SerializerOptions));
        }
        catch (IOException exception)
        {
            // Losing the recent list is an inconvenience, not a failure worth interrupting the user.
            _logger.LogWarning(exception, "The recent-repository file could not be written");
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "The recent-repository file could not be written");
        }
    }

    private void BackUpCorruptFile(string file, Exception cause)
    {
        string backup = file + ".corrupt";

        try
        {
            File.Move(file, backup, overwrite: true);
            _logger.LogWarning(
                cause,
                "The recent-repository file was unreadable and has been moved to {Backup}",
                backup);
        }
        catch (IOException)
        {
            _logger.LogWarning(cause, "The recent-repository file was unreadable and could not be backed up");
        }
    }

    private sealed record StoredDocument(int Version, List<RecentRepository> Repositories);
}
