using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Enigma.GitClient.Desktop.ViewModels.Panels;

namespace Enigma.GitClient.Desktop.Controls.Diff;

/// <summary>
/// One pane of a patch in a read-only AvaloniaEdit editor: the text as a document, the line numbers
/// and the marker in a gutter of its own, and the tints, the bands and the word-level highlight
/// behind the text.
/// </summary>
/// <remarks>
/// <para>
/// The editor brings what a list of rows had to rebuild by hand — selecting with the pointer and the
/// keyboard, one document scrolled smoothly instead of thousands of templated rows, search — and the
/// diff adds what an editor does not know about: its line numbers are the diff's two sides rather
/// than the document's own, and its colours say what each line is.
/// </para>
/// <para>
/// The document is rebuilt once when <see cref="Rows"/> or <see cref="Pane"/> changes, never edited:
/// there is nothing to type into and no undo to keep.
/// </para>
/// <para>
/// Two things it does differently from a plain editor. A press on a hunk band widens the context, as
/// the band button of the list did. And the copy gesture runs <see cref="CopyCommand"/> rather than
/// the editor's own copy, which would put a blank line in for every band and filler: the selection is
/// mirrored into a <see cref="DiffTextSelection"/> (<see cref="TextSelection"/>), and the ViewModel
/// copies the code from it the way it always has.
/// </para>
/// <para>
/// Styled as a <see cref="TextEditor"/>, so it wears AvaloniaEdit's own template. The <c>diff</c>
/// class it gives itself is what the application's styles select it by — a type selector would not
/// reach it, because the style key is the base editor's.
/// </para>
/// </remarks>
public sealed class DiffTextEditor : TextEditor
{
    /// <summary>Defines the <see cref="Rows"/> property.</summary>
    public static readonly StyledProperty<IReadOnlyList<DiffRowViewModel>?> RowsProperty =
        AvaloniaProperty.Register<DiffTextEditor, IReadOnlyList<DiffRowViewModel>?>(nameof(Rows));

    /// <summary>Defines the <see cref="Pane"/> property.</summary>
    public static readonly StyledProperty<DiffPane> PaneProperty =
        AvaloniaProperty.Register<DiffTextEditor, DiffPane>(nameof(Pane));

    /// <summary>Defines the <see cref="ShowWhitespace"/> property.</summary>
    public static readonly StyledProperty<bool> ShowWhitespaceProperty =
        AvaloniaProperty.Register<DiffTextEditor, bool>(nameof(ShowWhitespace));

    /// <summary>Defines the <see cref="TabWidth"/> property.</summary>
    public static readonly StyledProperty<int> TabWidthProperty =
        AvaloniaProperty.Register<DiffTextEditor, int>(nameof(TabWidth), 4);

    /// <summary>Defines the <see cref="AddedLineBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> AddedLineBrushProperty =
        AvaloniaProperty.Register<DiffTextEditor, IBrush?>(nameof(AddedLineBrush));

    /// <summary>Defines the <see cref="RemovedLineBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> RemovedLineBrushProperty =
        AvaloniaProperty.Register<DiffTextEditor, IBrush?>(nameof(RemovedLineBrush));

    /// <summary>Defines the <see cref="AddedWordBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> AddedWordBrushProperty =
        AvaloniaProperty.Register<DiffTextEditor, IBrush?>(nameof(AddedWordBrush));

    /// <summary>Defines the <see cref="RemovedWordBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> RemovedWordBrushProperty =
        AvaloniaProperty.Register<DiffTextEditor, IBrush?>(nameof(RemovedWordBrush));

    /// <summary>Defines the <see cref="FillerBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> FillerBrushProperty =
        AvaloniaProperty.Register<DiffTextEditor, IBrush?>(nameof(FillerBrush));

    /// <summary>Defines the <see cref="HunkBandBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> HunkBandBrushProperty =
        AvaloniaProperty.Register<DiffTextEditor, IBrush?>(nameof(HunkBandBrush));

    /// <summary>Defines the <see cref="HunkForegroundBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> HunkForegroundBrushProperty =
        AvaloniaProperty.Register<DiffTextEditor, IBrush?>(nameof(HunkForegroundBrush));

    /// <summary>Defines the <see cref="GutterBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> GutterBrushProperty =
        AvaloniaProperty.Register<DiffTextEditor, IBrush?>(nameof(GutterBrush));

