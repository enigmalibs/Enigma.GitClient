using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Controls;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons.Avalonia;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A branch or a tag badge's menu ends with the line's own items, under the badge's, so a right-click
/// on a badge reaches the line's actions too (FEATURE-F452). Against real git.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class BadgeLineMenuTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public BadgeLineMenuTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- the entries

    [Fact]
    public void TheEntries_StartWithASeparator_LeaveOutTheBadgesOwn_AndNeverDoubleOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAsync(services);
            CommitRowViewModel line = await LineAsync(services, repository);

            HistoryBranchViewModel topic = line.Branches.Single(branch => branch.Name == "topic");
            IReadOnlyList<HistoryMenuEntry> entries = line.BadgeMenuEntries(topic);

            Assert.True(entries[0].IsSeparator);
            Assert.False(entries[^1].IsSeparator);
            Assert.DoesNotContain(entries.Zip(entries.Skip(1)), pair => pair.First.IsSeparator && pair.Second.IsSeparator);

            // The badge offers its own "set as merge source"; the line's other branch is still named.
            Assert.DoesNotContain(entries, entry => ReferenceEquals(entry.Parameter, topic));
            Assert.Contains(entries, entry => entry.Header == "Set \"main\" as merge source");

            // Everything else the line offers is there, in the line's order.
            string[] expected = [.. line.MenuEntries.Where(entry => !entry.IsSeparator && !ReferenceEquals(entry.Parameter, topic)).Select(entry => entry.Header)];
            Assert.Equal(expected, entries.Where(entry => !entry.IsSeparator).Select(entry => entry.Header));
        });
    }

    // ---------------------------------------------------------------- the menus

    [Fact]
    public void ABranchBadgesMenu_EndsWithTheLinesItems()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAsync(services);
            (Window window, HistoryPageView view, HistoryPageViewModel page) = await ShowAsync(services, repository);

            try
            {
                RefBadge badge = BranchBadge(view, "topic");
                CommitRowViewModel line = page.Rows.Single(row => row.Subject == "Add the readme");
                ContextMenu menu = Request(badge);

                object[] items = [.. menu.Items.Cast<object>()];
                MenuItem copyName = items.OfType<MenuItem>().Single(item => (item.Header as string) == "Copy branch name");

                // The badge's own items come first, then a separator, then the line's.
                Assert.IsType<Separator>(items[Array.IndexOf(items, copyName) + 1]);

                MenuItem create = Item(menu, "Create branch here…");
                Assert.Same(line.Commands!.CreateBranchHere, create.Command);
                Assert.Same(line, create.CommandParameter);
                Assert.True(Array.IndexOf(items, create) > Array.IndexOf(items, copyName));

                Assert.Same(line.Commands.CreateTagHere, Item(menu, "Create tag here…").Command);
                Assert.Same(line.Commands.CheckoutCommit, Item(menu, "Check out this commit (detaches HEAD)").Command);
                Assert.Equal(line.ShortSha, Item(menu, "Copy short commit hash").CommandParameter);
                Assert.Equal(line.Sha, Item(menu, "Copy full commit hash").CommandParameter);

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ATagBadgesMenu_EndsWithTheLinesItems()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAsync(services);
            (Window window, HistoryPageView view, HistoryPageViewModel page) = await ShowAsync(services, repository);

            try
            {
                RefBadge badge = view.GetVisualDescendants()
                    .OfType<RefBadge>()
                    .Single(candidate => candidate.DataContext is HistoryTagViewModel { Name: "v1" });

                CommitRowViewModel line = page.Rows.Single(row => row.Subject == "Add the readme");
                ContextMenu menu = Request(badge);

                object[] items = [.. menu.Items.Cast<object>()];
                MenuItem copyName = items.OfType<MenuItem>().Single(item => (item.Header as string) == "Copy tag name");
                Assert.IsType<Separator>(items[Array.IndexOf(items, copyName) + 1]);

                Assert.Same(line.Commands!.CreateBranchHere, Item(menu, "Create branch here…").Command);
                Assert.Same(line, Item(menu, "Show what it changed").CommandParameter);

                // A tag adds the line's branches' items too.
                Assert.NotNull(Item(menu, "Set \"topic\" as merge source"));

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ABadgesOwnItem_IsNotRepeatedByTheLines()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAsync(services);
            (Window window, HistoryPageView view, _) = await ShowAsync(services, repository);

            try
            {
                ContextMenu menu = Request(BranchBadge(view, "topic"));

                Assert.Single(Shown(menu), item => (item.Header as string) == "Set \"topic\" as merge source");
                Assert.Single(Shown(menu), item => (item.Header as string) == "Set \"main\" as merge source");

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OpeningTheMenuAgain_DoesNotAddTheLinesItemsTwice()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAsync(services);
            (Window window, HistoryPageView view, _) = await ShowAsync(services, repository);

            try
            {
                RefBadge badge = BranchBadge(view, "topic");

                ContextMenu menu = Request(badge);
                int count = menu.Items.Count;
                menu.Close();
                Dispatcher.UIThread.RunJobs();

                menu = Request(badge);

                Assert.Equal(count, menu.Items.Count);
                Assert.Single(Shown(menu), item => (item.Header as string) == "Create branch here…");

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void EveryAppendedItem_HasAGlyph()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAsync(services);
            (Window window, HistoryPageView view, _) = await ShowAsync(services, repository);

            try
            {
                ContextMenu menu = Request(BranchBadge(view, "topic"));

                MenuItem[] appended = [.. menu.Items.OfType<MenuItem>().Where(item => ReferenceEquals(item.Tag, Views.BadgeMenus.LineItem))];

                Assert.NotEmpty(appended);
                Assert.All(appended, item => Assert.IsType<Icon>(item.Icon));

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheLinesOwnMenu_IsUnchanged()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAsync(services);
            (Window window, HistoryPageView view, HistoryPageViewModel page) = await ShowAsync(services, repository);

            try
            {
                CommitRowViewModel line = page.Rows.Single(row => row.Subject == "Add the readme");
                string[] before = [.. line.MenuEntries.Select(entry => entry.Header)];

                Request(BranchBadge(view, "topic")).Close();

                Assert.Equal(before, line.MenuEntries.Select(entry => entry.Header));
                Assert.Contains("Set \"topic\" as merge source", before);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Right-clicks a badge, as the page sees it: the request tunnels through the page, which adds the
    /// line's items, then the badge's menu opens.
    /// </summary>
    private static ContextMenu Request(RefBadge badge)
    {
        badge.RaiseEvent(new ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        ContextMenu menu = badge.ContextMenu!;

        if (!menu.IsOpen)
        {
            menu.Open(badge);
            Dispatcher.UIThread.RunJobs();
        }

        return menu;
    }

    private static IEnumerable<MenuItem> Shown(ContextMenu menu)
        => menu.Items.OfType<MenuItem>().Where(item => item.IsVisible);

    private static MenuItem Item(ContextMenu menu, string header)
        => Shown(menu).Single(item => (item.Header as string) == header);

    private static RefBadge BranchBadge(HistoryPageView view, string name)
        => view.GetVisualDescendants()
            .OfType<RefBadge>()
            .Single(badge => badge.DataContext is HistoryBranchViewModel branch && branch.Name == name);

    /// <summary>
    /// One commit, carrying <c>main</c> (checked out), <c>topic</c> and the tag <c>v1</c>.
    /// </summary>
    private static async Task<RepositoryHandle> BuildAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, "local"), "main");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");
        Git(repository, "branch", "topic");
        Git(repository, "tag", "v1");

        return repository;
    }

    private static async Task<CommitRowViewModel> LineAsync(TestServices services, RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
        await page.ReloadAsync();

        return page.Rows.Single(row => row.Subject == "Add the readme");
    }

    private static async Task<(Window Window, HistoryPageView View, HistoryPageViewModel Page)> ShowAsync(
        TestServices services,
        RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
        await page.ReloadAsync();

        HistoryPageView view = services.Get<HistoryPageView>();
        view.DataContext = page;

        Window window = new() { Content = view, Width = 1200, Height = 700 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return (window, view, page);
    }

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
