using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace Enigma.GitClient.Desktop.Controls.Diff;

/// <summary>
/// The gutter of a <see cref="DiffTextEditor"/>: the diff's own line numbers and the <c>+</c>/<c>−</c>
/// marker, beside the text and outside its scrolling.
/// </summary>
/// <remarks>
/// <para>
/// The editor's own line-number margin numbers the document, which is the wrong number twice over: a
/// diff has two sides, and its bands and fillers are lines of the document that are lines of neither
/// file. This one reads each line's numbers from the <see cref="DiffDocument"/> instead — the old and
/// the new for the unified pane, one side's own for each pane of the side-by-side rendering.
/// </para>
/// <para>
/// A margin is outside the text view's scrolling by construction, so a long line scrolled sideways
/// moves under the numbers rather than over them.
/// </para>
/// </remarks>
public sealed class DiffGutterMargin : AbstractMargin
{
    /// <summary>
    /// The space between a number and the marker column, which is what keeps the two from reading
    /// as one figure.
    /// </summary>
    public const double NumberPadding = 8;

    private readonly DiffTextEditor _editor;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="editor">The editor whose lines and brushes this gutter draws.</param>
    public DiffGutterMargin(DiffTextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        _editor = editor;
    }

    /// <summary>Gets how many number columns the gutter shows: two for the unified pane, one otherwise.</summary>
    public int NumberColumns => _editor.Pane == DiffPane.Unified ? 2 : 1;

    /// <summary>
    /// Gets the numbers a line shows, one per column, in the pane this gutter belongs to.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="pane">The pane.</param>
    /// <returns>The old and the new number for the unified pane, one side's own otherwise.</returns>
    public static IReadOnlyList<string> NumbersFor(DiffDocumentLine line, DiffPane pane)
    {
        ArgumentNullException.ThrowIfNull(line);

        return pane switch
        {
            DiffPane.Unified => [line.OldNumber, line.NewNumber],
            DiffPane.Right => [line.NewNumber],
            _ => [line.OldNumber],
        };
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => new((NumberColumns * _editor.GutterWidth) + _editor.MarkerWidth, 0);

    /// <inheritdoc />
    protected override void OnTextViewVisualLinesChanged()
    {
        base.OnTextViewVisualLinesChanged();
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        double numbersWidth = NumberColumns * _editor.GutterWidth;
        Size size = Bounds.Size;

        // Aliased edges, as behind the text: the rows' tints meet on fractional pixels.
        using DrawingContext.PushedState aliased = context.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased });

        if (_editor.GutterBrush is { } gutter)
        {
            context.FillRectangle(gutter, new Rect(0, 0, numbersWidth, size.Height));
        }

        TextView? view = TextView;

        if (view is null || !view.VisualLinesValid)
        {
            return;
        }

        IReadOnlyList<DiffDocumentLine> lines = _editor.Diff.Lines;
        Typeface numberFace = new(_editor.FontFamily);
        Typeface markerFace = new(_editor.FontFamily, FontStyle.Normal, FontWeight.SemiBold);

        foreach (VisualLine visual in view.VisualLines)
        {
            int index = visual.FirstDocumentLine.LineNumber - 1;

            if (index < 0 || index >= lines.Count)
            {
                continue;
            }

            DiffDocumentLine line = lines[index];
            double top = visual.VisualTop - view.VerticalOffset;

            // The first row of a wrapped line is where its numbers belong, as in any editor.
            double rowHeight = visual.TextLines.Count > 0 ? visual.TextLines[0].Height : visual.Height;

            if (line.Kind == DiffDocumentLineKind.HunkHeader)
            {
                if (_editor.HunkBandBrush is { } band)
                {
                    context.FillRectangle(band, new Rect(0, top, size.Width, visual.Height));
                }

                continue;
            }

            // The marker column carries the row's own tint, so a changed line reads as one band from
            // the gutter to the right edge; the number columns keep the gutter's colour.
            if (TintOf(line.Kind) is { } tint)
            {
                context.FillRectangle(tint, new Rect(numbersWidth, top, _editor.MarkerWidth, visual.Height));
            }

            IReadOnlyList<string> numbers = NumbersFor(line, _editor.Pane);

            for (int column = 0; column < numbers.Count; column++)
            {
                if (numbers[column].Length == 0)
                {
                    continue;
                }

                FormattedText number = Text(numbers[column], numberFace, _editor.LineNumberFontSize, _editor.GutterForegroundBrush);
                double right = ((column + 1) * _editor.GutterWidth) - NumberPadding;

                context.DrawText(number, new Point(right - number.Width, top + ((rowHeight - number.Height) / 2)));
            }

            if (line.Marker.Length > 0)
            {
                FormattedText marker = Text(line.Marker, markerFace, _editor.FontSize, MarkerBrushOf(line.Kind));

                context.DrawText(
                    marker,
                    new Point(
                        numbersWidth + ((_editor.MarkerWidth - marker.Width) / 2),
                        top + ((rowHeight - marker.Height) / 2)));
            }
        }
    }

    private IBrush? TintOf(DiffDocumentLineKind kind)
        => kind switch
        {
            DiffDocumentLineKind.Added => _editor.AddedLineBrush,
            DiffDocumentLineKind.Removed => _editor.RemovedLineBrush,
            DiffDocumentLineKind.Filler => _editor.FillerBrush,
            _ => null,
        };

    private IBrush? MarkerBrushOf(DiffDocumentLineKind kind)
        => kind switch
        {
            DiffDocumentLineKind.Added => _editor.AddedMarkerBrush,
            DiffDocumentLineKind.Removed => _editor.RemovedMarkerBrush,
            _ => _editor.MarkerBrush,
        };

    private static FormattedText Text(string text, Typeface face, double size, IBrush? brush)
        => new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            face,
            size > 0 ? size : 12,
            brush ?? Brushes.Gray);
}