    /// <summary>Defines the <see cref="GutterForegroundBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> GutterForegroundBrushProperty =
        AvaloniaProperty.Register<DiffTextEditor, IBrush?>(nameof(GutterForegroundBrush));

    /// <summary>Defines the <see cref="MarkerBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> MarkerBrushProperty =
        AvaloniaProperty.Register<DiffTextEditor, IBrush?>(nameof(MarkerBrush));

    /// <summary>Defines the <see cref="AddedMarkerBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> AddedMarkerBrushProperty =
        AvaloniaProperty.Register<DiffTextEditor, IBrush?>(nameof(AddedMarkerBrush));

    /// <summary>Defines the <see cref="RemovedMarkerBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> RemovedMarkerBrushProperty =
        AvaloniaProperty.Register<DiffTextEditor, IBrush?>(nameof(RemovedMarkerBrush));

    /// <summary>Defines the <see cref="WhitespaceBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> WhitespaceBrushProperty =
        AvaloniaProperty.Register<DiffTextEditor, IBrush?>(nameof(WhitespaceBrush));

    /// <summary>Defines the <see cref="TextSelectionBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> TextSelectionBrushProperty =
        AvaloniaProperty.Register<DiffTextEditor, IBrush?>(nameof(TextSelectionBrush));

    /// <summary>Defines the <see cref="LineNumberFontSize"/> property.</summary>
    public static readonly StyledProperty<double> LineNumberFontSizeProperty =
        AvaloniaProperty.Register<DiffTextEditor, double>(nameof(LineNumberFontSize), 11);

    /// <summary>Defines the <see cref="GutterWidth"/> property.</summary>
    public static readonly StyledProperty<double> GutterWidthProperty =
        AvaloniaProperty.Register<DiffTextEditor, double>(nameof(GutterWidth), 48);

    /// <summary>Defines the <see cref="MarkerWidth"/> property.</summary>
    public static readonly StyledProperty<double> MarkerWidthProperty =
        AvaloniaProperty.Register<DiffTextEditor, double>(nameof(MarkerWidth), 16);

    /// <summary>Defines the <see cref="TextSelection"/> property.</summary>
    public static readonly StyledProperty<DiffTextSelection?> TextSelectionProperty =
        AvaloniaProperty.Register<DiffTextEditor, DiffTextSelection?>(nameof(TextSelection));

    /// <summary>Defines the <see cref="FilePath"/> property.</summary>
    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<DiffTextEditor, string?>(nameof(FilePath));

    /// <summary>Defines the <see cref="CopyCommand"/> property.</summary>
    public static readonly StyledProperty<ICommand?> CopyCommandProperty =
        AvaloniaProperty.Register<DiffTextEditor, ICommand?>(nameof(CopyCommand));

    /// <summary>
    /// The class the application's styles select the editor by.
    /// </summary>
    public const string StyleClass = "diff";

    /// <summary>
    /// What hovering a band says it does — the band button's own words.
    /// </summary>
    public const string BandTip = "Show more of the file around this change";

    /// <summary>
    /// The key the Fluent theme sizes its scroll bars with — their full, expanded thickness.
    /// </summary>
    public const string ScrollBarSizeKey = "ScrollBarSize";

    /// <summary>
    /// What the Fluent theme gives <see cref="ScrollBarSizeKey"/>, for an editor that cannot reach the
    /// theme's resources.
    /// </summary>
    public const double DefaultScrollBarSize = 12;

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private Cursor? _textCursor;
    private bool _overBand;
    private bool _mirroring;

