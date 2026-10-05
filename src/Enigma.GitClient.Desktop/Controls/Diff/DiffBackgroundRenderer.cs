using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Enigma.GitClient.Core.Diff;

namespace Enigma.GitClient.Desktop.Controls.Diff;

/// <summary>
/// Paints what each line of a <see cref="DiffTextEditor"/> is, behind its text: the added, removed and
/// filler tints, the hunk bands with their headers, and the words the word-level diff marked as
/// changed.
/// </summary>
/// <remarks>
/// <para>
/// The background layer, so the editor's own selection is drawn over the tints and the text over
/// both, and only the lines on screen are ever painted.
/// </para>
/// <para>
/// A changed word is located by the text engine (<see cref="BackgroundGeometryBuilder"/>) from the
/// document offsets the segment names, which is what makes the highlight land on the right glyphs
/// with tabs expanded, the whitespace symbols on and the line wrapped. The segment is counted in
/// the line's raw text, and the document holds that text character for character
/// (<see cref="DiffDocument.Displayable"/>), so no mapping is needed between the two.
/// </para>
/// </remarks>
public sealed class DiffBackgroundRenderer : IBackgroundRenderer
{
    /// <summary>
    /// How far a band's header is set in from the left edge. Fixed rather than scrolled: the band is
    /// what the reader aims at, and a header that slid out of view with the text would leave an
    /// unlabelled stripe.
    /// </summary>
    public const double HeaderInset = 10;

    private readonly DiffTextEditor _editor;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="editor">The editor whose lines and brushes this renderer paints.</param>
    public DiffBackgroundRenderer(DiffTextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        _editor = editor;
    }

    /// <inheritdoc />
    public KnownLayer Layer => KnownLayer.Background;

    /// <inheritdoc />
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(drawingContext);

        if (!textView.VisualLinesValid || textView.Document is not { } document)
        {
            return;
        }

        IReadOnlyList<DiffDocumentLine> lines = _editor.Diff.Lines;
        double width = textView.Bounds.Width;

        // Aliased edges: two rows meet on a fractional pixel, and anti-aliasing both edges would leave
        // a faint seam between every pair of tinted lines.
        using DrawingContext.PushedState aliased = drawingContext.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased });

        BackgroundGeometryBuilder added = Words();
        BackgroundGeometryBuilder removed = Words();
        bool anyAdded = false;
        bool anyRemoved = false;

        foreach (VisualLine visual in textView.VisualLines)
        {
            int index = visual.FirstDocumentLine.LineNumber - 1;

            if (index < 0 || index >= lines.Count)
            {
                continue;
            }

            DiffDocumentLine line = lines[index];
            Rect row = new(0, visual.VisualTop - textView.VerticalOffset, width, visual.Height);

            if (line.Kind == DiffDocumentLineKind.HunkHeader)
            {
                DrawBand(drawingContext, line, row, visual);
                continue;
            }

            if (TintOf(line.Kind) is { } tint)
            {
                drawingContext.FillRectangle(tint, row);
            }

            if (line.Kind is not (DiffDocumentLineKind.Added or DiffDocumentLineKind.Removed) || line.Segments.Count == 0)
            {
                continue;
            }

            DocumentLine documentLine = document.GetLineByNumber(index + 1);
            BackgroundGeometryBuilder builder = line.Kind == DiffDocumentLineKind.Added ? added : removed;

            foreach (DiffSegment segment in line.Segments)
            {
                int start = Math.Clamp(segment.Start, 0, documentLine.Length);
                int end = Math.Clamp(segment.Start + segment.Length, 0, documentLine.Length);

                if (!segment.IsChanged || end <= start)
                {
                    continue;
                }

                builder.AddSegment(textView, new TextSegment { StartOffset = documentLine.Offset + start, Length = end - start });
                builder.CloseFigure();

                anyAdded |= builder == added;
                anyRemoved |= builder == removed;
            }
        }

        Fill(drawingContext, _editor.AddedWordBrush, anyAdded ? added : null);
        Fill(drawingContext, _editor.RemovedWordBrush, anyRemoved ? removed : null);
    }

    private static BackgroundGeometryBuilder Words() => new() { AlignToWholePixels = true };

    private static void Fill(DrawingContext context, IBrush? brush, BackgroundGeometryBuilder? builder)
    {
        if (brush is null || builder?.CreateGeometry() is not { } geometry)
        {
            return;
        }

        context.DrawGeometry(brush, null, geometry);
    }

    private void DrawBand(DrawingContext context, DiffDocumentLine line, Rect row, VisualLine visual)
    {
        if (_editor.HunkBandBrush is { } band)
        {
            context.FillRectangle(band, row);
        }

        if (line.HeaderText.Length == 0)
        {
            return;
        }

        FormattedText header = new(
            line.HeaderText,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(_editor.FontFamily),
            _editor.LineNumberFontSize > 0 ? _editor.LineNumberFontSize : 11,
            _editor.HunkForegroundBrush ?? Brushes.Gray)
        {
            MaxTextWidth = Math.Max(1, row.Width - HeaderInset),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        double rowHeight = visual.TextLines.Count > 0 ? visual.TextLines[0].Height : row.Height;

        context.DrawText(header, new Point(HeaderInset, row.Top + ((rowHeight - header.Height) / 2)));
    }

    private IBrush? TintOf(DiffDocumentLineKind kind)
        => kind switch
        {
            DiffDocumentLineKind.Added => _editor.AddedLineBrush,
            DiffDocumentLineKind.Removed => _editor.RemovedLineBrush,
            DiffDocumentLineKind.Filler => _editor.FillerBrush,
            _ => null,
        };
}
