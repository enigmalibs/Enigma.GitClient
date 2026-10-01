using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Controls;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Copying from the history: a branch's or a tag's name from its badge, a commit's hash from its line.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryCopyTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryCopyTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void ABranchBadge_CopiesTheBranchsName_AsTheBadgeShowsIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            HistoryPageViewModel page = await OpenAsync(services);

            HistoryBranchViewModel local = Branch(page, "feature");
            HistoryBranchViewModel remote = Branch(page, "origin/main");

            Assert.True(local.CanCopyName);

            await local.Commands.Copy!.ExecuteAsync(local.Name);
            await remote.Commands.Copy!.ExecuteAsync(remote.Name);

            Assert.Equal(["feature", "origin/main"], services.Interop.Copied);
        });
    }

    [Fact]
    public void ATagBadge_HasAMenuThatPushesTheTagAndCopiesItsName()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            HistoryPageViewModel page = await OpenAsync(services);

            HistoryTagViewModel tag = page.Rows.SelectMany(row => row.Badges).OfType<HistoryTagViewModel>().Single();
            Assert.Equal("v1.0", tag.Name);

            HistoryPageView view = services.Get<HistoryPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1200, Height = 600 };
            window.Show();

            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                RefBadge badge = view.GetVisualDescendants().OfType<RefBadge>().Single(candidate => candidate.DataContext is HistoryTagViewModel);
                ContextMenu menu = Assert.IsType<ContextMenu>(badge.ContextMenu);
                menu.Open(badge);

                // The branch badge's shape: the push first, a separator, the copy last.
                Control[] items = [.. menu.Items.OfType<Control>()];
                Assert.Equal(3, items.Length);

                MenuItem push = Assert.IsType<MenuItem>(items[0]);
                Assert.Equal("Push \"v1.0\"", push.Header);
                Assert.True(push.IsVisible);
                Assert.Equal(PhosphorIcon.ArrowUp, Assert.IsType<Icon>(push.Icon).Kind);
                Assert.Same(tag.Push, push.Command);
                Assert.Equal("v1.0", push.CommandParameter);

                Assert.IsType<Separator>(items[1]);

                MenuItem item = Assert.IsType<MenuItem>(items[2]);
                Assert.Equal("Copy tag name", item.Header);

                await ((IAsyncRelayCommand)item.Command!).ExecuteAsync(item.CommandParameter);
                menu.Close();

                Assert.Equal(["v1.0"], services.Interop.Copied);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ACommitsLine_CopiesItsShortAndItsFullHash()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            HistoryPageViewModel page = await OpenAsync(services);

            CommitRowViewModel line = page.Rows.First(row => row.Commit is not null);
            HistoryMenuEntry shortHash = line.MenuEntries.Single(entry => entry.Header == "Copy short commit hash");
            HistoryMenuEntry fullHash = line.MenuEntries.Single(entry => entry.Header == "Copy full commit hash");

            await ((IAsyncRelayCommand)shortHash.Command!).ExecuteAsync(shortHash.Parameter);
            await ((IAsyncRelayCommand)fullHash.Command!).ExecuteAsync(fullHash.Parameter);

            Assert.Equal([line.ShortSha, line.Sha], services.Interop.Copied);
            Assert.Equal(7, services.Interop.Copied[0].Length);
            Assert.Equal(40, services.Interop.Copied[1].Length);
        });
    }

    [Fact]
    public void TheUncommittedLine_HasNoHashToCopy()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            HistoryPageViewModel page = await OpenAsync(services, dirty: true);

            CommitRowViewModel uncommitted = page.Rows[0];
            Assert.True(uncommitted.IsUncommitted);
            Assert.DoesNotContain(uncommitted.MenuEntries, entry => entry.Header.StartsWith("Copy", StringComparison.Ordinal));
        });
    }

    // ---------------------------------------------------------------- helpers

    private static HistoryBranchViewModel Branch(HistoryPageViewModel page, string name)
        => page.Rows.SelectMany(row => row.Branches).Single(branch => branch.Name == name);

    private static async Task<HistoryPageViewModel> OpenAsync(TestServices services, bool dirty = false)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "copy"), "main");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");
        Git(repository, "tag", "v1.0");
        Git(repository, "branch", "feature");
        Git(repository, "update-ref", "refs/remotes/origin/main", "HEAD");

        if (dirty)
        {
            File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# edited\n");
        }

        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
        await page.ReloadAsync();

        return page;
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
