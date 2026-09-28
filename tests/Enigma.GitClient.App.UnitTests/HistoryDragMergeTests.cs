using System;
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
using Enigma.GitClient.App.Controls;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Dragging one branch badge of the history onto another, through the real input system, against
/// real git: the dragged branch is the source, the one it lands on is the destination.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryDragMergeTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryDragMergeTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void DroppingABranchOnAnother_OffersTheMergeAndTheFastForward_BothNamedInFull()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel page, HistoryPageView view) = await ShowAsync(services);

            try
            {
                Drag(window, Badge(view, "feature"), Badge(view, "main"));

                ContextMenu menu = Assert.IsType<ContextMenu>(view.DropMenu);
                MenuItem[] items = [.. menu.ItemsSource!.OfType<MenuItem>()];

                Assert.Equal(
                    ["Merge \"feature\" into \"main\"", "Merge \"feature\" into \"main\", fast-forward only"],
                    items.Select(item => item.Header?.ToString()));

                Assert.Same(page.MergeDropCommand, items[0].Command);
                Assert.Same(page.FastForwardDropCommand, items[1].Command);
                Assert.Equal(
                    [Enigma.Icons.Phosphor.PhosphorIcon.GitMerge, Enigma.Icons.Phosphor.PhosphorIcon.FastForward],
                    items.Select(item => Assert.IsType<Enigma.Icons.Avalonia.Icon>(item.Icon).Kind));

                HistoryBranchDrop drop = Assert.IsType<HistoryBranchDrop>(items[0].CommandParameter);
                Assert.Equal("feature", drop.Source.Name);
                Assert.Equal("main", drop.Target.Name);
                Assert.False(view.IsDragging);

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheMerge_RecordsAMergeCommitOnTheDestination()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel page, HistoryPageView view) = await ShowAsync(services);

            try
            {
                Drag(window, Badge(view, "feature"), Badge(view, "main"));
                HistoryBranchDrop drop = (HistoryBranchDrop)((MenuItem)view.DropMenu!.ItemsSource!.Cast<object>().First()).CommandParameter!;
                view.DropMenu.Close();

                await page.MergeDropCommand.ExecuteAsync(drop);

                RepositoryHandle repository = services.Get<IRepositoryContext>().Repository!;
                string[] parents = Git(repository, "rev-list", "--parents", "-n", "1", "main").Split(' ');

                // The commit and its two parents: a merge, although a fast-forward would have done.
                Assert.Equal(3, parents.Length);
                Assert.Equal(Git(repository, "rev-parse", "feature"), parents[2]);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheFastForward_MovesTheDestinationOntoTheSourceWithoutAMergeCommit()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel page, HistoryPageView view) = await ShowAsync(services);

            try
            {
                Drag(window, Badge(view, "feature"), Badge(view, "main"));
                HistoryBranchDrop drop = (HistoryBranchDrop)((MenuItem)view.DropMenu!.ItemsSource!.Cast<object>().Last()).CommandParameter!;
                view.DropMenu.Close();

                await page.FastForwardDropCommand.ExecuteAsync(drop);

                RepositoryHandle repository = services.Get<IRepositoryContext>().Repository!;
                Assert.Equal(Git(repository, "rev-parse", "feature"), Git(repository, "rev-parse", "main"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DroppingOnARemoteBranchOrOnItself_OffersNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, _, HistoryPageView view) = await ShowAsync(services);

            try
            {
                Drag(window, Badge(view, "feature"), Badge(view, "origin/main"));
                Assert.Null(view.DropMenu);

                RefBadge feature = Badge(view, "feature");
                Drag(window, feature, feature);
                Assert.Null(view.DropMenu);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ADrag_RingsTheBadgeItWouldLandOn_AndEscapeCancelsIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, _, HistoryPageView view) = await ShowAsync(services);

            try
            {
                RefBadge source = Badge(view, "feature");
                RefBadge target = Badge(view, "main");
                Point start = Centre(source, window);

                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(new Point(start.X + 8, start.Y + 8), RawInputModifiers.LeftMouseButton);
                window.MouseMove(Centre(target, window), RawInputModifiers.LeftMouseButton);

                Assert.True(view.IsDragging);
                Assert.Contains("droptarget", target.Classes);

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

                Assert.False(view.IsDragging);
                Assert.DoesNotContain("droptarget", target.Classes);

                window.MouseUp(Centre(target, window), MouseButton.Left);
                Assert.Null(view.DropMenu);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<(Window Window, HistoryPageViewModel Page, HistoryPageView View)> ShowAsync(TestServices services)
    {
        RepositoryHandle repository = await BuildRepositoryAsync(services);
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
        await page.ReloadAsync();

        HistoryPageView view = services.Get<HistoryPageView>();
        view.DataContext = page;

        Window window = new() { Content = view, Width = 1200, Height = 700 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return (window, page, view);
    }

    /// <summary>
    /// main, a feature branch one commit ahead of it, and a remote-tracking <c>origin/main</c>.
    /// </summary>
    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "drag"), "main");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");
        Git(repository, "update-ref", "refs/remotes/origin/main", "HEAD");

        Git(repository, "checkout", "-b", "feature");
        File.WriteAllText(Path.Combine(repository.WorkTreePath, "feature.txt"), "feature\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Start the feature");
        Git(repository, "checkout", "main");

        return repository;
    }

    private static RefBadge Badge(HistoryPageView view, string name)
        => view.GetVisualDescendants()
            .OfType<RefBadge>()
            .First(badge => badge.DataContext is HistoryBranchViewModel branch && branch.Name == name);

    private static void Drag(Window window, Visual from, Visual to)
    {
        Point start = Centre(from, window);
        Point end = Centre(to, window);

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X + 8, start.Y + 8), RawInputModifiers.LeftMouseButton);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
    }

    private static Point Centre(Visual target, Visual relativeTo)
        => target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), relativeTo)
            ?? throw new InvalidOperationException("The badge is not in the same tree as the window.");

    private static string Git(RepositoryHandle repository, params string[] arguments)
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
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? output.Trim()
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }
}