    private static readonly AvaloniaProperty[] Repaints =
    [
        AddedLineBrushProperty,
        RemovedLineBrushProperty,
        AddedWordBrushProperty,
        RemovedWordBrushProperty,
        FillerBrushProperty,
        HunkBandBrushProperty,
        HunkForegroundBrushProperty,
        GutterBrushProperty,
        GutterForegroundBrushProperty,
        MarkerBrushProperty,
        AddedMarkerBrushProperty,
        RemovedMarkerBrushProperty,
        FontSizeProperty,
        FontFamilyProperty,
    ];

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <remarks>
    /// On a <see cref="DiffTextArea"/>, whose <see cref="DiffTextView"/> never asks to be scrolled
    /// further than it can go — which is what kept a selection dragged to the end of a diff fighting
    /// its scroll viewer for ever (BUG-7823).
    /// </remarks>
    public DiffTextEditor()
        : base(new DiffTextArea())
    {
        Classes.Add(StyleClass);

        IsReadOnly = true;
        ShowLineNumbers = false;

        // A viewer, not an editor: nothing here links, drags, or scrolls past the last line, and the
        // line the caret is on is not news.
        TextEditorOptions options = Options;
        options.EnableHyperlinks = false;
        options.EnableEmailHyperlinks = false;
        options.EnableTextDragDrop = false;
        options.EnableRectangularSelection = false;
        options.HighlightCurrentLine = false;
        options.AllowScrollBelowDocument = false;
        options.CutCopyWholeLine = false;

        // The symbols the list drew: the dot for a space, the arrow for a tab.
        options.ShowSpacesGlyph = "·";
        options.ShowTabsGlyph = "→";
        options.ShowSpaces = false;
        options.ShowTabs = false;
        options.IndentationSize = 4;

        Gutter = new DiffGutterMargin(this);
        BackgroundRenderer = new DiffBackgroundRenderer(this);

        // First, so the numbers sit at the editor's edge whatever else is added beside them later.
        TextArea.LeftMargins.Insert(0, Gutter);
        TextArea.TextView.BackgroundRenderers.Add(BackgroundRenderer);

        TextArea.SelectionChanged += (_, _) => MirrorSelection();

        // The tokens' theme follows the window's, live, as the diff's own brushes do. A control
        // leaving the tree loses its inherited variant before it is told it is detached, so there is
        // briefly no variant at all to follow.
        ActualThemeVariantChanged += (_, _) =>
        {
            if (ActualThemeVariant is { } variant)
            {
                Highlighting?.Apply(variant);
            }
        };

        // Tunnelling, so the band and the copy gesture are claimed before the text area's own
        // handlers start a selection or copy the document's text.
        TextArea.AddHandler(PointerPressedEvent, OnPointerPressedOverText, RoutingStrategies.Tunnel);
        TextArea.AddHandler(PointerMovedEvent, OnPointerMovedOverText, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDownBeforeText, RoutingStrategies.Tunnel);

        Rebuild();
    }

    /// <summary>Gets or sets the rendering's rows.</summary>
    public IReadOnlyList<DiffRowViewModel>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>
    /// Gets or sets the viewer's text selection, which this editor's selection is mirrored into;
    /// <see langword="null"/> to mirror nothing.
    /// </summary>
    /// <remarks>
    /// Shared by every pane of the viewer, and one selection at a time: once another pane holds it,
    /// or it is cleared — a new patch, a new rendering — this editor lets its own selection go.
    /// </remarks>
    public DiffTextSelection? TextSelection
    {
        get => GetValue(TextSelectionProperty);
        set => SetValue(TextSelectionProperty, value);
    }

    /// <summary>
    /// Gets or sets the path of the file the patch is of, whose extension picks the grammar the text
    /// is highlighted with; <see langword="null"/> or an unknown extension for plain text.
    /// </summary>
    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    /// <summary>
    /// Gets the editor's syntax highlighting while it is on screen, <see langword="null"/> otherwise.
    /// </summary>
    /// <remarks>
    /// Installed when the editor is attached and taken off when it is detached: the tokenizer runs on
    /// a background thread for as long as it is installed, and an editor nobody can see has nothing
    /// to colour.
    /// </remarks>
    internal DiffSyntaxHighlighting? Highlighting { get; private set; }

    /// <summary>
    /// Gets or sets the command the copy gesture runs, in place of the editor's own copy;
    /// <see langword="null"/> to leave the gesture to the editor.
    /// </summary>
    public ICommand? CopyCommand
    {
        get => GetValue(CopyCommandProperty);
        set => SetValue(CopyCommandProperty, value);
    }

