using System;
using System.Collections.Generic;
using System.Text;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Desktop.ViewModels.Panels;

namespace Enigma.GitClient.Desktop.Controls.Diff;

/// <summary>
/// What one line of a diff document stands for.
/// </summary>
public enum DiffDocumentLineKind
{
    /// <summary>A hunk band: an empty line the band and its header are drawn over.</summary>
    HunkHeader,

    /// <summary>An unchanged line.</summary>
    Context,

    /// <summary>A line the new side added.</summary>
    Added,

    /// <summary>A line the new side removed.</summary>
    Removed,

    /// <summary>git's missing-newline marker.</summary>
    NoNewline,

    /// <summary>An empty line that keeps this side level with a longer other side.</summary>
    Filler,
}

/// <summary>
/// Everything the gutter and the background draw for one line of a diff document. The line's text is
/// the document's own.
/// </summary>
/// <param name="Kind">What the line stands for.</param>
/// <param name="OldNumber">The old-side line number, empty when the line shows none.</param>
/// <param name="NewNumber">The new-side line number, empty when the line shows none.</param>
/// <param name="Marker">The marker column's character, empty for context.</param>
/// <param name="Segments">The stretches the word-level diff marked as changed.</param>
/// <param name="HeaderText">A hunk band's header, empty for every other line.</param>
public sealed record DiffDocumentLine(
    DiffDocumentLineKind Kind,
    string OldNumber,
    string NewNumber,
    string Marker,
    IReadOnlyList<DiffSegment> Segments,
    string HeaderText);

/// <summary>
/// One pane of a rendering as the editor holds it: the text, one document line per row, and what
/// each line stands for.
/// </summary>
/// <remarks>
/// <para>
/// One line per row and never more, which is what lets every row index of the ViewModel — the map's
/// runs, the first change, the selection — be a line number of the document as well.
/// </para>
/// <para>
/// A band and a filler are empty lines. The band's header is drawn over its line rather than written
/// into it, so a grammar never tokenizes <c>@@</c> as code and a copy never has it to strip.
/// </para>
/// </remarks>
public sealed class DiffDocument
{
    /// <summary>
    /// The character a lone carriage return is shown as. One for one, so the offsets the word diff and
    /// the selection count in still land on the characters they name.
    /// </summary>
    public const char CarriageReturnSymbol = '␍';

    private DiffDocument(string text, IReadOnlyList<DiffDocumentLine> lines)
    {
        Text = text;
        Lines = lines;
    }

    /// <summary>Gets a document with nothing in it.</summary>
    public static DiffDocument Empty { get; } = new(string.Empty, []);

    /// <summary>Gets the document's text, one line per row.</summary>
    public string Text { get; }

    /// <summary>Gets what each line stands for, in line order.</summary>
    public IReadOnlyList<DiffDocumentLine> Lines { get; }

    /// <summary>
    /// Builds one pane of a rendering.
    /// </summary>
    /// <param name="rows">The rendering's rows, or <see langword="null"/> for none.</param>
    /// <param name="pane">The pane: the unified one, or a side of the side-by-side rendering.</param>
    /// <returns>The document.</returns>
    public static DiffDocument Build(IReadOnlyList<DiffRowViewModel>? rows, DiffPane pane)
    {
        if (rows is null || rows.Count == 0)
        {
            return Empty;
        }

        StringBuilder text = new();
        List<DiffDocumentLine> lines = new(rows.Count);

        for (int index = 0; index < rows.Count; index++)
        {
            if (index > 0)
            {
                text.Append('\n');
            }

            DiffRowViewModel row = rows[index];

            if (row.IsHunkHeader)
            {
                lines.Add(new DiffDocumentLine(DiffDocumentLineKind.HunkHeader, string.Empty, string.Empty, string.Empty, [], row.HeaderText));
                continue;
            }

            // A row the pane has no cell for is a filler on that side, whatever the reason.
            if (row.CellFor(pane) is not { IsFiller: false } cell)
            {
                lines.Add(new DiffDocumentLine(DiffDocumentLineKind.Filler, string.Empty, string.Empty, string.Empty, [], string.Empty));
                continue;
            }

            text.Append(Displayable(cell.Text));
            lines.Add(new DiffDocumentLine(KindOf(cell), cell.OldNumber, cell.NewNumber, cell.Marker, cell.Segments, string.Empty));
        }

        return new DiffDocument(text.ToString(), lines);
    }

    /// <summary>
    /// Makes a line's text safe to hold as one document line.
    /// </summary>
    /// <param name="text">The line's text.</param>
    /// <returns>The text, with any carriage return shown as <see cref="CarriageReturnSymbol"/>.</returns>
    /// <remarks>
    /// The parser has already taken the CRLF line endings off, but a carriage return on its own can
    /// still be inside a line, and the editor would break the line in two at it — moving every row
    /// below it down by one.
    /// </remarks>
    public static string Displayable(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Contains('\r', StringComparison.Ordinal)
            ? text.Replace('\r', CarriageReturnSymbol)
            : text;
    }

    private static DiffDocumentLineKind KindOf(DiffCellViewModel cell)
        => cell.IsAdded ? DiffDocumentLineKind.Added
            : cell.IsRemoved ? DiffDocumentLineKind.Removed
            : cell.IsNoNewline ? DiffDocumentLineKind.NoNewline
            : DiffDocumentLineKind.Context;
}
