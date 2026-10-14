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
/// Filtering the start window's list of repositories, and the reordering a filtered list refuses.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RepositoryFilterTests
{
    private static readonly string[] Names = ["alpha", "beta", "gamma"];

    private readonly HeadlessAvaloniaFixture _fixture;

    public RepositoryFilterTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- the rule

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("client", true)]
    [InlineData("CLIENT", true)]
    [InlineData("  git  ", true)]
    [InlineData("work", true)]
    [InlineData("projects/WORK", true)]
    [InlineData("server", false)]
    public void Matches_KeepsARepositoryWhoseNameOrPathHasTheText(string search, bool expected)
    {
        ListedRepository entry = new(Path.Combine("projects", "work", "enigma-gitclient"), "Enigma.GitClient");

        Assert.Equal(expected, RepositoriesPageViewModel.Matches(entry, search.Replace('/', Path.DirectorySeparatorChar)));
    }

    // ---------------------------------------------------------------- the list

    [Fact]
    public void Typing_HidesTheRepositoriesThatDoNotMatch_AndClearingShowsThemAllInOrder()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            RepositoriesPageViewModel model = await LoadAsync(services);

            model.SearchText = "MM";

            Assert.Equal(["gamma"], model.Repositories.Select(entry => entry.Name));
            Assert.True(model.IsFiltering);
            Assert.False(model.HasNoMatch);

            model.SearchText = string.Empty;

            Assert.Equal(Names, model.Repositories.Select(entry => entry.Name));
            Assert.False(model.IsFiltering);
        });
    }

    [Fact]
    public void ClearSearch_EmptiesTheBoxAndShowsEveryRepository()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            RepositoriesPageViewModel model = await LoadAsync(services);

            Assert.False(model.ClearSearchCommand.CanExecute(null));

            model.SearchText = "beta";
            Assert.True(model.ClearSearchCommand.CanExecute(null));

            model.ClearSearchCommand.Execute(null);

            Assert.Equal(string.Empty, model.SearchText);
            Assert.Equal(Names, model.Repositories.Select(entry => entry.Name));
        });
    }

    [Fact]
    public void AFilterThatKeepsNothing_SaysSoRatherThanThatThereAreNoRepositories()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            RepositoriesPageViewModel model = await LoadAsync(services);

            model.SearchText = "nothing like it";

            Assert.Empty(model.Repositories);
            Assert.True(model.HasRepositories);
            Assert.True(model.HasNoMatch);
        });
    }

    [Fact]
    public void SpacesAlone_FilterNothingAndLeaveTheOrderMovable()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            RepositoriesPageViewModel model = await LoadAsync(services);

            model.SearchText = "   ";

            Assert.Equal(Names, model.Repositories.Select(entry => entry.Name));
            Assert.False(model.IsFiltering);
            Assert.True(model.CanReorder);
        });
    }

    [Fact]
    public void AnotherProfilesList_IsFilteredByTheSameText()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IIdentityProfileStore profiles = services.Get<IIdentityProfileStore>();
            IdentityProfile work = await profiles.SaveAsync(IdentityProfile.Create("Work", GitIdentity.Empty));
            IdentityProfile home = await profiles.SaveAsync(IdentityProfile.Create("Home", GitIdentity.Empty));

            IRepositoryListStore store = services.Get<IRepositoryListStore>();
            await store.AddAsync(work.Id, Path.Combine(services.ConfigurationRoot, "work-api"), "work-api");
            await store.AddAsync(home.Id, Path.Combine(services.ConfigurationRoot, "home-api"), "home-api");
            await store.AddAsync(home.Id, Path.Combine(services.ConfigurationRoot, "home-site"), "home-site");

            RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
            await model.OnAppearingAsync();

            model.SearchText = "api";
            model.SelectedProfile = model.Profiles.Single(profile => profile.Id == home.Id);

            await WaitUntilAsync(() => model.Repositories.Select(entry => entry.Name).SequenceEqual(["home-api"]));
            Assert.Equal("api", model.SearchText);
        });
    }

    // ---------------------------------------------------------------- no reordering while filtered

    [Fact]
    public void MoveWhileFiltered_LeavesTheStoredOrderAlone()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            RepositoriesPageViewModel model = await LoadAsync(services);

            model.SearchText = "a";
            Assert.False(model.CanReorder);

            await model.MoveAsync(model.Repositories[0], 2);

            IReadOnlyList<ListedRepository> stored =
                await services.Get<IRepositoryListStore>().GetAsync(IdentityProfile.DefaultId);
            Assert.Equal(Names, stored.Select(entry => entry.Name));
        });
    }

    [Fact]
    public void DraggingARowWhileFiltered_StartsNoDragAndMovesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, RepositoriesPageView page, Window window) = await ShowAsync(services);

            model.SearchText = "a";
            Dispatcher.UIThread.RunJobs();

            ContentPresenter first = Row(page, 0);
            ContentPresenter last = Row(page, model.Repositories.Count - 1);

            Point start = Centre(first, window);
            Point end = Below(last, window);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X, start.Y + 8), RawInputModifiers.LeftMouseButton);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);

            Assert.False(page.IsDragging);
            Assert.DoesNotContain("insertafter", last.Classes);

            window.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            IReadOnlyList<ListedRepository> stored =
                await services.Get<IRepositoryListStore>().GetAsync(IdentityProfile.DefaultId);
            Assert.Equal(Names, stored.Select(entry => entry.Name));

            window.Close();
        });
    }

    [Fact]
    public void TheGripIsHiddenWhileFiltered_AndBackOnceTheFilterIsCleared()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, RepositoriesPageView page, Window window) = await ShowAsync(services);

            model.SearchText = "beta";
            Dispatcher.UIThread.RunJobs();

            Assert.False(Grip(Row(page, 0)).IsVisible);

            model.SearchText = string.Empty;
            Dispatcher.UIThread.RunJobs();

            Assert.All(Enumerable.Range(0, Names.Length), index => Assert.True(Grip(Row(page, index)).IsVisible));

            window.Close();
        });
    }

    [Fact]
    public void TheFilterBox_SitsAboveTheList()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (_, RepositoriesPageView page, Window window) = await ShowAsync(services);

            TextBox filter = page.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Filter repositories");

            Assert.True(filter.IsEffectivelyVisible);

            double filterBottom = filter.TranslatePoint(new Point(0, filter.Bounds.Height), window)!.Value.Y;
            double firstRowTop = Row(page, 0).TranslatePoint(default, window)!.Value.Y;
            Assert.True(filterBottom <= firstRowTop, "the filter box must sit above the first row");

            window.Close();
        });
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<RepositoriesPageViewModel> LoadAsync(TestServices services)
    {
        IRepositoryListStore store = services.Get<IRepositoryListStore>();

        foreach (string name in Names)
        {
            await store.AddAsync(IdentityProfile.DefaultId, Path.Combine(services.ConfigurationRoot, name), name);
        }

        RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
        await model.OnAppearingAsync();

        return model;
    }

    private static async Task<(RepositoriesPageViewModel Model, RepositoriesPageView Page, Window Window)> ShowAsync(
        TestServices services)
    {
        RepositoriesPageViewModel model = await LoadAsync(services);

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

    private static Control Grip(ContentPresenter row)
        => row.GetVisualDescendants()
            .OfType<Control>()
            .Single(control => AutomationProperties.GetName(control) == "Drag to reorder");

    private static Point Centre(Visual target, Visual relativeTo)
        => target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), relativeTo)
            ?? throw new InvalidOperationException("The row is not in the same tree as the window.");

    private static Point Below(Visual target, Visual relativeTo)
        => target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height * 0.8), relativeTo)
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
