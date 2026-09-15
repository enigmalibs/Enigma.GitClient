using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Diff;

/// <summary>
/// What happened to a file between two trees.
/// </summary>
public enum FileChangeKind
{
    /// <summary>The file did not exist before.</summary>
    Added,

    /// <summary>The file existed and its content changed.</summary>
    Modified,

    /// <summary>The file no longer exists.</summary>
    Deleted,

    /// <summary>The file moved, possibly with edits.</summary>
    Renamed,

    /// <summary>The file was copied from another, possibly with edits.</summary>
    Copied,

    /// <summary>Only the file's mode changed — for example it became executable.</summary>
    ModeChanged,

    /// <summary>The entry changed type, for example a file became a symbolic link.</summary>
    TypeChanged,

    /// <summary>The file has unresolved conflicts.</summary>
    Unmerged,

    /// <summary>git reported something the client does not model.</summary>
    Unknown,
}

/// <summary>
/// What one line of a patch represents.
/// </summary>
public enum DiffLineKind
{
    /// <summary>A line present on both sides, shown for context.</summary>
    Context,

    /// <summary>A line only the new side has.</summary>
    Added,

    /// <summary>A line only the old side has.</summary>
    Removed,

    /// <summary>
    /// git's <c>\ No newline at end of file</c> marker. It is kept as a line so the viewer can show
    /// it, because "the file lost its trailing newline" is a real, reviewable change.
    /// </summary>
    NoNewline,
}

/// <summary>
/// A stretch of a line, marked as changed or unchanged by the word-level diff.
/// </summary>
/// <param name="Start">The index of the first character, into the line's text.</param>
/// <param name="Length">How many characters the stretch covers.</param>
/// <param name="IsChanged">Whether the stretch differs from the paired line.</param>
public readonly record struct DiffSegment(int Start, int Length, bool IsChanged)
{
    /// <summary>
    /// Gets the index just past the stretch.
    /// </summary>
    public int End => Start + Length;
}

/// <summary>
/// One line of a patch.
/// </summary>
public sealed class DiffLine
{
    private static readonly IReadOnlyList<DiffSegment> NoSegments = [];

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="kind">What the line represents.</param>
    /// <param name="text">The line's content, without its leading <c>+</c>, <c>-</c> or space.</param>
    /// <param name="oldLineNumber">The line's number on the old side, when it has one.</param>
    /// <param name="newLineNumber">The line's number on the new side, when it has one.</param>
    public DiffLine(DiffLineKind kind, string text, int? oldLineNumber, int? newLineNumber)
    {
        ArgumentNullException.ThrowIfNull(text);

        Kind = kind;
        Text = text;
        OldLineNumber = oldLineNumber;
        NewLineNumber = newLineNumber;
        Segments = NoSegments;
    }

    /// <summary>
    /// Gets what the line represents.
    /// </summary>
    public DiffLineKind Kind { get; }

    /// <summary>
    /// Gets the line's content, without the leading marker character.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the line's number on the old side, or <see langword="null"/> when it has none.
    /// </summary>
    public int? OldLineNumber { get; }

    /// <summary>
    /// Gets the line's number on the new side, or <see langword="null"/> when it has none.
    /// </summary>
    public int? NewLineNumber { get; }

