using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Enigma.GitClient.Desktop.Controls.Diff;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// BUG-7823 as it happens in a real window: the text view is measured at one size and arranged at a
/// slightly different one, its arrange pass clamps the scroll offset to the arranged size, and a
/// selection being dragged past the end answers every clamp by bringing the caret into view again —
/// a layout pass per frame, for ever.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiffTextViewScrollTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public DiffTextViewScrollTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Measures its child at one size and arranges it at another, as layout rounding in a window can.
    /// </summary>
    private sealed class MismatchedHost : Decorator
    {
        public Size Measured { get; init; }

        public Size Arranged { get; init; }

        protected override Size MeasureOverride(Size availableSize)
        {
            Child?.Measure(Measured);
            return Arranged;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Child?.Arrange(new Rect(Arranged));
            return finalSize;
        }
    }

    [Theory]
    [InlineData(250.0, 253.5)]
    [InlineData(253.5, 250.0)]
    public void ACaretHeldPastTheEnd_SettlesWhateverSizeTheViewIsArrangedAt(double measured, double arranged)
    {
        _fixture.Run(() =>
        {
            (TextView view, Window window) = Show(new DiffTextView(), measured, arranged);

            int events = HoldTheCaretPastTheEnd(view, window, passes: 20);

            // A couple of moves to get to the end; after that, nothing. The fight raised events on
            // every pass for as long as the drag lasted.
            Assert.True(events <= 4, $"{events} scroll events over 20 layout passes");

            window.Close();
        });
    }

    [Fact]
    public void TheStockTextView_FightsItsOwnArrangeClamp()
    {
        // The control experiment: AvaloniaEdit's own view, in the same host, does not settle — which is
        // what proves the host reproduces what froze the application.
        _fixture.Run(() =>
        {
            (TextView view, Window window) = Show(new TextView(), measured: 250, arranged: 253.5);

            int events = HoldTheCaretPastTheEnd(view, window, passes: 20);

            Assert.True(events > 20, $"only {events} scroll events: the host no longer reproduces the fight");

            window.Close();
        });
    }

    [Fact]
    public void OffsetToShow_ScrollsJustFarEnoughToShowARectangle()
    {
        Size viewport = new(300, 400);
        Size extent = new(1000, 2000);

        // Below the view: its bottom edge comes to the view's bottom edge.
        Assert.Equal(new Vector(0, 620), DiffTextView.OffsetToShow(new Rect(10, 1000, 10, 20), default, viewport, viewport, extent));

        // Already in view: nothing moves.
        Assert.Equal(new Vector(0, 100), DiffTextView.OffsetToShow(new Rect(10, 200, 10, 20), new Vector(0, 100), viewport, viewport, extent));

        // Above and left of it: its top-left corner comes to the view's.
        Assert.Equal(new Vector(50, 60), DiffTextView.OffsetToShow(new Rect(50, 60, 10, 20), new Vector(100, 100), viewport, viewport, extent));
    }

    [Theory]
    [InlineData(400.0, 403.5)]
    [InlineData(403.5, 400.0)]
    public void OffsetToShow_NeverGoesPastWhatTheViewCanBeScrolledTo(double measured, double arranged)
    {
        Size extent = new(1000, 2000);

        // The caret at the very end, with AvaloniaEdit's 5-pixel margin around it.
        Vector offset = DiffTextView.OffsetToShow(
            new Rect(995, 1985, 10, 20),
            default,
            new Size(300, measured),
            new Size(300, arranged),
            extent);

        // Neither the arrange pass (which clamps to the arranged size) nor the scroll viewer (which
        // clamps to the measured one) has anything left to take back.
        Assert.Equal(1000 - 300, offset.X);
        Assert.Equal(2000 - Math.Max(measured, arranged), offset.Y);
    }

    [Fact]
    public void OffsetToShow_LeavesContentSmallerThanTheViewWhereItIs()
        => Assert.Equal(
            default,
            DiffTextView.OffsetToShow(new Rect(40, 40, 10, 20), default, new Size(300, 400), new Size(300, 400), new Size(100, 100)));

    // ---------------------------------------------------------------- helpers

    private static (TextView View, Window Window) Show(TextView view, double measured, double arranged)
    {
        view.Document = new TextDocument(string.Join("\n", Enumerable.Range(0, 200).Select(index => "line " + index.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        // As the diff editor has it: nothing scrolls below the last line, so the end is a hard edge.
        view.Options = new AvaloniaEdit.TextEditorOptions { AllowScrollBelowDocument = false };

        // What the scroll viewer's presenter tells a logical scrollable it is hosted in.
        ILogicalScrollable scrollable = view;
        scrollable.CanHorizontallyScroll = true;
        scrollable.CanVerticallyScroll = true;

        MismatchedHost host = new()
        {
            Measured = new Size(300, measured),
            Arranged = new Size(300, arranged),
            Child = view,
        };

        Window window = new() { Content = host, Width = 400, Height = 400 };
        window.Show();
        Pass(window);

        return (view, window);
    }

    /// <summary>
    /// Does what AvaloniaEdit's selection handler does while a drag is held past the end: every scroll
    /// offset change brings the caret — at the end of the document — back into view, with its margin.
    /// </summary>
    /// <returns>How many scroll offset changes the passes raised.</returns>
    private static int HoldTheCaretPastTheEnd(TextView view, Window window, int passes)
    {
        int events = 0;

        void BringTheEndIntoView()
        {
            int last = view.Document.LineCount;
            double top = view.GetVisualTopByDocumentLine(last);

            view.MakeVisible(new Rect(40, top, 1, view.DefaultLineHeight).Inflate(5));
        }

        view.ScrollOffsetChanged += (_, _) =>
        {
            events++;
            BringTheEndIntoView();
        };

        BringTheEndIntoView();

        for (int pass = 0; pass < passes; pass++)
        {
            Pass(window);
        }

        return events;
    }

    private static void Pass(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
    }
}