    /// <summary>Gets or sets which pane of the rendering this editor shows.</summary>
    public DiffPane Pane
    {
        get => GetValue(PaneProperty);
        set => SetValue(PaneProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether spaces and tabs are shown as symbols.</summary>
    public bool ShowWhitespace
    {
        get => GetValue(ShowWhitespaceProperty);
        set => SetValue(ShowWhitespaceProperty, value);
    }

    /// <summary>Gets or sets how many columns a tab advances to.</summary>
    public int TabWidth
    {
        get => GetValue(TabWidthProperty);
        set => SetValue(TabWidthProperty, value);
    }

    /// <summary>Gets or sets the tint behind an added line.</summary>
    public IBrush? AddedLineBrush
    {
        get => GetValue(AddedLineBrushProperty);
        set => SetValue(AddedLineBrushProperty, value);
    }

    /// <summary>Gets or sets the tint behind a removed line.</summary>
    public IBrush? RemovedLineBrush
    {
        get => GetValue(RemovedLineBrushProperty);
        set => SetValue(RemovedLineBrushProperty, value);
    }

    /// <summary>Gets or sets the tint behind a changed word of an added line.</summary>
    public IBrush? AddedWordBrush
    {
        get => GetValue(AddedWordBrushProperty);
        set => SetValue(AddedWordBrushProperty, value);
    }

    /// <summary>Gets or sets the tint behind a changed word of a removed line.</summary>
    public IBrush? RemovedWordBrush
    {
        get => GetValue(RemovedWordBrushProperty);
        set => SetValue(RemovedWordBrushProperty, value);
    }

    /// <summary>Gets or sets the tint of a filler line.</summary>
    public IBrush? FillerBrush
    {
        get => GetValue(FillerBrushProperty);
        set => SetValue(FillerBrushProperty, value);
    }

    /// <summary>Gets or sets the colour of a hunk band.</summary>
    public IBrush? HunkBandBrush
    {
        get => GetValue(HunkBandBrushProperty);
        set => SetValue(HunkBandBrushProperty, value);
    }

    /// <summary>Gets or sets the colour a hunk band's header is written in.</summary>
    public IBrush? HunkForegroundBrush
    {
        get => GetValue(HunkForegroundBrushProperty);
        set => SetValue(HunkForegroundBrushProperty, value);
    }

    /// <summary>Gets or sets the background of the line-number columns.</summary>
    public IBrush? GutterBrush
    {
        get => GetValue(GutterBrushProperty);
        set => SetValue(GutterBrushProperty, value);
    }

    /// <summary>Gets or sets the colour the line numbers are written in.</summary>
    public IBrush? GutterForegroundBrush
    {
        get => GetValue(GutterForegroundBrushProperty);
        set => SetValue(GutterForegroundBrushProperty, value);
    }

    /// <summary>Gets or sets the colour of a marker that is neither an addition nor a removal.</summary>
    public IBrush? MarkerBrush
    {
        get => GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    /// <summary>Gets or sets the colour of an added line's marker.</summary>
    public IBrush? AddedMarkerBrush
    {
        get => GetValue(AddedMarkerBrushProperty);
        set => SetValue(AddedMarkerBrushProperty, value);
    }

    /// <summary>Gets or sets the colour of a removed line's marker.</summary>
    public IBrush? RemovedMarkerBrush
    {
        get => GetValue(RemovedMarkerBrushProperty);
        set => SetValue(RemovedMarkerBrushProperty, value);
    }

    /// <summary>Gets or sets the colour the whitespace symbols are drawn in.</summary>
    public IBrush? WhitespaceBrush
    {
        get => GetValue(WhitespaceBrushProperty);
        set => SetValue(WhitespaceBrushProperty, value);
    }

    /// <summary>Gets or sets the tint behind the selected text.</summary>
    public IBrush? TextSelectionBrush
    {
        get => GetValue(TextSelectionBrushProperty);
        set => SetValue(TextSelectionBrushProperty, value);
    }

    /// <summary>Gets or sets how large the line numbers are.</summary>
    public double LineNumberFontSize
    {
        get => GetValue(LineNumberFontSizeProperty);
        set => SetValue(LineNumberFontSizeProperty, value);
    }

    /// <summary>Gets or sets how wide one line-number column is.</summary>
    public double GutterWidth
    {
        get => GetValue(GutterWidthProperty);
        set => SetValue(GutterWidthProperty, value);
    }

    /// <summary>Gets or sets how wide the marker column is.</summary>
    public double MarkerWidth
    {
        get => GetValue(MarkerWidthProperty);
        set => SetValue(MarkerWidthProperty, value);
    }

    /// <summary>Gets what each line of the document stands for.</summary>
    public DiffDocument Diff { get; private set; } = DiffDocument.Empty;

    /// <summary>
    /// Gets the scroll viewer the editor's template wraps the text in, once the template is applied.
    /// </summary>
    /// <remarks>
    /// The text area is a logical scrollable counted in pixels, so this viewer's offset, extent and
    /// viewport are the editor's own. It is what scrolling goes through here: the editor's own
    /// <c>ScrollToVerticalOffset</c> and <c>ScrollToHorizontalOffset</c> leave the view where it is
    /// in AvaloniaEdit 12, while the viewer's offset moves it.
    /// </remarks>
    public ScrollViewer? ScrollHost { get; private set; }

    /// <summary>Gets the gutter that carries the line numbers and the marker.</summary>
    public DiffGutterMargin Gutter { get; }

    /// <summary>Gets what draws the tints, the bands and the word-level highlight.</summary>
    public DiffBackgroundRenderer BackgroundRenderer { get; }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextEditor);

    /// <summary>
    /// Finds the row a point is on.
    /// </summary>
    /// <param name="point">The point, in the text view's coordinates.</param>
    /// <returns>The row's index, or <see langword="null"/> when the point is below the last line.</returns>
    public int? RowAt(Point point)
    {
        TextView view = TextArea.TextView;

        if (Rows is not { Count: > 0 } rows || !view.VisualLinesValid)
        {
            return null;
        }

        VisualLine? line = view.GetVisualLineFromVisualTop(point.Y + view.VerticalOffset);
        int row = (line?.FirstDocumentLine.LineNumber ?? 0) - 1;

        return row >= 0 && row < rows.Count ? row : null;
    }

    /// <summary>
    /// Scrolls so that a row is the first line on screen, as far as the document lets it.
    /// </summary>
    /// <param name="row">The row.</param>
    public void ScrollToRow(int row)
    {
        ApplyTemplate();

        if (ScrollHost is not { } scroll)
        {
            return;
        }

        int line = Math.Clamp(row + 1, 1, Math.Max(1, Document.LineCount));

        scroll.Offset = new Vector(scroll.Offset.X, TextArea.TextView.GetVisualTopByDocumentLine(line));
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        ScrollHost = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");

        // The theme's scroll bars lie over the content and grow under the pointer, and the horizontal
        // one grew over the last line, even scrolled to the end (BUG-09AD). Padding the viewer, not
        // the text, ends the scrolled text that much higher: the bar covers the padding instead.
        if (ScrollHost is { } scroll)
        {
            scroll.Padding = new Thickness(0, 0, 0, ScrollBarAllowance());
        }
    }

    /// <summary>
    /// How much room is left under the last line: as much as the horizontal scroll bar takes once it
    /// has grown under the pointer.
    /// </summary>
    /// <returns>The theme's scroll bar size.</returns>
    private double ScrollBarAllowance()
        => this.TryFindResource(ScrollBarSizeKey, ActualThemeVariant, out object? size) && size is double thickness
            ? thickness
            : DefaultScrollBarSize;

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        Highlighting ??= new DiffSyntaxHighlighting(this, ActualThemeVariant ?? ThemeVariant.Dark);
        Highlighting.SetFile(FilePath);

        // Only while it is on screen: the selection belongs to the viewer and outlives the editor.
        if (TextSelection is { } selection)
        {
            selection.Changed += OnTextSelectionChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TextSelection is { } selection)
        {
            selection.Changed -= OnTextSelectionChanged;
        }

        Highlighting?.Dispose();
        Highlighting = null;

        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RowsProperty || change.Property == PaneProperty)
        {
            Rebuild();
        }
        else if (change.Property == FilePathProperty)
        {
            Highlighting?.SetFile(FilePath);
        }

        else if (change.Property == TextSelectionProperty)
        {
            if (change.OldValue is DiffTextSelection old)
            {
                old.Changed -= OnTextSelectionChanged;
            }

            if (change.NewValue is DiffTextSelection now && VisualRoot is not null)
            {
                now.Changed += OnTextSelectionChanged;
            }
        }
        else if (change.Property == ShowWhitespaceProperty)
        {
            Options.ShowSpaces = ShowWhitespace;
            Options.ShowTabs = ShowWhitespace;
        }
        else if (change.Property == TabWidthProperty)
        {
            Options.IndentationSize = Math.Max(1, TabWidth);
        }
        else if (change.Property == WhitespaceBrushProperty)
        {
            TextArea.TextView.NonPrintableCharacterBrush = WhitespaceBrush ?? Brushes.Gray;
        }
        else if (change.Property == TextSelectionBrushProperty)
        {
            TextArea.SelectionBrush = TextSelectionBrush;
        }
        else if (change.Property == LineNumberFontSizeProperty
            || change.Property == GutterWidthProperty
            || change.Property == MarkerWidthProperty)
        {
            Gutter.InvalidateMeasure();
            Gutter.InvalidateVisual();
        }
        else if (Array.IndexOf(Repaints, change.Property) >= 0)
        {
            Repaint();
        }
    }

