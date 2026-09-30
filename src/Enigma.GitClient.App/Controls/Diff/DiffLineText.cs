using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
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
/// <para>
/// It also draws its share of the reader's text selection (<see cref="Selection"/>), and says which
/// character is under a point (<see cref="IndexAt"/>), which is what selecting with the pointer needs.
/// It never edits anything: there is no caret and nothing to type into.
/// </para>
/// <para>
/// The control clips itself. It is the one control here that draws outside its own bounds by
/// construction — past its right edge, because it measures to the line's natural width, and past
/// its left one, because <see cref="HorizontalOffset"/> is applied to the render origin rather than
/// to the layout. Leaving that ink to an ancestor is what let a scrolled line paint over the line
/// numbers beside it: the clip on the pane holds the gutter as well as the text, so "inside the
/// pane" was never "inside the text column".
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

    /// <summary>Defines the <see cref="Selection"/> property.</summary>
    public static readonly StyledProperty<DiffTextSelection?> SelectionProperty =
        AvaloniaProperty.Register<DiffLineText, DiffTextSelection?>(nameof(Selection));

    /// <summary>Defines the <see cref="Row"/> property.</summary>
    public static readonly StyledProperty<int> RowProperty =
        AvaloniaProperty.Register<DiffLineText, int>(nameof(Row), -1);

    /// <summary>Defines the <see cref="Pane"/> property.</summary>
    public static readonly StyledProperty<DiffPane> PaneProperty =
        AvaloniaProperty.Register<DiffLineText, DiffPane>(nameof(Pane));

    /// <summary>Defines the <see cref="SelectionBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        AvaloniaProperty.Register<DiffLineText, IBrush?>(nameof(SelectionBrush));

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
    private TextLayout? _hitTesting;
    private double _wrapWidth = double.PositiveInfinity;
    private string _expanded = string.Empty;
    private int[] _map = [];

    static DiffLineText()
    {
        // A default rather than an attribute on each template: the containment belongs to the
        // control that renders outside its bounds, not to the three rows and the conflict pane that
        // happen to hold one.
        ClipToBoundsProperty.OverrideDefaultValue<DiffLineText>(true);

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
            HorizontalOffsetProperty,
            SelectionProperty,
            RowProperty,
            PaneProperty,
            SelectionBrushProperty);
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

    /// <summary>
    /// Gets or sets the viewer's text selection, which this line draws its part of; <see langword="null"/>
    /// for a line that is never selected.
    /// </summary>
    public DiffTextSelection? Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    /// <summary>Gets or sets the index of the row this line is on, which the selection is counted in.</summary>
    public int Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    /// <summary>Gets or sets the pane this line is in.</summary>
    public DiffPane Pane
    {
        get => GetValue(PaneProperty);
        set => SetValue(PaneProperty, value);
    }

    /// <summary>Gets or sets the tint drawn behind the selected characters.</summary>
    public IBrush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
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

    /// <summary>
    /// Finds the place in the line's raw text nearest to a point.
    /// </summary>
    /// <param name="point">The point, in this control's coordinates.</param>
    /// <returns>
    /// The index the place is before: 0 left of the text, the text's length right of it. A point inside a
    /// tab lands before or after the tab, whichever half it is in.
    /// </returns>
    /// <remarks>
    /// The text engine does the hit test, on the text as it is drawn — scrolled, tabs expanded, wrapped —
    /// and the expansion's map turns its answer back into the raw text the selection counts in.
    /// </remarks>
    public int IndexAt(Point point)
    {
        string text = Text ?? string.Empty;

        if (_formatted is null || _expanded.Length == 0 || text.Length == 0)
        {
            return 0;
        }

        // FormattedText draws but does not hit-test; a layout of the same drawn text, in the same face
        // and at the same width, does. Built on the first question after each measure, not on every
        // row that is merely drawn.
        _hitTesting ??= new TextLayout(
            _expanded,
            new Typeface(FontFamily),
            EffectiveFontSize,
            Foreground ?? Brushes.Gray,
            textWrapping: double.IsInfinity(_wrapWidth) ? TextWrapping.NoWrap : TextWrapping.Wrap,
            maxWidth: _wrapWidth);

        TextHitTestResult hit = _hitTesting.HitTestPoint(new Point(point.X + ScrolledPixels(), point.Y));
        int drawn = Math.Clamp(hit.TextPosition, 0, _expanded.Length);

        if (drawn >= _map.Length)
        {
            return text.Length;
        }

        // A drawn character is one raw character, except a tab, which is drawn as several: the place
        // goes before the tab in its first half and after it in its second.
        int source = _map[drawn];
        int first = drawn;
        int last = drawn;

        while (first > 0 && _map[first - 1] == source)
        {
            first--;
        }

        while (last < _map.Length - 1 && _map[last + 1] == source)
        {
            last++;
        }

        int width = last - first + 1;

        return width > 1 && (drawn - first) * 2 >= width ? source + 1 : source;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Only while it is on screen: the selection belongs to the viewer and outlives every row, and a
        // row it still held a handler of could never be collected.
        if (Selection is { } selection)
        {
            selection.Changed += OnSelectionChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (Selection is { } selection)
        {
            selection.Changed -= OnSelectionChanged;
        }

        ForgetHitTesting();

        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectionProperty && VisualRoot is not null)
        {
            if (change.OldValue is DiffTextSelection old)
            {
                old.Changed -= OnSelectionChanged;
            }

            if (change.NewValue is DiffTextSelection now)
            {
                now.Changed += OnSelectionChanged;
            }
        }
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

        DrawSelection(context, formatted, origin);

        context.DrawText(formatted, origin);
    }

    private void OnSelectionChanged(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>
    /// Tints this line's part of the selection, behind the glyphs and over the word-level tint.
    /// </summary>
    private void DrawSelection(DrawingContext context, FormattedText formatted, Point origin)
    {
        if (SelectionBrush is not { } brush
            || Selection?.RangeOn(Row, Pane, (Text ?? string.Empty).Length) is not { } range
            || range.End <= range.Start)
        {
            return;
        }

        (int start, int end) = Drawn(range.Start, range.End);

        if (end <= start)
        {
            return;
        }

        Geometry? area = formatted.BuildHighlightGeometry(origin, start, end - start);

        if (area is not null)
        {
            context.DrawGeometry(brush, null, area);
        }
    }

    /// <summary>
    /// Turns a range of the raw text into the range of drawn characters that shows it: a selected tab
    /// covers its whole width.
    /// </summary>
    private (int Start, int End) Drawn(int start, int end)
    {
        int from = _map.Length;
        int to = _map.Length;

        for (int index = 0; index < _map.Length; index++)
        {
            if (from == _map.Length && _map[index] >= start)
            {
                from = index;
            }

            if (_map[index] >= end)
            {
                to = index;
                break;
            }
        }

        return (from, to);
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

    private void ForgetHitTesting()
    {
        _hitTesting?.Dispose();
        _hitTesting = null;
    }

    private FormattedText? Build(double availableWidth)
    {
        string text = Text ?? string.Empty;

        ForgetHitTesting();
        _wrapWidth = WrapLines && !double.IsInfinity(availableWidth) && availableWidth > 0
            ? availableWidth
            : double.PositiveInfinity;

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

        if (!double.IsInfinity(_wrapWidth))
        {
            formatted.MaxTextWidth = _wrapWidth;
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