    /// <summary>
    /// Gets the word-level segments, empty when the line was not paired with another. An empty list
    /// means "highlight the whole line by its kind"; a non-empty one means "tint only the changed
    /// stretches more strongly".
    /// </summary>
    public IReadOnlyList<DiffSegment> Segments { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether a word-level diff produced segments for this line.
    /// </summary>
    public bool HasSegments => Segments.Count > 0;

    /// <inheritdoc />
    public override string ToString()
        => Kind switch
        {
            DiffLineKind.Added => $"+{Text}",
            DiffLineKind.Removed => $"-{Text}",
            DiffLineKind.NoNewline => $"\\{Text}",
            _ => $" {Text}",
        };
}

/// <summary>
/// One contiguous block of changes inside a file, with the surrounding context git chose to include.
/// </summary>
public sealed class DiffHunk
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="oldStart">The first line number the hunk covers on the old side.</param>
    /// <param name="oldCount">How many old-side lines the hunk covers.</param>
    /// <param name="newStart">The first line number the hunk covers on the new side.</param>
    /// <param name="newCount">How many new-side lines the hunk covers.</param>
    /// <param name="sectionHeading">
    /// The context git appends after the <c>@@</c> markers — usually the enclosing function.
    /// </param>
    /// <param name="lines">The hunk's lines.</param>
    public DiffHunk(
        int oldStart,
        int oldCount,
        int newStart,
        int newCount,
        string sectionHeading,
        IReadOnlyList<DiffLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        OldStart = oldStart;
        OldCount = oldCount;
        NewStart = newStart;
        NewCount = newCount;
        SectionHeading = sectionHeading ?? string.Empty;
        Lines = lines;
    }

    /// <summary>Gets the first line number the hunk covers on the old side.</summary>
    public int OldStart { get; }

    /// <summary>Gets how many old-side lines the hunk covers.</summary>
    public int OldCount { get; }

    /// <summary>Gets the first line number the hunk covers on the new side.</summary>
    public int NewStart { get; }

    /// <summary>Gets how many new-side lines the hunk covers.</summary>
    public int NewCount { get; }

    /// <summary>
    /// Gets the context git appends after the <c>@@</c> markers, empty when there is none.
    /// </summary>
    public string SectionHeading { get; }

    /// <summary>Gets the hunk's lines, in order.</summary>
    public IReadOnlyList<DiffLine> Lines { get; }

    /// <summary>
    /// Gets the hunk's header as git writes it, which is what the viewer shows in its band.
    /// </summary>
    public string Header
    {
        get
        {
            string range = $"@@ -{FormatRange(OldStart, OldCount)} +{FormatRange(NewStart, NewCount)} @@";
            return SectionHeading.Length == 0 ? range : $"{range} {SectionHeading}";
        }
    }

    private static string FormatRange(int start, int count)
        => count == 1
            ? start.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{start.ToString(System.Globalization.CultureInfo.InvariantCulture)},{count.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <inheritdoc />
    public override string ToString() => Header;
}

/// <summary>
/// Everything that happened to one file, as the viewer needs it.
/// </summary>
public sealed class FilePatch
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="oldPath">The path before the change, <see langword="null"/> for an addition.</param>
    /// <param name="newPath">The path after the change, <see langword="null"/> for a deletion.</param>
    /// <param name="changeKind">What happened to the file.</param>
    /// <param name="hunks">The hunks, empty for a binary file or a pure mode change.</param>
    /// <param name="oldMode">The file's mode before the change.</param>
    /// <param name="newMode">The file's mode after the change.</param>
    /// <param name="similarityIndex">The similarity percentage git reported for a rename or copy.</param>
    /// <param name="isBinary">Whether git refused to show a textual diff.</param>
    /// <param name="isCombined">Whether this is a merge's combined diff.</param>
    /// <param name="isTruncated">Whether parsing stopped early because the file was too large.</param>
    public FilePatch(
        string? oldPath,
        string? newPath,
        FileChangeKind changeKind,
        IReadOnlyList<DiffHunk> hunks,
        string? oldMode = null,
        string? newMode = null,
        int? similarityIndex = null,
        bool isBinary = false,
        bool isCombined = false,
        bool isTruncated = false)
    {
        ArgumentNullException.ThrowIfNull(hunks);

        OldPath = oldPath;
        NewPath = newPath;
        ChangeKind = changeKind;
        Hunks = hunks;
        OldMode = oldMode;
        NewMode = newMode;
        SimilarityIndex = similarityIndex;
        IsBinary = isBinary;
        IsCombined = isCombined;
        IsTruncated = isTruncated;

        int added = 0;
        int removed = 0;

        foreach (DiffHunk hunk in hunks)
        {
            foreach (DiffLine line in hunk.Lines)
            {
                if (line.Kind == DiffLineKind.Added)
                {
                    added++;
                }
                else if (line.Kind == DiffLineKind.Removed)
                {
                    removed++;
                }
            }
        }

        AddedLines = added;
        RemovedLines = removed;
    }