    /// <summary>
    /// Tells the viewer what is selected here, in rows and raw-text columns.
    /// </summary>
    /// <remarks>
    /// A line of the document is a row and a character of it a character of the row's text — the
    /// document holds the text one for one — so the editor's location is the selection's position less
    /// one on each axis. An empty selection clears the shared one only when it is this pane's: a click
    /// in one pane says nothing about what the other one holds.
    /// </remarks>
    private void MirrorSelection()
    {
        if (_mirroring || TextSelection is not { } target)
        {
            return;
        }

        _mirroring = true;

        try
        {
            AvaloniaEdit.Editing.Selection selection = TextArea.Selection;

            if (selection.IsEmpty)
            {
                if (target.IsActive && target.Pane == Pane)
                {
                    target.Clear();
                }

                return;
            }

            ISegment range = selection.SurroundingSegment;
            TextLocation start = Document.GetLocation(range.Offset);
            TextLocation end = Document.GetLocation(range.EndOffset);

            target.Begin(Pane, new DiffTextPosition(start.Line - 1, start.Column - 1));
            target.ExtendTo(new DiffTextPosition(end.Line - 1, end.Column - 1));
        }
        finally
        {
            _mirroring = false;
        }
    }

    private void OnTextSelectionChanged(object? sender, EventArgs e)
    {
        if (_mirroring || sender is not DiffTextSelection selection || TextArea.Selection.IsEmpty)
        {
            return;
        }

        if (!selection.IsActive || selection.Pane != Pane)
        {
            _mirroring = true;

            try
            {
                TextArea.ClearSelection();
            }
            finally
            {
                _mirroring = false;
            }
        }
    }

