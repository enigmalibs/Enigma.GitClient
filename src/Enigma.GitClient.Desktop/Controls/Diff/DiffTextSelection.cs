using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Desktop.Controls.Diff;

/// <summary>
/// Which column of text a selection is made in.
/// </summary>
/// <remarks>
/// A selection stays in the pane it started in: the two sides of the side-by-side rendering are two
/// files, and a block that ran across both would be neither.
/// </remarks>
public enum DiffPane
{
    /// <summary>The unified rendering's one text column.</summary>
    Unified,

    /// <summary>The side-by-side rendering's old file.</summary>
    Left,

    /// <summary>The side-by-side rendering's new file.</summary>
    Right,
}

/// <summary>
/// A place between two characters of a rendering: the row, and the index in that row's raw text — the
/// text as git wrote it, before tabs are expanded.
/// </summary>
/// <param name="Row">The row's index in its rendering.</param>
/// <param name="Column">The index in the row's text the place is before.</param>
public readonly record struct DiffTextPosition(int Row, int Column) : IComparable<DiffTextPosition>
{
    /// <inheritdoc />
    public int CompareTo(DiffTextPosition other)
        => Row != other.Row ? Row.CompareTo(other.Row) : Column.CompareTo(other.Column);
}

/// <summary>
/// The text the reader has selected in a diff: where it started, where it has been dragged to, and in
/// which pane.
/// </summary>
/// <remarks>
/// <para>
/// One instance for a whole viewer, shared by every row the way the rendering options are: the list is
/// virtualised, so the rows that draw a selection come and go as it scrolls, and a selection kept by
/// the rows themselves would be lost with them. Each row asks this for its own part of it.
/// </para>
/// <para>
/// It is a selection and nothing more. There is no caret to type at and no text to change: the diff is
/// read-only by construction.
/// </para>
/// </remarks>
public sealed class DiffTextSelection
{
    /// <summary>
    /// Raised whenever what is selected changes, so the rows can repaint their part of it.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Gets the pane the selection is in.</summary>
    public DiffPane Pane { get; private set; }

    /// <summary>Gets where the selection started.</summary>
    public DiffTextPosition Anchor { get; private set; }

    /// <summary>Gets where the selection has been extended to.</summary>
    public DiffTextPosition Caret { get; private set; }

    /// <summary>Gets a value indicating whether a selection has been started and not cleared.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets a value indicating whether nothing is selected.</summary>
    public bool IsEmpty => !IsActive || Anchor == Caret;

    /// <summary>Gets the earlier of the two ends.</summary>
    public DiffTextPosition Start => Anchor.CompareTo(Caret) <= 0 ? Anchor : Caret;

    /// <summary>Gets the later of the two ends.</summary>
    public DiffTextPosition End => Anchor.CompareTo(Caret) <= 0 ? Caret : Anchor;

    /// <summary>
    /// Starts a selection, empty, at a place in a pane.
    /// </summary>
    /// <param name="pane">The pane.</param>
    /// <param name="position">Where it starts.</param>
    public void Begin(DiffPane pane, DiffTextPosition position)
    {
        if (IsActive && Pane == pane && Anchor == position && Caret == position)
        {
            return;
        }

        Pane = pane;
        Anchor = position;
        Caret = position;
        IsActive = true;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves the selection's free end, keeping where it started.
    /// </summary>
    /// <param name="position">Where it now ends, in the same pane.</param>
    public void ExtendTo(DiffTextPosition position)
    {
        if (!IsActive || Caret == position)
        {
            return;
        }

        Caret = position;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Selects nothing.
    /// </summary>
    public void Clear()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Anchor = default;
        Caret = default;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Works out the part of one row that is selected.
    /// </summary>
    /// <param name="row">The row's index.</param>
    /// <param name="pane">The pane the row's text is in.</param>
    /// <param name="length">How long the row's raw text is.</param>
    /// <returns>
    /// The selected range of the raw text, end excluded — possibly empty, for a row inside the selection
    /// that has no text — or <see langword="null"/> when the row is not in the selection.
    /// </returns>
    public (int Start, int End)? RangeOn(int row, DiffPane pane, int length)
    {
        if (IsEmpty || pane != Pane)
        {
            return null;
        }

        DiffTextPosition start = Start;
        DiffTextPosition end = End;

        if (row < start.Row || row > end.Row)
        {
            return null;
        }

        int size = Math.Max(0, length);
        int from = row == start.Row ? Math.Clamp(start.Column, 0, size) : 0;
        int to = row == end.Row ? Math.Clamp(end.Column, 0, size) : size;

        return (from, Math.Max(from, to));
    }

    /// <summary>
    /// Collects the selected text, one line per row.
    /// </summary>
    /// <param name="lineAt">
    /// The raw text of a row in the selection's pane, or <see langword="null"/> for a row that has none
    /// there — a hunk band, a filler, git's missing-newline marker — which the copy leaves out.
    /// </param>
    /// <returns>The lines, joined by <c>\n</c>; empty when nothing is selected.</returns>
    public string Text(Func<int, string?> lineAt)
    {
        ArgumentNullException.ThrowIfNull(lineAt);

        if (IsEmpty)
        {
            return string.Empty;
        }

        List<string> lines = [];

        for (int row = Start.Row; row <= End.Row; row++)
        {
            if (lineAt(row) is not { } text)
            {
                continue;
            }

            if (RangeOn(row, Pane, text.Length) is { } range)
            {
                lines.Add(text[range.Start..range.End]);
            }
        }

        return string.Join('\n', lines);
    }
}
