using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Enigma.GitClient.Core.Diff;

/// <summary>
/// What one row of the rendered diff stands for.
/// </summary>
public enum DiffRowKind
{
    /// <summary>The band carrying a hunk's <c>@@</c> ranges and its section heading.</summary>
    HunkHeader,

    /// <summary>A line of the patch.</summary>
    Line,

    /// <summary>
    /// Nothing at all. Side-by-side rendering pads the shorter side of a change with these so both
    /// columns stay in step; a unified rendering never produces one.
    /// </summary>
    Filler,
}

/// <summary>
/// One row of the unified rendering.
/// </summary>
public sealed class DiffRow
{
    /// <summary>
    /// Initialises a hunk-header row.
    /// </summary>
    /// <param name="hunk">The hunk the band introduces.</param>
    public DiffRow(DiffHunk hunk)
    {
        ArgumentNullException.ThrowIfNull(hunk);

        Kind = DiffRowKind.HunkHeader;
        Hunk = hunk;
    }

    /// <summary>
    /// Initialises a line row.
    /// </summary>
    /// <param name="line">The line.</param>
    public DiffRow(DiffLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Kind = DiffRowKind.Line;
        Line = line;
    }

    /// <summary>Gets what the row stands for.</summary>
    public DiffRowKind Kind { get; }

    /// <summary>Gets the hunk, for a header row.</summary>
    public DiffHunk? Hunk { get; }

    /// <summary>Gets the line, for a line row.</summary>
    public DiffLine? Line { get; }

    /// <inheritdoc />
    public override string ToString() => Hunk?.Header ?? Line?.ToString() ?? string.Empty;
}

/// <summary>
/// One row of the side-by-side rendering: what the old side shows on that line, and what the new
/// side shows beside it. Either may be absent, which is what a filler is.
/// </summary>
public sealed class DiffPairRow
{
    /// <summary>
    /// Initialises a hunk-header row, which spans both sides.
    /// </summary>
    /// <param name="hunk">The hunk the band introduces.</param>
    public DiffPairRow(DiffHunk hunk)
    {
        ArgumentNullException.ThrowIfNull(hunk);

        Kind = DiffRowKind.HunkHeader;
        Hunk = hunk;
    }

    /// <summary>
    /// Initialises a paired line row.
    /// </summary>
    /// <param name="left">The old side's line, or <see langword="null"/>.</param>
    /// <param name="right">The new side's line, or <see langword="null"/>.</param>
    public DiffPairRow(DiffLine? left, DiffLine? right)
    {
        Kind = left is null && right is null ? DiffRowKind.Filler : DiffRowKind.Line;
        Left = left;
        Right = right;
    }

    /// <summary>Gets what the row stands for.</summary>
    public DiffRowKind Kind { get; }

    /// <summary>Gets the hunk, for a header row.</summary>
    public DiffHunk? Hunk { get; }

    /// <summary>Gets the old side's line, absent when the new side added one.</summary>
    public DiffLine? Left { get; }

    /// <summary>Gets the new side's line, absent when the old side lost one.</summary>
    public DiffLine? Right { get; }

    /// <summary>Gets a value indicating whether the old side has nothing on this row.</summary>
    public bool IsLeftFiller => Kind == DiffRowKind.Line && Left is null;

    /// <summary>Gets a value indicating whether the new side has nothing on this row.</summary>
    public bool IsRightFiller => Kind == DiffRowKind.Line && Right is null;

    /// <inheritdoc />
    public override string ToString()
        => Hunk is not null
            ? Hunk.Header
            : $"{Left?.Text ?? "—"} | {Right?.Text ?? "—"}";
}

/// <summary>
/// Projects a parsed patch onto the flat row lists the viewer renders, in either shape.
/// </summary>
/// <remarks>
/// The projection lives here, beside the model, rather than in the ViewModel: it is pure list
/// arithmetic with no UI in it, the alignment is the part most worth testing, and both shapes have
/// to agree about line numbering. A flat list is also what a virtualising panel needs — it can
/// realise row 8 000 without walking the 7 999 before it.
/// </remarks>
public static class DiffRowBuilder
{
    /// <summary>
    /// Projects a patch onto the unified rendering: every hunk's band, then its lines in order.
    /// </summary>
    /// <param name="patch">The parsed patch.</param>
    /// <returns>The rows.</returns>
    public static IReadOnlyList<DiffRow> BuildUnified(FilePatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        List<DiffRow> rows = [];

        foreach (DiffHunk hunk in patch.Hunks)
        {
            rows.Add(new DiffRow(hunk));

            foreach (DiffLine line in hunk.Lines)
            {
                rows.Add(new DiffRow(line));
            }
        }

        return rows;
    }

