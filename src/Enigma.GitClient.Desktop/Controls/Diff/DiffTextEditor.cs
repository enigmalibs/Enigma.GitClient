using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
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

    /// <summary>
    /// The class the application's styles select the editor by.
    /// </summary>
    public const string StyleClass = "diff";

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
    public DiffTextEditor()
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

        Rebuild();
    }

    /// <summary>Gets or sets the rendering's rows.</summary>
    public IReadOnlyList<DiffRowViewModel>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
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

    /// <summary>Gets the gutter that carries the line numbers and the marker.</summary>
    public DiffGutterMargin Gutter { get; }

    /// <summary>Gets what draws the tints, the bands and the word-level highlight.</summary>
    public DiffBackgroundRenderer BackgroundRenderer { get; }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextEditor);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RowsProperty || change.Property == PaneProperty)
        {
            Rebuild();
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
