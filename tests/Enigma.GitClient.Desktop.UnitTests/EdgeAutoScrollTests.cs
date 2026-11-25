using Avalonia;
using Avalonia.Controls;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.Views;
using Enigma.GitClient.Desktop.Views.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The edge scrolling every dragged list shares (CODE-REVIEW-6E56 XAML-02): it scrolls towards the
/// edge the pointer is held near, never past an end, and stops when the pointer leaves the band.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class EdgeAutoScrollTests
{
    private const double ViewportHeight = 300;

    private readonly HeadlessAvaloniaFixture _fixture;

    public EdgeAutoScrollTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static (Window Window, ScrollViewer Scroll) ShowAList()
    {
        ScrollViewer scroll = new() { Content = new Border { Height = 3_000 } };
        Window window = new() { Content = scroll, Width = 200, Height = ViewportHeight };
        window.Show();
        window.UpdateLayout();

        return (window, scroll);
    }

    [Fact]
    public void NearTheBottom_ItScrollsDown_AndStopsOnceThePointerLeavesTheBand()
    {
        _fixture.Run(() =>
        {
            (Window window, ScrollViewer scroll) = ShowAList();
            EdgeAutoScroll edge = new(() => scroll);

            edge.Follow(ViewportHeight - 1, ViewportHeight);

            Assert.True(edge.IsScrolling);
            Assert.True(edge.Step > 0);

            edge.ScrollOnce();

            Assert.Equal(edge.Step, scroll.Offset.Y);

            edge.Follow(ViewportHeight / 2, ViewportHeight);

            Assert.False(edge.IsScrolling);
            Assert.Equal(0, edge.Step);

            window.Close();
        });
    }

    [Fact]
    public void NearTheTop_ItScrollsUp_NoFurtherThanTheTop()
    {
        _fixture.Run(() =>
        {
            (Window window, ScrollViewer scroll) = ShowAList();
            scroll.Offset = new Vector(0, 10);
            EdgeAutoScroll edge = new(() => scroll);

            edge.Follow(0, ViewportHeight);

            Assert.Equal(-BranchDragGesture.ScrollStep, edge.Step);

            edge.ScrollOnce();
            edge.ScrollOnce();

            Assert.Equal(0, scroll.Offset.Y);

            window.Close();
        });
    }

    [Fact]
    public void AfterEachStepThatMovedTheList_TheViewIsTold_AndAtTheEndItIsNot()
    {
        _fixture.Run(() =>
        {
            (Window window, ScrollViewer scroll) = ShowAList();
            double furthest = scroll.Extent.Height - scroll.Viewport.Height;
            scroll.Offset = new Vector(0, furthest - 1);
            int told = 0;
            EdgeAutoScroll edge = new(() => scroll, () => told++);

            edge.Follow(ViewportHeight, ViewportHeight);
            edge.ScrollOnce();

            Assert.Equal(furthest, scroll.Offset.Y);
            Assert.Equal(1, told);

            // Nothing left that way: the list stays, and nothing is said.
            edge.ScrollOnce();

            Assert.Equal(furthest, scroll.Offset.Y);
            Assert.Equal(1, told);

            window.Close();
        });
    }

    [Fact]
    public void Stop_StopsTheScrolling()
    {
        _fixture.Run(() =>
        {
            (Window window, ScrollViewer scroll) = ShowAList();
            EdgeAutoScroll edge = new(() => scroll);
            edge.Follow(ViewportHeight, ViewportHeight);

            edge.Stop();

            Assert.False(edge.IsScrolling);
            Assert.Equal(0, edge.Step);

            window.Close();
        });
    }

    [Fact]
    public void AListWithNoScrollViewerYet_StopsTheScrolling()
    {
        _fixture.Run(() =>
        {
            EdgeAutoScroll edge = new(() => null);
            edge.Follow(ViewportHeight, ViewportHeight);

            edge.ScrollOnce();

            Assert.False(edge.IsScrolling);
        });
    }
}