    private void OnPointerPressedOverText(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 1
            || !e.GetCurrentPoint(TextArea).Properties.IsLeftButtonPressed
            || BandCommandAt(e.GetPosition(TextArea.TextView)) is not { } command)
        {
            return;
        }

        command.Execute(null);
        e.Handled = true;
    }

    private void OnPointerMovedOverText(object? sender, PointerEventArgs e)
    {
        bool overBand = BandCommandAt(e.GetPosition(TextArea.TextView)) is not null;

        if (overBand == _overBand)
        {
            return;
        }

        _overBand = overBand;

        TextView view = TextArea.TextView;

        if (overBand)
        {
            _textCursor = view.Cursor;
            view.Cursor = HandCursor;
            ToolTip.SetTip(TextArea, BandTip);
        }
        else
        {
            view.Cursor = _textCursor;
            ToolTip.SetTip(TextArea, null);
        }
    }

    /// <summary>
    /// The command a press at a point would run: the band's, when the point is on a band whose context
    /// can still be widened.
    /// </summary>
    private ICommand? BandCommandAt(Point point)
        => RowAt(point) is { } row
            && Rows![row] is { IsHunkHeader: true, ExpandContextCommand: { } command }
            && command.CanExecute(null)
                ? command
                : null;

    private void OnKeyDownBeforeText(object? sender, KeyEventArgs e)
    {
        if (CopyCommand is not { } copy || !IsCopyGesture(e))
        {
            return;
        }

        if (copy.CanExecute(null))
        {
            copy.Execute(null);
        }

        // Claimed either way: with nothing to copy, the editor's own copy would copy nothing too.
        e.Handled = true;
    }

    private bool IsCopyGesture(KeyEventArgs e)
    {
        if (Application.Current?.PlatformSettings?.HotkeyConfiguration.Copy is { } gestures)
        {
            foreach (KeyGesture gesture in gestures)
            {
                if (gesture.Matches(e))
                {
                    return true;
                }
            }

            return false;
        }

        return e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control;
    }

    private void Rebuild()
    {
        Diff = DiffDocument.Build(Rows, Pane);

        TextDocument document = new(Diff.Text);
        document.UndoStack.SizeLimit = 0;

        Document = document;

        Gutter.InvalidateMeasure();
        Repaint();
    }

    private void Repaint()
    {
        Gutter.InvalidateVisual();
        TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }
}
