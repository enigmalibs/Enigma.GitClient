using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Status;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The watcher end to end, on a real repository: a change made outside the application shows without
/// the automatic refresh ever ticking. Only the quiet period passes on the test's clock — the periodic
/// refresh would need fifteen seconds of it.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RepositoryWatcherIntegrationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly HeadlessAvaloniaFixture _fixture;

    public RepositoryWatcherIntegrationTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void AFileWrittenOutsideTheApp_ShowsInTheWorkingTreePanel_WithoutATimerTick()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await InitWithACommitAsync(services, "panel");
            await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "README.md"), "# watched\n\nedited\n");

            (HistoryPageViewModel history, IRepositoryWatcher watcher, List<AutoRefreshResult> refreshes) = await OpenAsync(services, repository);

            history.SelectedRow = history.Rows.First(row => row.IsUncommitted);
            await UntilAsync(() => history.WorkingTree.Unstaged.FileCount == 1);

            await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "new.txt"), "written in an editor\n");
            TimeSpan waited = await AdvanceUntilAsync(services, () => history.WorkingTree.Unstaged.FileCount == 2);

            Assert.True(watcher.IsWatching);
            Assert.True(waited < TimeSpan.FromSeconds(5), $"The clock moved {waited}.");
            Assert.Contains(refreshes, result => result.Changes == RepositoryChanges.WorkingTree);
            Assert.All(refreshes, result => Assert.Equal(QuietFetchResult.NotAttempted, result.Fetch));
        });
    }

    [Fact]
    public void AFileWrittenInACleanRepository_BringsTheUncommittedLine()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await InitWithACommitAsync(services, "clean");

            (HistoryPageViewModel history, _, List<AutoRefreshResult> refreshes) = await OpenAsync(services, repository);
            Assert.DoesNotContain(history.Rows, row => row.IsUncommitted);

            await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "notes.txt"), "a thought\n");
            await AdvanceUntilAsync(services, () => history.Rows.FirstOrDefault()?.IsUncommitted == true && !history.IsBusy);

            Assert.All(refreshes, result => Assert.Equal(QuietFetchResult.NotAttempted, result.Fetch));
        });
    }

    [Fact]
    public void ACommitMadeInATerminal_ShowsInTheHistory()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await InitWithACommitAsync(services, "terminal");

            (HistoryPageViewModel history, _, List<AutoRefreshResult> refreshes) = await OpenAsync(services, repository);
            Assert.DoesNotContain(history.Rows, row => row.Subject == "Made in a terminal");

            Git(repository, "commit", "--allow-empty", "-m", "Made in a terminal");
            await AdvanceUntilAsync(services, () => history.Rows.Any(row => row.Subject == "Made in a terminal") && !history.IsBusy);

            Assert.Contains(refreshes, result => result.Changes.HasFlag(RepositoryChanges.References) && result.Changed);
            Assert.All(refreshes, result => Assert.Equal(QuietFetchResult.NotAttempted, result.Fetch));
        });
    }

    [Fact]
    public void TheWorkingTree_IsReadOneAtATime_AndChangesDuringAReadingMakeOneMore()
    {
        _fixture.RunAsync(async () =>
        {
            HeldProbe probe = null!;
            using TestServices services = TestServices.Build(useRealRefReader: true, configure: collection =>
            {
                ServiceDescriptor real = collection.Single(descriptor => descriptor.ServiceType == typeof(IWorkingTreeProbe));
                collection.Remove(real);
                collection.AddSingleton<IWorkingTreeProbe>(provider =>
                    probe = new HeldProbe((IWorkingTreeProbe)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!)));
            });

            RepositoryHandle repository = await InitWithACommitAsync(services, "one-at-a-time");
            (HistoryPageViewModel history, _, _) = await OpenAsync(services, repository);
            int before = probe.Calls;

            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            probe.Hold = release.Task;

            Task reading = history.RefreshWorkingTreeAsync();

            // A build still writing: two more changes while git status runs.
            Task second = history.RefreshWorkingTreeAsync();
            Task third = history.RefreshWorkingTreeAsync();

            Assert.True(second.IsCompleted);
            Assert.True(third.IsCompleted);
            Assert.Equal(before + 1, probe.Calls);

            probe.Hold = null;
            release.SetResult();
            await reading;

            Assert.Equal(before + 2, probe.Calls);
        });
    }

    // ---------------------------------------------------------------- plumbing

    /// <summary>
    /// Opens the repository the way the repository window has it: the history drawn, the refreshes
    /// handed to it, and the watcher following the context.
    /// </summary>
    private static async Task<(HistoryPageViewModel History, IRepositoryWatcher Watcher, List<AutoRefreshResult> Refreshes)> OpenAsync(
        TestServices services,
        RepositoryHandle repository)
    {
        // The window's view model is what hands every refresh to the history.
        _ = services.Get<MainWindowViewModel>();
        HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
        IRepositoryWatcher watcher = services.Get<IRepositoryWatcher>();

        List<AutoRefreshResult> refreshes = [];
        services.Get<IAutoRefreshService>().Refreshed += (_, result) => refreshes.Add(result);

        await services.Get<IRepositoryContext>().OpenAsync(repository);
        await history.ReloadAsync();
        await UntilAsync(() => watcher.IsWatching && !history.IsBusy);

        return (history, watcher, refreshes);
    }

    /// <summary>
    /// Lets the test's clock run in steps of a tenth of a second — the operating system reporting in
    /// real time meanwhile — until something holds.
    /// </summary>
    /// <returns>How far the clock was moved.</returns>
    private static async Task<TimeSpan> AdvanceUntilAsync(TestServices services, Func<bool> condition)
    {
        TimeSpan step = TimeSpan.FromMilliseconds(100);
        TimeSpan advanced = TimeSpan.Zero;
        Stopwatch waited = Stopwatch.StartNew();

        while (!condition())
        {
            Assert.True(waited.Elapsed < Patience, "The change never showed.");

            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();

            // Only the quiet period is let pass: the automatic refresh's own interval never comes.
            if (advanced < TimeSpan.FromSeconds(5))
            {
                services.Clock.Advance(step);
                advanced += step;
                Dispatcher.UIThread.RunJobs();
            }
        }

        return advanced;
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        Stopwatch waited = Stopwatch.StartNew();

        while (!condition())
        {
            Assert.True(waited.Elapsed < Patience, "The condition was never met.");

            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// The real probe, counted, and held open while a test says so.
    /// </summary>
    private sealed class HeldProbe(IWorkingTreeProbe inner) : IWorkingTreeProbe
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task? Hold { get; set; }

        public async Task<bool> IsDirtyAsync(RepositoryHandle repository, bool includeUntracked = true, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            if (Hold is { } hold)
            {
                await hold.WaitAsync(cancellationToken);
            }

            return await inner.IsDirtyAsync(repository, includeUntracked, cancellationToken);
        }
    }

    private static async Task<RepositoryHandle> InitWithACommitAsync(TestServices services, string name)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, name), "main");

        await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "README.md"), "# watched\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");

        return repository;
    }

    private static void Git(RepositoryHandle repository, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = repository.WorkTreePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.Environment["GIT_AUTHOR_NAME"] = "Ada Lovelace";
        startInfo.Environment["GIT_AUTHOR_EMAIL"] = "ada@example.com";
        startInfo.Environment["GIT_COMMITTER_NAME"] = "Ada Lovelace";
        startInfo.Environment["GIT_COMMITTER_EMAIL"] = "ada@example.com";

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }
    }
}
