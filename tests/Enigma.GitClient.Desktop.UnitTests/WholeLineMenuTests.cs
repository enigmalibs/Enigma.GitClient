using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
/// A line's menu opens wherever the line is right-clicked — in its padding, between its columns — and
/// not only over its text.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class WholeLineMenuTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public WholeLineMenuTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void ABranchLine_OpensItsMenuAnywhereOnIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
            await page.OnAppearingAsync();

            BranchesPageView view = services.Get<BranchesPageView>();
            view.DataContext = page;

            AssertTheWholeCompactLineOpensItsMenu<BranchRowViewModel>(view);
        });
    }

    [Fact]
    public void ATagLine_OpensItsMenuAnywhereOnIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await BuildRepositoryAsync(services);

            TagsPageViewModel page = services.Get<TagsPageViewModel>();
            await page.OnAppearingAsync();

            TagsPageView view = services.Get<TagsPageView>();
            view.DataContext = page;

            AssertTheWholeCompactLineOpensItsMenu<TagRowViewModel>(view);
        });
    }

    [Fact]
    public void ARemoteLine_OpensItsMenuAnywhereOnIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await BuildRepositoryAsync(services);

            RemotesPageViewModel page = services.Get<RemotesPageViewModel>();
            await page.OnAppearingAsync();
            await WaitUntilAsync(() => page.Remotes.Count > 0);

            RemotesPageView view = services.Get<RemotesPageView>();
            view.DataContext = page;

            AssertTheWholeCompactLineOpensItsMenu<RemoteRowViewModel>(view);
        });
    }

    [Fact]
    public void AChangedFileLine_OpensItsMenuBetweenItsColumns()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# edited\n");

            WorkingTreePanelViewModel page = services.Get<WorkingTreePanelViewModel>();
            page.IsActive = true;
            await page.RefreshAsync();
            await WaitUntilAsync(() => page.HasUnstaged);

            WorkingTreePanelView view = new() { DataContext = page };

            Window window = new() { Content = view, Width = 1200, Height = 700 };
            window.Show();

            try
            {
                Render(window);

                Grid line = view.GetVisualDescendants()
                    .OfType<Grid>()
                    .First(grid => grid.ContextMenu is not null && grid.DataContext is ChangedFileNodeViewModel);

                Point gap = GapAfterFirstChild(line);

                Assert.Same(line, MenuOwnerAt(window, line.TranslatePoint(gap, window)));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(ChangedFilesViewMode.List)]
    [InlineData(ChangedFilesViewMode.Tree)]
    public void AFileLine_OpensItsMenuAnywhereOnIt(ChangedFilesViewMode mode)
    {
        _fixture.Run(() =>
        {
            ChangedFilesPanelViewModel panel = new(new RecordingSystemInterop()) { ViewMode = mode };
            panel.SetFiles(
            [
                new ChangedFile { Path = "README.md", AddedLines = 3, RemovedLines = 1, HasLineCounts = true },
                new ChangedFile { Path = "src/app/Program.cs", AddedLines = 20, HasLineCounts = true, ChangeKind = FileChangeKind.Added },
                new ChangedFile { Path = "src/lib/Engine.cs", RemovedLines = 4, HasLineCounts = true },
            ]);

            ChangedFilesPanelView view = new() { DataContext = panel };
            Window window = new() { Content = view, Width = 420, Height = 500 };
            window.Show();

            try
            {
                Render(window);

                // In the tree, a file under a folder: its line starts with the indentation.
                Grid line = FileLines(view).First(row => mode == ChangedFilesViewMode.List || ((ChangedFileNodeViewModel)row.DataContext!).Path == "src/app/Program.cs");

                AssertTheWholeFileLineOpensItsMenu(window, line);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheWorkingTreesFileLines_OpenTheirMenusAnywhereOnThem()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# edited\n");
            File.WriteAllText(Path.Combine(repository.WorkTreePath, "staged.txt"), "staged\n");
            Git(repository, "add", "staged.txt");

            WorkingTreePanelViewModel page = services.Get<WorkingTreePanelViewModel>();
            page.IsActive = true;
            await page.RefreshAsync();
            await WaitUntilAsync(() => page.HasUnstaged && page.HasStaged);

            WorkingTreePanelView view = new() { DataContext = page };

            Window window = new() { Content = view, Width = 1200, Height = 700 };
            window.Show();

            try
            {
                Render(window);

                // Not staged, then staged: the two panels are the same control.
                Grid[] lines = [.. FileLines(view)];
                Assert.Equal(2, lines.Length);

                foreach (Grid line in lines)
                {
                    AssertTheWholeFileLineOpensItsMenu(window, line);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheHistoryPanelsFileLines_OpenTheirMenusAnywhereOnThem()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await BuildRepositoryAsync(services);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            HistoryPageView view = services.Get<HistoryPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1200, Height = 800 };
            window.Show();

            try
            {
                page.SelectedRow = page.Rows.First(row => row.Commit is not null);
                await WaitUntilAsync(() => page.Files.Nodes.Count > 0);
                Render(window);

                Border panel = view.FindControl<Border>("DetailsPanel")
                    ?? throw new InvalidOperationException("The history page has no details panel.");
                Assert.True(panel.IsVisible);

                AssertTheWholeFileLineOpensItsMenu(window, FileLines(panel).First());
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Shows a page whose lines are drawn like the changed files, and right-clicks one of them
    /// everywhere a file line is right-clicked: the container's padding included.
    /// </summary>
    private static void AssertTheWholeCompactLineOpensItsMenu<TRow>(Control view)
    {
        Window window = new() { Content = view, Width = 1100, Height = 600 };
        window.Show();

        try
        {
            Render(window);

            Grid line = view.GetVisualDescendants()
                .OfType<Grid>()
                .First(grid => grid.ContextMenu is not null && grid.DataContext is TRow);

            AssertTheWholeFileLineOpensItsMenu(window, line);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The file lines of the changed-files panels under a control, in order: the row grid that carries
    /// each line's menu.
    /// </summary>
    private static IEnumerable<Grid> FileLines(Control root)
        => root.GetVisualDescendants()
            .OfType<Grid>()
            .Where(grid => grid.ContextMenu is not null && grid.DataContext is ChangedFileNodeViewModel { IsDirectory: false } && grid.IsEffectivelyVisible);

    /// <summary>
    /// Right-clicks a file line — for real, pointer down and up — in the corner of its container's
    /// padding (the indentation, in a tree), at the right end of the line, in the gap after its first
    /// column and over its name, and checks each one opens that line's own menu.
    /// </summary>
    private static void AssertTheWholeFileLineOpensItsMenu(Window window, Grid line)
    {
        Control container = line.GetVisualAncestors().OfType<Control>().First(control => control is ListBoxItem or TreeViewItem);
        ContextMenu menu = line.ContextMenu!;

        Point rowTop = line.TranslatePoint(default, container) ?? throw new InvalidOperationException("The line is not in its container.");
        double middle = rowTop.Y + (line.Bounds.Height / 2);

        TextBlock name = line.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text is { Length: > 0 });

        (string Where, Point At)[] points =
        [
            ("in the container's corner", container.TranslatePoint(new Point(2, 2), window) ?? default),
            ("at the start of the line", container.TranslatePoint(new Point(2, middle), window) ?? default),
            ("at the end of the line", container.TranslatePoint(new Point(container.Bounds.Width - 3, middle), window) ?? default),
            ("between its columns", line.TranslatePoint(GapAfterFirstChild(line), window) ?? default),
            ("over its name", name.TranslatePoint(new Point(2, name.Bounds.Height / 2), window) ?? default),
        ];

        foreach ((string where, Point at) in points)
        {
            window.MouseDown(at, MouseButton.Right);
            window.MouseUp(at, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();

            Assert.True(menu.IsOpen, $"a right-click {where} did not open the line's menu");

            menu.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Point GapAfterFirstChild(Grid grid)
    {
        Control first = grid.Children.Where(child => child.IsVisible).OrderBy(child => child.Bounds.Left).First();
        Point gap = new(first.Bounds.Right + 3, grid.Bounds.Height / 2);

        Assert.False(
            grid.Children.Any(child => child.IsVisible && child.Bounds.Contains(gap)),
            "the point picked for the test is inside a cell, so it proves nothing");

        return gap;
    }

    private static Control? MenuOwnerAt(Window window, Point? point)
        => (window.InputHitTest(point ?? throw new InvalidOperationException("The point is not in the window.")) as Visual)?
            .GetSelfAndVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(control => control.ContextMenu is not null);

    private static void Render(Window window)
    {
        window.UpdateLayout();

        for (int attempt = 0; attempt < 10; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            Dispatcher.UIThread.RunJobs();
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
    /// A repository with a branch, a tag and a remote, opened.
    /// </summary>
    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "menus"), "main");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");
        Git(repository, "branch", "feature");
        Git(repository, "tag", "v1.0");
        Git(repository, "remote", "add", "origin", Path.Combine(root, "elsewhere.git"));

        await services.Get<IRepositoryContext>().OpenAsync(repository);

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
