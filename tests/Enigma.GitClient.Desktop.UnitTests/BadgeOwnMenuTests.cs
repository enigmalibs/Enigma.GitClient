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
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A branch or a tag badge's menu holds the badge's own items only: the line's items — about the
/// commit, not the badge — stay on the line's menu (FEATURE-83E5, which takes back FEATURE-F452's
/// appended items). Against real git.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class BadgeOwnMenuTests
{
    /// <summary>The line's items, none of which a badge's menu may carry.</summary>
    private static readonly string[] LinesItems =
    [
        "Show what it changed",
        "Create branch here…",
        "Create tag here…",
        "Check out this commit (detaches HEAD)",
        "Copy short commit hash",
        "Copy full commit hash",
    ];

    private readonly HeadlessAvaloniaFixture _fixture;

    public BadgeOwnMenuTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void ABranchBadgesMenu_HoldsTheBadgesItemsOnly()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAsync(services);
            (Window window, HistoryPageView view, _) = await ShowAsync(services, repository);

            try
            {
                ContextMenu menu = Request(BranchBadge(view, "topic"));

                // Its own items, ending with the name to copy — nothing after it.
                Assert.Equal("Copy branch name", Shown(menu).Last().Header as string);
                Assert.IsType<MenuItem>(menu.Items.Cast<object>().Last());
                Assert.Contains(Shown(menu), item => (item.Header as string) == "Set \"topic\" as merge source");

                Assert.DoesNotContain(Shown(menu), item => LinesItems.Contains(item.Header as string));

                // Another branch of the line is the line's business, not this badge's.
                Assert.DoesNotContain(Shown(menu), item => (item.Header as string) == "Set \"main\" as merge source");

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ATagBadgesMenu_HoldsTheBadgesItemsOnly()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAsync(services);
            (Window window, HistoryPageView view, _) = await ShowAsync(services, repository);

            try
            {
                RefBadge badge = view.GetVisualDescendants()
                    .OfType<RefBadge>()
                    .Single(candidate => candidate.DataContext is HistoryTagViewModel { Name: "v1" });

                ContextMenu menu = Request(badge);

                Assert.Equal("Copy tag name", Shown(menu).Last().Header as string);
                Assert.IsType<MenuItem>(menu.Items.Cast<object>().Last());
                Assert.DoesNotContain(Shown(menu), item => LinesItems.Contains(item.Header as string));
                Assert.DoesNotContain(Shown(menu), item => (item.Header as string)?.StartsWith("Set \"", StringComparison.Ordinal) == true);

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void RequestingTheMenuAgain_ChangesNothing()
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
                object[] first = [.. menu.Items.Cast<object>()];
                menu.Close();
                Dispatcher.UIThread.RunJobs();

                menu = Request(badge);

                Assert.Equal(first, menu.Items.Cast<object>());

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheLinesOwnMenu_KeepsEveryItem()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAsync(services);
            (Window window, HistoryPageView view, HistoryPageViewModel page) = await ShowAsync(services, repository);

            try
            {
                CommitRowViewModel line = page.Rows.Single(row => row.Subject == "Add the readme");

                Request(BranchBadge(view, "topic")).Close();

                string[] headers = [.. line.MenuEntries.Where(entry => !entry.IsSeparator).Select(entry => entry.Header)];

                Assert.All(LinesItems, header => Assert.Contains(header, headers));
                Assert.Contains("Set \"topic\" as merge source", headers);
                Assert.Contains("Set \"main\" as merge source", headers);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Right-clicks a badge, as the page sees it: the request tunnels through the page, then the badge's
    /// menu opens.
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
