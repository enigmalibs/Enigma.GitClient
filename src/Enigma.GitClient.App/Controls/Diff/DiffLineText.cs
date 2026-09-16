using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Enigma.GitClient.Core.Diff;

namespace Enigma.GitClient.App.Controls.Diff;

/// <summary>
/// Draws one line of a patch: its text in a monospace face, the stretches the word-level diff marked
/// as changed tinted behind the glyphs, tabs expanded to the configured width, and — when asked —
/// the whitespace itself shown as dimmed symbols.
/// </summary>
/// <remarks>
/// <para>
/// A <c>TextBlock</c> with <c>Inlines</c> could carry the word-level tint, but it re-creates an
/// inline collection per row and gives no control over tab expansion or whitespace rendering. One
/// <see cref="FormattedText"/> per row draws the same thing in a single pass and measures its own
/// width, which is what the shared horizontal scrollbar needs.
/// </para>
/// <para>
/// Tabs are expanded here rather than left to the text engine on purpose: a tab's width in a
/// proportional-metrics run depends on what precedes it, so two lines that differ only after a tab
/// would not line up. Expanding to a fixed column width is what makes indented code readable.
/// </para>
/// </remarks>
public sealed class DiffLineText : Control
{
    /// <summary>Defines the <see cref="Text"/> property.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<DiffLineText, string?>(nameof(Text));

    /// <summary>Defines the <see cref="Segments"/> property.</summary>
    public static readonly StyledProperty<IReadOnlyList<DiffSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<DiffLineText, IReadOnlyList<DiffSegment>?>(nameof(Segments));

    /// <summary>Defines the <see cref="HighlightBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> HighlightBrushProperty =
        AvaloniaProperty.Register<DiffLineText, IBrush?>(nameof(HighlightBrush));

    /// <summary>Defines the <see cref="WhitespaceBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> WhitespaceBrushProperty =
        AvaloniaProperty.Register<DiffLineText, IBrush?>(nameof(WhitespaceBrush));

    /// <summary>Defines the <see cref="ShowWhitespace"/> property.</summary>
    public static readonly StyledProperty<bool> ShowWhitespaceProperty =
        AvaloniaProperty.Register<DiffLineText, bool>(nameof(ShowWhitespace));

    /// <summary>Defines the <see cref="WrapLines"/> property.</summary>
    public static readonly StyledProperty<bool> WrapLinesProperty =
        AvaloniaProperty.Register<DiffLineText, bool>(nameof(WrapLines));

    /// <summary>Defines the <see cref="TabWidth"/> property.</summary>
    public static readonly StyledProperty<int> TabWidthProperty =
        AvaloniaProperty.Register<DiffLineText, int>(nameof(TabWidth), 4);

    /// <summary>Defines the <see cref="HorizontalOffset"/> property.</summary>
    public static readonly StyledProperty<double> HorizontalOffsetProperty =
        AvaloniaProperty.Register<DiffLineText, double>(nameof(HorizontalOffset));

    /// <summary>Defines the <see cref="Foreground"/> property.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<DiffLineText>();

    /// <summary>Defines the <see cref="FontFamily"/> property.</summary>
    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<DiffLineText>();

    /// <summary>Defines the <see cref="FontSize"/> property.</summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<DiffLineText>();

    private FormattedText? _formatted;
    private string _expanded = string.Empty;
    private int[] _map = [];

    static DiffLineText()
    {
        AffectsMeasure<DiffLineText>(
            TextProperty,
            SegmentsProperty,
            ShowWhitespaceProperty,
            WrapLinesProperty,
            TabWidthProperty,
            FontFamilyProperty,
            FontSizeProperty);

        AffectsRender<DiffLineText>(
            ForegroundProperty,
            HighlightBrushProperty,
            WhitespaceBrushProperty,
            HorizontalOffsetProperty);
    }

