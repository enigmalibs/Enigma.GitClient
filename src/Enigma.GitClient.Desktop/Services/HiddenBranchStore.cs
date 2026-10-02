using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Enigma.GitClient.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Remembers, per repository, which branches the user hid from the history.
/// </summary>
/// <remarks>
/// Synchronous on purpose: the history asks for the set as it builds each query, on the UI thread and
/// before git runs, and the file it reads is a few hundred bytes. An asynchronous read would either
/// race the first page of a repository just opened — drawing the hidden branches for a moment — or
/// hold that page back.
/// </remarks>
public interface IHiddenBranchStore
{
    /// <summary>
    /// Reads the refs hidden in a repository.
    /// </summary>
    /// <param name="workTreePath">The repository's work tree.</param>
    /// <returns>The full names of the hidden refs; empty when there are none.</returns>
    IReadOnlySet<string> Get(string workTreePath);

    /// <summary>
    /// Hides a ref in a repository, or shows it again.
    /// </summary>
    /// <param name="workTreePath">The repository's work tree.</param>
    /// <param name="refName">The ref's full name — <c>refs/heads/topic</c>.</param>
    /// <param name="hidden">Whether it is hidden from now on.</param>
    /// <param name="existingRefs">
    /// Every ref the repository has, or <see langword="null"/> when that is not known: the entries of
    /// refs no longer among them are dropped as the set is written.
    /// </param>
    /// <returns>The repository's hidden refs, as written.</returns>
    /// <exception cref="ArgumentException"><paramref name="refName"/> is not a full ref name.</exception>
    IReadOnlySet<string> SetHidden(string workTreePath, string refName, bool hidden, IReadOnlyCollection<string>? existingRefs = null);

    /// <summary>
    /// Shows every ref of a repository again.
    /// </summary>
    /// <param name="workTreePath">The repository's work tree.</param>
    void ShowAll(string workTreePath);
}

/// <summary>
/// Default <see cref="IHiddenBranchStore"/>: a versioned JSON document in the configuration directory,
/// one list of full ref names per work tree.
/// </summary>
/// <remarks>
/// <para>
/// Every change reads the file again before writing it, and writes it atomically: several instances of
/// the application share the file, and a branch hidden in one repository's window must not be lost to
/// a write from another's.
/// </para>
/// <para>
/// A file that cannot be read is moved aside rather than overwritten by the next write, as the
/// identity profiles are. Losing it is an inconvenience — every branch shows again — never a failure
/// worth interrupting anyone for, so a read or a write that fails is logged and the set it would have
/// changed is what the session carries on with.
/// </para>
/// </remarks>
public sealed class HiddenBranchStore : IHiddenBranchStore
{
    /// <summary>The file's name inside the configuration directory.</summary>
    public const string FileName = "hidden-branches.json";

    /// <summary>The schema version written into the file.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly IReadOnlySet<string> Nothing = new HashSet<string>(StringComparer.Ordinal);

    private readonly IAppPaths _paths;
    private readonly ILogger<HiddenBranchStore> _logger;
    private readonly Lock _gate = new();

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="paths">Where the configuration directory is.</param>
    /// <param name="logger">Receives a file that could not be read or written.</param>
    public HiddenBranchStore(IAppPaths paths, ILogger<HiddenBranchStore> logger)
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
    public IReadOnlySet<string> Get(string workTreePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workTreePath);

