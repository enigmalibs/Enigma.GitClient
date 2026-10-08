using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The automatic fetch and refresh: when it runs, what it does quietly, and what the graph does with
/// what it found.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class AutoRefreshTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly HeadlessAvaloniaFixture _fixture;

    public AutoRefreshTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static RepositoryHandle Handle(string name)
        => OperatingSystem.IsWindows()
            ? new RepositoryHandle($@"C:\src\{name}", $@"C:\src\{name}\.git")
            : new RepositoryHandle($"/src/{name}", $"/src/{name}/.git");

    private static GitBranch Branch(string name, string sha)
        => new($"refs/heads/{name}", sha, false, null, BranchTracking.None, GitSignature.Empty, DateTimeOffset.UnixEpoch, name);

    /// <summary>
    /// The container with a scripted quiet fetch, over the fake reference reader.
    /// </summary>
    private static TestServices BuildScripted(Action<ServiceCollection>? configure = null)
        => TestServices.Build(configure: services =>
        {
            services.RemoveAll<ISyncOperations>();
            services.AddSingleton<ScriptedSync>();
            services.AddSingleton<ISyncOperations>(provider => provider.GetRequiredService<ScriptedSync>());
            configure?.Invoke(services);
        });

    /// <summary>
    /// Waits for the next refresh the service reports, pumping the dispatcher the tick continues on.
    /// </summary>
    private static async Task<AutoRefreshResult> NextRefreshAsync(IAutoRefreshService service, Action trigger)
    {
        TaskCompletionSource<AutoRefreshResult> seen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<AutoRefreshResult> handler = (_, result) => seen.TrySetResult(result);

        service.Refreshed += handler;

        try
        {
            trigger();
            return await seen.Task.WaitAsync(Patience);
        }
        finally
        {
            service.Refreshed -= handler;
        }
    }

    // ---------------------------------------------------------------- when it runs

    [Fact]
    public void ItRuns_OnlyWhileARepositoryIsOpen()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();

            Assert.False(service.IsRunning);

            await services.Get<IRepositoryContext>().OpenAsync(Handle("auto"));
            Assert.True(service.IsRunning);

            services.Get<IRepositoryContext>().Close();
            Assert.False(service.IsRunning);
        });
    }

    [Fact]
    public void ItTicks_AtTheIntervalTheSettingsName()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            ScriptedSync sync = services.Get<ScriptedSync>();
            services.Get<ISettingsService>().Update(current => current with { AutoRefreshSeconds = 15 });

            await services.Get<IRepositoryContext>().OpenAsync(Handle("auto"));

            // Fourteen seconds in: nothing yet.
            services.Clock.Advance(TimeSpan.FromSeconds(14));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, sync.QuietFetches);

            await NextRefreshAsync(service, () => services.Clock.Advance(TimeSpan.FromSeconds(1)));
            Assert.Equal(1, sync.QuietFetches);

            await NextRefreshAsync(service, () => services.Clock.Advance(TimeSpan.FromSeconds(15)));
            Assert.Equal(2, sync.QuietFetches);
        });
    }

    [Fact]
    public void AnIntervalOfZero_StopsItAndANewOneRestartsIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            ISettingsService settings = services.Get<ISettingsService>();
            ScriptedSync sync = services.Get<ScriptedSync>();

            await services.Get<IRepositoryContext>().OpenAsync(Handle("auto"));

            settings.Update(current => current with { AutoRefreshSeconds = 0 });
            Assert.False(service.IsRunning);

            services.Clock.Advance(TimeSpan.FromMinutes(5));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, sync.QuietFetches);

            settings.Update(current => current with { AutoRefreshSeconds = 60 });
            Assert.True(service.IsRunning);

            services.Clock.Advance(TimeSpan.FromSeconds(30));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, sync.QuietFetches);

            await NextRefreshAsync(service, () => services.Clock.Advance(TimeSpan.FromSeconds(30)));
            Assert.Equal(1, sync.QuietFetches);
        });
    }

    // ---------------------------------------------------------------- what a refresh does

    [Fact]
    public void ARefresh_SaysWhetherAReferenceMoved()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            ScriptedSync sync = services.Get<ScriptedSync>();
            FakeRefReader reader = (FakeRefReader)services.Get<IRefReader>();

            reader.Refs = new RefCollection([Branch("main", "aaa")], [], [], []);
            IRepositoryContext context = services.Get<IRepositoryContext>();
            await context.OpenAsync(Handle("auto"));
            sync.RefreshAfter = context;

            AutoRefreshResult quiet = await service.RefreshNowAsync();
            Assert.Equal(new AutoRefreshResult(QuietFetchResult.Fetched, false), quiet);

            // What the fetch brought: a branch that was not there.
            sync.OnFetch = () => reader.Refs = new RefCollection([Branch("main", "aaa"), Branch("new", "bbb")], [], [], []);

            AutoRefreshResult moved = await service.RefreshNowAsync();
            Assert.True(moved.Changed);
        });
    }

    [Fact]
    public void ABusyRepository_SkipsTheRefreshAndSaysNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            services.Get<ScriptedSync>().Result = QuietFetchResult.Skipped;

            await services.Get<IRepositoryContext>().OpenAsync(Handle("auto"));

            int raised = 0;
            service.Refreshed += (_, _) => raised++;

            Assert.Same(AutoRefreshResult.NotRun, await service.RefreshNowAsync());
            Assert.Equal(0, raised);
        });
    }

    [Fact]
    public void AFailedFetch_StillReadsTheLocalStateAndTellsNobody()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            FakeRefReader reader = (FakeRefReader)services.Get<IRefReader>();
            services.Get<ScriptedSync>().Result = QuietFetchResult.Failed;

            await services.Get<IRepositoryContext>().OpenAsync(Handle("auto"));
            int reads = reader.ReadCount;

            AutoRefreshResult result = await service.RefreshNowAsync();

            Assert.Equal(QuietFetchResult.Failed, result.Fetch);
            Assert.True(reader.ReadCount > reads);
            Assert.Empty(services.InfoBar.Shown);
            Assert.Equal(0, services.Overlay.ShowCount);
        });
    }

    [Fact]
    public void ASecondRefreshWhileOneIsRunning_DoesNotRun()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            ScriptedSync sync = services.Get<ScriptedSync>();

            await services.Get<IRepositoryContext>().OpenAsync(Handle("auto"));

            TaskCompletionSource release = new();
            sync.Gate = release.Task;

            Task<AutoRefreshResult> first = service.RefreshNowAsync();

            Assert.Same(AutoRefreshResult.NotRun, await service.RefreshNowAsync());

            release.SetResult();
            Assert.Equal(QuietFetchResult.Fetched, (await first.WaitAsync(Patience)).Fetch);
            Assert.Equal(1, sync.QuietFetches);
        });
    }

    // ---------------------------------------------------------------- a change seen on disk

    [Fact]
    public void ALocalRefresh_ReadsTheReferencesOnlyWhenTheyMayHaveMoved_AndFetchesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            ScriptedSync sync = services.Get<ScriptedSync>();
            FakeRefReader reader = (FakeRefReader)services.Get<IRefReader>();

            reader.Refs = new RefCollection([Branch("main", "aaa")], [], [], []);
            await services.Get<IRepositoryContext>().OpenAsync(Handle("auto"));
            int reads = reader.ReadCount;

            AutoRefreshResult workingTree = await service.RefreshLocallyAsync(RepositoryChanges.WorkingTree);

            Assert.Equal(QuietFetchResult.NotAttempted, workingTree.Fetch);
            Assert.Equal(RepositoryChanges.WorkingTree, workingTree.Changes);
            Assert.False(workingTree.Changed);
            Assert.Equal(reads, reader.ReadCount);

            // A commit made in a terminal: main moved.
            reader.Refs = new RefCollection([Branch("main", "bbb")], [], [], []);

            AutoRefreshResult references = await service.RefreshLocallyAsync(RepositoryChanges.References);

            Assert.Equal(RepositoryChanges.References, references.Changes);
            Assert.True(references.Changed);
            Assert.Equal(reads + 1, reader.ReadCount);
            Assert.Equal(0, sync.QuietFetches);
        });
    }

    [Fact]
    public void ALocalRefresh_IsPublishedWithWhatChanged()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();

            await services.Get<IRepositoryContext>().OpenAsync(Handle("auto"));

            AutoRefreshResult published = await NextRefreshAsync(
                service,
                () => _ = service.RefreshLocallyAsync(RepositoryChanges.Everything));

            Assert.Equal(RepositoryChanges.Everything, published.Changes);
            Assert.Equal(QuietFetchResult.NotAttempted, published.Fetch);
            Assert.False(published.Requested);
        });
    }

    [Fact]
    public void ALocalRefresh_WaitsForTheRunningOne_ThenRuns()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            ScriptedSync sync = services.Get<ScriptedSync>();

            await services.Get<IRepositoryContext>().OpenAsync(Handle("auto"));

            List<AutoRefreshResult> published = [];
            service.Refreshed += (_, result) => published.Add(result);

            TaskCompletionSource release = new();
            sync.Gate = release.Task;

            Task<AutoRefreshResult> periodic = service.RefreshNowAsync();
            Task<AutoRefreshResult> local = service.RefreshLocallyAsync(RepositoryChanges.References);

            Dispatcher.UIThread.RunJobs();
            Assert.False(local.IsCompleted);

            release.SetResult();

            Assert.Equal(QuietFetchResult.Fetched, (await periodic.WaitAsync(Patience)).Fetch);
            Assert.Equal(QuietFetchResult.NotAttempted, (await local.WaitAsync(Patience)).Fetch);
            Assert.Equal([QuietFetchResult.Fetched, QuietFetchResult.NotAttempted], published.Select(result => result.Fetch));
        });
    }

    [Fact]
    public void APeriodicRefresh_StillSkipsWhileALocalOneRuns()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            ScriptedSync sync = services.Get<ScriptedSync>();
            IRepositoryContext context = services.Get<IRepositoryContext>();

            await context.OpenAsync(Handle("auto"));

            // A tick that falls in the middle of the local refresh: the references being read is as
            // far inside it as anything can be.
            Task<AutoRefreshResult>? tick = null;
            context.StateRefreshed += (_, _) => tick ??= service.RefreshNowAsync();

            AutoRefreshResult local = await service.RefreshLocallyAsync(RepositoryChanges.References);

            Assert.Equal(RepositoryChanges.References, local.Changes);
            Assert.Same(AutoRefreshResult.NotRun, await tick!.WaitAsync(Patience));
            Assert.Equal(0, sync.QuietFetches);
        });
    }

    [Fact]
    public void ALocalRefresh_WithNoRepositoryOpen_DoesNotRun()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();

            Assert.Same(AutoRefreshResult.NotRun, await service.RefreshLocallyAsync(RepositoryChanges.Everything));
        });
    }

    [Fact]
    public void ALocalRefresh_CancelledWhileItWaits_PublishesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            ScriptedSync sync = services.Get<ScriptedSync>();

            await services.Get<IRepositoryContext>().OpenAsync(Handle("auto"));

            TaskCompletionSource release = new();
            sync.Gate = release.Task;

            Task<AutoRefreshResult> periodic = service.RefreshNowAsync();

            List<AutoRefreshResult> published = [];
            service.Refreshed += (_, result) => published.Add(result);

            using CancellationTokenSource stop = new();
            Task<AutoRefreshResult> local = service.RefreshLocallyAsync(RepositoryChanges.Everything, stop.Token);

            // The watcher's session ending — another repository opened — while the refresh waited.
            stop.Cancel();
            release.SetResult();
            await periodic.WaitAsync(Patience);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => local.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.DoesNotContain(published, result => result.Fetch == QuietFetchResult.NotAttempted);
        });
    }

    // ---------------------------------------------------------------- opening a repository

    /// <summary>
    /// A repository with no commit: something to discover and open, with no object git marks
    /// read-only — which a teardown on Windows could not delete.
    /// </summary>
    private static Task<RepositoryHandle> InitEmptyAsync(TestServices services, string name)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        return services.Get<IRepositoryService>().InitAsync(Path.Combine(root, name), "main");
    }

    [Fact]
    public void OpeningARepository_FetchesItOnce_WithoutWaitingForTheFetch()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            ScriptedSync sync = services.Get<ScriptedSync>();
            RepositoryHandle repository = await InitEmptyAsync(services, "opened");

            TaskCompletionSource release = new();
            sync.Gate = release.Task;

            RepositoryDiscoveryResult opened = await services.Get<IRepositoryOpener>().OpenAsync(repository.WorkTreePath);

            // Open, and the fetch under way — still waiting for the remotes when the opening is over.
            Assert.True(opened.IsFound);
            Assert.True(services.Get<IRepositoryContext>().IsRepositoryOpen);
            Assert.Equal(1, sync.QuietFetches);

            AutoRefreshResult result = await NextRefreshAsync(service, release.SetResult);

            Assert.Equal(QuietFetchResult.Fetched, result.Fetch);
            Assert.Equal(1, sync.QuietFetches);
            Assert.Empty(services.InfoBar.Shown);
            Assert.Equal(0, services.Overlay.ShowCount);
        });
    }

    [Fact]
    public void OpeningARepository_FetchesItEvenWithTheAutomaticRefreshOff()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            services.Get<ISettingsService>().Update(current => current with { AutoRefreshSeconds = 0 });
            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            ScriptedSync sync = services.Get<ScriptedSync>();
            RepositoryHandle repository = await InitEmptyAsync(services, "no-interval");

            await services.Get<IRepositoryOpener>().OpenAsync(repository.WorkTreePath);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, sync.QuietFetches);
            Assert.False(service.IsRunning);

            // And nothing more until the next opening: there is no interval to run at.
            services.Clock.Advance(TimeSpan.FromMinutes(5));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, sync.QuietFetches);
        });
    }

    [Fact]
    public void AFolderThatIsNoRepository_FetchesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            ScriptedSync sync = services.Get<ScriptedSync>();
            string folder = Path.Combine(services.ConfigurationRoot, "not-a-repository");
            Directory.CreateDirectory(folder);

            RepositoryDiscoveryResult opened = await services.Get<IRepositoryOpener>().OpenAsync(folder);

            Assert.False(opened.IsFound);
            Assert.Equal(0, sync.QuietFetches);
        });
    }

    // ---------------------------------------------------------------- the quiet fetch itself

    [Fact]
    public void TheQuietFetch_FailsWithoutAWord()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await InitAsync(services, "quiet");
            Git(repository, "remote", "add", "origin", Path.Combine(services.ConfigurationRoot, "no-such-remote"));

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            QuietFetchResult result = await services.Get<ISyncOperations>().FetchQuietlyAsync();

            Assert.Equal(QuietFetchResult.Failed, result);
            Assert.Empty(services.InfoBar.Shown);
            Assert.Equal(0, services.Overlay.ShowCount);
        });
    }

    [Fact]
    public void TheQuietFetch_NeverWaitsBehindTheReadersOwnWork()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await InitAsync(services, "busy");
            IRepositoryContext context = services.Get<IRepositoryContext>();
            await context.OpenAsync(repository);

            TaskCompletionSource release = new();
            Task holding = context.RunExclusiveAsync((_, _) => release.Task, refreshAfter: false);

            Assert.Equal(QuietFetchResult.Skipped, await services.Get<ISyncOperations>().FetchQuietlyAsync());

            release.SetResult();
            await holding.WaitAsync(Patience);

            Assert.Equal(QuietFetchResult.Fetched, await services.Get<ISyncOperations>().FetchQuietlyAsync());
        });
    }

    // ---------------------------------------------------------------- what the graph does with it

    [Fact]
    public void TheHistory_StaysExactlyAsItWasWhenNothingMoved()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await InitAsync(services, "still");
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            CommitRowViewModel[] before = [.. history.Rows];
            history.SelectedRow = before[0];

            await history.RefreshInPlaceAsync(referencesMoved: false);

            Assert.Equal(before, history.Rows);
            Assert.Same(before[0], history.SelectedRow);
        });
    }

    [Fact]
    public void TheHistory_RedrawsWhenSomethingMovedAndKeepsTheSelectedCommit()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await InitAsync(services, "moved");
            IRepositoryContext context = services.Get<IRepositoryContext>();
            await context.OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            string selected = history.Rows.Single(row => row.Subject == "Add the readme").Sha;
            history.SelectedRow = history.Rows.Single(row => row.Sha == selected);

            int replacing = 0;
            int replaced = 0;
            history.RowsReplacing += (_, _) => replacing++;
            history.RowsReplaced += (_, _) => replaced++;

            // Someone commits from a terminal.
            Commit(repository, "src/later.txt", "later\n", "Committed in a terminal");
            await context.RefreshAsync();

            await history.RefreshInPlaceAsync(referencesMoved: true);

            Assert.Contains(history.Rows, row => row.Subject == "Committed in a terminal");
            Assert.Equal(selected, history.SelectedRow?.Sha);
            Assert.Equal(1, replacing);
            Assert.Equal(1, replaced);
        });
    }

    [Fact]
    public void TheHistory_NoticesUncommittedWorkAppearing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await InitAsync(services, "dirty");
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();
            Assert.False(history.Rows[0].IsUncommitted);

            File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# changed\n");

            await history.RefreshInPlaceAsync(referencesMoved: false);

            Assert.True(history.Rows[0].IsUncommitted);
        });
    }

    [Fact]
    public void TheHistory_WaitsForTheDiffsToCloseBeforeRedrawing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await InitAsync(services, "reading");
            IRepositoryContext context = services.Get<IRepositoryContext>();
            await context.OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            CommitRowViewModel reading = history.Rows[0];
            history.RowCommands.ShowChanges.Execute(reading);
            Assert.True(history.IsDiffViewOpen);

            Commit(repository, "src/later.txt", "later\n", "Committed while reading");
            await context.RefreshAsync();

            await history.RefreshInPlaceAsync(referencesMoved: true);

            // The commit being read is still the one on screen.
            Assert.True(history.IsDiffViewOpen);
            Assert.Same(reading, history.SelectedRow);
            Assert.DoesNotContain(history.Rows, row => row.Subject == "Committed while reading");

            TaskCompletionSource redrawn = new(TaskCreationOptions.RunContinuationsAsynchronously);
            history.RowsReplaced += (_, _) => redrawn.TrySetResult();

            history.CloseDiffViewCommand.Execute(null);
            await redrawn.Task.WaitAsync(Patience);

            Assert.Contains(history.Rows, row => row.Subject == "Committed while reading");
            Assert.Equal(reading.Sha, history.SelectedRow?.Sha);
        });
    }

    [Fact]
    public void TheRepositoryWindow_HandsEachRefreshToTheHistory()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(
                useRealRefReader: true,
                configure: collection =>
                {
                    collection.RemoveAll<ISyncOperations>();
                    collection.AddSingleton<ScriptedSync>();
                    collection.AddSingleton<ISyncOperations>(provider => provider.GetRequiredService<ScriptedSync>());
                });

            RepositoryHandle repository = await InitAsync(services, "wired");
            IRepositoryContext context = services.Get<IRepositoryContext>();

            // The repository window's ViewModel is what connects the two.
            _ = services.Get<ViewModels.MainWindowViewModel>();
            await context.OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            TaskCompletionSource redrawn = new(TaskCreationOptions.RunContinuationsAsynchronously);
            history.RowsReplaced += (_, _) => redrawn.TrySetResult();

            services.Get<ScriptedSync>().OnFetch = () => Commit(repository, "src/fetched.txt", "x\n", "As if fetched");
            services.Get<ScriptedSync>().RefreshAfter = context;

            await services.Get<IAutoRefreshService>().RefreshNowAsync();
            await redrawn.Task.WaitAsync(Patience);

            Assert.Contains(history.Rows, row => row.Subject == "As if fetched");
        });
    }

    // ---------------------------------------------------------------- what moved between two refreshes

    private static readonly RefBadgeItem OriginMain = new(GitRefKind.RemoteBranch, "origin/main", false);

    // Once pushed, main and origin/main are on one commit, and drawn as one badge: main's, carrying
    // its upstream.
    private static readonly RefBadgeItem MainWithOriginMain = new(GitRefKind.LocalBranch, "main", true, "origin/main");

    [Fact]
    public void TheHistory_RedrawsARemoteBranchAPushMovedSinceItWasDrawn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await InitBehindItsRemoteAsync(services, "pushed");
            IRepositoryContext context = services.Get<IRepositoryContext>();
            await context.OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();
            Assert.Contains(OriginMain, Row(history, "Add the readme").Refs);

            int replaced = 0;
            history.RowsReplaced += (_, _) => replaced++;

            // What a push leaves behind: the remote-tracking branch moved, and the context re-read
            // after it. The refresh that follows sees nothing move during its own fetch.
            Git(repository, "push", "origin", "main");
            await context.RefreshAsync();

            await history.RefreshInPlaceAsync(referencesMoved: false);

            Assert.Equal(1, replaced);
            Assert.Contains(MainWithOriginMain, Row(history, "Pushed later").Refs);
            Assert.DoesNotContain(OriginMain, Row(history, "Add the readme").Refs);

            // Drawn from where the references are now: the next refresh has nothing to redraw.
            CommitRowViewModel[] drawn = [.. history.Rows];

            await history.RefreshInPlaceAsync(referencesMoved: false);

            Assert.Equal(drawn, history.Rows);
            Assert.Equal(1, replaced);
        });
    }

    [Fact]
    public void TheHistory_RedrawsATagFetchedSinceItWasDrawn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await InitBehindItsRemoteAsync(services, "tagged");
            IRepositoryContext context = services.Get<IRepositoryContext>();
            await context.OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            // Someone tags the remote, and a fetch brings the tag.
            Git(RemoteOf(services, "tagged"), "tag", "v1.0", "main");
            Git(repository, "fetch", "--tags", "origin");
            await context.RefreshAsync();

            await history.RefreshInPlaceAsync(referencesMoved: false);

            Assert.Contains(new RefBadgeItem(GitRefKind.Tag, "v1.0", false), Row(history, "Add the readme").Refs);
        });
    }

    [Fact]
    public void TheNextRefresh_RedrawsAPushMadeBetweenTwoRefreshes()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildWindow();
            RepositoryHandle repository = await InitBehindItsRemoteAsync(services, "between");
            IRepositoryContext context = services.Get<IRepositoryContext>();

            _ = services.Get<ViewModels.MainWindowViewModel>();
            await context.OpenAsync(repository);
            services.Get<ScriptedSync>().RefreshAfter = context;

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            TaskCompletionSource redrawn = new(TaskCreationOptions.RunContinuationsAsynchronously);
            history.RowsReplaced += (_, _) => redrawn.TrySetResult();

            // Pushed — and re-read — between two ticks.
            Git(repository, "push", "origin", "main");
            await context.RefreshAsync();

            AutoRefreshResult tick = await services.Get<IAutoRefreshService>().RefreshNowAsync();
            await redrawn.Task.WaitAsync(Patience);

            Assert.False(tick.Changed);
            Assert.Contains(MainWithOriginMain, Row(history, "Pushed later").Refs);
        });
    }

    [Fact]
    public void TheToolbarPush_RedrawsTheHistoryAtOnce()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildWindow();
            RepositoryHandle repository = await InitBehindItsRemoteAsync(services, "toolbar");
            IRepositoryContext context = services.Get<IRepositoryContext>();

            ViewModels.MainWindowViewModel shell = services.Get<ViewModels.MainWindowViewModel>();
            await context.OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            string selected = Row(history, "Add the readme").Sha;
            history.SelectedRow = Row(history, "Add the readme");

            // As the real push does: push, then re-read the references.
            services.Get<ScriptedSync>().OnPush = async () =>
            {
                Git(repository, "push", "origin", "main");
                await context.RefreshAsync();
                return true;
            };

            await shell.PushCommand.ExecuteAsync(null);

            Assert.Contains(MainWithOriginMain, Row(history, "Pushed later").Refs);
            Assert.Equal(selected, history.SelectedRow?.Sha);
            Assert.Equal(0, services.Get<ScriptedSync>().QuietFetches);
            Assert.False(shell.IsBusy);
        });
    }

    // ---------------------------------------------------------------- the one refresh button

    /// <summary>
    /// The scripted fetch over the real reference reader, with the repository window's ViewModel
    /// resolved — it is what connects the refresh to the history.
    /// </summary>
    private static TestServices BuildWindow(Action<ServiceCollection>? configure = null)
        => TestServices.Build(
            useRealRefReader: true,
            configure: collection =>
            {
                collection.RemoveAll<ISyncOperations>();
                collection.AddSingleton<ScriptedSync>();
                collection.AddSingleton<ISyncOperations>(provider => provider.GetRequiredService<ScriptedSync>());
                configure?.Invoke(collection);
            });

    [Fact]
    public void ARequestedRefresh_RunsTheSameFetchAndSaysItWasAskedFor()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            ScriptedSync sync = services.Get<ScriptedSync>();
            FakeRefReader reader = (FakeRefReader)services.Get<IRefReader>();

            IRepositoryContext context = services.Get<IRepositoryContext>();
            await context.OpenAsync(Handle("asked"));
            sync.RefreshAfter = context;
            int reads = reader.ReadCount;

            AutoRefreshResult requested = await NextRefreshAsync(service, () => _ = service.RequestRefreshAsync());

            Assert.Equal(new AutoRefreshResult(QuietFetchResult.Fetched, false, Requested: true), requested);
            Assert.Equal(1, sync.QuietFetches);
            Assert.True(reader.ReadCount > reads);

            // The periodic one is not the reader asking.
            Assert.False((await service.RefreshNowAsync()).Requested);
        });
    }

    [Fact]
    public void ARequestWhileARefreshIsRunning_RunsNoSecondOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildScripted();
            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            ScriptedSync sync = services.Get<ScriptedSync>();

            await services.Get<IRepositoryContext>().OpenAsync(Handle("asked"));

            TaskCompletionSource release = new();
            sync.Gate = release.Task;

            Task<AutoRefreshResult> periodic = service.RefreshNowAsync();

            Assert.Same(AutoRefreshResult.NotRun, await service.RequestRefreshAsync());

            release.SetResult();
            await periodic.WaitAsync(Patience);
            Assert.Equal(1, sync.QuietFetches);
        });
    }

    [Fact]
    public void TheRefreshButton_FetchesAndRedrawsTheHistoryEvenWhenNothingMoved()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildWindow();
            RepositoryHandle repository = await InitAsync(services, "button");
            IRepositoryContext context = services.Get<IRepositoryContext>();

            ViewModels.MainWindowViewModel shell = services.Get<ViewModels.MainWindowViewModel>();
            await context.OpenAsync(repository);
            services.Get<ScriptedSync>().RefreshAfter = context;

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            string selected = history.Rows[0].Sha;
            history.SelectedRow = history.Rows[0];

            TaskCompletionSource redrawn = new(TaskCreationOptions.RunContinuationsAsynchronously);
            history.RowsReplaced += (_, _) => redrawn.TrySetResult();

            await shell.RefreshCommand.ExecuteAsync(null);
            await redrawn.Task.WaitAsync(Patience);

            // Nothing moved, and the reader asked anyway: the history is read again, in place.
            Assert.Equal(1, services.Get<ScriptedSync>().QuietFetches);
            Assert.Equal(selected, history.SelectedRow?.Sha);
            Assert.False(shell.IsBusy);
        });
    }

    [Fact]
    public void TheRefreshButton_PicksUpACommitMadeInATerminal()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildWindow();
            RepositoryHandle repository = await InitAsync(services, "terminal");
            IRepositoryContext context = services.Get<IRepositoryContext>();

            ViewModels.MainWindowViewModel shell = services.Get<ViewModels.MainWindowViewModel>();
            await context.OpenAsync(repository);

            // Offline: the fetch fails, and what changed on disk is still read.
            services.Get<ScriptedSync>().Result = QuietFetchResult.Failed;

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            TaskCompletionSource redrawn = new(TaskCreationOptions.RunContinuationsAsynchronously);
            history.RowsReplaced += (_, _) => redrawn.TrySetResult();

            Commit(repository, "src/later.txt", "later\n", "Committed in a terminal");

            await shell.RefreshCommand.ExecuteAsync(null);
            await redrawn.Task.WaitAsync(Patience);

            Assert.Contains(history.Rows, row => row.Subject == "Committed in a terminal");
            Assert.Empty(services.InfoBar.Shown);
        });
    }

    [Fact]
    public void NoRefreshEverAsksAHostForItsRepositories()
    {
        _fixture.RunAsync(async () =>
        {
            FakeHostProvider provider = new();

            using TestServices services = BuildScripted(collection =>
            {
                collection.RemoveAll<IRepositoryHostProvider>();
                collection.AddSingleton<IRepositoryHostProvider>(provider);
            });

            IAutoRefreshService service = services.Get<IAutoRefreshService>();
            await services.Get<IIdentityProfileStore>().SaveAsync(
                IdentityProfile.Create("Work", new GitIdentity("Ada Lovelace", "ada@work.example")));

            ProfilesPageViewModel page = services.Get<ProfilesPageViewModel>();
            await page.OnAppearingAsync();
            await page.ConnectAsync(page.Profiles[0], new AddHostAccountDialogViewModel(services.Get<IHostProviderRegistry>().Providers)
            {
                InstanceUrl = "https://github.com",
                Token = "ghp_token",
            });

            await services.Get<IRepositoryContext>().OpenAsync(Handle("hosted"));
            await WaitUntilAsync(() => page.IsNotBusy);

            // The repositories are listed in their dialog, when it opens, and nowhere else: calling a
            // hosting API on every refresh would spend the account's rate limit for nothing.
            await service.RefreshNowAsync();
            await service.RequestRefreshAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.Single(page.Profiles[0].Integrations);
            Assert.Empty(provider.Queries);
        });
    }

    /// <summary>
    /// Pumps the dispatcher until the condition holds.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        Stopwatch watch = Stopwatch.StartNew();

        while (!condition())
        {
            Assert.True(watch.Elapsed < Patience, "the condition never held");

            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<RepositoryHandle> InitAsync(TestServices services, string name)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, name), "main");
        Commit(repository, "README.md", "# one\n", "Add the readme");

        return repository;
    }

    /// <summary>
    /// A repository with a bare remote, <c>origin</c>, that has "Add the readme" but not "Pushed later":
    /// <c>origin/main</c> is one commit behind <c>main</c>.
    /// </summary>
    private static async Task<RepositoryHandle> InitBehindItsRemoteAsync(TestServices services, string name)
    {
        RepositoryHandle repository = await InitAsync(services, name);

        string remote = RemoteOf(services, name);
        Directory.CreateDirectory(remote);
        Git(remote, "init", "--bare");

        Git(repository, "remote", "add", "origin", remote);
        Git(repository, "push", "--set-upstream", "origin", "main");
        Commit(repository, "src/later.txt", "later\n", "Pushed later");

        return repository;
    }

    private static string RemoteOf(TestServices services, string name)
        => Path.Combine(services.ConfigurationRoot, "remotes", name + ".git");

    private static CommitRowViewModel Row(HistoryPageViewModel history, string subject)
        => history.Rows.Single(row => row.Subject == subject);

    private static void Commit(RepositoryHandle repository, string path, string content, string message)
    {
        string full = Path.Combine(repository.WorkTreePath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);

        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", message);
    }

    private static void Git(RepositoryHandle repository, params string[] arguments)
        => Git(repository.WorkTreePath, arguments);

    private static void Git(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo start = new()
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.Environment["GIT_AUTHOR_NAME"] = "Ada Lovelace";
        start.Environment["GIT_AUTHOR_EMAIL"] = "ada@example.com";
        start.Environment["GIT_COMMITTER_NAME"] = "Ada Lovelace";
        start.Environment["GIT_COMMITTER_EMAIL"] = "ada@example.com";

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }
    }

    /// <summary>
    /// An <see cref="ISyncOperations"/> whose quiet fetch answers what the test says, and whose other
    /// operations nobody here calls.
    /// </summary>
    private sealed class ScriptedSync : ISyncOperations
    {
        public QuietFetchResult Result { get; set; } = QuietFetchResult.Fetched;

        public int QuietFetches { get; private set; }

        public Action? OnFetch { get; set; }

        public Task? Gate { get; set; }

        /// <summary>Gets or sets a context to refresh after the fetch, as the real one does.</summary>
        public IRepositoryContext? RefreshAfter { get; set; }

        /// <summary>Gets or sets what the toolbar's push does; unset, nobody may push.</summary>
        public Func<Task<bool>>? OnPush { get; set; }

        public async Task<QuietFetchResult> FetchQuietlyAsync(CancellationToken cancellationToken = default)
        {
            QuietFetches++;

            if (Gate is { } gate)
            {
                await gate;
            }

            OnFetch?.Invoke();

            if (RefreshAfter is { } context && Result == QuietFetchResult.Fetched)
            {
                await context.RefreshAsync(cancellationToken);
            }

            return Result;
        }

        public Task<bool> FetchAsync() => throw new NotSupportedException();

        public Task<bool> PullAsync() => throw new NotSupportedException();

        public Task<bool> PushAsync(bool setUpstream = false) => OnPush?.Invoke() ?? throw new NotSupportedException();

        public Task<bool> PullBranchAsync(string branch) => throw new NotSupportedException();

        public Task<bool> PushBranchAsync(string branch) => throw new NotSupportedException();

        public Task<bool> PushTagAsync(string tag) => throw new NotSupportedException();
    }
}
