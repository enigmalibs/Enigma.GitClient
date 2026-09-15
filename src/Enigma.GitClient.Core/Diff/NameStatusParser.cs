using System;
using System.Collections.Generic;
using System.Globalization;
using Enigma.GitClient.Core.Files;

namespace Enigma.GitClient.Core.Diff;

/// <summary>
/// Parses git's <c>--name-status -z</c> and <c>--numstat -z</c> output.
/// </summary>
/// <remarks>
/// <c>-z</c> is not optional here. Without it a rename is written as <c>dir/{old =&gt; new}</c>, a
/// shorthand that cannot be taken apart reliably, and any path containing a space or a quote is
/// escaped. With it, a rename is three plain NUL-separated records and every path is literal.
/// </remarks>
public static class NameStatusParser
{
    /// <summary>
    /// Parses <c>--name-status -z</c> output into changed files.
    /// </summary>
    /// <param name="payload">Everything git wrote to standard output.</param>
    /// <param name="staging">The staging state to stamp on every entry.</param>
    /// <returns>The changed files, in the order git listed them.</returns>
    public static IReadOnlyList<ChangedFile> ParseNameStatus(
        string payload,
        FileStagingState staging = FileStagingState.NotApplicable)
    {
        ArgumentNullException.ThrowIfNull(payload);

        List<ChangedFile> files = [];
        string[] records = payload.Split('\0');
        int index = 0;

        while (index < records.Length)
        {
            string status = records[index];
            index++;

            if (status.Length == 0)
            {
                continue;
            }

            char code = status[0];

            // A rename or a copy carries a similarity score and is followed by two paths rather
            // than one.
            bool hasTwoPaths = code is 'R' or 'C';

            if (index >= records.Length)
            {
                break;
            }

            string first = records[index];
            index++;

            string? second = null;

            if (hasTwoPaths)
            {
                if (index >= records.Length)
                {
                    break;
                }

                second = records[index];
                index++;
            }

            if (first.Length == 0)
            {
                continue;
            }

            files.Add(new ChangedFile
            {
                Path = ChangedFile.NormalisePath(second ?? first),
                OldPath = hasTwoPaths ? ChangedFile.NormalisePath(first) : null,
                ChangeKind = ToChangeKind(code),
                Staging = staging,
                IsConflicted = code == 'U',
            });
        }

        return files;
    }

    /// <summary>
    /// Parses <c>--numstat -z</c> output into per-path line counts.
    /// </summary>
    /// <param name="payload">Everything git wrote to standard output.</param>
    /// <returns>
    /// A map from path to its added and removed line counts. A binary file, which git reports as
    /// <c>-</c>, is present with <see langword="null"/> counts.
    /// </returns>
    public static IReadOnlyDictionary<string, (int? Added, int? Removed)> ParseNumstat(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Dictionary<string, (int? Added, int? Removed)> counts = new(StringComparer.Ordinal);
        string[] records = payload.Split('\0');
        int index = 0;

        while (index < records.Length)
        {
            string record = records[index];
            index++;

            if (record.Length == 0)
            {
                continue;
            }

            string[] fields = record.Split('\t');

            if (fields.Length < 3)
            {
                continue;
            }

            string path = fields[2];

            if (path.Length == 0)
            {
                // A rename: the next two records are the old and the new path.
                if (index + 1 >= records.Length)
                {
                    break;
                }

                index++;
                path = records[index];
                index++;
            }

            counts[ChangedFile.NormalisePath(path)] = (ParseCount(fields[0]), ParseCount(fields[1]));
        }

        return counts;
    }

    /// <summary>
    /// Combines a name-status listing with its line counts.
    /// </summary>
    /// <param name="files">The files from <c>--name-status</c>.</param>
    /// <param name="counts">The counts from <c>--numstat</c>.</param>
    /// <returns>The files, with counts and the binary flag filled in.</returns>
    public static IReadOnlyList<ChangedFile> Combine(
        IReadOnlyList<ChangedFile> files,
        IReadOnlyDictionary<string, (int? Added, int? Removed)> counts)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(counts);

        List<ChangedFile> combined = new(files.Count);

        foreach (ChangedFile file in files)
        {
            if (!counts.TryGetValue(file.Path, out (int? Added, int? Removed) count))
            {
                combined.Add(file);
                continue;
            }

            combined.Add(file with
            {
                AddedLines = count.Added ?? 0,
                RemovedLines = count.Removed ?? 0,

                // git writes "-" for both counts exactly when it treats the file as binary.
                IsBinary = count.Added is null && count.Removed is null,
            });
        }

        return combined;
    }

    /// <summary>
    /// Maps git's status letter to a change kind.
    /// </summary>
    /// <param name="code">The status letter.</param>
    /// <returns>The change kind.</returns>
    public static FileChangeKind ToChangeKind(char code)
        => char.ToUpperInvariant(code) switch
        {
            'A' => FileChangeKind.Added,
            'M' => FileChangeKind.Modified,
            'D' => FileChangeKind.Deleted,
            'R' => FileChangeKind.Renamed,
            'C' => FileChangeKind.Copied,
            'T' => FileChangeKind.TypeChanged,
            'U' => FileChangeKind.Unmerged,
            _ => FileChangeKind.Unknown,
        };

    private static int? ParseCount(string value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
}
