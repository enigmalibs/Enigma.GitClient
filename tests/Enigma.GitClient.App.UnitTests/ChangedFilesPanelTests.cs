using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.ViewModels.Panels;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.App.Views.Panels;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives the changed-files panel: the two shapes the specification asks the user to choose
/// between, the filter, the selection that the diff viewer will follow, and the rendered rows.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ChangedFilesPanelTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public ChangedFilesPanelTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A change touching every shape of row the panel has to draw: a plain edit at the root, a
    /// nested addition, a deletion, a rename and a binary file.
    /// </summary>
    private static IReadOnlyList<ChangedFile> SampleFiles()
        =>
        [
            new ChangedFile
            {
                Path = "README.md",
                ChangeKind = FileChangeKind.Modified,
                AddedLines = 4,
                RemovedLines = 1,
                HasLineCounts = true,
            },
            new ChangedFile
            {
                Path = "src/app/Program.cs",
                ChangeKind = FileChangeKind.Added,
                AddedLines = 40,
                HasLineCounts = true,
            },
            new ChangedFile
            {
                Path = "src/app/Legacy.cs",
                ChangeKind = FileChangeKind.Deleted,
                RemovedLines = 12,
                HasLineCounts = true,
            },
            new ChangedFile
            {
                Path = "docs/guide.md",
                OldPath = "docs/manual.md",
                ChangeKind = FileChangeKind.Renamed,
                AddedLines = 2,
                RemovedLines = 2,
                HasLineCounts = true,
            },
            new ChangedFile { Path = "assets/logo.png", ChangeKind = FileChangeKind.Modified, IsBinary = true },
        ];

    private static ChangedFilesPanelViewModel Loaded(RecordingSystemInterop? interop = null)
    {
        ChangedFilesPanelViewModel panel = new(interop ?? new RecordingSystemInterop());
        panel.SetFiles(SampleFiles());
        return panel;
    }

    // ---------------------------------------------------------------- the two shapes

    [Fact]
    public void Panel_ShowsOneFlatRowPerFileInListMode()
    {
        ChangedFilesPanelViewModel panel = Loaded();
        panel.ViewMode = ChangedFilesViewMode.List;

        Assert.Equal(5, panel.Nodes.Count);
        Assert.All(panel.Nodes, node => Assert.False(node.IsDirectory));

        // The name and nothing else: a path repeated on every row costs the name the width it
        // needs to be read at all. The whole path is still what the row stands for, and what its
        // tooltip shows.
        ChangedFileNodeViewModel program = panel.Nodes.Single(node => node.Path == "src/app/Program.cs");
        Assert.Equal("Program.cs", program.Label);
        Assert.Equal("src/app/Program.cs", program.Path);
        Assert.Equal("src/app", program.File!.DirectoryPath);
    }

    [Fact]
    public void Panel_NestsFilesUnderTheirDirectoriesInTreeMode()
    {
        ChangedFilesPanelViewModel panel = Loaded();

        Assert.Equal(ChangedFilesViewMode.Tree, panel.ViewMode);

        // Directories first, then the root-level file.
        Assert.Equal(["assets", "docs", "src/app", "README.md"], panel.Nodes.Select(node => node.Label));

        ChangedFileNodeViewModel nested = panel.Nodes.Single(node => node.Label == "src/app");
        Assert.True(nested.IsDirectory);
        Assert.Equal("2 files", nested.DirectorySummary);
        Assert.Equal(["Legacy.cs", "Program.cs"], nested.Children.Select(child => child.Label));

        // Every file is reachable, and only once.
        Assert.Equal(5, PathsOf(panel).Count);
    }

    [Fact]
    public void Panel_CanShowEverySingleChildDirectoryOnItsOwnRow()
    {
        ChangedFilesPanelViewModel panel = Loaded();
        panel.CollapseDirectories = false;

        ChangedFileNodeViewModel source = panel.Nodes.Single(node => node.Label == "src");
        Assert.Equal("app", Assert.Single(source.Children).Label);
    }

    [Fact]
    public void Panel_KeepsTheSelectedFileAcrossAViewModeSwitch()
    {
        ChangedFilesPanelViewModel panel = Loaded();

        Assert.True(panel.SelectPath("src/app/Program.cs"));
        Assert.Equal("src/app/Program.cs", panel.SelectedFile?.Path);

        panel.ViewMode = ChangedFilesViewMode.List;

        // The toggle is a different view of the same thing, not a reset.
        Assert.Equal("src/app/Program.cs", panel.SelectedFile?.Path);

        panel.ViewMode = ChangedFilesViewMode.Tree;

        Assert.Equal("src/app/Program.cs", panel.SelectedFile?.Path);
    }

    // ---------------------------------------------------------------- the filter

    [Fact]
    public void Panel_FiltersOnThePathAndOnTheRenameSource()
    {
        ChangedFilesPanelViewModel panel = Loaded();

        panel.SearchText = "program";

        Assert.Equal(["src/app/Program.cs"], PathsOf(panel));

        // A rename is findable under the name it used to have, which is how anyone looks for it.
        panel.SearchText = "manual";

        Assert.Equal(["docs/guide.md"], PathsOf(panel));

        panel.ClearSearchCommand.Execute(null);

        Assert.Equal(5, PathsOf(panel).Count);
    }

    [Fact]
    public void Panel_DropsTheSelectionWhenTheFilterHidesIt()
    {
        ChangedFilesPanelViewModel panel = Loaded();
        panel.SelectPath("README.md");

        panel.SearchText = "program";

        Assert.Null(panel.SelectedFile);

        panel.SearchText = string.Empty;
        panel.SelectPath("README.md");
        panel.SearchText = "readme";

        // Still visible, so still selected.
        Assert.Equal("README.md", panel.SelectedFile?.Path);
    }

    [Fact]
    public void Panel_ClearSearchCommandOnlyRunsWhenThereIsSomethingToClear()
    {
        ChangedFilesPanelViewModel panel = Loaded();

        Assert.False(panel.ClearSearchCommand.CanExecute(null));

        panel.SearchText = "x";

        Assert.True(panel.ClearSearchCommand.CanExecute(null));
    }

    [Fact]
    public void Panel_ReportsAnEmptyResultWhenNothingMatches()
    {
        ChangedFilesPanelViewModel panel = Loaded();

        panel.SearchText = "nothing matches this";

        Assert.True(panel.IsEmpty);
        Assert.Empty(panel.Nodes);

        // The filter hides rows; it does not throw the change away.
        Assert.Equal(5, panel.FileCount);
    }

    // ---------------------------------------------------------------- what a row says

    [Theory]
    [InlineData("README.md", "M")]
    [InlineData("src/app/Program.cs", "A")]
    [InlineData("src/app/Legacy.cs", "D")]
    [InlineData("docs/guide.md", "R")]
    public void Panel_ShowsGitsOwnLetterForEachChange(string path, string glyph)
    {
        ChangedFilesPanelViewModel panel = Loaded();
        panel.ViewMode = ChangedFilesViewMode.List;

        Assert.Equal(glyph, panel.Nodes.Single(node => node.Path == path).StatusGlyph);
    }

    [Fact]
    public void Panel_ShowsTheWholeOldPathWhenAFileActuallyMoved()
    {
        ChangedFilesPanelViewModel panel = new(new RecordingSystemInterop());
        panel.SetFiles(
        [
            new ChangedFile
            {
                Path = "src/app/Moved.cs",
                OldPath = "tools/Moved.cs",
                ChangeKind = FileChangeKind.Renamed,
            },
        ]);
        panel.ViewMode = ChangedFilesViewMode.List;

        Assert.Equal("← tools/Moved.cs", Assert.Single(panel.Nodes).RenameLabel);
    }

    [Fact]
    public void Panel_NamesWhereARenamedFileCameFrom()
    {
        ChangedFilesPanelViewModel panel = Loaded();
        panel.ViewMode = ChangedFilesViewMode.List;

        ChangedFileNodeViewModel renamed = panel.Nodes.Single(node => node.Path == "docs/guide.md");

        Assert.True(renamed.IsRenamed);
        Assert.True(renamed.IsMoved);
        // Renamed inside its own directory, so only the old name is worth the width.
        Assert.Equal("← manual.md", renamed.RenameLabel);
        Assert.Equal("docs/manual.md", renamed.RenameSource);
    }

    [Fact]
    public void Panel_SaysBinaryRatherThanCountingLinesItCannotCount()
    {
        ChangedFilesPanelViewModel panel = Loaded();
        panel.ViewMode = ChangedFilesViewMode.List;

        Assert.Equal("binary", panel.Nodes.Single(node => node.Path == "assets/logo.png").LineCounts);
        Assert.Equal("+4 −1", panel.Nodes.Single(node => node.Path == "README.md").LineCounts);
    }

    [Fact]
    public void Panel_SaysNothingAboutTheSizeOfAChangeNobodyMeasured()
    {
        RecordingSystemInterop interop = new();
        ChangedFilesPanelViewModel panel = new(interop);

        // git status reports what changed, not by how much. "+0 −0" would claim the change is
        // empty, which is a different thing from not having been counted.
        panel.SetFiles([new ChangedFile { Path = "src/app.txt", ChangeKind = FileChangeKind.Modified }]);
        panel.ViewMode = ChangedFilesViewMode.List;

        Assert.Equal(string.Empty, Assert.Single(panel.Nodes).LineCounts);

        // The header stops at the file count for the same reason.
        Assert.False(panel.HasLineCounts);
        Assert.Equal("1 file", panel.Summary);
    }

    [Fact]
    public void Panel_SummarisesTheWholeChangeInItsHeader()
    {
        ChangedFilesPanelViewModel panel = Loaded();

        Assert.Equal(5, panel.FileCount);
        Assert.Equal(46, panel.AddedLines);
        Assert.Equal(15, panel.RemovedLines);
        Assert.Equal("5 files, +46 −15", panel.Summary);
    }

    [Fact]
    public void Panel_IsEmptyBeforeAnythingIsSelected()
    {
        ChangedFilesPanelViewModel panel = new(new RecordingSystemInterop());

        Assert.True(panel.IsEmpty);
        Assert.Equal("No files", panel.Summary);

        panel.SetFiles(SampleFiles());
        panel.Clear();

        Assert.True(panel.IsEmpty);
        Assert.Equal(0, panel.FileCount);
        Assert.Null(panel.SelectedFile);
    }

    // ---------------------------------------------------------------- the selection

    [Fact]
    public void Panel_RaisesSelectionChangedForTheDiffViewerToFollow()
    {
        ChangedFilesPanelViewModel panel = Loaded();
        panel.ViewMode = ChangedFilesViewMode.List;

        int raised = 0;
        panel.SelectionChanged += (_, _) => raised++;

        panel.SelectedNode = panel.Nodes[0];
        panel.SelectedNode = null;

        Assert.Equal(2, raised);
    }

    [Fact]
    public void Panel_SelectingADirectoryRowSelectsNoFile()
    {
        ChangedFilesPanelViewModel panel = Loaded();

        panel.SelectedNode = panel.Nodes.Single(node => node.Label == "src/app");

        Assert.NotNull(panel.SelectedNode);
        Assert.Null(panel.SelectedFile);
    }

    [Fact]
    public void Panel_CannotSelectAPathItDoesNotShow()
    {
        ChangedFilesPanelViewModel panel = Loaded();

        Assert.False(panel.SelectPath("src/app/Missing.cs"));
        Assert.False(panel.SelectPath(null));
        Assert.Null(panel.SelectedFile);
    }

    // ---------------------------------------------------------------- the row menu

    [Fact]
    public void Panel_CopiesARowsPathAndItsName()
    {
        RecordingSystemInterop interop = new();
        ChangedFilesPanelViewModel panel = Loaded(interop);
        panel.ViewMode = ChangedFilesViewMode.List;

        ChangedFileNodeViewModel row = panel.Nodes.Single(node => node.Path == "src/app/Program.cs");

        row.CopyPathCommand.Execute(row);
        row.CopyNameCommand.Execute(row);

        Assert.Equal(["src/app/Program.cs", "Program.cs"], interop.Copied);
    }

    [Fact]
    public void Panel_OpensAndRevealsARowsFileUnderTheWorkTree()
    {
        RecordingSystemInterop interop = new();
        ChangedFilesPanelViewModel panel = Loaded(interop);
        panel.ViewMode = ChangedFilesViewMode.List;
        panel.WorkTreePath = OperatingSystem.IsWindows() ? @"C:\src\client" : "/src/client";

        ChangedFileNodeViewModel row = panel.Nodes.Single(node => node.Path == "src/app/Program.cs");

        row.OpenFileCommand.Execute(row);
        row.RevealFileCommand.Execute(row);

        string expected = Path.Combine(panel.WorkTreePath, "src", "app", "Program.cs");

        Assert.Equal([expected], interop.Opened);
        Assert.Equal([expected], interop.Revealed);
    }

    [Fact]
    public void Panel_WillNotOpenSomethingThatIsNotOnDisk()
    {
        RecordingSystemInterop interop = new();
        ChangedFilesPanelViewModel panel = Loaded(interop);

        ChangedFileNodeViewModel directory = panel.Nodes.Single(node => node.Label == "src/app");
        ChangedFileNodeViewModel deleted = ChangedFilesPanelViewModel.Flatten(panel.Nodes)
            .Single(node => node.Path == "src/app/Legacy.cs");
        ChangedFileNodeViewModel present = ChangedFilesPanelViewModel.Flatten(panel.Nodes)
            .Single(node => node.Path == "src/app/Program.cs");

        // No work tree yet, so nothing can be reached at all.
        Assert.False(panel.OpenFileCommand.CanExecute(present));

        panel.WorkTreePath = OperatingSystem.IsWindows() ? @"C:\src\client" : "/src/client";

        Assert.True(panel.OpenFileCommand.CanExecute(present));

        // A directory is not a file, and a deleted file is not there any more.
        Assert.False(panel.OpenFileCommand.CanExecute(directory));
        Assert.False(panel.OpenFileCommand.CanExecute(deleted));
        Assert.False(panel.RevealFileCommand.CanExecute(deleted));

        directory.OpenFileCommand.Execute(directory);
        deleted.RevealFileCommand.Execute(deleted);

        Assert.Empty(interop.Opened);
        Assert.Empty(interop.Revealed);
    }

    // ---------------------------------------------------------------- a very large change

    [Fact]
    public void Panel_HandlesACommitTouchingTenThousandFiles()
    {
        ChangedFilesPanelViewModel panel = new(new RecordingSystemInterop());

        List<ChangedFile> files = new(10_000);

        for (int index = 0; index < 10_000; index++)
        {
            string number = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            files.Add(new ChangedFile
            {
                Path = $"src/module{(index % 40).ToString(System.Globalization.CultureInfo.InvariantCulture)}/File{number}.cs",
                ChangeKind = FileChangeKind.Modified,
                AddedLines = 1,
                RemovedLines = 1,
            });
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        panel.SetFiles(files);
        stopwatch.Stop();

        Assert.Equal(10_000, panel.FileCount);

        // One root ("src"), holding the forty module directories.
        ChangedFileNodeViewModel root = Assert.Single(panel.Nodes);
        Assert.Equal(40, root.Children.Count);

        // Past the limit the tree opens closed, so selecting such a commit realises one row rather
        // than ten thousand.
        Assert.False(root.IsExpanded);
        Assert.All(root.Children, node => Assert.False(node.IsExpanded));

        Assert.True(
            stopwatch.ElapsedMilliseconds < 2000,
            $"building the panel took {stopwatch.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} ms");
    }

    [Fact]
    public void Panel_OpensTheTreeForAChangeSmallEnoughToRead()
    {
        ChangedFilesPanelViewModel panel = Loaded();

        Assert.True(ChangedFilesPanelViewModel.DefaultAutoExpandLimit > 5);
        Assert.All(panel.Nodes.Where(node => node.IsDirectory), node => Assert.True(node.IsExpanded));
    }

    // ---------------------------------------------------------------- the history page

    [Fact]
    public void HistoryPage_FillsThePanelFromTheSelectedCommit()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            page.SelectedRow = page.Rows.Single(row => row.Subject == "Rework the sources");
            await WaitForFilesAsync(page);

            Assert.Equal(
                ["src/app/Program.cs", "src/app/Removed.cs", "src/app/Renamed.cs"],
                PathsOf(page.Files).Order(StringComparer.Ordinal));

            ChangedFileNodeViewModel renamed = ChangedFilesPanelViewModel.Flatten(page.Files.Nodes)
                .Single(node => node.Path == "src/app/Renamed.cs");

            Assert.True(renamed.IsRenamed);
            Assert.Equal("← Original.cs", renamed.RenameLabel);
            Assert.Equal("src/app/Original.cs", renamed.RenameSource);
        });
    }

    [Fact]
    public void HistoryPage_ShowsTheCommitHeaderBesideTheFiles()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            page.SelectedRow = page.Rows.Single(row => row.Subject == "Rework the sources");

            Assert.True(page.HasSelectedCommit);
            Assert.Equal("Rework the sources", page.SelectedSubject);
            Assert.Equal(40, page.SelectedSha.Length);
            Assert.Contains("Ada Lovelace", page.SelectedAuthor, StringComparison.Ordinal);
            Assert.NotEqual(string.Empty, page.SelectedDate);
        });
    }

    [Fact]
    public void HistoryPage_ShowsTheUncommittedChangesLikeAnyOtherRow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            await File.WriteAllTextAsync(
                Path.Combine(repository.WorkTreePath, "README.md"),
                "# uncommitted\n");

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            page.SelectedRow = page.Rows.Single(row => row.IsUncommitted);
            await WaitForFilesAsync(page);

            Assert.Equal(["README.md"], PathsOf(page.Files));

            // The pseudo-row has no commit, so the details header has nothing to describe.
            Assert.False(page.HasSelectedCommit);
        });
    }

    [Fact]
    public void HistoryPage_EmptiesThePanelWhenTheSelectionGoesAway()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            page.SelectedRow = page.Rows[0];
            await WaitForFilesAsync(page);

            page.SelectedRow = null;

            Assert.True(page.Files.IsEmpty);
            Assert.Equal(0, page.Files.FileCount);
        });
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void Panel_ActuallyDrawsItsRows()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                ChangedFilesPanelViewModel panel = Loaded();
                ChangedFilesPanelView view = new() { DataContext = panel };

                IReadOnlyList<string> tree = RenderAndReadText(view, "changed-files-tree.png");

                Assert.Contains("5 files, +46 −15", tree);
                Assert.Contains("src/app", tree);
                Assert.Contains("Program.cs", tree);
                Assert.Contains("binary", tree);
                Assert.Contains("A", tree);
                Assert.Contains("D", tree);
                Assert.Contains("R", tree);

                panel.ViewMode = ChangedFilesViewMode.List;

                IReadOnlyList<string> list = RenderAndReadText(view, "changed-files-list.png");

                Assert.Contains("Program.cs", list);

                // Names, not paths: the directory is a row of its own in the tree and nowhere in
                // the list.
                Assert.DoesNotContain("src/app", list);
                Assert.Contains("+4 −1", list);
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void HistoryPage_DrawsTheDetailsPaneOnceACommitIsSelected()
    {
        _fixture.RunAsync(async () =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                using TestServices services = TestServices.Build(useRealRefReader: true);
                RepositoryHandle repository = await BuildRepositoryAsync(services);
                await services.Get<IRepositoryContext>().OpenAsync(repository);

                HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
                await page.ReloadAsync();

                // The dialog is what draws the panels, and it is asked for rather than implied by
                // the selection.
                page.RowCommands.ShowChanges.Execute(page.Rows.Single(row => row.Subject == "Rework the sources"));
                await WaitForFilesAsync(page);

                HistoryPageView view = services.Get<HistoryPageView>();
                view.DataContext = page;

                IReadOnlyList<string> texts = RenderAndReadText(view, "history-page-details.png", 1200, 700);

                // The graph is still there, and the details pane now sits under it.
                Assert.Contains("Rework the sources", texts);
                Assert.Contains("Program.cs", texts);
                Assert.Contains("src/app", texts);
                Assert.Contains(texts, text => text.StartsWith("3 files", StringComparison.Ordinal));
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void Panel_DrawsASelectedRowWithoutLosingItsCounts()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                ChangedFilesPanelViewModel panel = Loaded();
                panel.SelectPath("src/app/Program.cs");

                ChangedFilesPanelView view = new() { DataContext = panel };

                IReadOnlyList<string> texts = RenderAndReadText(view, "changed-files-selected.png", 320, 320);

                // The selected row is the one the diff viewer follows, so it is the row most often
                // read: its counts must survive the selection highlight and the tree's indent.
                Assert.Contains("+40 −0", texts);

                TextBlock counts = view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(block => block.Text == "+40 −0");

                Point corner = counts.TranslatePoint(new Point(counts.Bounds.Width, 0), view)
                    ?? throw new InvalidOperationException("The counts are not in the panel's tree.");

                // Drawn is not the same as visible: a row wider than the panel puts its counts past
                // the edge, where they are silently clipped.
                Assert.True(
                    corner.X <= view.Bounds.Width,
                    $"the counts end at {corner.X.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} "
                    + $"in a panel {view.Bounds.Width.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} wide");
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void Panel_DrawsItsEmptyStateWhenThereIsNothingToShow()
    {
        _fixture.Run(() =>
        {
            ChangedFilesPanelView view = new() { DataContext = new ChangedFilesPanelViewModel(new RecordingSystemInterop()) };

            IReadOnlyList<string> texts = RenderAndReadText(view, "changed-files-empty.png");

            Assert.Contains("No files", texts);
            Assert.Contains(texts, text => text.Contains("Select a commit", StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// Shows the panel off-screen, waits for a frame that actually holds content, saves it beside
    /// the test assembly and returns every piece of text the rendered tree carries.
    /// </summary>
    private static IReadOnlyList<string> RenderAndReadText(
        Control view,
        string fileName,
        double width = 420,
        double height = 320)
    {
        Window window = new() { Content = view, Width = width, Height = height };
        window.Show();

        string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);

        int colours = 0;

        for (int attempt = 0; attempt < 20; attempt++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            window.UpdateLayout();
            window.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            Dispatcher.UIThread.RunJobs();

            using Bitmap frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("The changed-files panel produced no rendered frame.");

            frame.Save(path, PngBitmapEncoderOptions.Default);
            colours = SnapshotColours.Count(path);

            if (colours >= 8)
            {
                break;
            }
        }

        // A panel that laid out but never drew is one flat colour, and reads exactly like a correct
        // one from every other angle — only the pixels tell the two apart.
        Assert.True(
            colours >= 8,
            $"{fileName}: the frame holds only {colours.ToString(System.Globalization.CultureInfo.InvariantCulture)} distinct colours");

        string[] texts = [.. view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty)
            .Where(text => text.Length > 0)];

        // Detach before closing: a closed window still owns its content, and the next render of the
        // same panel would be rejected for already having a visual parent.
        window.Content = null;
        window.Close();

        return texts;
    }

    // ---------------------------------------------------------------- fixture

    private static IReadOnlyList<string> PathsOf(ChangedFilesPanelViewModel panel)
        => [.. ChangedFilesPanelViewModel.Flatten(panel.Nodes)
            .Where(node => !node.IsDirectory)
            .Select(node => node.Path)];

    /// <summary>
    /// Builds a repository whose tip commit adds, renames and deletes, so the panel has something
    /// of every shape to show.
    /// </summary>
    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "changed-files"), "main");

        Write(repository, "README.md", "# project\n");
        Write(repository, "src/app/Original.cs", "// one\n// two\n// three\n");
        Write(repository, "src/app/Removed.cs", "// gone\n");
        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "Add the initial files");

        await GitAsync(repository, "mv", "src/app/Original.cs", "src/app/Renamed.cs");
        Write(repository, "src/app/Renamed.cs", "// one\n// two changed\n// three\n");
        Write(repository, "src/app/Program.cs", "// brand new\n");
        File.Delete(Path.Combine(repository.WorkTreePath, "src/app/Removed.cs"));
        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "Rework the sources");

        return repository;
    }

    private static void Write(RepositoryHandle repository, string relativePath, string content)
    {
        string full = Path.Combine(repository.WorkTreePath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static Task GitAsync(RepositoryHandle repository, params string[] arguments)
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

        return process.ExitCode == 0
            ? Task.CompletedTask
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }

    /// <summary>
    /// Waits for the panel the page fills in the background, which the selection setter starts
    /// without awaiting.
    /// </summary>
    private static async Task WaitForFilesAsync(HistoryPageViewModel page)
    {
        for (int attempt = 0; attempt < 200 && page.Files.FileCount == 0; attempt++)
        {
            await Task.Delay(10);
        }
    }
}