    /// <summary>Gets or sets the line's text, without its diff marker.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets the stretches the word-level diff marked as changed.</summary>
    public IReadOnlyList<DiffSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    /// <summary>Gets or sets the tint drawn behind a changed stretch.</summary>
    public IBrush? HighlightBrush
    {
        get => GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    /// <summary>Gets or sets the colour the whitespace symbols are drawn in.</summary>
    public IBrush? WhitespaceBrush
    {
        get => GetValue(WhitespaceBrushProperty);
        set => SetValue(WhitespaceBrushProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether spaces and tabs are shown as symbols.</summary>
    public bool ShowWhitespace
    {
        get => GetValue(ShowWhitespaceProperty);
        set => SetValue(ShowWhitespaceProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether a long line wraps instead of scrolling.</summary>
    public bool WrapLines
    {
        get => GetValue(WrapLinesProperty);
        set => SetValue(WrapLinesProperty, value);
    }

    /// <summary>Gets or sets how many columns a tab advances to.</summary>
    public int TabWidth
    {
        get => GetValue(TabWidthProperty);
        set => SetValue(TabWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets how far the text is scrolled, in characters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Render-only, and deliberately so: the pane it sits in scrolls, the row does not re-measure,
    /// and the line numbers and the marker beside it stay exactly where they are — which is what
    /// makes a scrolled diff still readable. Its unit is the character rather than the pixel
    /// because the whole control already reasons in columns.
    /// </para>
    /// <para>
    /// A wrapped line has no overflow to scroll to, so the offset is ignored while
    /// <see cref="WrapLines"/> is on rather than left to shift text out of view.
    /// </para>
    /// </remarks>
    public double HorizontalOffset
    {
        get => GetValue(HorizontalOffsetProperty);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>Gets or sets the brush the text is drawn in.</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Gets or sets the font family.</summary>
    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>Gets or sets the font size.</summary>
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Expands tabs to a fixed column grid and, when asked, substitutes visible symbols for the
    /// whitespace.
    /// </summary>
    /// <param name="text">The raw line.</param>
    /// <param name="tabWidth">How many columns a tab advances to.</param>
    /// <param name="showWhitespace">Whether to substitute symbols.</param>
    /// <returns>
    /// The text to draw, and a map from each of its characters back to the index it came from in
    /// <paramref name="text"/>, so the word-level segments still land on the right glyphs.
    /// </returns>
    public static (string Expanded, int[] Map) Expand(string text, int tabWidth, bool showWhitespace)
    {
        ArgumentNullException.ThrowIfNull(text);

        int width = tabWidth < 1 ? 1 : tabWidth;
        StringBuilder builder = new(text.Length);
        List<int> map = new(text.Length);

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];

            if (character == '\t')
            {
                int advance = width - (builder.Length % width);

                // The arrow marks the tab's own column and the rest is padding, so the following
                // text still starts exactly on the tab stop.
                builder.Append(showWhitespace ? '→' : ' ');
                map.Add(index);

                for (int pad = 1; pad < advance; pad++)
                {
                    builder.Append(' ');
                    map.Add(index);
                }

                continue;
            }

            builder.Append(showWhitespace && character == ' ' ? '·' : character);
            map.Add(index);
        }

        return (builder.ToString(), map.ToArray());
    }

    /// <summary>
    /// Counts the columns a line occupies once its tabs are expanded.
    /// </summary>
    /// <param name="text">The raw line.</param>
    /// <param name="tabWidth">How many columns a tab advances to.</param>
    /// <returns>The column count.</returns>
    /// <remarks>
    /// The same arithmetic as <see cref="Expand"/> without the string: the widest line of a patch is
    /// asked for every pane of every file, over thousands of lines, and building each expansion to
    /// measure its length would allocate the whole patch again to learn one number.
    /// </remarks>
    public static int ExpandedLength(string text, int tabWidth)
    {
        ArgumentNullException.ThrowIfNull(text);

        int width = tabWidth < 1 ? 1 : tabWidth;
        int length = 0;

        foreach (char character in text)
        {
            length += character == '\t' ? width - (length % width) : 1;
        }

        return length;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        FormattedText? formatted = Build(availableSize.Width);

        return formatted is null
            ? new Size(0, Math.Max(FontSize * 1.35, 1))
            : new Size(formatted.Width, formatted.Height);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        FormattedText? formatted = _formatted;

        if (formatted is null || _expanded.Length == 0)
        {
            return;
        }

        Point origin = new(-ScrolledPixels(), 0);
        IBrush? highlight = HighlightBrush;
        IReadOnlyList<DiffSegment>? segments = Segments;

        if (highlight is not null && segments is { Count: > 0 })
        {
            foreach (DiffSegment segment in segments)
            {
                if (!segment.IsChanged || segment.Length <= 0)
                {
                    continue;
                }

                (int start, int end) = MapRange(segment);

                if (end <= start)
                {
                    continue;
                }

                // The text engine knows where the glyphs for a range actually landed, wrapping and
                // shaping included; asking it beats measuring substrings ourselves. Built at the
                // scrolled origin so the tint travels with the words it is behind.
                Geometry? area = formatted.BuildHighlightGeometry(origin, start, end - start);

                if (area is not null)
                {
                    context.DrawGeometry(highlight, null, area);
                }
            }
        }

        context.DrawText(formatted, origin);
    }

    /// <summary>
    /// Turns the offset, which is counted in characters, into the pixels the text moves by.
    /// </summary>
    private double ScrolledPixels()
    {
        double offset = HorizontalOffset;

        if (WrapLines || offset <= 0)
        {
            return 0;
        }

        return offset * DiffTypography.MeasureCharacterWidth(FontFamily, EffectiveFontSize);
    }

    private double EffectiveFontSize => FontSize > 0 ? FontSize : 12;

    private (int Start, int End) MapRange(DiffSegment segment)
    {
        int start = -1;
        int end = -1;

        for (int index = 0; index < _map.Length; index++)
        {
            int source = _map[index];

            if (source >= segment.Start && source < segment.End)
            {
                if (start < 0)
                {
                    start = index;
                }

                end = index + 1;
            }
        }

        return start < 0 ? (0, 0) : (start, end);
    }

    private FormattedText? Build(double availableWidth)
    {
        string text = Text ?? string.Empty;

        (_expanded, _map) = Expand(text, TabWidth, ShowWhitespace);

        if (_expanded.Length == 0)
        {
            _formatted = null;
            return null;
        }

        double size = EffectiveFontSize;

        FormattedText formatted = new(
            _expanded,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily),
            size,
            Foreground ?? Brushes.Gray);

        if (WrapLines && !double.IsInfinity(availableWidth) && availableWidth > 0)
        {
            formatted.MaxTextWidth = availableWidth;
        }

        if (ShowWhitespace && WhitespaceBrush is not null)
        {
            DimWhitespace(formatted);
        }

        _formatted = formatted;

        return formatted;
    }

    private void DimWhitespace(FormattedText formatted)
    {
        IBrush brush = WhitespaceBrush!;
        int run = -1;

        for (int index = 0; index <= _expanded.Length; index++)
        {
            bool isSymbol = index < _expanded.Length && _expanded[index] is '·' or '→' or ' ';

            if (isSymbol)
            {
                if (run < 0)
                {
                    run = index;
                }

                continue;
            }

            if (run >= 0)
            {
                formatted.SetForegroundBrush(brush, run, index - run);
                run = -1;
            }
        }
    }
}