    /// <summary>
    /// Projects a patch onto the side-by-side rendering.
    /// </summary>
    /// <param name="patch">The parsed patch.</param>
    /// <returns>The rows.</returns>
    /// <remarks>
    /// A run of removed lines immediately followed by a run of added lines is one edit, and the two
    /// runs are paired line by line so a reader's eye can move straight across. Whichever run is
    /// shorter is padded with fillers, which keeps every later line on the same row on both sides —
    /// without that, one unbalanced edit shifts the rest of the file and the view stops being
    /// readable.
    /// </remarks>
    public static IReadOnlyList<DiffPairRow> BuildSideBySide(FilePatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        List<DiffPairRow> rows = [];

        foreach (DiffHunk hunk in patch.Hunks)
        {
            rows.Add(new DiffPairRow(hunk));
            AlignHunk(hunk, rows);
        }

        return rows;
    }

    /// <summary>
    /// Renders lines as they would be copied: their text, with no <c>+</c> or <c>-</c> marker.
    /// </summary>
    /// <param name="lines">The lines to copy.</param>
    /// <returns>The text, newline separated.</returns>
    /// <remarks>
    /// Copying a diff is nearly always copying code, and code pasted with markers on it does not
    /// compile. The whole-patch copy is the other command, and it keeps them.
    /// </remarks>
    public static string CopyText(IEnumerable<DiffLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        StringBuilder builder = new();

        foreach (DiffLine line in lines)
        {
            if (line.Kind == DiffLineKind.NoNewline)
            {
                continue;
            }

            builder.Append(line.Text).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Rebuilds the patch as git wrote it, headers and markers included.
    /// </summary>
    /// <param name="patch">The parsed patch.</param>
    /// <returns>The patch text.</returns>
    public static string PatchText(FilePatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        StringBuilder builder = new();

        builder.Append(CultureInfo.InvariantCulture, $"--- {Side(patch.OldPath, "a")}\n");
        builder.Append(CultureInfo.InvariantCulture, $"+++ {Side(patch.NewPath, "b")}\n");

        foreach (DiffHunk hunk in patch.Hunks)
        {
            builder.Append(hunk.Header).Append('\n');

            foreach (DiffLine line in hunk.Lines)
            {
                builder.Append(line.ToString()).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string Side(string? path, string prefix)
        => path is null ? "/dev/null" : $"{prefix}/{path}";

    private static void AlignHunk(DiffHunk hunk, List<DiffPairRow> rows)
    {
        List<DiffLine> removed = [];
        List<DiffLine> added = [];

        foreach (DiffLine line in hunk.Lines)
        {
            switch (line.Kind)
            {
                case DiffLineKind.Removed:
                    removed.Add(line);
                    break;

                case DiffLineKind.Added:
                    added.Add(line);
                    break;

                case DiffLineKind.NoNewline:
                    // The marker belongs to the side whose line it follows, so it rides along with
                    // that run rather than becoming a row of its own.
                    if (added.Count > 0)
                    {
                        added.Add(line);
                    }
                    else if (removed.Count > 0)
                    {
                        removed.Add(line);
                    }
                    else
                    {
                        rows.Add(new DiffPairRow(line, line));
                    }

                    break;

                case DiffLineKind.Context:
                default:
                    Flush(removed, added, rows);
                    rows.Add(new DiffPairRow(line, line));
                    break;
            }
        }

        Flush(removed, added, rows);
    }

    private static void Flush(List<DiffLine> removed, List<DiffLine> added, List<DiffPairRow> rows)
    {
        int count = Math.Max(removed.Count, added.Count);

        for (int index = 0; index < count; index++)
        {
            rows.Add(new DiffPairRow(
                index < removed.Count ? removed[index] : null,
                index < added.Count ? added[index] : null));
        }

        removed.Clear();
        added.Clear();
    }
}
