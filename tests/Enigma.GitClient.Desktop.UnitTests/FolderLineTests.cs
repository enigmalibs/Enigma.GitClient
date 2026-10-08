using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Enigma.GitClient.Desktop.Views.Panels;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A folder line in the files panel is never selected: a click folds or unfolds it, and the file that
/// was selected keeps the selection — and a click on a file takes it on the first try, wherever the
/// selection was before (BUG-1B14).
/// </summary>
/// <remarks>
/// The panel alone, on files that no repository holds: what is tested is the view and its view model.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class FolderLineTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public FolderLineTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void AClickOnAFolder_FoldsIt_AndSelectsNothing()
    {
        _fixture.Run(() =>
        {
            (Window window, ChangedFilesPanelViewModel panel, ChangedFilesPanelView view) = Show();

            try
            {
                ChangedFileNodeViewModel docs = Folder(panel, "docs");
                Assert.True(docs.IsExpanded);

                Click(window, OnLine(view, docs, window));

                Assert.False(docs.IsExpanded);
                Assert.Null(panel.SelectedNode);

                // Further along the line, so it is a second click and not a double-click.
                Click(window, OnLine(view, docs, window, 140));

                Assert.True(docs.IsExpanded);
                Assert.Null(panel.SelectedNode);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void WithAFileSelected_AClickOnAFolder_KeepsTheFile()
    {
        _fixture.Run(() =>
        {
            (Window window, ChangedFilesPanelViewModel panel, ChangedFilesPanelView view) = Show();

            try
            {
                ChangedFileNodeViewModel program = File(panel, "src/app/Program.cs");
                ChangedFileNodeViewModel docs = Folder(panel, "docs");

                Click(window, OnLine(view, program, window));
                Assert.Same(program, panel.SelectedNode);

                int changes = 0;
                panel.SelectionChanged += (_, _) => changes++;

                Click(window, OnLine(view, docs, window, 140));

                Assert.Same(program, panel.SelectedNode);
                Assert.Equal(0, changes);

                // The tree shows the file as selected still, not the folder.
                Assert.Same(program, Tree(view).SelectedItem);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ARootFileThenANestedFile_TheNestedFileTakesTheSelectionOnTheFirstClick()
    {
        _fixture.Run(() =>
        {
            (Window window, ChangedFilesPanelViewModel panel, ChangedFilesPanelView view) = Show();

            try
            {
                ChangedFileNodeViewModel readme = File(panel, "README.md");
                ChangedFileNodeViewModel guide = File(panel, "docs/guide.md");

                // The root file is one the hidden list holds too; the nested one is not.
                Click(window, OnLine(view, readme, window));
                Assert.Same(readme, panel.SelectedNode);

                Click(window, OnLine(view, guide, window, 140));

                Assert.Same(guide, panel.SelectedNode);
                Assert.Same(guide, Tree(view).SelectedItem);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ADoubleClickOnAFolder_FoldsItOnce()
    {
        _fixture.Run(() =>
        {
            (Window window, ChangedFilesPanelViewModel panel, ChangedFilesPanelView view) = Show();

            try
            {
                ChangedFileNodeViewModel docs = Folder(panel, "docs");
                Point point = OnLine(view, docs, window);

                Click(window, point);
                Click(window, point);

                Assert.False(docs.IsExpanded);
                Assert.Null(panel.SelectedNode);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheKeyboard_DoesNotSelectAFolder()
    {
        _fixture.Run(() =>
        {
            (Window window, ChangedFilesPanelViewModel panel, ChangedFilesPanelView view) = Show();

            try
            {
                ChangedFileNodeViewModel guide = File(panel, "docs/guide.md");
                Click(window, OnLine(view, guide, window));
                Assert.Same(guide, panel.SelectedNode);

                // The line above the guide is its folder's.
                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                Settle(window);

                Assert.Same(guide, panel.SelectedNode);
                Assert.Same(guide, Tree(view).SelectedItem);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ARightClickOnAFolder_OpensItsMenu_AndSelectsNothing()
    {
        _fixture.Run(() =>
        {
            (Window window, ChangedFilesPanelViewModel panel, ChangedFilesPanelView view) = Show();

            try
            {
                ChangedFileNodeViewModel docs = Folder(panel, "docs");
                Point point = OnLine(view, docs, window);

                window.MouseDown(point, MouseButton.Right);
                window.MouseUp(point, MouseButton.Right);
                Settle(window);

                Control row = RowOf(view, docs);
                Assert.True(row.ContextMenu!.IsOpen);
                Assert.Null(panel.SelectedNode);
                Assert.True(docs.IsExpanded);

                row.ContextMenu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- set-up

    /// <summary>
    /// A file at the root and files in three folders, shown as the tree, every folder open.
    /// </summary>
    private static (Window Window, ChangedFilesPanelViewModel Panel, ChangedFilesPanelView View) Show()
    {
        ChangedFilesPanelViewModel panel = new(new RecordingSystemInterop()) { ViewMode = ChangedFilesViewMode.Tree };
        panel.SetFiles(
        [
            new ChangedFile { Path = "README.md", ChangeKind = FileChangeKind.Modified },
            new ChangedFile { Path = "docs/guide.md", ChangeKind = FileChangeKind.Added },
            new ChangedFile { Path = "docs/notes.md", ChangeKind = FileChangeKind.Added },
            new ChangedFile { Path = "src/app/Program.cs", ChangeKind = FileChangeKind.Added },
            new ChangedFile { Path = "src/app/Legacy.cs", ChangeKind = FileChangeKind.Deleted },
        ]);

        ChangedFilesPanelView view = new() { DataContext = panel };
        Window window = new() { Content = view, Width = 420, Height = 480 };
        window.Show();
        Settle(window);

        return (window, panel, view);
    }

    private static ChangedFileNodeViewModel File(ChangedFilesPanelViewModel panel, string path)
        => ChangedFilesPanelViewModel.Flatten(panel.Nodes).Single(node => !node.IsDirectory && node.Path == path);

    private static ChangedFileNodeViewModel Folder(ChangedFilesPanelViewModel panel, string path)
        => ChangedFilesPanelViewModel.Flatten(panel.Nodes).Single(node => node.IsDirectory && node.Path == path);

    private static TreeView Tree(ChangedFilesPanelView view)
        => view.GetVisualDescendants().OfType<TreeView>().Single();

    /// <summary>
    /// The line's own row: a tree item's is above its children, and carries the line's menu.
    /// </summary>
    private static Control RowOf(ChangedFilesPanelView view, ChangedFileNodeViewModel node)
        => view.GetVisualDescendants()
            .OfType<Control>()
            .First(control => control.ContextMenu is not null && ReferenceEquals(control.DataContext, node));

    /// <summary>
    /// A point half way down a line's row, so far from its item's left edge: on its name, past the
    /// chevron.
    /// </summary>
    private static Point OnLine(ChangedFilesPanelView view, ChangedFileNodeViewModel node, Window window, double x = 80)
    {
        TreeViewItem item = view.GetVisualDescendants()
            .OfType<TreeViewItem>()
            .First(candidate => ReferenceEquals(candidate.DataContext, node));

        Control row = RowOf(view, node);

        Point middle = row.TranslatePoint(new Point(0, row.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The line is not in the window.");
        Point left = item.TranslatePoint(new Point(x, 0), window)
            ?? throw new InvalidOperationException("The line is not in the window.");

        return new Point(left.X, middle.Y);
    }

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
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
}
