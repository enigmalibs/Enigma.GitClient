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
/// A repository in a profile's list.
/// </summary>
/// <param name="Path">The absolute work-tree path.</param>
/// <param name="Name">The display name, normally the last path segment.</param>
public sealed record ListedRepository(string Path, string Name)
{
    /// <summary>
    /// Gets a value indicating whether the path still exists on disk. A repository that has been
    /// moved or deleted is kept in the list and flagged, rather than silently disappearing.
    /// </summary>
    [JsonIgnore]
    public bool Exists => Directory.Exists(Path);
}

/// <summary>
/// Keeps each profile's list of repositories, in the order the user gave it.
/// </summary>
/// <remarks>
/// The order is the user's: adding a repository that is already listed leaves it where it is, and a new
/// one goes at the end. Nothing is ever dropped to make room.
/// </remarks>
public interface IRepositoryListStore
{
    /// <summary>
    /// Reads a profile's list.
    /// </summary>
    /// <param name="profileId">The profile's identifier.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The profile's repositories, in order; empty for a profile with none.</returns>
    Task<IReadOnlyList<ListedRepository>> GetAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a repository at the end of a profile's list, or — when it is listed already — keeps it where
    /// it stands and takes the new name.
    /// </summary>
    /// <param name="profileId">The profile's identifier.</param>
    /// <param name="path">The repository's work-tree path.</param>
    /// <param name="name">The repository's display name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The profile's updated list.</returns>
    Task<IReadOnlyList<ListedRepository>> AddAsync(
        string profileId,
        string path,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a repository out of a profile's list. The repository itself is not touched.
    /// </summary>
    /// <param name="profileId">The profile's identifier.</param>
    /// <param name="path">The repository's work-tree path.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The profile's updated list.</returns>
    Task<IReadOnlyList<ListedRepository>> RemoveAsync(
        string profileId,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets a profile's whole list, which is what deleting the profile does.
    /// </summary>
    /// <param name="profileId">The profile's identifier.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the list is gone.</returns>
    Task RemoveProfileAsync(string profileId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores every profile's list in one versioned JSON file in the user's configuration directory.
/// </summary>
/// <remarks>
/// <para>
/// A new file rather than a new version of the recent-repositories one it replaces: that list belonged
/// to nobody and is not carried over, and a build that still reads the old file can never overwrite
/// this one with it. The old file is left where it is.
/// </para>
/// <para>
/// Every change reads the file again before writing it, and writes it atomically: every running
/// instance shares the file, and a list changed in one must not be lost to a change in another.
/// </para>
/// </remarks>
public sealed class RepositoryListStore : IRepositoryListStore
{
    /// <summary>
    /// The file's name inside the configuration directory.
    /// </summary>
    public const string FileName = "repository-lists.json";

    /// <summary>
    /// The schema version written into the file, so a future format change can migrate rather than
    /// guess.
    /// </summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<RepositoryListStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="paths">Where the configuration directory is.</param>
    /// <param name="logger">Receives a corrupt-file report.</param>
    public RepositoryListStore(IAppPaths paths, ILogger<RepositoryListStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ListedRepository>> GetAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return Read().TryGetValue(profileId, out List<ListedRepository>? entries) ? entries : [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ListedRepository>> AddAsync(
        string profileId,
        string path,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return MutateAsync(
            profileId,
            entries =>
            {
                string full = Path.GetFullPath(path);
                ListedRepository entry = new(full, string.IsNullOrWhiteSpace(name) ? Path.GetFileName(full) : name);
                int index = entries.FindIndex(existing => SamePath(existing.Path, full));

                if (index < 0)
                {
                    entries.Add(entry);
                }
                else
                {
                    entries[index] = entry;
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ListedRepository>> RemoveAsync(
        string profileId,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return MutateAsync(
            profileId,
            entries => entries.RemoveAll(entry => SamePath(entry.Path, Path.GetFullPath(path))),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Dictionary<string, List<ListedRepository>> lists = Read();

            if (lists.Remove(profileId))
            {
                Write(lists);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ListedRepository>> MutateAsync(
        string profileId,
        Action<List<ListedRepository>> mutate,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Dictionary<string, List<ListedRepository>> lists = Read();

            if (!lists.TryGetValue(profileId, out List<ListedRepository>? entries))
            {
                entries = [];
                lists[profileId] = entries;
            }

            mutate(entries);

            Write(lists);
            return [.. entries];
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool SamePath(string left, string right)
        => string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private Dictionary<string, List<ListedRepository>> Read()
    {
        string file = _paths.GetConfigurationFile(FileName);

        if (!File.Exists(file))
        {
            return new Dictionary<string, List<ListedRepository>>(StringComparer.Ordinal);
        }

        try
        {
            StoredDocument? document = JsonSerializer.Deserialize<StoredDocument>(
                File.ReadAllText(file),
                SerializerOptions);

            Dictionary<string, List<ListedRepository>> lists = new(StringComparer.Ordinal);

            if (document?.Profiles is null)
            {
                return lists;
            }

            if (document.Version > CurrentVersion)
            {
                // Written by a newer build. Reading it is still the best available answer: the
                // entries we understand are shown, and the file is only rewritten when the user
                // changes something.
                _logger.LogInformation(
                    "The repository-list file is version {Version}; this build understands {Current}",
                    document.Version,
                    CurrentVersion);
            }

            foreach ((string profileId, List<ListedRepository?>? entries) in document.Profiles)
            {
                // A hand-edited entry without a path cannot be opened or forgotten; the rest of the
                // list is still worth reading.
                lists[profileId] =
                [
                    .. (entries ?? [])
                        .Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Path))
                        .Select(entry => entry! with { Name = entry!.Name ?? string.Empty }),
                ];
            }

            return lists;
        }
        catch (JsonException exception)
        {
            BackUpCorruptFile(file, exception);
            return new Dictionary<string, List<ListedRepository>>(StringComparer.Ordinal);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "The repository-list file could not be read");
            return new Dictionary<string, List<ListedRepository>>(StringComparer.Ordinal);
        }
    }

    private void Write(Dictionary<string, List<ListedRepository>> lists)
    {
        string file = _paths.GetConfigurationFile(FileName);

        Dictionary<string, List<ListedRepository?>?> profiles = new(StringComparer.Ordinal);

        foreach ((string profileId, List<ListedRepository> entries) in lists)
        {
            profiles[profileId] = [.. entries];
        }

        try
        {
            // Atomically: every running instance shares this file, and a reader that caught a
            // plain write half-way through would take the lists for a corrupt file and move it aside.
            AtomicFile.WriteAllText(
                file,
                JsonSerializer.Serialize(new StoredDocument(CurrentVersion, profiles), SerializerOptions));
        }
        catch (IOException exception)
        {
            // Losing a list is an inconvenience, not a failure worth interrupting the user.
            _logger.LogWarning(exception, "The repository-list file could not be written");
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "The repository-list file could not be written");
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
                "The repository-list file was unreadable and has been moved to {Backup}",
                backup);
        }
        catch (IOException)
        {
            _logger.LogWarning(cause, "The repository-list file was unreadable and could not be backed up");
        }
    }

    /// <summary>
    /// The file's shape.
    /// </summary>
    /// <param name="Version">The schema version.</param>
    /// <param name="Profiles">Each profile's list, by the profile's identifier.</param>
    private sealed record StoredDocument(int Version, Dictionary<string, List<ListedRepository?>?>? Profiles);
}
