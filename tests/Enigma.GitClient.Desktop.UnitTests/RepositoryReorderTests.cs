using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Putting the repositories in the order the user wants: dragging a row between two others, driven
/// with a real pointer on the headless platform.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RepositoryReorderTests
{
    private static readonly string[] Names = ["alpha", "beta", "gamma"];

    private readonly HeadlessAvaloniaFixture _fixture;

    public RepositoryReorderTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- the arithmetic

    [Theory]
    [InlineData(0, 0)]
    [InlineData(19, 0)]
    [InlineData(21, 1)]
    [InlineData(59, 1)]
    [InlineData(61, 2)]
    [InlineData(99, 2)]
    [InlineData(101, 3)]
    [InlineData(500, 3)]
    [InlineData(-50, 0)]
    public void SlotFor_IsTheGapBeforeTheFirstRowWhoseMiddleIsBelowThePointer(double y, int expected)
    {
        // Three rows of forty, one after the other.
        (double Top, double Height)[] rows = [(0, 40), (40, 40), (80, 40)];

        Assert.Equal(expected, RepositoryReorderGesture.SlotFor(y, rows));
    }

    [Fact]
    public void SlotFor_AnEmptyListHasOneSlot()
        => Assert.Equal(0, RepositoryReorderGesture.SlotFor(10, []));

    [Theory]
    [InlineData(1, 0, true, 0)]
    [InlineData(1, 1, false, 1)]
    [InlineData(1, 2, false, 1)]
    [InlineData(1, 3, true, 2)]
    [InlineData(0, 3, true, 2)]
    [InlineData(2, 0, true, 0)]
    public void ASlotMovesTheRowOnlyWhenItIsNotEitherSideOfIt(int from, int slot, bool moves, int target)
    {
        Assert.Equal(moves, RepositoryReorderGesture.Moves(from, slot));
        Assert.Equal(target, RepositoryReorderGesture.TargetIndex(from, slot));
    }

    // ---------------------------------------------------------------- the gesture

    [Fact]
    public void DraggingARowPastTheLastOne_PutsItAtTheEndAndRemembersIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, RepositoriesPageView page, Window window) = await ShowAsync(services);

            ContentPresenter first = Row(page, 0);
            ContentPresenter last = Row(page, 2);

            Point start = Centre(first, window);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X, start.Y + 8), RawInputModifiers.LeftMouseButton);

            Point end = Below(last, window);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);

            Assert.True(page.IsDragging);
            Assert.True(last.Classes.Contains("insertafter"), "the line must show the gap below the last row");

            window.MouseUp(end, MouseButton.Left);

            Assert.False(page.IsDragging);
            Assert.DoesNotContain("insertafter", last.Classes);

            await WaitUntilAsync(() => model.Repositories.Select(entry => entry.Name).SequenceEqual(["beta", "gamma", "alpha"]));

            IReadOnlyList<ListedRepository> stored =
                await services.Get<IRepositoryListStore>().GetAsync(IdentityProfile.DefaultId);
            Assert.Equal(["beta", "gamma", "alpha"], stored.Select(entry => entry.Name));

            window.Close();
        });
    }

    [Fact]
    public void DraggingARowAboveTheFirstOne_PutsItFirst()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, RepositoriesPageView page, Window window) = await ShowAsync(services);

            ContentPresenter first = Row(page, 0);
            ContentPresenter last = Row(page, 2);

            Point start = Centre(last, window);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X, start.Y - 8), RawInputModifiers.LeftMouseButton);

            Point end = Above(first, window);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);

            Assert.True(first.Classes.Contains("insertbefore"), "the line must show the gap above the first row");

            window.MouseUp(end, MouseButton.Left);

            await WaitUntilAsync(() => model.Repositories.Select(entry => entry.Name).SequenceEqual(["gamma", "alpha", "beta"]));

            window.Close();
        });
    }

    [Fact]
    public void ADropNextToTheRowItself_ShowsNoLineAndMovesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, RepositoriesPageView page, Window window) = await ShowAsync(services);

            ContentPresenter middle = Row(page, 1);

            Point start = Centre(middle, window);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X + 10, start.Y + 2), RawInputModifiers.LeftMouseButton);

            Assert.True(page.IsDragging);
            Assert.DoesNotContain(
                Enumerable.Range(0, 3).Select(index => Row(page, index)),
                row => row.Classes.Contains("insertbefore") || row.Classes.Contains("insertafter"));

            window.MouseUp(start, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Names, model.Repositories.Select(entry => entry.Name));

            window.Close();
        });
    }

    [Fact]
    public void APressOnARowsButton_StartsNoDrag()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, RepositoriesPageView page, Window window) = await ShowAsync(services);

            Button forget = Row(page, 0).GetVisualDescendants()
                .OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "Forget this repository");

            Point start = Centre(forget, window);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(Below(Row(page, 2), window), RawInputModifiers.LeftMouseButton);

            Assert.False(page.IsDragging);

            window.MouseUp(Below(Row(page, 2), window), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Names, model.Repositories.Select(entry => entry.Name));

            window.Close();
        });
    }

    [Fact]
    public void Escape_CallsTheDragOff()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, RepositoriesPageView page, Window window) = await ShowAsync(services);

            ContentPresenter last = Row(page, 2);
            Point start = Centre(Row(page, 0), window);
            Point end = Below(last, window);

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X, start.Y + 8), RawInputModifiers.LeftMouseButton);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            Assert.False(page.IsDragging);
            Assert.DoesNotContain("insertafter", last.Classes);

            window.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Names, model.Repositories.Select(entry => entry.Name));

            window.Close();
        });
    }

    [Fact]
    public void EveryRowCarriesTheGripThatSaysItCanBeDragged()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (_, RepositoriesPageView page, Window window) = await ShowAsync(services);

            foreach (int index in Enumerable.Range(0, 3))
            {
                Assert.Contains(
                    Row(page, index).GetVisualDescendants().OfType<Control>(),
                    control => AutomationProperties.GetName(control) == "Drag to reorder");
            }

            window.Close();
        });
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<(RepositoriesPageViewModel Model, RepositoriesPageView Page, Window Window)> ShowAsync(
        TestServices services)
    {
        IRepositoryListStore store = services.Get<IRepositoryListStore>();

        foreach (string name in Names)
        {
            await store.AddAsync(IdentityProfile.DefaultId, Path.Combine(services.ConfigurationRoot, name), name);
        }

        RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
        await model.OnAppearingAsync();

        RepositoriesPageView page = services.Get<RepositoriesPageView>();
        page.DataContext = model;

        Window window = new() { Content = page, Width = 1100, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (model, page, window);
    }

    private static ContentPresenter Row(RepositoriesPageView page, int index)
    {
        ItemsControl list = page.GetVisualDescendants().OfType<ItemsControl>().Single(control => control.Name == "RepositoryList");

        return Assert.IsType<ContentPresenter>(list.ContainerFromIndex(index));
    }

    private static Point Centre(Visual target, Visual relativeTo)
        => target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), relativeTo)
            ?? throw new InvalidOperationException("The row is not in the same tree as the window.");

    private static Point Below(Visual target, Visual relativeTo)
        => target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height * 0.8), relativeTo)
            ?? throw new InvalidOperationException("The row is not in the same tree as the window.");

    private static Point Above(Visual target, Visual relativeTo)
        => target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height * 0.2), relativeTo)
            ?? throw new InvalidOperationException("The row is not in the same tree as the window.");

    private static async Task WaitUntilAsync(Func<bool> condition, int attempts = 300)
    {
        for (int attempt = 0; attempt < attempts && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "the page never reached the state the test waited for");
    }
}