    /// <summary>Gets the path before the change, or <see langword="null"/> for an addition.</summary>
    public string? OldPath { get; }

    /// <summary>Gets the path after the change, or <see langword="null"/> for a deletion.</summary>
    public string? NewPath { get; }

    /// <summary>
    /// Gets the path the UI labels the file with: the new path when there is one, the old one
    /// otherwise.
    /// </summary>
    public string DisplayPath => NewPath ?? OldPath ?? string.Empty;

    /// <summary>Gets what happened to the file.</summary>
    public FileChangeKind ChangeKind { get; }

    /// <summary>Gets the hunks, empty for a binary file or a pure mode change.</summary>
    public IReadOnlyList<DiffHunk> Hunks { get; }

    /// <summary>Gets the file's mode before the change, when git reported one.</summary>
    public string? OldMode { get; }

    /// <summary>Gets the file's mode after the change, when git reported one.</summary>
    public string? NewMode { get; }

    /// <summary>Gets the similarity percentage git reported for a rename or a copy.</summary>
    public int? SimilarityIndex { get; }

    /// <summary>Gets a value indicating whether git refused to show a textual diff.</summary>
    public bool IsBinary { get; }

    /// <summary>
    /// Gets a value indicating whether this is a merge's combined diff, whose lines carry one
    /// marker per parent and so are not rendered like an ordinary patch.
    /// </summary>
    public bool IsCombined { get; }

    /// <summary>
    /// Gets a value indicating whether parsing stopped early because the file exceeded the
    /// configured limit. The hunks that were parsed are still present.
    /// </summary>
    public bool IsTruncated { get; }

    /// <summary>Gets how many lines the change adds.</summary>
    public int AddedLines { get; }

    /// <summary>Gets how many lines the change removes.</summary>
    public int RemovedLines { get; }

    /// <summary>
    /// Gets a value indicating whether the entry is a submodule, which git represents with mode
    /// <c>160000</c> and a <c>Subproject commit</c> line rather than content.
    /// </summary>
    public bool IsSubmodule
        => string.Equals(OldMode, "160000", StringComparison.Ordinal) ||
           string.Equals(NewMode, "160000", StringComparison.Ordinal);

    /// <summary>
    /// Gets a value indicating whether the patch carries no line-level change at all — a pure mode
    /// change, a pure rename, or a binary file.
    /// </summary>
    public bool HasNoTextualChange => Hunks.Count == 0;

    /// <inheritdoc />
    public override string ToString() => $"{ChangeKind} {DisplayPath} +{AddedLines} -{RemovedLines}";
}

/// <summary>
/// A whole patch: every file it touches.
/// </summary>
/// <param name="Files">The files, in the order git emitted them.</param>
public sealed record PatchSet(IReadOnlyList<FilePatch> Files)
{
    /// <summary>
    /// An empty patch, which is what an unchanged comparison produces.
    /// </summary>
    public static readonly PatchSet Empty = new([]);

    /// <summary>Gets how many lines the whole patch adds.</summary>
    public int AddedLines
    {
        get
        {
            int total = 0;
            foreach (FilePatch file in Files)
            {
                total += file.AddedLines;
            }

            return total;
        }
    }

    /// <summary>Gets how many lines the whole patch removes.</summary>
    public int RemovedLines
    {
        get
        {
            int total = 0;
            foreach (FilePatch file in Files)
            {
                total += file.RemovedLines;
            }

            return total;
        }
    }

    /// <summary>Gets a value indicating whether the patch touches nothing.</summary>
    public bool IsEmpty => Files.Count == 0;
}
