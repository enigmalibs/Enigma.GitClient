using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Merging;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// The branches dialog's manual merge: a source, a destination, and the two merges between them.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ManualMergeTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public ManualMergeTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static RepositoryHandle Handle(string name)
        => OperatingSystem.IsWindows()
            ? new RepositoryHandle($@"C:\src\{name}", $@"C:\src\{name}\.git")
            : new RepositoryHandle($"/src/{name}", $"/src/{name}/.git");

    private static GitBranch Local(string name, bool isCurrent = false)
        => new($"refs/heads/{name}", "abc" + name, isCurrent, null, BranchTracking.None, GitSignature.Empty, DateTimeOffset.UnixEpoch, name);

    private static GitBranch Remote(string name)
        => new($"refs/remotes/{name}", "def" + name, false, null, BranchTracking.None, GitSignature.Empty, DateTimeOffset.UnixEpoch, name);

    /// <summary>
    /// The page over a repository with <c>main</c> (checked out), <c>feature</c> and
    /// <c>origin/main</c>, and a drop service that records what it is asked.
    /// </summary>
    private static async Task<(TestServices Services, BranchesPageViewModel Page, RecordingDropOperations Drops)> OpenAsync(
        params GitBranch[] extraLocal)
    {
        TestServices services = TestServices.Build(configure: collection =>
        {
            collection.RemoveAll<IBranchDropOperations>();
            collection.AddSingleton<IBranchDropOperations, RecordingDropOperations>();
        });

        FakeRefReader reader = (FakeRefReader)services.Get<IRefReader>();
        reader.Head = new HeadState(false, false, "main", "abcmain", RepositoryOperation.None);
        reader.Refs = new RefCollection([Local("main", isCurrent: true), Local("feature"), .. extraLocal], [Remote("origin/main")], [], []);

        BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
        await page.OnAppearingAsync();
        await services.Get<IRepositoryContext>().OpenAsync(Handle("manual"));

        return (services, page, (RecordingDropOperations)services.Get<IBranchDropOperations>());
    }

    [Fact]
    public void TheSources_AreEveryBranchAndTheDestinations_OnlyTheLocalOnes()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, BranchesPageViewModel page, _) = await OpenAsync();
            using TestServices scope = services;

            Assert.Equal(["main", "feature", "origin/main"], page.MergeSources);
            Assert.Equal(["main", "feature"], page.MergeDestinations);
        });
    }

    [Fact]
    public void TheMerges_AreOfferedOnlyForAPairThatMeansSomething()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, BranchesPageViewModel page, _) = await OpenAsync();
            using TestServices scope = services;

            Assert.False(page.ManualMergeCommand.CanExecute(null));
            Assert.False(page.ClearManualMergeCommand.CanExecute(null));

            page.SelectedMergeSource = "feature";
            Assert.False(page.ManualMergeCommand.CanExecute(null));
            Assert.True(page.ClearManualMergeCommand.CanExecute(null));

            // Into itself: nothing to merge.
            page.SelectedMergeDestination = "feature";
            Assert.False(page.ManualMergeCommand.CanExecute(null));
            Assert.False(page.ManualFastForwardCommand.CanExecute(null));

            page.SelectedMergeDestination = "main";
            Assert.True(page.ManualMergeCommand.CanExecute(null));
            Assert.True(page.ManualFastForwardCommand.CanExecute(null));
        });
    }

    [Theory]
    [InlineData(false, FastForwardMode.WhenPossible)]
    [InlineData(true, FastForwardMode.Only)]
    public void Merging_AsksForWhatDroppingTheSourceOnTheDestinationWould(bool fastForward, FastForwardMode expected)
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, BranchesPageViewModel page, RecordingDropOperations drops) = await OpenAsync();
            using TestServices scope = services;

            page.SelectedMergeSource = "origin/main";
            page.SelectedMergeDestination = "feature";

            await (fastForward ? page.ManualFastForwardCommand : page.ManualMergeCommand).ExecuteAsync(null);

            (BranchDropRequest request, FastForwardMode mode) = Assert.Single(drops.Requests);
            Assert.Equal(new BranchDropRequest("origin/main", true, "feature", false, false), request);
            Assert.Equal(expected, mode);
        });
    }

    [Fact]
    public void MergingIntoTheCurrentBranch_SaysItIsTheCurrentOne()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, BranchesPageViewModel page, RecordingDropOperations drops) = await OpenAsync();
            using TestServices scope = services;

            page.SelectedMergeSource = "feature";
            page.SelectedMergeDestination = "main";
            await page.ManualMergeCommand.ExecuteAsync(null);

            Assert.True(Assert.Single(drops.Requests).Request.TargetIsCurrent);
        });
    }

    [Fact]
    public void Clear_EmptiesBothChoices()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, BranchesPageViewModel page, _) = await OpenAsync();
            using TestServices scope = services;

            page.SelectedMergeSource = "feature";
            page.SelectedMergeDestination = "main";

            page.ClearManualMergeCommand.Execute(null);

            Assert.Null(page.SelectedMergeSource);
            Assert.Null(page.SelectedMergeDestination);
            Assert.False(page.ClearManualMergeCommand.CanExecute(null));
        });
    }

    [Fact]
    public void TheChoices_SurviveARefreshAndGoWithTheirBranch()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, BranchesPageViewModel page, _) = await OpenAsync(Local("topic"));
            using TestServices scope = services;

            page.SelectedMergeSource = "topic";
            page.SelectedMergeDestination = "feature";

            // The filter narrows the list, not the merge.
            page.SearchText = "main";
            Assert.Equal("topic", page.SelectedMergeSource);
            Assert.Equal("feature", page.SelectedMergeDestination);

            FakeRefReader reader = (FakeRefReader)services.Get<IRefReader>();
            reader.Refs = new RefCollection([Local("main", isCurrent: true), Local("feature")], [Remote("origin/main")], [], []);
            await services.Get<IRepositoryContext>().RefreshAsync();

            Assert.Null(page.SelectedMergeSource);
            Assert.Equal("feature", page.SelectedMergeDestination);
        });
    }

    [Fact]
    public void TheBranchesView_CarriesTheManualMergeSection()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, BranchesPageViewModel page, _) = await OpenAsync();
            using TestServices scope = services;

            BranchesPageView view = services.Get<BranchesPageView>();
            view.DataContext = page;

            Assert.Same(page.MergeSources, view.MergeSource.ItemsSource);
            Assert.Same(page.MergeDestinations, view.MergeDestination.ItemsSource);
            Assert.True(view.ManualMerge.IsVisible);
        });
    }

    /// <summary>
    /// Records the drops the page asks for, and changes nothing.
    /// </summary>
    private sealed class RecordingDropOperations : IBranchDropOperations
    {
        public List<(BranchDropRequest Request, FastForwardMode Mode)> Requests { get; } = [];

        public Task<bool> DropAsync(BranchDropRequest request, FastForwardMode fastForward = FastForwardMode.WhenPossible)
        {
            Requests.Add((request, fastForward));
            return Task.FromResult(false);
        }
    }
}
