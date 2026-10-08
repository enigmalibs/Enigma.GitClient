using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.GitClient.Desktop.Views.Panels;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The details panel's file lists answer a click as the history's lines do: the selected file lets go
/// of the selection, which puts its diff away, and any other file takes it.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class FileListToggleTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public FileListToggleTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- a commit's files

    [Fact]
    public void TheSelectedFile_LetsGoOnAClick_AndItsDiffGoesWithIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view) = await ShowCommitFilesAsync(services);

            CommitRowViewModel row = model.SelectedRow!;
            ChangedFileNodeViewModel guide = FileNode(model.Files, "docs/guide.md");

            // Nothing picked: the click picks the file and its diff opens over the graph.
            Click(window, OnFile(view, "DetailsFiles", guide, window));

            Assert.Same(guide, model.Files.SelectedNode);
            Assert.True(model.IsDiffViewOpen);

            // The same file again — further along it, so it is a second click and not a double-click:
            // it lets go, the diff goes, and the line keeps its panel.
            Click(window, OnFile(view, "DetailsFiles", guide, window, 180));

            Assert.Null(model.Files.SelectedNode);
            Assert.False(model.IsDiffViewOpen);
            Assert.Same(row, model.SelectedRow);
            Assert.True(model.IsDetailsPanelOpen);

            // And it comes back with the next click.
            Click(window, OnFile(view, "DetailsFiles", guide, window));

            Assert.Same(guide, model.Files.SelectedNode);
            Assert.True(model.IsDiffViewOpen);

            window.Close();
        });
    }

    [Fact]
    public void AnotherFile_TakesTheSelection_AndTheDiffStaysOpenOnIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view) = await ShowCommitFilesAsync(services);

            ChangedFileNodeViewModel guide = FileNode(model.Files, "docs/guide.md");
            ChangedFileNodeViewModel notes = FileNode(model.Files, "docs/notes.md");

            Click(window, OnFile(view, "DetailsFiles", guide, window));
            Click(window, OnFile(view, "DetailsFiles", notes, window, 180));

            Assert.Same(notes, model.Files.SelectedNode);
            Assert.True(model.IsDiffViewOpen);

            window.Close();
        });
    }

    [Fact]
    public void ADoubleClickOnTheSelectedFile_LeavesItSelected()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view) = await ShowCommitFilesAsync(services);

            ChangedFileNodeViewModel guide = FileNode(model.Files, "docs/guide.md");
            Click(window, OnFile(view, "DetailsFiles", guide, window));

            // On the file that is selected: the first press lets go of it, the second takes it back.
            Point point = OnFile(view, "DetailsFiles", guide, window, 180);
            Click(window, point);
            Click(window, point);

            Assert.Same(guide, model.Files.SelectedNode);
            Assert.True(model.IsDiffViewOpen);

            window.Close();
        });
    }

    [Fact]
    public void ARightClickOrAModifiedClickOnTheSelectedFile_LeavesItSelected()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view) = await ShowCommitFilesAsync(services);

            ChangedFileNodeViewModel guide = FileNode(model.Files, "docs/guide.md");
            Click(window, OnFile(view, "DetailsFiles", guide, window));

            // The file's menu is what a right-click is for.
            Point point = OnFile(view, "DetailsFiles", guide, window, 180);
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            Settle(window);

            Assert.Same(guide, model.Files.SelectedNode);
            Assert.True(model.IsDiffViewOpen);

            // A Shift click is the list's own business, never the toggle.
            Click(window, OnFile(view, "DetailsFiles", guide, window, 100), RawInputModifiers.Shift);

            Assert.Same(guide, model.Files.SelectedNode);
            Assert.True(model.IsDiffViewOpen);

            window.Close();
        });
    }

    [Fact]
    public void APressThatLeavesTheFile_DoesNotLetGoOfIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view) = await ShowCommitFilesAsync(services);

            ChangedFileNodeViewModel guide = FileNode(model.Files, "docs/guide.md");
            ChangedFileNodeViewModel notes = FileNode(model.Files, "docs/notes.md");
            Click(window, OnFile(view, "DetailsFiles", guide, window));

            // Pressed on the selected file, released on another: not a click on the selected one.
            Point from = OnFile(view, "DetailsFiles", guide, window, 180);
            Point to = OnFile(view, "DetailsFiles", notes, window, 180);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(to);
            window.MouseUp(to, MouseButton.Left);
            Settle(window);

            Assert.NotNull(model.Files.SelectedNode);
            Assert.True(model.IsDiffViewOpen);

            window.Close();
        });
    }

    [Fact]
    public void InTheTree_TheSelectedFileLetsGo_AndAFolderIsNeverSelected()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view) = await ShowCommitFilesAsync(services);

            model.Files.ShowAsTreeCommand.Execute(null);
            Settle(window);

            ChangedFileNodeViewModel folder = Assert.Single(model.Files.Nodes, node => node.IsDirectory);
            ChangedFileNodeViewModel guide = FileNode(model.Files, "docs/guide.md");

            Click(window, OnFile(view, "DetailsFiles", guide, window));

            Assert.Same(guide, model.Files.SelectedNode);
            Assert.True(model.IsDiffViewOpen);

            Click(window, OnFile(view, "DetailsFiles", guide, window, 200));

            Assert.Null(model.Files.SelectedNode);
            Assert.False(model.IsDiffViewOpen);

            // A folder is never selected (BUG-1B14): with nothing selected, a click on its line folds
            // it, and the graph stays.
            Click(window, OnFile(view, "DetailsFiles", folder, window, 120));

            Assert.False(folder.IsExpanded);
            Assert.Null(model.Files.SelectedNode);
            Assert.False(model.IsDiffViewOpen);

            Click(window, OnFile(view, "DetailsFiles", folder, window, 200));
            Assert.True(folder.IsExpanded);

            // With a file's diff open, the file keeps the selection and the diff stays on it.
            Click(window, OnFile(view, "DetailsFiles", guide, window));
            Click(window, OnFile(view, "DetailsFiles", folder, window, 120));

            Assert.False(folder.IsExpanded);
            Assert.Same(guide, model.Files.SelectedNode);
            Assert.True(model.IsDiffViewOpen);
            await WaitUntilAsync(() => model.Diff.Title == "docs/guide.md" && !model.Diff.HasMessage);

            // And the next file is one click away.
            Click(window, OnFile(view, "DetailsFiles", folder, window, 200));
            ChangedFileNodeViewModel notes = FileNode(model.Files, "docs/notes.md");
            Click(window, OnFile(view, "DetailsFiles", notes, window, 180));

            Assert.Same(notes, model.Files.SelectedNode);
            Assert.True(model.IsDiffViewOpen);

            // Its chevron is a button: it folds the folder and leaves the selection where it is.
            ToggleButton chevron = ChevronOf(view, folder);
            Click(window, chevron.TranslatePoint(new Point(chevron.Bounds.Width / 2, chevron.Bounds.Height / 2), window)!.Value);

            Assert.False(folder.IsExpanded);
            Assert.Same(notes, model.Files.SelectedNode);

            window.Close();
        });
    }

    [Fact]
    public void FilteringTheFiles_NeverPutsTheDiffAway()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view) = await ShowCommitFilesAsync(services);

            ChangedFileNodeViewModel guide = FileNode(model.Files, "docs/guide.md");
            Click(window, OnFile(view, "DetailsFiles", guide, window));

            // The list is built again, through no selection, on the way to the same file.
            model.Files.SearchText = "guide";
            Settle(window);
            model.Files.SearchText = string.Empty;
            Settle(window);

            Assert.Equal("docs/guide.md", model.Files.SelectedNode?.Path);
            Assert.True(model.IsDiffViewOpen);

            window.Close();
        });
    }

    // ---------------------------------------------------------------- the working tree's files

    [Theory]
    [InlineData("UnstagedFiles", "README.md")]
    [InlineData("StagedFiles", "docs/staged.md")]
    public void TheSelectedWorkingTreeFile_LetsGoOnAClick_AndItsDiffGoesWithIt(string list, string path)
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view) = await ShowWorkingTreeAsync(services);

            CommitRowViewModel uncommitted = model.SelectedRow!;
            ChangedFilesPanelViewModel half = list == "UnstagedFiles" ? model.WorkingTree.Unstaged : model.WorkingTree.Staged;
            ChangedFileNodeViewModel file = FileNode(half, path);

            Click(window, OnFile(view, list, file, window));

            Assert.Equal(path, model.WorkingTree.SelectedChange?.File.Path);
            Assert.True(model.IsDiffViewOpen);

            // The same file again: it lets go, the diff goes, and the uncommitted line keeps its panel.
            Click(window, OnFile(view, list, file, window, 180));

            Assert.Null(half.SelectedNode);
            Assert.Null(model.WorkingTree.SelectedChange);
            Assert.False(model.IsDiffViewOpen);
            Assert.Same(uncommitted, model.SelectedRow);
            Assert.True(model.IsWorkingTreeShown);

            // And it comes back with the next click.
            Click(window, OnFile(view, list, file, window));

            Assert.Equal(path, model.WorkingTree.SelectedChange?.File.Path);
            Assert.True(model.IsDiffViewOpen);

            window.Close();
        });
    }

    [Fact]
    public void TheSelectedFilesStageButton_StagesIt_AndDoesNotLetGo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view) = await ShowWorkingTreeAsync(services);

            ChangedFileNodeViewModel readme = FileNode(model.WorkingTree.Unstaged, "README.md");
            Click(window, OnFile(view, "UnstagedFiles", readme, window));

            Assert.Same(readme, model.WorkingTree.Unstaged.SelectedNode);
            Assert.True(model.IsDiffViewOpen);

            // The row's own button, on the selected line: it stages the file, and the file stays picked
            // on its way to the other half, its diff still open.
            Button stage = RowButtonOf(view, "UnstagedFiles", readme, "Stage");
            Click(window, stage.TranslatePoint(new Point(stage.Bounds.Width / 2, stage.Bounds.Height / 2), window)!.Value);

            await WaitUntilAsync(() => model.WorkingTree.Staged.FileCount == 2);
            Settle(window);

            Assert.Equal(0, model.WorkingTree.Unstaged.FileCount);
            Assert.Equal("README.md", model.WorkingTree.Staged.SelectedNode?.Path);
            Assert.True(model.IsDiffViewOpen);

            window.Close();
        });
    }

    [Fact]
    public void AFileInTheOtherHalf_TakesTheSelection_AndTheDiffStaysOpenOnIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view) = await ShowWorkingTreeAsync(services);

            ChangedFileNodeViewModel readme = FileNode(model.WorkingTree.Unstaged, "README.md");
            ChangedFileNodeViewModel staged = FileNode(model.WorkingTree.Staged, "docs/staged.md");

            Click(window, OnFile(view, "UnstagedFiles", readme, window));
            Click(window, OnFile(view, "StagedFiles", staged, window, 180));

            Assert.Null(model.WorkingTree.Unstaged.SelectedNode);
            Assert.Same(staged, model.WorkingTree.Staged.SelectedNode);
            Assert.Equal("docs/staged.md", model.WorkingTree.SelectedChange?.File.Path);
            Assert.True(model.IsDiffViewOpen);

            window.Close();
        });
    }

    // ---------------------------------------------------------------- the panel's own toggle

    [Fact]
    public void ToggleSelection_ReleasesOnlyWhenTheSelectedLineIsClickedAgain()
    {
        _fixture.Run(() =>
        {
            // Two lines side by side, which is the list's shape: the tree would fold them under a
            // directory row.
            ChangedFilesPanelViewModel panel = new(new RecordingSystemInterop()) { ViewMode = ChangedFilesViewMode.List };
            panel.SetFiles(
            [
                new ChangedFile { Path = "docs/guide.md", ChangeKind = FileChangeKind.Added },
                new ChangedFile { Path = "docs/notes.md", ChangeKind = FileChangeKind.Added },
            ]);

            int released = 0;
            panel.SelectionReleased += (_, _) => released++;

            ChangedFileNodeViewModel guide = panel.Nodes[0];
            ChangedFileNodeViewModel notes = panel.Nodes[1];

            panel.ToggleSelection(guide);
            Assert.Same(guide, panel.SelectedNode);

            panel.ToggleSelection(notes);
            Assert.Same(notes, panel.SelectedNode);
            Assert.Equal(0, released);

            panel.ToggleSelection(notes);
            Assert.Null(panel.SelectedNode);
            Assert.Equal(1, released);

            // A rebuild that passes through nothing is not the reader letting go.
            panel.SelectPath("docs/guide.md");
            panel.SearchText = "notes";
            panel.SearchText = string.Empty;
            panel.SetFiles([]);

            Assert.Equal(1, released);
        });
    }

    // ---------------------------------------------------------------- set-up

    /// <summary>
    /// A repository whose last commit adds two files in one folder, shown on the history page with
    /// that commit's line selected and its files in the panel.
    /// </summary>
    private static async Task<(Window Window, HistoryPageViewModel Model, HistoryPageView View)> ShowCommitFilesAsync(TestServices services)
    {
        RepositoryHandle repository = await BuildRepositoryAsync(services);
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel model = services.Get<HistoryPageViewModel>();
        await model.ReloadAsync();

        HistoryPageView view = services.Get<HistoryPageView>();
        view.DataContext = model;

        Window window = new() { Content = view, Width = 1200, Height = 900 };
        window.Show();
        window.UpdateLayout();

        model.SelectedRow = model.Rows.First(row => row.Subject == "Add the guide and the notes");
        await WaitUntilAsync(() => model.Files.FileCount == 2);
        Settle(window);

        return (window, model, view);
    }

    /// <summary>
    /// The same repository with one change not staged (<c>README.md</c>) and one staged
    /// (<c>docs/staged.md</c>), shown with the uncommitted line selected and its working tree read.
    /// </summary>
    private static async Task<(Window Window, HistoryPageViewModel Model, HistoryPageView View)> ShowWorkingTreeAsync(TestServices services)
    {
        RepositoryHandle repository = await BuildRepositoryAsync(services);

        await WriteAsync(repository, "README.md", "# toggles\n\nmore\n");
        await WriteAsync(repository, "docs/staged.md", "staged\n");
        Git(repository, "add", "docs/staged.md");

        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel model = services.Get<HistoryPageViewModel>();
        await model.ReloadAsync();

        HistoryPageView view = services.Get<HistoryPageView>();
        view.DataContext = model;

        Window window = new() { Content = view, Width = 1200, Height = 900 };
        window.Show();
        window.UpdateLayout();

        model.SelectedRow = model.Rows.First(row => row.IsUncommitted);
        await WaitUntilAsync(() => model.WorkingTree.Unstaged.FileCount == 1 && model.WorkingTree.Staged.FileCount == 1);
        Settle(window);

        return (window, model, view);
    }

    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "toggles"), "main");

        await WriteAsync(repository, "README.md", "# toggles\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");

        await WriteAsync(repository, "docs/guide.md", "guide\n");
        await WriteAsync(repository, "docs/notes.md", "notes\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the guide and the notes");

        return repository;
    }

    private static async Task WriteAsync(RepositoryHandle repository, string path, string content)
    {
        string full = Path.Combine(repository.WorkTreePath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content);
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

    private static ChangedFileNodeViewModel FileNode(ChangedFilesPanelViewModel panel, string path)
        => ChangedFilesPanelViewModel.Flatten(panel.Nodes).Single(node => !node.IsDirectory && node.Path == path);

    /// <summary>
    /// A point on a line of one of the page's file lists, so far from its left edge.
    /// </summary>
    private static Point OnFile(Control page, string panelName, ChangedFileNodeViewModel node, Window window, double x = 60)
    {
        ChangedFilesPanelView panel = page.GetVisualDescendants()
            .OfType<ChangedFilesPanelView>()
            .Single(candidate => candidate.Name == panelName);

        Control item = panel.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control is ListBoxItem or TreeViewItem && control.IsEffectivelyVisible)
            .First(control => ReferenceEquals(control.DataContext, node));

        // Half way down the line's own row — a tree item's is above its children — and so far from the
        // item's left edge.
        Control row = item.GetVisualDescendants()
            .OfType<Control>()
            .First(control => control.ContextMenu is not null && ReferenceEquals(control.DataContext, node));

        Point middle = row.TranslatePoint(new Point(0, row.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The file's line is not in the window.");
        Point left = item.TranslatePoint(new Point(x, 0), window)
            ?? throw new InvalidOperationException("The file's line is not in the window.");

        return new Point(left.X, middle.Y);
    }

    private static Button RowButtonOf(Control page, string panelName, ChangedFileNodeViewModel node, string label)
        => page.GetVisualDescendants()
            .OfType<ChangedFilesPanelView>()
            .Single(candidate => candidate.Name == panelName)
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.IsEffectivelyVisible
                && ReferenceEquals(button.DataContext, node)
                && AutomationProperties.GetName(button) == label);

    private static ToggleButton ChevronOf(Control page, ChangedFileNodeViewModel folder)
    {
        TreeViewItem item = page.GetVisualDescendants()
            .OfType<TreeViewItem>()
            .First(candidate => ReferenceEquals(candidate.DataContext, folder));

        return item.GetVisualDescendants()
            .OfType<ToggleButton>()
            .First(button => ReferenceEquals(button.TemplatedParent, item));
    }

    /// <summary>
    /// A press and a release with the left button, and whatever they posted run.
    /// </summary>
    private static void Click(Window window, Point point, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
        Settle(window);
    }

    private static void Settle(Window window)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
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
}
