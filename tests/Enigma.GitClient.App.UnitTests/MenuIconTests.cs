using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Repositories;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Every action of every context menu carries a glyph for its kind of action.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class MenuIconTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public MenuIconTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void EveryItemOfAHistoryLinesMenu_HasAGlyph()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            // A stash line, an uncommitted line and commits with branches on them: every kind of menu.
            File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# stashed\n");
            Git(repository, "stash", "push", "-m", "Work");
            File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# dirty\n");

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            page.BranchCommands.SetAsMergeSource.Execute(page.Rows.SelectMany(row => row.Branches).First(branch => branch.Name == "feature"));

            Assert.Contains(page.Rows, row => row.IsStash);
            Assert.Contains(page.Rows, row => row.IsUncommitted);

            foreach (CommitRowViewModel row in page.Rows)
            {
                Assert.All(
                    row.MenuEntries.Where(entry => !entry.IsSeparator),
                    entry => Assert.True(entry.HasIcon, $"\"{entry.Header}\" has no glyph"));
            }

            HistoryMenuEntry show = page.Rows.First(row => row.Commit is not null).MenuEntries[0];
            Assert.Equal(PhosphorIcon.GitDiff, show.Icon);
        });
    }

    [Fact]
    public void AHistoryLinesMenu_DrawsItsGlyphs()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await BuildRepositoryAsync(services);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            HistoryPageView view = services.Get<HistoryPageView>();
            view.DataContext = page;

            Window window = Show(view);

            try
            {
                Grid line = view.GetVisualDescendants().OfType<Grid>().First(grid => grid.ContextMenu is not null && grid.DataContext is CommitRowViewModel);
                ContextMenu menu = line.ContextMenu!;
                menu.Open(line);
                Render(window);

                MenuItem[] items = [.. menu.GetLogicalDescendants().OfType<MenuItem>().Where(item => item.Header as string != "-")];

                Assert.NotEmpty(items);
                Assert.All(items, item =>
                {
                    Icon icon = Assert.IsType<Icon>(item.Icon);
                    Assert.True(icon.IsVisible, $"\"{item.Header}\" draws no glyph");
                    Assert.Contains("menu", icon.Classes);
                });
                Assert.Equal(PhosphorIcon.GitDiff, ((Icon)items[0].Icon!).Kind);

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheBadgesMenus_HaveAGlyphOnEveryItem()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await BuildRepositoryAsync(services);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            HistoryPageView view = services.Get<HistoryPageView>();
            view.DataContext = page;

            Window window = Show(view);

            try
            {
                ContextMenu[] menus = [.. view.GetVisualDescendants()
                    .OfType<Enigma.GitClient.App.Controls.RefBadge>()
                    .Select(badge => badge.ContextMenu)
                    .OfType<ContextMenu>()];

                Assert.True(menus.Length >= 2, "the history drew no branch and tag badges with menus");
                AssertEveryItemHasAGlyph(menus);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheDialogsAndTheChangesPage_HaveAGlyphOnEveryMenuItem()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            Git(repository, "remote", "add", "origin", Path.Combine(services.ConfigurationRoot, "elsewhere.git"));
            File.WriteAllText(Path.Combine(repository.WorkTreePath, "notes.txt"), "put aside\n");
            Git(repository, "stash", "push", "--include-untracked", "-m", "Nothing much");
            File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# edited\n");
            await services.Get<IRepositoryContext>().RefreshAsync();

            BranchesPageViewModel branches = services.Get<BranchesPageViewModel>();
            await branches.OnAppearingAsync();
            TagsPageViewModel tags = services.Get<TagsPageViewModel>();
            await tags.OnAppearingAsync();
            RemotesPageViewModel remotes = services.Get<RemotesPageViewModel>();
            await remotes.OnAppearingAsync();
            ChangesPageViewModel changes = services.Get<ChangesPageViewModel>();
            await changes.OnAppearingAsync();
            await WaitUntilAsync(() => remotes.Remotes.Count > 0 && changes.HasUnstaged && changes.HasStashes);

            foreach ((Control view, object model) in new (Control, object)[]
            {
                (services.Get<BranchesPageView>(), branches),
                (services.Get<TagsPageView>(), tags),
                (services.Get<RemotesPageView>(), remotes),
                (services.Get<ChangesPageView>(), changes),
            })
            {
                view.DataContext = model;
                Window window = Show(view);

                try
                {
                    ContextMenu[] menus = [.. view.GetVisualDescendants()
                        .OfType<Control>()
                        .Select(control => control.ContextMenu)
                        .OfType<ContextMenu>()
                        .Distinct()];

                    Assert.True(menus.Length > 0, $"{view.GetType().Name} drew no line with a menu");
                    AssertEveryItemHasAGlyph(menus);
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    private static void AssertEveryItemHasAGlyph(IEnumerable<ContextMenu> menus)
    {
        foreach (ContextMenu menu in menus)
        {
            foreach (MenuItem item in menu.Items.OfType<MenuItem>())
            {
                Assert.True(item.Icon is Control, $"\"{item.Header}\" has no glyph");
                Assert.NotEmpty(((Control)item.Icon!).GetSelfAndLogicalDescendants().OfType<Icon>());
            }
        }
    }

    private static Window Show(Control view)
    {
        Window window = new() { Content = view, Width = 1200, Height = 700 };
        window.Show();
        Render(window);

        return window;
    }

    private static void Render(Window window)
    {
        window.UpdateLayout();

        for (int attempt = 0; attempt < 5; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
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

    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "icons"), "main");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");
        Git(repository, "tag", "v1.0");
        Git(repository, "branch", "feature");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "app.txt"), "two\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the application");

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
