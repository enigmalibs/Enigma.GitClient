using System;
using System.Collections.Generic;
using System.Globalization;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;

namespace Enigma.GitClient.Core.Status;

/// <summary>
/// Parses <c>git status --porcelain=v2 -z</c>.
/// </summary>
/// <remarks>
/// <para>
/// Version 2 is the only status format git promises to keep stable, and the only one that reports a
/// rename's original path unambiguously. With <c>-z</c> every record is NUL-terminated and every
/// path is literal, so a file called <c>"weird name"</c> parses like any other — which version 1
/// cannot manage, because it quotes and escapes.
/// </para>
/// <para>
/// A record's first character says what it is: <c>#</c> a branch header, <c>1</c> an ordinary
/// change, <c>2</c> a rename or copy (whose original path is the <em>next</em> NUL-terminated
/// field), <c>u</c> an unmerged path, <c>?</c> an untracked file and <c>!</c> an ignored one.
/// </para>
/// </remarks>
public static class PorcelainV2Parser
{
    /// <summary>
    /// Parses a whole status payload.
    /// </summary>
    /// <param name="payload">Everything git wrote to standard output.</param>
    /// <returns>The status.</returns>
    public static WorkingTreeStatus Parse(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        string oid = string.Empty;
        string head = string.Empty;
        string upstream = string.Empty;
        int ahead = 0;
        int behind = 0;

        List<ChangedFile> staged = [];
        List<ChangedFile> unstaged = [];
        List<ChangedFile> untracked = [];
        List<ChangedFile> conflicted = [];
        List<ChangedFile> ignored = [];

        string[] records = payload.Split('\u0000');

        for (int index = 0; index < records.Length; index++)
        {
            string record = records[index];

            if (record.Length == 0)
            {
                continue;
            }

            switch (record[0])
            {
                case '#':
                    ReadBranchHeader(record, ref oid, ref head, ref upstream, ref ahead, ref behind);
                    break;

                case '1':
                    AddOrdinary(record, staged, unstaged);
                    break;

                case '2':
                    // The original path is a field of its own, not part of the record, so it is the
                    // next entry in the NUL-separated stream.
                    string? original = index + 1 < records.Length ? records[index + 1] : null;
                    index++;
                    AddRenamed(record, original, staged, unstaged);
                    break;

                case 'u':
                    AddUnmerged(record, conflicted);
                    break;

                case '?':
                    untracked.Add(Simple(record, FileChangeKind.Added, FileStagingState.Unstaged));
                    break;

                case '!':
                    ignored.Add(Simple(record, FileChangeKind.Unknown, FileStagingState.NotApplicable));
                    break;

                default:
                    // An unknown record kind is git telling us about something this version does not
                    // model. Skipping it is better than guessing.
                    break;
            }
        }

        return new WorkingTreeStatus(
            new WorkingTreeBranch(oid, head, upstream, ahead, behind),
            staged,
            unstaged,
            untracked,
            conflicted,
            ignored);
    }

    /// <summary>
    /// Maps one of git's status letters to a change kind.
    /// </summary>
    /// <param name="code">The letter, where <c>.</c> means unmodified.</param>
    /// <returns>The change kind, or <see langword="null"/> for an unmodified side.</returns>
    public static FileChangeKind? ToChangeKind(char code)
        => code switch
        {
            '.' => null,
            'M' => FileChangeKind.Modified,
            'A' => FileChangeKind.Added,
            'D' => FileChangeKind.Deleted,
            'R' => FileChangeKind.Renamed,
            'C' => FileChangeKind.Copied,
            'T' => FileChangeKind.TypeChanged,
            'U' => FileChangeKind.Unmerged,
            _ => FileChangeKind.Unknown,
        };

    private static void ReadBranchHeader(
        string record,
        ref string oid,
        ref string head,
        ref string upstream,
        ref int ahead,
        ref int behind)
    {
        // "# branch.ab +1 -2" — the key is the second space-separated token.
        string[] parts = record.Split(' ');

        if (parts.Length < 3)
        {
            return;
        }

        switch (parts[1])
        {
            case "branch.oid":
                oid = parts[2];
                break;

            case "branch.head":
                head = parts[2];
                break;

            case "branch.upstream":
                upstream = parts[2];
                break;

            case "branch.ab" when parts.Length >= 4:
                ahead = ParseSigned(parts[2]);
                behind = ParseSigned(parts[3]);
                break;

            default:
                break;
        }
    }

