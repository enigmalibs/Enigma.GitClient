using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Enigma.GitClient.Desktop.Controls;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The command bar's layout: the commands at the bar's true centre while both sides fit beside them,
/// moved as little as they must when not, and the flexible end the first to give way.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class CommandBarPanelTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public CommandBarPanelTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A bar <paramref name="width"/> wide, with a start and a centre of fixed widths and an end that
    /// stretches between its minimum and its maximum, as the search block does.
    /// </summary>
    /// <remarks>
    /// Shown, because a headless window that is not lays itself out at its own default size whatever
    /// it was measured at.
    /// </remarks>
    private sealed class Bar : IDisposable
    {
        public Bar(double width, double start, double centre, double endMin, double endMax)
        {
            Start = new Border { Width = start, Height = 20 };
            Centre = new Border { Width = centre, Height = 54 };
            End = new Border { MinWidth = endMin, MaxWidth = endMax, Height = 30 };
            Panel = new CommandBarPanel { Children = { Start, Centre, End } };
            Window = new Window { Content = Panel, Width = width, Height = 100 };

            Window.Show();
            Window.UpdateLayout();
        }

        public void Dispose() => Window.Close();

        public Border Start { get; }

        public Border Centre { get; }

        public Border End { get; }

        public CommandBarPanel Panel { get; }

        public Window Window { get; }
    }

    [Fact]
    public void WithRoomOnBothSides_TheCentreIsAtTheTrueCentre()
    {
        _fixture.Run(() =>
        {
            using Bar bar = new(width: 1000, start: 100, centre: 300, endMin: 100, endMax: 200);

            Assert.Equal(new Rect(0, 40, 100, 20), bar.Start.Bounds);
            Assert.Equal(350, bar.Centre.Bounds.X);

            // At its maximum, against the right edge.
            Assert.Equal(800, bar.End.Bounds.X);
            Assert.Equal(200, bar.End.Bounds.Width);
        });
    }

    [Fact]
    public void TheEnd_IsGivenTheRoomRightOfTheCentre_UpToItsMaxWidth()
    {
        _fixture.Run(() =>
        {
            // 600 would not fit: the end takes the 340 the true centre leaves it, and the centre stays.
            using Bar bar = new(width: 1000, start: 100, centre: 300, endMin: 200, endMax: 600);

            Assert.Equal(350, bar.Centre.Bounds.X);
            Assert.Equal(660, bar.End.Bounds.X);
            Assert.Equal(340, bar.End.Bounds.Width);
        });
    }

    [Fact]
    public void AWideStart_MovesTheCentreRight_NeverOverIt()
    {
        _fixture.Run(() =>
        {
            using Bar bar = new(width: 1000, start: 400, centre: 300, endMin: 100, endMax: 200);

            // The spacing after the start, and not a pixel more.
            Assert.Equal(410, bar.Centre.Bounds.X);
            Assert.Equal(800, bar.End.Bounds.X);
            Assert.Equal(200, bar.End.Bounds.Width);
        });
    }

    [Fact]
    public void AWideEnd_ShrinksToItsMinWidth_BeforeTheCentreMovesLeft()
    {
        _fixture.Run(() =>
        {
            using Bar bar = new(width: 1000, start: 100, centre: 300, endMin: 400, endMax: 600);

            // The true centre would leave the end 340; the centre moves 60 to the left to keep it 400.
            Assert.Equal(290, bar.Centre.Bounds.X);
            Assert.Equal(600, bar.End.Bounds.X);
            Assert.Equal(400, bar.End.Bounds.Width);
        });
    }

    [Fact]
    public void TooNarrowABar_PutsTheCentreAfterTheStart_AndTheEndRunsPastTheEdgeNotOverIt()
    {
        _fixture.Run(() =>
        {
            using Bar bar = new(width: 600, start: 100, centre: 300, endMin: 400, endMax: 600);

            Assert.Equal(110, bar.Centre.Bounds.X);

            // 180 are left: the end starts there, at its minimum, and what does not fit is past the edge.
            Assert.Equal(420, bar.End.Bounds.X);
            Assert.Equal(400, bar.End.Bounds.Width);
        });
    }

    [Fact]
    public void TheBar_IsAsTallAsItsTallestSlot_AndCentresTheOthersInIt()
    {
        _fixture.Run(() =>
        {
            using Bar bar = new(width: 1000, start: 100, centre: 300, endMin: 100, endMax: 200);
            bar.Panel.VerticalAlignment = VerticalAlignment.Top;
            bar.Window.UpdateLayout();

            Assert.Equal(54, bar.Panel.Bounds.Height);
            Assert.Equal(bar.Centre.Bounds.Center.Y, bar.Start.Bounds.Center.Y);
            Assert.Equal(bar.Centre.Bounds.Center.Y, bar.End.Bounds.Center.Y);

            // Unbounded, as in a horizontal stack, it asks for every slot at its desired width — an
            // empty end's being its minimum.
            bar.Panel.Measure(Size.Infinity);
            Assert.Equal(100 + 10 + 300 + 10 + 100, bar.Panel.DesiredSize.Width);
        });
    }

    [Fact]
    public void ABarTooNarrowForTheFullCentre_IsCompact_UntilItIsWideEnoughAgain()
    {
        _fixture.Run(() =>
        {
            // A centre that loses 100 when compact, as the commands lose their names.
            TextBlock name = new() { Classes = { "commandlabel" }, Width = 100 };
            StackPanel centre = new()
            {
                Orientation = Orientation.Horizontal,
                Children = { new Border { Width = 200, Height = 54 }, name },
            };
            CommandBarPanel panel = new()
            {
                Children =
                {
                    new Border { Width = 100, Height = 20 },
                    centre,
                    new Border { MinWidth = 400, MaxWidth = 600, Height = 30 },
                },
            };
            Window window = new() { Content = panel, Width = 760, Height = 100 };
            window.Show();

            try
            {
                window.UpdateLayout();

                // 100 + 300 + 400 and the two gaps is 820: compact, and the name goes.
                Assert.True(panel.IsCompact);
                Assert.Contains(":compact", panel.Classes);
                Assert.False(name.IsVisible);
                Assert.Equal(200, centre.Bounds.Width);

                // At 200 the centre would fit, and it stays compact all the same: the decision is the
                // full centre's, so it does not undo itself.
                window.UpdateLayout();
                Assert.True(panel.IsCompact);

                window.Width = 900;
                window.UpdateLayout();

                Assert.False(panel.IsCompact);
                Assert.True(name.IsVisible);
                Assert.Equal(300, centre.Bounds.Width);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AnEndThatIsNotShown_TakesNoRoomFromTheCentre()
    {
        _fixture.Run(() =>
        {
            using Bar bar = new(width: 1000, start: 100, centre: 300, endMin: 400, endMax: 600);
            bar.End.IsVisible = false;
            bar.Window.UpdateLayout();

            Assert.Equal(350, bar.Centre.Bounds.X);
        });
    }
}
