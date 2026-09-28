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
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.ViewModels.Panels;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

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

            AssertTheWholeLineOpensTheMenu<BranchRowViewModel>(view);
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

            AssertTheWholeLineOpensTheMenu<TagRowViewModel>(view);
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

            AssertTheWholeLineOpensTheMenu<RemoteRowViewModel>(view);
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

            ChangesPageViewModel page = services.Get<ChangesPageViewModel>();
            await page.OnAppearingAsync();
            await WaitUntilAsync(() => page.HasUnstaged);

            ChangesPageView view = services.Get<ChangesPageView>();
            view.DataContext = page;

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

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Shows the page and right-clicks — by hit test — a line of it in its left padding and in the gap
    /// after its first column, where no child is.
    /// </summary>
    private static void AssertTheWholeLineOpensTheMenu<TRow>(Control view)
    {
        Window window = new() { Content = view, Width = 1100, Height = 600 };
        window.Show();

        try
        {
            Render(window);

            Border line = view.GetVisualDescendants()
                .OfType<Border>()
                .First(border => border.ContextMenu is not null && border.DataContext is TRow);

            Grid columns = line.GetVisualChildren().OfType<Grid>().Single();

            // The border's own padding, left of the first column.
            Point padding = new(2, line.Bounds.Height / 2);
            Assert.Same(line, MenuOwnerAt(window, line.TranslatePoint(padding, window)));

            // The spacing between the first two columns, which carries no child.
            Point gap = GapAfterFirstChild(columns);
            Assert.Same(line, MenuOwnerAt(window, columns.TranslatePoint(gap, window)));

            // And over the text, as before.
            TextBlock text = line.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text is { Length: > 0 });
            Assert.Same(line, MenuOwnerAt(window, text.TranslatePoint(new Point(1, text.Bounds.Height / 2), window)));
        }
        finally
        {
            window.Close();
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