        lock (_gate)
        {
            Dictionary<string, List<string>> repositories = Read();

            return Find(repositories, workTreePath) is { } key ? ToSet(repositories[key]) : Nothing;
        }
    }

    /// <inheritdoc />
    public IReadOnlySet<string> SetHidden(
        string workTreePath,
        string refName,
        bool hidden,
        IReadOnlyCollection<string>? existingRefs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workTreePath);

        if (!IsRefName(refName))
        {
            throw new ArgumentException($"'{refName}' is not a full ref name.", nameof(refName));
        }

        lock (_gate)
        {
            Dictionary<string, List<string>> repositories = Read();
            string key = Find(repositories, workTreePath) ?? Normalise(workTreePath);

            HashSet<string> set = repositories.TryGetValue(key, out List<string>? stored)
                ? [.. ToSet(stored)]
                : new HashSet<string>(StringComparer.Ordinal);

            if (hidden)
            {
                set.Add(refName);
            }
            else
            {
                set.Remove(refName);
            }

            // A branch deleted since it was hidden: its entry would only come back to life on a new
            // branch of the same name.
            if (existingRefs is not null)
            {
                set.RemoveWhere(reference => !string.Equals(reference, refName, StringComparison.Ordinal)
                    && !existingRefs.Contains(reference));
            }

            if (set.Count == 0)
            {
                repositories.Remove(key);
            }
            else
            {
                repositories[key] = [.. set.Order(StringComparer.Ordinal)];
            }

            Write(repositories);

            return set;
        }
    }

    /// <inheritdoc />
    public void ShowAll(string workTreePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workTreePath);

        lock (_gate)
        {
            Dictionary<string, List<string>> repositories = Read();

            if (Find(repositories, workTreePath) is { } key)
            {
                repositories.Remove(key);
                Write(repositories);
            }
        }
    }

    /// <summary>
    /// Answers whether a string is one full ref name, which is all this file keeps.
    /// </summary>
    private static bool IsRefName(string? value)
        => value is { Length: > 5 }
           && value.StartsWith("refs/", StringComparison.Ordinal)
           && value.AsSpan().IndexOfAny("*?[ \t\n") < 0;

    private static IReadOnlySet<string> ToSet(IEnumerable<string?> entries)
        => entries.Where(IsRefName).Select(entry => entry!).ToHashSet(StringComparer.Ordinal);

    private static string Normalise(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Finds the key a work tree is stored under — the same directory spelled with or without a
    /// trailing separator, and in any case on Windows.
    /// </summary>
    private static string? Find(Dictionary<string, List<string>> repositories, string workTreePath)
    {
        string wanted = Normalise(workTreePath);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (string key in repositories.Keys)
        {
            if (string.Equals(Normalise(key), wanted, comparison))
            {
                return key;
            }
        }

        return null;
    }

    private Dictionary<string, List<string>> Read()
    {
        string file = _paths.GetConfigurationFile(FileName);
        Dictionary<string, List<string>> repositories = new(StringComparer.Ordinal);

        if (!File.Exists(file))
        {
            return repositories;
        }

        try
        {
            StoredDocument? document = JsonSerializer.Deserialize<StoredDocument>(File.ReadAllText(file), SerializerOptions);

            foreach ((string path, List<string>? entries) in document?.Repositories ?? [])
            {
                // A hand-edited entry that is not a full ref name hides nothing; the rest of the
                // file is still worth reading.
                if (!string.IsNullOrWhiteSpace(path) && entries is not null)
                {
                    repositories[path] = [.. ToSet(entries)];
                }
            }
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The hidden branches could not be read; the file was moved aside");
            Backup(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The hidden branches could not be read");
        }

        return repositories;
    }

    private void Write(Dictionary<string, List<string>> repositories)
    {
        string file = _paths.GetConfigurationFile(FileName);

        try
        {
            AtomicFile.WriteAllText(
                file,
                JsonSerializer.Serialize(
                    new StoredDocument(CurrentVersion, repositories.ToDictionary(pair => pair.Key, List<string>? (pair) => pair.Value, StringComparer.Ordinal)),
                    SerializerOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The hidden branches could not be written");
        }
    }

    private void Backup(string file)
    {
        try
        {
            string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string backup = Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, $"hidden-branches.corrupt-{stamp}.json");

            File.Move(file, backup, overwrite: true);
            BackupPath = backup;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The unreadable hidden branches could not be moved aside");
        }
    }

    /// <summary>
    /// The file's shape.
    /// </summary>
    /// <param name="Version">The schema version.</param>
    /// <param name="Repositories">The hidden refs, by work-tree path.</param>
    /// <remarks>An entry of a list may still be <see langword="null"/> in a hand-edited file; <see cref="ToSet"/> drops it.</remarks>
    private sealed record StoredDocument(int Version, Dictionary<string, List<string>?>? Repositories);
}
