using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Opening a repository starts two reads together — the history's commits and the context's
/// references — and the history has to be right whichever of them answers first.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryOpeningTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryOpeningTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void RowsDrawnBeforeTheReferences_GetTheirBadgesAsSoonAsTheReferencesArrive()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildGated();
            GatedRefReader references = services.Get<GatedRefReader>();
            CountingCommitLogReader log = services.Get<CountingCommitLogReader>();
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            HistoryPageViewModel page = await AppearedAsync(services);
            Task opening = services.Get<IRepositoryContext>().OpenAsync(repository);

            // The log answers first: the lines are there, and nothing names them yet.
            await WaitUntilAsync(() => page.Rows.Count > 0 && page.IsNotBusy);
            Assert.DoesNotContain(page.Rows, row => row.HasRefs);

            references.Open();
            await opening;

            // No automatic refresh has run — the clock has not moved — and the badges are there.
            await WaitUntilAsync(() => page.IsNotBusy && page.Rows.Any(row => row.HasRefs));

            Assert.Contains(page.Rows, row => row.Refs.Any(badge => badge is { Kind: GitRefKind.Tag, Name: "v1.0" }));
            Assert.Contains(page.Rows, row => row.Refs.Any(badge => badge is { Kind: GitRefKind.LocalBranch, Name: "feature" }));
            Assert.Equal(2, log.Reads);
        });
    }

    [Fact]
    public void ReferencesReadBeforeTheRows_AreDrawnByTheOneRead()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildGated();
            GatedRefReader references = services.Get<GatedRefReader>();
            CountingCommitLogReader log = services.Get<CountingCommitLogReader>();
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            log.Hold();
            references.Open();

            HistoryPageViewModel page = await AppearedAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            log.Release();

            await WaitUntilAsync(() => page.IsNotBusy && page.Rows.Count > 0);

            Assert.Contains(page.Rows, row => row.Refs.Any(badge => badge is { Kind: GitRefKind.Tag, Name: "v1.0" }));
            Assert.Equal(1, log.Reads);
        });
    }

    [Fact]
    public void AWalkThatStartedBeforeTheReferences_IsWalkedAgainWhenABranchIsHidden()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildGated();
            GatedRefReader references = services.Get<GatedRefReader>();
            CountingCommitLogReader log = services.Get<CountingCommitLogReader>();
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            services.Get<IHiddenBranchStore>().SetHidden(repository.WorkTreePath, GitBranch.LocalPrefix + "feature", true);

            HistoryPageViewModel page = await AppearedAsync(services);

            // The first walk is asked for before any reference is known, so it leaves nothing out.
            log.Hold();
            Task opening = services.Get<IRepositoryContext>().OpenAsync(repository);
            await WaitUntilAsync(() => log.Reads == 1);
            Assert.Empty(log.Queries[0].ExcludedRefs);

            references.Open();
            await opening;
            log.Release();

            await WaitUntilAsync(() => page.IsNotBusy && page.Rows.Count > 0);

            Assert.Contains(GitBranch.LocalPrefix + "feature", log.Queries[^1].ExcludedRefs);
            Assert.DoesNotContain(page.Rows, row => row.Subject == "Start the feature");
            Assert.Contains(page.Rows, row => row.Subject == "Extend the application");
        });
    }

    [Fact]
    public void ALaterReadOfTheReferences_DoesNotRedrawTheHistory()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildGated();
            GatedRefReader references = services.Get<GatedRefReader>();
            CountingCommitLogReader log = services.Get<CountingCommitLogReader>();
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            references.Open();

            HistoryPageViewModel page = await AppearedAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);
            await WaitUntilAsync(() => page.IsNotBusy && page.Rows.Count > 0);

            int reads = log.Reads;

            await services.Get<IRepositoryContext>().RefreshAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(reads, log.Reads);
        });
    }

    // ---------------------------------------------------------------- the loader

    [Fact]
    public void TheLoader_ShowsUntilTheCommitsAndTheReferencesAreBothRead()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildGated();
            GatedRefReader references = services.Get<GatedRefReader>();
            CountingCommitLogReader log = services.Get<CountingCommitLogReader>();
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            HistoryPageViewModel page = await AppearedAsync(services);
            Assert.False(page.IsLoading);

            log.Hold();
            Task opening = services.Get<IRepositoryContext>().OpenAsync(repository);
            await WaitUntilAsync(() => log.Reads == 1);
            Assert.True(page.IsLoading);

            // The commits are drawn; their badges are still being read.
            log.Release();
            await WaitUntilAsync(() => page.Rows.Count > 0 && page.IsNotBusy);
            Assert.True(page.IsLoading);

            references.Open();
            await opening;
            await WaitUntilAsync(() => page.IsNotBusy && page.Rows.Any(row => row.HasRefs));

            Assert.False(page.IsLoading);
        });
    }

    [Fact]
    public void TheLoader_IsLaidOverTheListWhileLoading_AndGoneOnceItIsDrawn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildGated();
            GatedRefReader references = services.Get<GatedRefReader>();
            CountingCommitLogReader log = services.Get<CountingCommitLogReader>();
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            HistoryPageViewModel page = await AppearedAsync(services);
            HistoryPageView view = services.Get<HistoryPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1000, Height = 700 };
            window.Show();

            try
            {
                ProgressBar bar = view.FindControl<ProgressBar>("LoadingBar")
                    ?? throw new InvalidOperationException("The history page has no loader.");
                Panel workspace = view.FindControl<Panel>("Workspace")
                    ?? throw new InvalidOperationException("The history page has no workspace.");

                Assert.False(bar.IsVisible);

                log.Hold();
                references.Open();
                Task opening = services.Get<IRepositoryContext>().OpenAsync(repository);
                await WaitUntilAsync(() => log.Reads == 1);
                window.UpdateLayout();

                Assert.True(bar.IsVisible);
                Assert.True(bar.IsIndeterminate);
                Assert.Same(workspace, bar.Parent);
                Assert.Equal(0, bar.Bounds.Top);

                log.Release();
                await opening;
                await WaitUntilAsync(() => page.IsNotBusy && page.Rows.Count > 0);
                window.UpdateLayout();

                Assert.False(bar.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    private static TestServices BuildGated()
        => TestServices.Build(useRealRefReader: true, configure: services =>
        {
            services.RemoveAll<IRefReader>();
            services.AddSingleton<RefReader>();
            services.AddSingleton<GatedRefReader>();
            services.AddSingleton<IRefReader>(provider => provider.GetRequiredService<GatedRefReader>());

            services.RemoveAll<ICommitLogReader>();
            services.AddSingleton<CommitLogReader>();
            services.AddSingleton<CountingCommitLogReader>();
            services.AddSingleton<ICommitLogReader>(provider => provider.GetRequiredService<CountingCommitLogReader>());
        });

    /// <summary>
    /// The history page as the window has it when a repository is opened: shown, and listening.
    /// </summary>
    private static async Task<HistoryPageViewModel> AppearedAsync(TestServices services)
    {
        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
        await page.OnAppearingAsync();

        return page;
    }

    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "opening"), "main");

        Commit(repository, "README.md", "# one\n", "Add the readme");
        Git(repository, "tag", "v1.0");
        Git(repository, "checkout", "-b", "feature");
        Commit(repository, "feature.txt", "feature\n", "Start the feature");
        Git(repository, "checkout", "main");
        Commit(repository, "app.txt", "two\n", "Extend the application");

        return repository;
    }

    private static void Commit(RepositoryHandle repository, string path, string content, string message)
    {
        File.WriteAllText(Path.Combine(repository.WorkTreePath, path), content);
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", message);
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

    private static async Task WaitUntilAsync(Func<bool> condition, int attempts = 300)
    {
        for (int attempt = 0; attempt < attempts && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "the page never reached the state the test waited for");
    }

    /// <summary>
    /// The real reference reader, answering only once the test opens it — a slow
    /// <c>for-each-ref</c>, on demand.
    /// </summary>
    private sealed class GatedRefReader(RefReader inner) : IRefReader
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => _gate.TrySetResult();

        public Task<RefCollection> GetRefsAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
            => inner.GetRefsAsync(repository, cancellationToken);

        public Task<HeadState> GetHeadStateAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
            => inner.GetHeadStateAsync(repository, cancellationToken);

        public async Task<RepositoryRefState> GetStateAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
        {
            await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            return await inner.GetStateAsync(repository, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// The real commit log reader, counting its reads, recording what it was asked, and — while held
    /// — keeping the first read waiting until the test releases it.
    /// </summary>
    private sealed class CountingCommitLogReader(CommitLogReader inner) : ICommitLogReader
    {
        // The gate, and whether the next read still has to wait at it: only one read is held.
        private TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _holdNext;

        public int Reads { get; private set; }

        public List<CommitLogQuery> Queries { get; } = [];

        public void Hold()
        {
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _holdNext = true;
        }

        public void Release() => _gate.TrySetResult();

        public async Task<CommitLogPage> GetPageAsync(
            RepositoryHandle repository,
            CommitLogQuery query,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            Queries.Add(query);

            TaskCompletionSource? held = _holdNext ? _gate : null;
            _holdNext = false;

            // Like a git process that had already answered, the held read comes back even when its
            // load was cancelled in the meantime; the page has to throw that answer away itself.
            CommitLogPage page = await inner.GetPageAsync(repository, query, CancellationToken.None).ConfigureAwait(true);

            if (held is not null)
            {
                await held.Task.ConfigureAwait(true);
            }

            return page;
        }

        public Task<GitCommit?> GetCommitAsync(RepositoryHandle repository, string revision, CancellationToken cancellationToken = default)
            => inner.GetCommitAsync(repository, revision, cancellationToken);

        public Task<int> CountAsync(RepositoryHandle repository, CommitLogQuery query, CancellationToken cancellationToken = default)
            => inner.CountAsync(repository, query, cancellationToken);
    }
}
