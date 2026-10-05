using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit.Rendering;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Desktop.Controls.Diff;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The diff editor on its own: the document it holds for a pane, the gutter's numbers, and the
/// colours it paints behind the text.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiffTextEditorTests
{
    private const string SamplePatch =
        "diff --git a/src/app.txt b/src/app.txt\n" +
        "index 1111111..2222222 100644\n" +
        "--- a/src/app.txt\n" +
        "+++ b/src/app.txt\n" +
        "@@ -1,4 +1,5 @@\n" +
        " one\n" +
        "-two\n" +
        "-three\n" +
        "+two changed\n" +
        "+three changed\n" +
        "+four\n" +
        " five\n";

    private readonly HeadlessAvaloniaFixture _fixture;

    public DiffTextEditorTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static FilePatch Parse(string patch) => UnifiedDiffParser.Parse(patch).Files[0];

    /// <summary>The unified rendering's rows, built the way the viewer builds them.</summary>
    private static List<DiffRowViewModel> Unified(FilePatch patch)
    {
        DiffRenderOptions options = new();
        List<DiffRowViewModel> rows = [];

        foreach (DiffRow row in DiffRowBuilder.BuildUnified(patch))
        {
            rows.Add(row.Kind == DiffRowKind.HunkHeader
                ? new DiffRowViewModel(row.Hunk!, options) { Index = rows.Count }
                : new DiffRowViewModel(new DiffCellViewModel(row.Line, options), null, null, options) { Index = rows.Count });
        }

        return rows;
    }

    /// <summary>The side-by-side rendering's rows, built the way the viewer builds them.</summary>
    private static List<DiffRowViewModel> SideBySide(FilePatch patch)
    {
        DiffRenderOptions options = new();
        List<DiffRowViewModel> rows = [];

        foreach (DiffPairRow row in DiffRowBuilder.BuildSideBySide(patch))
        {
            rows.Add(row.Kind == DiffRowKind.HunkHeader
                ? new DiffRowViewModel(row.Hunk!, options) { Index = rows.Count }
                : new DiffRowViewModel(
                    null,
                    new DiffCellViewModel(row.Left, options, showOldNumber: true, showNewNumber: false),
                    new DiffCellViewModel(row.Right, options, showOldNumber: false, showNewNumber: true),
                    options)
                {
                    Index = rows.Count,
                });
        }

        return rows;
    }

    // ---------------------------------------------------------------- the document

    [Fact]
    public void Document_HoldsOneLinePerRow_WithTheBandEmpty()
    {
        List<DiffRowViewModel> rows = Unified(Parse(SamplePatch));

        DiffDocument document = DiffDocument.Build(rows, DiffPane.Unified);

        Assert.Equal(rows.Count, document.Lines.Count);
        Assert.Equal(
            ["", "one", "two", "three", "two changed", "three changed", "four", "five"],
            document.Text.Split('\n'));

        Assert.Equal(DiffDocumentLineKind.HunkHeader, document.Lines[0].Kind);
        Assert.Equal("@@ -1,4 +1,5 @@", document.Lines[0].HeaderText);
        Assert.Equal(DiffDocumentLineKind.Context, document.Lines[1].Kind);
        Assert.Equal(DiffDocumentLineKind.Removed, document.Lines[2].Kind);
        Assert.Equal(DiffDocumentLineKind.Added, document.Lines[4].Kind);
        Assert.Equal("−", document.Lines[2].Marker);
        Assert.Equal("+", document.Lines[4].Marker);
    }

    [Fact]
    public void Document_KeepsEachSideLevelWithFillers()
    {
        List<DiffRowViewModel> rows = SideBySide(Parse(SamplePatch));

        DiffDocument left = DiffDocument.Build(rows, DiffPane.Left);
        DiffDocument right = DiffDocument.Build(rows, DiffPane.Right);

        // Both sides have a line for every row, so a row is the same line number on both.
        Assert.Equal(rows.Count, left.Lines.Count);
        Assert.Equal(rows.Count, right.Lines.Count);
        Assert.Equal(rows.Count, left.Text.Split('\n').Length);
        Assert.Equal(rows.Count, right.Text.Split('\n').Length);

        // The new side has one line more than the old one, so the old side carries one filler.
        int filler = left.Lines.ToList().FindIndex(line => line.Kind == DiffDocumentLineKind.Filler);

        Assert.True(filler > 0, "the old side has no filler for the line only the new side has");
        Assert.Equal(string.Empty, left.Text.Split('\n')[filler]);
        Assert.Equal("four", right.Text.Split('\n')[filler]);
        Assert.Equal(DiffDocumentLineKind.Added, right.Lines[filler].Kind);
    }

    [Fact]
    public void Document_ShowsALoneCarriageReturnAsOneSymbol()
    {
        Assert.Equal("a␍b", DiffDocument.Displayable("a\rb"));
        Assert.Equal("plain", DiffDocument.Displayable("plain"));

        // One for one: the offsets the word diff counts in still name the same characters.
        Assert.Equal("a\rb".Length, DiffDocument.Displayable("a\rb").Length);
    }

    [Fact]
    public void Document_IsEmptyWithoutRows()
    {
        Assert.Same(DiffDocument.Empty, DiffDocument.Build(null, DiffPane.Unified));
        Assert.Same(DiffDocument.Empty, DiffDocument.Build([], DiffPane.Left));
    }

    // ---------------------------------------------------------------- the gutter

    [Fact]
    public void Gutter_NumbersEachPaneWithItsOwnSide()
    {
        List<DiffRowViewModel> unified = Unified(Parse(SamplePatch));
        List<DiffRowViewModel> side = SideBySide(Parse(SamplePatch));

        DiffDocumentLine context = DiffDocument.Build(unified, DiffPane.Unified).Lines[1];
        DiffDocumentLine removed = DiffDocument.Build(unified, DiffPane.Unified).Lines[2];
        DiffDocumentLine added = DiffDocument.Build(unified, DiffPane.Unified).Lines[4];

        Assert.Equal(["1", "1"], DiffGutterMargin.NumbersFor(context, DiffPane.Unified));
        Assert.Equal(["2", ""], DiffGutterMargin.NumbersFor(removed, DiffPane.Unified));
        Assert.Equal(["", "2"], DiffGutterMargin.NumbersFor(added, DiffPane.Unified));

        Assert.Equal(["1"], DiffGutterMargin.NumbersFor(DiffDocument.Build(side, DiffPane.Left).Lines[1], DiffPane.Left));
        Assert.Equal(["1"], DiffGutterMargin.NumbersFor(DiffDocument.Build(side, DiffPane.Right).Lines[1], DiffPane.Right));
    }

    [Fact]
    public void Gutter_IsTwoColumnsUnified_AndOneOnEachSide()
    {
        _fixture.Run(() =>
        {
            DiffTextEditor editor = new() { GutterWidth = 30, MarkerWidth = 12 };

            Assert.Same(editor.Gutter, editor.TextArea.LeftMargins[0]);
            Assert.Equal(2, editor.Gutter.NumberColumns);

            editor.Gutter.Measure(Size.Infinity);
            Assert.Equal(72, editor.Gutter.DesiredSize.Width);

            editor.Pane = DiffPane.Left;
            editor.Gutter.Measure(Size.Infinity);

            Assert.Equal(1, editor.Gutter.NumberColumns);
            Assert.Equal(42, editor.Gutter.DesiredSize.Width);
        });
    }

    // ---------------------------------------------------------------- the editor

    [Fact]
    public void Editor_RebuildsItsDocumentForItsRowsAndPane()
    {
        _fixture.Run(() =>
        {
            List<DiffRowViewModel> rows = SideBySide(Parse(SamplePatch));
            DiffTextEditor editor = new() { Rows = rows, Pane = DiffPane.Right };

            Assert.Equal(rows.Count, editor.Document.LineCount);
            Assert.Equal(DiffDocument.Build(rows, DiffPane.Right).Text, editor.Document.Text);

            editor.Pane = DiffPane.Left;

            Assert.Equal(DiffDocument.Build(rows, DiffPane.Left).Text, editor.Document.Text);

            editor.Rows = null;

            Assert.Equal(string.Empty, editor.Document.Text);
            Assert.Empty(editor.Diff.Lines);
        });
    }

    [Fact]
    public void Editor_IsReadOnly()
    {
        _fixture.Run(() =>
        {
            DiffTextEditor editor = new() { Rows = Unified(Parse(SamplePatch)), Pane = DiffPane.Unified };
            string before = editor.Document.Text;

            editor.TextArea.PerformTextInput("typed");

            Assert.True(editor.IsReadOnly);
            Assert.Equal(before, editor.Document.Text);
            Assert.False(editor.TextArea.Options.EnableHyperlinks);
            Assert.False(editor.TextArea.Options.HighlightCurrentLine);
        });
    }

    [Fact]
    public void Editor_ShowsWhitespaceAndExpandsTabsThroughItsOptions()
    {
        _fixture.Run(() =>
        {
            DiffTextEditor editor = new();

            Assert.False(editor.Options.ShowSpaces);
            Assert.False(editor.Options.ShowTabs);

            editor.ShowWhitespace = true;
            editor.TabWidth = 8;

            Assert.True(editor.Options.ShowSpaces);
            Assert.True(editor.Options.ShowTabs);
            Assert.Equal("·", editor.Options.ShowSpacesGlyph);
            Assert.Equal("→", editor.Options.ShowTabsGlyph);
            Assert.Equal(8, editor.Options.IndentationSize);

            // A width of nothing is still one column, as the list's expansion treated it.
            editor.TabWidth = 0;

            Assert.Equal(1, editor.Options.IndentationSize);
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Editor_TakesTheDiffColoursAndMetricsFromTheTheme(string variantName)
    {
        _fixture.Run(() =>
        {
            ThemeVariant variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Application application = Application.Current!;
            ThemeVariant? original = application.RequestedThemeVariant;
            application.RequestedThemeVariant = variant;

            try
            {
                DiffTextEditor editor = new();
                Window window = new() { Content = editor, Width = 400, Height = 200 };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(Colour("DiffAddedLineColor", variant), SolidColour(editor.AddedLineBrush));
                Assert.Equal(Colour("DiffRemovedLineColor", variant), SolidColour(editor.RemovedLineBrush));
                Assert.Equal(Colour("DiffAddedWordColor", variant), SolidColour(editor.AddedWordBrush));
                Assert.Equal(Colour("DiffRemovedWordColor", variant), SolidColour(editor.RemovedWordBrush));
                Assert.Equal(Colour("DiffHunkBandColor", variant), SolidColour(editor.HunkBandBrush));
                Assert.Equal(Colour("DiffGutterColor", variant), SolidColour(editor.GutterBrush));
                Assert.Equal(Colour("DiffFillerColor", variant), SolidColour(editor.FillerBrush));
                Assert.Equal(Colour("DiffTextSelectionColor", variant), SolidColour(editor.TextArea.SelectionBrush));
                Assert.Equal(Colour("DiffWhitespaceColor", variant), SolidColour(editor.TextArea.TextView.NonPrintableCharacterBrush));

                Assert.True(application.TryGetResource(DiffTypography.GutterWidthKey, variant, out object? gutter));
                Assert.Equal((double)gutter!, editor.GutterWidth);

                Assert.True(application.TryGetResource(DiffTypography.FontFamilyKey, variant, out object? family));
                Assert.Equal(((FontFamily)family!).Name, editor.FontFamily.Name);

                window.Close();
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void Editor_PaintsEveryKindOfLine()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;
            ThemeVariant? original = application.RequestedThemeVariant;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                DiffTextEditor editor = new() { Rows = Unified(Parse(SamplePatch)), Pane = DiffPane.Unified };
                Window window = new() { Content = editor, Width = 600, Height = 300 };
                window.Show();

                using Bitmap frame = Frame(window);
                TextView view = editor.TextArea.TextView;
                double right = view.Bounds.Width - 4;

                // Far to the right of every line, where nothing but the line's own tint is drawn.
                AssertColour(Colour("DiffHunkBandColor", ThemeVariant.Dark), frame, PointIn(view, right, LineTop(view, 1) + 2));
                AssertColour(Colour("DiffRemovedLineColor", ThemeVariant.Dark), frame, PointIn(view, right, LineTop(view, 3) + 2));
                AssertColour(Colour("DiffAddedLineColor", ThemeVariant.Dark), frame, PointIn(view, right, LineTop(view, 5) + 2));

                // "two changed" against "two": the changed word carries the word tint, at the top edge
                // of its line where no glyph reaches.
                Point word = view.GetVisualPosition(new AvaloniaEdit.TextViewPosition(5, 7), VisualYPosition.LineTop);
                AssertColour(Colour("DiffAddedWordColor", ThemeVariant.Dark), frame, PointIn(view, word.X - view.HorizontalOffset, word.Y - view.VerticalOffset + 1));

                // The number columns keep the gutter's colour; the marker column takes the row's tint.
                DiffGutterMargin gutter = editor.Gutter;
                double markerX = (gutter.NumberColumns * editor.GutterWidth) + 1;

                AssertColour(Colour("DiffGutterColor", ThemeVariant.Dark), frame, PointIn(gutter, 1, LineTop(view, 2) + 1));
                AssertColour(Colour("DiffAddedLineColor", ThemeVariant.Dark), frame, PointIn(gutter, markerX, LineTop(view, 5) + 1));

                window.Close();
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    private static double LineTop(TextView view, int line)
        => view.GetVisualTopByDocumentLine(line) - view.VerticalOffset;

    private static PixelPoint PointIn(Visual visual, double x, double y)
    {
        Point point = visual.TranslatePoint(new Point(x, y), TopLevel.GetTopLevel(visual)!)
            ?? throw new InvalidOperationException("The visual is not in a window.");

        return new PixelPoint((int)point.X, (int)point.Y);
    }

    private static Bitmap Frame(Window window)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        }

        return window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was rendered.");
    }

    private static void AssertColour(Color expected, Bitmap frame, PixelPoint at)
    {
        Color actual = PixelAt(frame, at);

        Assert.True(
            Math.Abs(expected.R - actual.R) <= 2 && Math.Abs(expected.G - actual.G) <= 2 && Math.Abs(expected.B - actual.B) <= 2,
            $"expected {expected} at {at}, found {actual}");
    }

    private static Color PixelAt(Bitmap frame, PixelPoint at)
    {
        byte[] pixel = new byte[4];
        GCHandle handle = GCHandle.Alloc(pixel, GCHandleType.Pinned);

        try
        {
            frame.CopyPixels(new PixelRect(at.X, at.Y, 1, 1), handle.AddrOfPinnedObject(), pixel.Length, 4);
        }
        finally
        {
            handle.Free();
        }

        // The frame is in whichever byte order the platform renders in.
        return frame.Format == PixelFormats.Rgba8888
            ? Color.FromRgb(pixel[0], pixel[1], pixel[2])
            : Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }

    private static Color Colour(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out object? value), $"{key} is not defined");
        return Assert.IsType<Color>(value);
    }

    private static Color SolidColour(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
