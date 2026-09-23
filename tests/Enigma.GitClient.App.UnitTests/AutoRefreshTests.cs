using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

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
    private static TestServices BuildScripted()
        => TestServices.Build(configure: services =>
        {
            services.RemoveAll<ISyncOperations>();
            services.AddSingleton<ScriptedSync>();
            services.AddSingleton<ISyncOperations>(provider => provider.GetRequiredService<ScriptedSync>());
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

    // ---------------------------------------------------------------- helpers

    private static async Task<RepositoryHandle> InitAsync(TestServices services, string name)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, name), "main");
        Commit(repository, "README.md", "# one\n", "Add the readme");

        return repository;
    }

    private static void Commit(RepositoryHandle repository, string path, string content, string message)
    {
        string full = Path.Combine(repository.WorkTreePath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);

        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", message);
    }

    private static void Git(RepositoryHandle repository, params string[] arguments)
    {
        ProcessStartInfo start = new()
        {
            FileName = "git",
            WorkingDirectory = repository.WorkTreePath,
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

        public Task<bool> PushAsync(bool setUpstream = false) => throw new NotSupportedException();

        public Task<bool> PullBranchAsync(string branch) => throw new NotSupportedException();

        public Task<bool> PushBranchAsync(string branch) => throw new NotSupportedException();
    }
}
