using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The file-system watcher's decisions — when a change becomes a refresh, which changes are the
/// application's own, what a failing watch falls back on — on a clock that only moves when the test
/// moves it, with the operating system's side scripted.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RepositoryWatcherTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly HeadlessAvaloniaFixture _fixture;

    public RepositoryWatcherTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static TimeSpan Milliseconds(int value) => TimeSpan.FromMilliseconds(value);

    // ---------------------------------------------------------------- when a change becomes a refresh

    [Fact]
    public void NothingRunsBeforeAQuietPeriod_ThenOneRefreshCarriesTheChange()
    {
        _fixture.RunAsync(async () =>
        {
            using Harness harness = Harness.Create();
            await harness.OpenAsync("watched");

            harness.Report(RepositoryChanges.WorkingTree);

            harness.Advance(Milliseconds(299));
            Assert.Empty(harness.Refresh.Calls);

            harness.Advance(Milliseconds(1));
            Assert.Equal([RepositoryChanges.WorkingTree], harness.Refresh.Changes);
            Assert.Equal([Milliseconds(300)], harness.Refresh.Times);
        });
    }

    [Fact]
    public void EveryChangeRestartsTheQuietPeriod()
    {
        _fixture.RunAsync(async () =>
        {
            using Harness harness = Harness.Create();
            await harness.OpenAsync("watched");

            harness.Report(RepositoryChanges.WorkingTree);
            harness.Advance(Milliseconds(200));
            harness.Report(RepositoryChanges.WorkingTree);
            harness.Advance(Milliseconds(200));

            Assert.Empty(harness.Refresh.Calls);

            harness.Advance(Milliseconds(100));
            Assert.Equal([Milliseconds(500)], harness.Refresh.Times);
        });
    }

    [Fact]
    public void ABurst_IsOneRefresh_CarryingEverythingItSaw()
    {
        _fixture.RunAsync(async () =>
        {
            using Harness harness = Harness.Create();
            await harness.OpenAsync("watched");

            for (int index = 0; index < 1_000; index++)
            {
                harness.Watch.Sink.OnChanged(index % 10 == 0 ? RepositoryChanges.References : RepositoryChanges.WorkingTree);
            }

            harness.Settle();
            harness.AdvanceInSteps(TimeSpan.FromSeconds(5), Milliseconds(50));

            Assert.Equal([RepositoryChanges.Everything], harness.Refresh.Changes);
        });
    }

    [Fact]
    public void ChangesThatNeverStop_RefreshEveryTwoSeconds_AndOnceMoreWhenTheyDo()
    {
        _fixture.RunAsync(async () =>
        {
            using Harness harness = Harness.Create();
            await harness.OpenAsync("watched");

            // A build writing something every 100 ms for five seconds.
            for (int tick = 0; tick < 50; tick++)
            {
                harness.Report(RepositoryChanges.WorkingTree);
                harness.Advance(Milliseconds(100));
            }

            harness.AdvanceInSteps(TimeSpan.FromSeconds(2), Milliseconds(100));

            Assert.Equal(
                [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), Milliseconds(5_200)],
                harness.Refresh.Times);
        });
    }

    [Fact]
    public void ChangesDuringARefresh_MakeExactlyOneMore_AfterIt()
    {
        _fixture.RunAsync(async () =>
        {
            using Harness harness = Harness.Create();
            await harness.OpenAsync("watched");

            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Refresh.Gate = release.Task;

            harness.Report(RepositoryChanges.WorkingTree);
            harness.Advance(Milliseconds(300));
            Assert.Single(harness.Refresh.Calls);

            // A commit made while the first refresh is still reading.
            harness.Report(RepositoryChanges.References);
            harness.Report(RepositoryChanges.WorkingTree);
            harness.Report(RepositoryChanges.References);
            harness.AdvanceInSteps(TimeSpan.FromSeconds(3), Milliseconds(100));
            Assert.Single(harness.Refresh.Calls);

            release.SetResult();
            await harness.UntilAsync(() => harness.Refresh.Calls.Count == 2);

            harness.AdvanceInSteps(TimeSpan.FromSeconds(5), Milliseconds(100));

            Assert.Equal([RepositoryChanges.WorkingTree, RepositoryChanges.Everything], harness.Refresh.Changes);
        });
    }

    // ---------------------------------------------------------------- the application's own writes

    [Fact]
    public void TheApplicationsOwnWrite_IsNotAChange_NorWhatFollowsItClosely()
    {
        _fixture.RunAsync(async () =>
        {
            using Harness harness = Harness.Create();
            await harness.OpenAsync("watched");

            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task write = harness.Context.RunExclusiveAsync((_, _) => release.Task, refreshAfter: false);

            // A checkout run by the application: its own refresh follows it.
            harness.Report(RepositoryChanges.Everything);
            harness.Advance(Milliseconds(400));

            release.SetResult();
            await write;

            // Its last events, delivered after it ended.
            harness.Report(RepositoryChanges.WorkingTree);
            harness.Advance(Milliseconds(499));
            harness.Report(RepositoryChanges.References);
            harness.AdvanceInSteps(TimeSpan.FromSeconds(1), Milliseconds(100));

            Assert.Empty(harness.Refresh.Calls);

            // Past the grace period, a change is somebody else's again.
            harness.Report(RepositoryChanges.WorkingTree);
            harness.Advance(Milliseconds(300));

            Assert.Equal([RepositoryChanges.WorkingTree], harness.Refresh.Changes);
        });
    }

    // ---------------------------------------------------------------- when the watch goes wrong

    [Fact]
    public void AnOverflow_RefreshesEverything_AndTheWatchGoesOn()
    {
        _fixture.RunAsync(async () =>
        {
            using Harness harness = Harness.Create();
            await harness.OpenAsync("watched");

            harness.Watch.Sink.OnFaulted(new InternalBufferOverflowException("Too many changes at once."), eventsLost: true);
            harness.Settle();
            harness.Advance(Milliseconds(300));

            Assert.Equal([RepositoryChanges.Everything], harness.Refresh.Changes);
            Assert.True(harness.Watcher.IsWatching);
            Assert.False(harness.Watch.IsDisposed);

            harness.Report(RepositoryChanges.WorkingTree);
            harness.Advance(Milliseconds(300));
            Assert.Equal(2, harness.Refresh.Calls.Count);
        });
    }

    [Fact]
    public void AFailedWatch_RefreshesEverythingOnce_AndLeavesTheRestToThePeriodicRefresh()
    {
        _fixture.RunAsync(async () =>
        {
            using Harness harness = Harness.Create();
            await harness.OpenAsync("watched");
            ScriptedWatch watch = harness.Watch;

            watch.Sink.OnFaulted(
                new IOException("The configured user limit (8192) on the number of inotify watches has been reached."),
                eventsLost: false);

            await harness.UntilAsync(() => watch.IsDisposed);

            Assert.Equal([RepositoryChanges.Everything], harness.Refresh.Changes);
            Assert.False(harness.Watcher.IsWatching);

            // Whatever the failed watch still says is not listened to.
            watch.Sink.OnChanged(RepositoryChanges.WorkingTree);
            harness.AdvanceInSteps(TimeSpan.FromSeconds(3), Milliseconds(100));

            Assert.Single(harness.Refresh.Calls);
            Assert.Single(harness.Logger.Entries, entry => entry.Level == LogLevel.Warning);
        });
    }

    [Fact]
    public void AWatchTheSystemRefuses_CrashesNothing_RefreshesNothing_AndWarnsOnce()
    {
        _fixture.RunAsync(async () =>
        {
            using Harness harness = Harness.Create();
            harness.Source.Failure = new IOException("The configured user limit (128) on the number of inotify instances has been reached.");

            await harness.Context.OpenAsync(Handle("first"));
            await harness.UntilAsync(() => harness.Source.Attempts == 1 && harness.Logger.Entries.Count > 0);

            await harness.Context.OpenAsync(Handle("second"));
            await harness.UntilAsync(() => harness.Source.Attempts == 2 && harness.Logger.Entries.Count > 1);

            Assert.False(harness.Watcher.IsWatching);
            Assert.Empty(harness.Refresh.Calls);
            Assert.Single(harness.Logger.Entries, entry => entry.Level == LogLevel.Warning);
            Assert.Contains("max_user_watches", harness.Logger.Entries[0].Message, StringComparison.Ordinal);
            Assert.Equal(LogLevel.Debug, harness.Logger.Entries[1].Level);
        });
    }

    // ---------------------------------------------------------------- following the setting and the repository

    [Fact]
    public void TheSetting_IsFollowedLive()
    {
        _fixture.RunAsync(async () =>
        {
            using Harness harness = Harness.Create();
            ISettingsService settings = harness.Services.Get<ISettingsService>();
            await harness.OpenAsync("watched");
            ScriptedWatch first = harness.Watch;

            settings.Update(current => current with { WatchFileSystem = false });

            Assert.False(harness.Watcher.IsWatching);
            Assert.True(first.IsDisposed);

            // Off: a change nobody reported in time is the periodic refresh's.
            first.Sink.OnChanged(RepositoryChanges.WorkingTree);
            harness.AdvanceInSteps(TimeSpan.FromSeconds(1), Milliseconds(100));
            Assert.Empty(harness.Refresh.Calls);

            settings.Update(current => current with { WatchFileSystem = true });
            await harness.UntilAsync(() => harness.Watcher.IsWatching);

            Assert.Equal(2, harness.Source.Attempts);
            Assert.NotSame(first, harness.Watch);
        });
    }

    [Fact]
    public void NothingIsWatched_WhileTheSettingIsOff()
    {
        _fixture.RunAsync(async () =>
        {
            using Harness harness = Harness.Create(watch: false);

            await harness.Context.OpenAsync(Handle("watched"));
            await Task.Delay(50);
            harness.Settle();

            Assert.Equal(0, harness.Source.Attempts);
            Assert.False(harness.Watcher.IsWatching);
        });
    }

    [Fact]
    public void ARepositorySwitchedMidBurst_RefreshesNothingForTheOldOne()
    {
        _fixture.RunAsync(async () =>
        {
            using Harness harness = Harness.Create();
            await harness.OpenAsync("first");
            ScriptedWatch first = harness.Watch;

            harness.Report(RepositoryChanges.Everything);
            harness.Advance(Milliseconds(200));

            await harness.OpenAsync("second");

            Assert.True(first.IsDisposed);
            Assert.Equal("second", harness.Watch.Repository.Name);

            // The old watch's last words, and the burst it was waiting to report.
            first.Sink.OnChanged(RepositoryChanges.WorkingTree);
            harness.AdvanceInSteps(TimeSpan.FromSeconds(3), Milliseconds(100));

            Assert.Empty(harness.Refresh.Calls);
        });
    }

    [Fact]
    public void ClosingTheRepository_OrDisposingTheWatcher_LetsGoOfTheWatch()
    {
        _fixture.RunAsync(async () =>
        {
            using Harness harness = Harness.Create();
            await harness.OpenAsync("first");
            ScriptedWatch first = harness.Watch;

            harness.Context.Close();

            Assert.True(first.IsDisposed);
            Assert.False(harness.Watcher.IsWatching);

            await harness.OpenAsync("second");
            ScriptedWatch second = harness.Watch;

            harness.Watcher.Dispose();

            Assert.True(second.IsDisposed);
            Assert.False(harness.Watcher.IsWatching);
        });
    }

    // ---------------------------------------------------------------- plumbing

    private static RepositoryHandle Handle(string name)
        => OperatingSystem.IsWindows()
            ? new RepositoryHandle($@"C:\src\{name}", $@"C:\src\{name}\.git")
            : new RepositoryHandle($"/src/{name}", $"/src/{name}/.git");

    /// <summary>
    /// A watcher over the real repository context and settings, with the operating system and the
    /// refresh it would run scripted.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        private Harness(TestServices services)
        {
            Services = services;
            Refresh = new ScriptedLocalRefresh(services.Clock);
            Watcher = new RepositoryWatcher(
                services.Get<IRepositoryContext>(),
                Refresh,
                services.Get<ISettingsService>(),
                Source,
                services.Clock,
                Logger);
        }

        public TestServices Services { get; }

        public ScriptedRepositoryEventSource Source { get; } = new();

        public ScriptedLocalRefresh Refresh { get; }

        public RecordingLogger<RepositoryWatcher> Logger { get; } = new();

        public RepositoryWatcher Watcher { get; }

        public IRepositoryContext Context => Services.Get<IRepositoryContext>();

        /// <summary>Gets the latest watch the watcher started.</summary>
        public ScriptedWatch Watch => Source.Current;

        public static Harness Create(bool watch = true)
        {
            TestServices services = TestServices.Build();
            services.Get<ISettingsService>().Update(current => current with { WatchFileSystem = watch });

            return new Harness(services);
        }

        /// <summary>
        /// Opens a repository and waits for its watch to start, which happens on the thread pool; the
        /// times the refresh records are counted from here.
        /// </summary>
        public async Task OpenAsync(string name)
        {
            int before = Source.Attempts;

            await Context.OpenAsync(Handle(name));
            await UntilAsync(() => Source.Attempts > before && Watcher.IsWatching);

            Refresh.Start = Services.Clock.GetUtcNow();
        }

        /// <summary>Reports a change through the latest watch, as the operating system would.</summary>
        public void Report(RepositoryChanges changes)
        {
            Watch.Sink.OnChanged(changes);
            Settle();
        }

        /// <summary>Moves the clock on and runs whatever that set going.</summary>
        public void Advance(TimeSpan by)
        {
            Services.Clock.Advance(by);
            Settle();
        }

        /// <summary>Moves the clock on a step at a time, as real time would pass.</summary>
        public void AdvanceInSteps(TimeSpan total, TimeSpan step)
        {
            for (TimeSpan passed = TimeSpan.Zero; passed < total; passed += step)
            {
                Advance(step);
            }
        }

        public void Settle() => Dispatcher.UIThread.RunJobs();

        /// <summary>Pumps the dispatcher until something holds, in real time.</summary>
        public async Task UntilAsync(Func<bool> condition)
        {
            Stopwatch waited = Stopwatch.StartNew();

            while (!condition())
            {
                if (waited.Elapsed > Patience)
                {
                    throw new TimeoutException("The watcher never got there.");
                }

                await Task.Delay(5);
                Settle();
            }
        }

        public void Dispose()
        {
            Watcher.Dispose();
            Services.Dispose();
        }
    }

    /// <summary>
    /// Stands in for the automatic refresh: records every refresh the watcher asks for, and when, and
    /// can hold one open.
    /// </summary>
    private sealed class ScriptedLocalRefresh(ManualTimeProvider clock) : IAutoRefreshService
    {
        public List<(RepositoryChanges Changes, TimeSpan At)> Calls { get; } = [];

        public IReadOnlyList<RepositoryChanges> Changes => [.. Calls.Select(call => call.Changes)];

        public IReadOnlyList<TimeSpan> Times => [.. Calls.Select(call => call.At)];

        public DateTimeOffset Start { get; set; }

        /// <summary>Gets or sets what a refresh waits for before it completes.</summary>
        public Task? Gate { get; set; }

        public bool IsRunning => false;

        public event EventHandler<AutoRefreshResult>? Refreshed
        {
            add { }
            remove { }
        }

        public Task<AutoRefreshResult> RefreshNowAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(AutoRefreshResult.NotRun);

        public Task<AutoRefreshResult> RequestRefreshAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(AutoRefreshResult.NotRun);

        public async Task<AutoRefreshResult> RefreshLocallyAsync(RepositoryChanges changes, CancellationToken cancellationToken = default)
        {
            Calls.Add((changes, clock.GetUtcNow() - Start));

            if (Gate is { } gate)
            {
                await gate.WaitAsync(cancellationToken);
            }

            return new AutoRefreshResult(QuietFetchResult.NotAttempted, false) { Changes = changes };
        }
    }
}
