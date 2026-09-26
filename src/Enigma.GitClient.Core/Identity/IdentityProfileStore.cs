using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Core.Identity;

/// <summary>
/// The identity profiles the user keeps.
/// </summary>
public interface IIdentityProfileStore
{
    /// <summary>
    /// Reads every profile.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The profiles, in the order they were added.</returns>
    Task<IReadOnlyList<IdentityProfile>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a profile, or replaces the one with the same identifier where it stands.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The profile as it was stored, trimmed.</returns>
    /// <exception cref="ArgumentException">The profile is not usable; nothing was written.</exception>
    Task<IdentityProfile> SaveAsync(IdentityProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a profile. git's configuration is not touched.
    /// </summary>
    /// <param name="profileId">The profile's identifier.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when there was a profile to remove.</returns>
    Task<bool> RemoveAsync(string profileId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IIdentityProfileStore"/>: a versioned JSON list in the configuration directory.
/// </summary>
/// <remarks>
/// <para>
/// Every change reads the file again before writing it, and writes it atomically: several instances of
/// the application share the file, and a profile added in one must not be lost to a save in another.
/// </para>
/// <para>
/// A file that cannot be read is moved aside rather than overwritten by the next save, as the settings
/// file is — the profiles in it are something the user typed.
/// </para>
/// </remarks>
public sealed class IdentityProfileStore : IIdentityProfileStore, IDisposable
{
    /// <summary>The file holding the profiles.</summary>
    public const string FileName = "identity-profiles.json";

    /// <summary>The schema version written into the file.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<IdentityProfileStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="paths">Where the configuration directory is.</param>
    /// <param name="logger">Receives a corrupt-file report.</param>
    public IdentityProfileStore(IAppPaths paths, ILogger<IdentityProfileStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Gets the path an unreadable file was moved to, or <see langword="null"/> when that has not
    /// happened.
    /// </summary>
    public string? BackupPath { get; private set; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentityProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IdentityProfile> SaveAsync(IdentityProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Id);

        IdentityProfile stored = profile.With(profile.Label, profile.Identity);

        if (IdentityProfileRules.Validate(stored) is { } problem)
        {
            throw new ArgumentException(problem, nameof(profile));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            List<IdentityProfile> profiles = [.. await ReadAsync(cancellationToken).ConfigureAwait(false)];
            int index = profiles.FindIndex(existing => string.Equals(existing.Id, stored.Id, StringComparison.Ordinal));

            if (index < 0)
            {
                profiles.Add(stored);
            }
            else
            {
                profiles[index] = stored;
            }

            await WriteAsync(profiles, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        return stored;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            List<IdentityProfile> profiles = [.. await ReadAsync(cancellationToken).ConfigureAwait(false)];

            if (profiles.RemoveAll(existing => string.Equals(existing.Id, profileId, StringComparison.Ordinal)) == 0)
            {
                return false;
            }

            await WriteAsync(profiles, cancellationToken).ConfigureAwait(false);

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task<IReadOnlyList<IdentityProfile>> ReadAsync(CancellationToken cancellationToken)
    {
        string file = _paths.GetConfigurationFile(FileName);

        if (!File.Exists(file))
        {
            return [];
        }

        try
        {
            string json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            StoredDocument? document = JsonSerializer.Deserialize<StoredDocument>(json, SerializerOptions);

            List<IdentityProfile> profiles = [];

            foreach (IdentityProfile? profile in document?.Profiles ?? [])
            {
                // A hand-edited entry without an identifier cannot be edited or removed; the rest of
                // the file is still worth reading.
                if (profile is null || string.IsNullOrWhiteSpace(profile.Id))
                {
                    continue;
                }

                profiles.Add(profile with
                {
                    Label = profile.Label ?? string.Empty,
                    Name = profile.Name ?? string.Empty,
                    Email = profile.Email ?? string.Empty,
                });
            }

            return profiles;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The identity profiles could not be read; the file was moved aside");

            Backup(file);

            return [];
        }
    }

    private async Task WriteAsync(List<IdentityProfile> profiles, CancellationToken cancellationToken)
    {
        string file = _paths.GetConfigurationFile(FileName);
        string json = JsonSerializer.Serialize(new StoredDocument(CurrentVersion, [.. profiles]), SerializerOptions);

        await AtomicFile.WriteAllTextAsync(file, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    private void Backup(string file)
    {
        try
        {
            string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string backup = Path.Combine(
                Path.GetDirectoryName(file) ?? string.Empty,
                $"identity-profiles.corrupt-{stamp}.json");

            File.Move(file, backup, overwrite: true);

            BackupPath = backup;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing more to do: an empty list is what this session runs on, and the file stays
            // where it was for the user to look at.
            _logger.LogWarning(exception, "The unreadable identity profiles could not be moved aside");
        }
    }

    /// <summary>
    /// The file's shape.
    /// </summary>
    /// <param name="Version">The schema version.</param>
    /// <param name="Profiles">The profiles.</param>
    private sealed record StoredDocument(int Version, List<IdentityProfile?>? Profiles);
}
