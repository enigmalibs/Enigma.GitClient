using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
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
    public void InTheTree_TheSelectedFileLetsGo_AndAFoldersChevronOnlyFoldsIt()
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

            // A selected folder lets go on a click too.
            Click(window, OnFile(view, "DetailsFiles", folder, window, 120));
            Assert.Same(folder, model.Files.SelectedNode);

            Click(window, OnFile(view, "DetailsFiles", folder, window, 200));
            Assert.Null(model.Files.SelectedNode);

            // Its chevron is a button: it folds the folder and leaves the selection where it is.
            Click(window, OnFile(view, "DetailsFiles", folder, window, 120));
            Assert.Same(folder, model.Files.SelectedNode);
            Assert.True(folder.IsExpanded);

            ToggleButton chevron = ChevronOf(view, folder);
            Click(window, chevron.TranslatePoint(new Point(chevron.Bounds.Width / 2, chevron.Bounds.Height / 2), window)!.Value);

            Assert.False(folder.IsExpanded);
            Assert.Same(folder, model.Files.SelectedNode);

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

    // ---------------------------------------------------------------- the panel's own toggle

    [Fact]
    public void ToggleSelection_ReleasesOnlyWhenTheSelectedLineIsClickedAgain()
    {
        _fixture.Run(() =>
        {
            ChangedFilesPanelViewModel panel = new(new RecordingSystemInterop());
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