    private static int ParseSigned(string value)
    {
        // The counts arrive as "+3" and "-0"; the sign says which side it is, not the magnitude.
        string digits = value.Length > 0 && (value[0] == '+' || value[0] == '-') ? value[1..] : value;

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : 0;
    }

    /// <summary>
    /// Reads a <c>1</c> record: <c>1 &lt;XY&gt; &lt;sub&gt; &lt;mH&gt; &lt;mI&gt; &lt;mW&gt;
    /// &lt;hH&gt; &lt;hI&gt; &lt;path&gt;</c>.
    /// </summary>
    private static void AddOrdinary(string record, List<ChangedFile> staged, List<ChangedFile> unstaged)
    {
        string[] fields = record.Split(' ', 9);

        if (fields.Length < 9)
        {
            return;
        }

        Emit(fields[1], fields[2], fields[8], null, staged, unstaged);
    }

    /// <summary>
    /// Reads a <c>2</c> record, which is a <c>1</c> record plus a score field.
    /// </summary>
    private static void AddRenamed(
        string record,
        string? original,
        List<ChangedFile> staged,
        List<ChangedFile> unstaged)
    {
        string[] fields = record.Split(' ', 10);

        if (fields.Length < 10)
        {
            return;
        }

        Emit(fields[1], fields[2], fields[9], original, staged, unstaged);
    }

    /// <summary>
    /// Reads a <c>u</c> record: an unmerged path, whose XY is the pair of conflict states.
    /// </summary>
    private static void AddUnmerged(string record, List<ChangedFile> conflicted)
    {
        string[] fields = record.Split(' ', 11);

        if (fields.Length < 11)
        {
            return;
        }

        conflicted.Add(new ChangedFile
        {
            Path = ChangedFile.NormalisePath(fields[10]),
            ChangeKind = FileChangeKind.Unmerged,
            Staging = FileStagingState.Unstaged,
            IsConflicted = true,
            IndexStatus = ToChangeKind(fields[1][0]),
            WorkTreeStatus = fields[1].Length > 1 ? ToChangeKind(fields[1][1]) : null,
            ConflictState = fields[1],
            Submodule = SubmoduleState.Parse(fields[2]),
            IsSubmodule = SubmoduleState.Parse(fields[2]).IsSubmodule,
        });
    }

    /// <summary>
    /// Turns one XY pair into up to two entries: a staged one when the index differs from HEAD, and
    /// an unstaged one when the work tree differs from the index. A file edited, staged and edited
    /// again is genuinely in both lists, and hiding either half is how a client loses a change.
    /// </summary>
    private static void Emit(
        string xy,
        string submoduleField,
        string path,
        string? original,
        List<ChangedFile> staged,
        List<ChangedFile> unstaged)
    {
        if (xy.Length < 2)
        {
            return;
        }

        FileChangeKind? index = ToChangeKind(xy[0]);
        FileChangeKind? workTree = ToChangeKind(xy[1]);

        SubmoduleState submodule = SubmoduleState.Parse(submoduleField);
        string normalised = ChangedFile.NormalisePath(path);
        string? normalisedOriginal = original is { Length: > 0 } ? ChangedFile.NormalisePath(original) : null;

        bool both = index is not null && workTree is not null;

        if (index is not null)
        {
            staged.Add(new ChangedFile
            {
                Path = normalised,
                OldPath = normalisedOriginal,
                ChangeKind = index.Value,
                Staging = both ? FileStagingState.PartiallyStaged : FileStagingState.Staged,
                IndexStatus = index,
                WorkTreeStatus = workTree,
                Submodule = submodule,
                IsSubmodule = submodule.IsSubmodule,
            });
        }

        if (workTree is not null)
        {
            unstaged.Add(new ChangedFile
            {
                Path = normalised,

                // The work-tree half of a rename is a change to the *new* path; naming the old one
                // here would suggest the rename is unstaged, which it is not.
                OldPath = index is null ? normalisedOriginal : null,
                ChangeKind = workTree.Value,
                Staging = both ? FileStagingState.PartiallyStaged : FileStagingState.Unstaged,
                IndexStatus = index,
                WorkTreeStatus = workTree,
                Submodule = submodule,
                IsSubmodule = submodule.IsSubmodule,
            });
        }
    }

    private static ChangedFile Simple(string record, FileChangeKind kind, FileStagingState staging)
        => new()
        {
            Path = ChangedFile.NormalisePath(record[1..].TrimStart(' ')),
            ChangeKind = kind,
            Staging = staging,
            IsUntracked = staging == FileStagingState.Unstaged,
        };
}
