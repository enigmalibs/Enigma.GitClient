using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Controls.Diff;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Enigma.GitClient.Desktop.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A refresh that finds the file on screen unchanged leaves the reader's place in its diff alone: the
/// rows, the selected text, the widened context (BUG-6590). A file that did change is drawn again.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiffRefreshTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public DiffRefreshTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private const string SamplePatch =
        "diff --git a/src/app.txt b/src/app.txt\n" +
        "index 1111111..2222222 100644\n" +
        "--- a/src/app.txt\n" +
        "+++ b/src/app.txt\n" +
        "@@ -1,4 +1,5 @@\n" +
        " one\n" +
        "-two\n" +
        "+two changed\n" +
        "+four\n" +
        " five\n";

    private const string ChangedPatch =
        "diff --git a/src/app.txt b/src/app.txt\n" +
        "index 1111111..3333333 100644\n" +
        "--- a/src/app.txt\n" +
        "+++ b/src/app.txt\n" +
        "@@ -1,4 +1,5 @@\n" +
        " one\n" +
        "-two\n" +
        "+two changed again\n" +
        "+four\n" +
        " five\n";

    private static readonly RepositoryHandle Repository = OperatingSystem.IsWindows()
        ? new RepositoryHandle(@"C:\src\client", @"C:\src\client\.git")
        : new RepositoryHandle("/src/client", "/src/client/.git");

    private static readonly ChangedFile File = new()
    {
        Path = "src/app.txt",
        ChangeKind = FileChangeKind.Modified,
        AddedLines = 2,
        RemovedLines = 1,
    };

    private static FilePatch Parse(string patch) => UnifiedDiffParser.Parse(patch).Files[0];

    private static (DiffViewerViewModel Viewer, RecordingDiffService Diffs) Build()
    {
        RecordingDiffService diffs = new() { Patch = Parse(SamplePatch) };

        SettingsService settings = new(
            new AppPaths(Path.Combine(Path.GetTempPath(), "enigma-refresh-tests-" + Guid.NewGuid().ToString("N"))),
            NullLogger<SettingsService>.Instance);

        DiffViewerViewModel viewer = new(diffs, new RecordingSystemInterop(), settings, NullLogger<DiffViewerViewModel>.Instance);
        viewer.ShowUnifiedCommand.Execute(null);

        return (viewer, diffs);
    }

    private static async Task<(DiffViewerViewModel Viewer, RecordingDiffService Diffs)> ShownAsync()
    {
        (DiffViewerViewModel viewer, RecordingDiffService diffs) = Build();
        await viewer.ShowAsync(Repository, DiffTarget.WorkingTree(), File);

        return (viewer, diffs);
    }

    // ---------------------------------------------------------------- the viewer

    [Fact]
    public void ARefreshThatReadsTheSamePatch_ChangesNothingTheReaderHas()
    {
        _fixture.RunAsync(async () =>
        {
            (DiffViewerViewModel viewer, RecordingDiffService diffs) = await ShownAsync();

            // The reader widened the context and selected some text.
            await viewer.ExpandContextCommand.ExecuteAsync(null);
            viewer.Render.Selection.Begin(DiffPane.Unified, new DiffTextPosition(1, 0));
            viewer.Render.Selection.ExtendTo(new DiffTextPosition(3, 2));

            IReadOnlyList<DiffRowViewModel> rows = viewer.UnifiedRows;
            int context = viewer.ContextLines;
            int redraws = 0;
            viewer.PatchChanged += (_, _) => redraws++;

            await viewer.RefreshAsync(File);

            // Read again, with the reader's own context...
            Assert.Equal(context, diffs.PatchRequests[^1].ContextLines);

            // ...and nothing drawn again.
            Assert.Same(rows, viewer.UnifiedRows);
            Assert.Equal(0, redraws);
            Assert.False(viewer.Render.Selection.IsEmpty);
            Assert.Equal(new DiffTextPosition(3, 2), viewer.Render.Selection.Caret);
            Assert.Equal(context, viewer.ContextLines);
        });
    }

    [Fact]
    public void ARefreshThatReadsAChangedPatch_DrawsItAsANewFile()
    {
        _fixture.RunAsync(async () =>
        {
            (DiffViewerViewModel viewer, RecordingDiffService diffs) = await ShownAsync();

            viewer.Render.Selection.Begin(DiffPane.Unified, new DiffTextPosition(1, 0));
            viewer.Render.Selection.ExtendTo(new DiffTextPosition(3, 2));

            IReadOnlyList<DiffRowViewModel> rows = viewer.UnifiedRows;
            int redraws = 0;
            viewer.PatchChanged += (_, _) => redraws++;

            diffs.Patch = Parse(ChangedPatch);
            await viewer.RefreshAsync(File);

            Assert.NotSame(rows, viewer.UnifiedRows);
            Assert.Equal(1, redraws);
            Assert.True(viewer.Render.Selection.IsEmpty);
            Assert.Contains(viewer.UnifiedRows, row => row.Single?.Text == "two changed again");
        });
    }

    [Fact]
    public void ARefreshThatFindsTheFileWithoutText_SaysSo_OnlyOnce()
    {
        _fixture.RunAsync(async () =>
        {
            (DiffViewerViewModel viewer, RecordingDiffService diffs) = await ShownAsync();

            // The change went away, or became something with no lines to show.
            diffs.Patch = null;
            await viewer.RefreshAsync(File);

            Assert.False(viewer.HasPatch);
            Assert.True(viewer.HasMessage);

            int redraws = 0;
            viewer.PatchChanged += (_, _) => redraws++;

            await viewer.RefreshAsync(File);

            Assert.Equal(0, redraws);
        });
    }

    [Fact]
    public void ARefreshThatCannotRead_KeepsTheDiffOnScreen()
    {
        _fixture.RunAsync(async () =>
        {
            (DiffViewerViewModel viewer, RecordingDiffService diffs) = await ShownAsync();
            IReadOnlyList<DiffRowViewModel> rows = viewer.UnifiedRows;

            diffs.Failure = new GitCommandException(
                new GitCommand(Repository.WorkTreePath, ["diff"]),
                128,
                "fatal: Unable to create '.git/index.lock': File exists.\n",
                string.Empty);

            await viewer.RefreshAsync(File);

            Assert.Same(rows, viewer.UnifiedRows);
            Assert.False(viewer.HasMessage);
            Assert.False(viewer.IsBusy);
        });
    }

    [Fact]
    public void TheReadersOwnReload_StillReportsAFailure()
    {
        _fixture.RunAsync(async () =>
        {
            (DiffViewerViewModel viewer, RecordingDiffService diffs) = await ShownAsync();

            diffs.Failure = new GitCommandException(
                new GitCommand(Repository.WorkTreePath, ["diff"]),
                128,
                "fatal: bad revision\n",
                string.Empty);

            await viewer.ReloadAsync();

            Assert.Equal("The diff could not be read.", viewer.Message);
        });
    }

    [Fact]
    public void IsShowing_NamesTheFileOnScreen_AndNothingElse()
    {
        _fixture.RunAsync(async () =>
        {
            (DiffViewerViewModel viewer, _) = await ShownAsync();

            // A fresh target instance: the comparison is by value.
            Assert.True(viewer.IsShowing(Repository, DiffTarget.WorkingTree(), File.Path));

            Assert.False(viewer.IsShowing(Repository, DiffTarget.Staged(), File.Path));
            Assert.False(viewer.IsShowing(Repository, DiffTarget.WorkingTree(), "src/other.txt"));
            Assert.False(viewer.IsShowing(new RepositoryHandle(Repository.WorkTreePath + "-other", Repository.GitDirectory), DiffTarget.WorkingTree(), File.Path));

            viewer.Clear();

            Assert.False(viewer.IsShowing(Repository, DiffTarget.WorkingTree(), File.Path));
        });
    }

    [Fact]
    public void ARefreshWithNothingOnScreen_ReadsNothing()
    {
        _fixture.RunAsync(async () =>
        {
            (DiffViewerViewModel viewer, RecordingDiffService diffs) = Build();
            int requests = diffs.PatchRequests.Count;

            await viewer.RefreshAsync(File);

            Assert.Equal(requests, diffs.PatchRequests.Count);
            Assert.False(viewer.HasPatch);
        });
    }

    [Fact]
    public void AnotherFileShown_StillStartsFromTheDefaultContext()
    {
        _fixture.RunAsync(async () =>
        {
            (DiffViewerViewModel viewer, _) = await ShownAsync();
            int initial = viewer.ContextLines;

            await viewer.ExpandContextCommand.ExecuteAsync(null);
            Assert.NotEqual(initial, viewer.ContextLines);

            await viewer.ShowAsync(Repository, DiffTarget.WorkingTree(), File with { Path = "src/other.txt" });

            Assert.Equal(3, viewer.ContextLines);
        });
    }

    // ---------------------------------------------------------------- the history, on a real repository

    [Fact]
    public void TheHistory_KeepsTheWorkingTreeDiff_WhenARefreshFindsTheFileUnchanged()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Counted();
            (Window window, HistoryPageViewModel model, RepositoryHandle repository) = await ShowReadmeDiffAsync(services);

            IReadOnlyList<DiffRowViewModel> rows = model.Diff.UnifiedRows;
            model.Diff.Render.Selection.Begin(DiffPane.Unified, new DiffTextPosition(1, 0));
            model.Diff.Render.Selection.ExtendTo(new DiffTextPosition(2, 1));

            int redraws = 0;
            model.Diff.PatchChanged += (_, _) => redraws++;

            // What the automatic refresh does once the fetch is over — and the file is read again.
            CountingDiffService diffs = (CountingDiffService)services.Get<IDiffService>();
            int reads = diffs.PatchReads;

            await services.Get<IRepositoryContext>().RefreshAsync();
            await WaitUntilAsync(() => diffs.PatchReads > reads && diffs.InFlight == 0);
            await SettleAsync(window);

            Assert.True(model.IsDiffViewOpen);
            Assert.Equal("README.md", model.WorkingTree.SelectedChange?.File.Path);
            Assert.Same(rows, model.Diff.UnifiedRows);
            Assert.Equal(0, redraws);
            Assert.False(model.Diff.Render.Selection.IsEmpty);

            window.Close();
        });
    }

    [Fact]
    public void TheHistory_RedrawsTheWorkingTreeDiff_WhenTheFileChanged()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Counted();
            (Window window, HistoryPageViewModel model, RepositoryHandle repository) = await ShowReadmeDiffAsync(services);

            IReadOnlyList<DiffRowViewModel> rows = model.Diff.UnifiedRows;

            await System.IO.File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "README.md"), "# refresh\n\nedited again\n");
            await services.Get<IRepositoryContext>().RefreshAsync();
            await WaitUntilAsync(() => !ReferenceEquals(rows, model.Diff.UnifiedRows));

            Assert.True(model.IsDiffViewOpen);
            Assert.Contains(model.Diff.UnifiedRows, row => row.Single?.Text == "edited again");

            window.Close();
        });
    }

    [Fact]
    public void TheHistory_ShowsAFileStagedElsewhere_AsTheStagedHalfsDiff()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Counted();
            (Window window, HistoryPageViewModel model, RepositoryHandle repository) = await ShowReadmeDiffAsync(services);

            // Staged in a terminal: the file is followed into the other half, whose diff is another one.
            Git(repository, "add", "README.md");
            await services.Get<IRepositoryContext>().RefreshAsync();
            await WaitUntilAsync(() => model.WorkingTree.SelectedChange?.Target.Kind == DiffTargetKind.Staged);
            await SettleAsync(window);

            Assert.True(model.Diff.IsShowing(repository, DiffTarget.Staged(), "README.md"));
            Assert.True(model.IsDiffViewOpen);

            window.Close();
        });
    }

    // ---------------------------------------------------------------- plumbing

    /// <summary>
    /// The application's container with the real diff service counted, so a test can wait for the
    /// refresh's read rather than for a length of time.
    /// </summary>
    private static TestServices Counted()
        => TestServices.Build(
            useRealRefReader: true,
            services =>
            {
                services.RemoveAll<IDiffService>();
                services.AddSingleton<IDiffService>(provider =>
                    new CountingDiffService(ActivatorUtilities.CreateInstance<DiffService>(provider)));
            });

    /// <summary>An <see cref="IDiffService"/> that counts the patch reads it passes on.</summary>
    private sealed class CountingDiffService(IDiffService inner) : IDiffService
    {
        private int _reads;
        private int _inFlight;

        public int PatchReads => Volatile.Read(ref _reads);

        public int InFlight => Volatile.Read(ref _inFlight);

        public Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(
            RepositoryHandle repository,
            DiffTarget target,
            CancellationToken cancellationToken = default)
            => inner.GetChangedFilesAsync(repository, target, cancellationToken);

        public Task<FilePatch?> GetPatchAsync(
            RepositoryHandle repository,
            DiffTarget target,
            string path,
            DiffOptions? options = null,
            CancellationToken cancellationToken = default)
            => CountAsync(inner.GetPatchAsync(repository, target, path, options, cancellationToken));

        public Task<FilePatch?> GetPatchAsync(
            RepositoryHandle repository,
            DiffTarget target,
            ChangedFile file,
            DiffOptions? options = null,
            CancellationToken cancellationToken = default)
            => CountAsync(inner.GetPatchAsync(repository, target, file, options, cancellationToken));

        private async Task<FilePatch?> CountAsync(Task<FilePatch?> read)
        {
            Interlocked.Increment(ref _inFlight);

            try
            {
                return await read.ConfigureAwait(true);
            }
            finally
            {
                Interlocked.Increment(ref _reads);
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    private static async Task<(Window Window, HistoryPageViewModel Model, RepositoryHandle Repository)> ShowReadmeDiffAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, "refresh"), "main");

        await System.IO.File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "README.md"), "# refresh\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");

        await System.IO.File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "README.md"), "# refresh\n\nedited\n");

        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel model = services.Get<HistoryPageViewModel>();
        await model.ReloadAsync();

        HistoryPageView view = services.Get<HistoryPageView>();
        view.DataContext = model;

        Window window = new() { Content = view, Width = 1200, Height = 900 };
        window.Show();
        window.UpdateLayout();

        model.SelectedRow = model.Rows.First(row => row.IsUncommitted);
        await WaitUntilAsync(() => model.WorkingTree.Unstaged.FileCount == 1);

        model.Diff.ShowUnifiedCommand.Execute(null);
        model.WorkingTree.SelectFirstFile();

        await WaitUntilAsync(() => model.Diff.UnifiedRows.Any(row => row.Single?.Text == "edited"));
        await SettleAsync(window);

        return (window, model, repository);
    }

    private static async Task SettleAsync(Window window)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 300 && !condition(); attempt++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition(), "the condition was never met");
    }

    private static void Git(RepositoryHandle repository, params string[] arguments)
    {
        System.Diagnostics.ProcessStartInfo startInfo = new()
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

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }
    }
}
