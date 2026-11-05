using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
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
/// A badge that stands for a local branch and its upstream deletes both at once, after one question
/// (FEATURE-70CB). Against real git, with a bare <c>origin</c>.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryDeleteBothTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryDeleteBothTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- the menu

    [Fact]
    public void AJoinedBadge_OffersTheThreeDeletes_BothLast()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);
            Git(repository, "branch", "topic");
            Git(repository, "push", "--set-upstream", "origin", "topic");
            (Window window, HistoryPageView view) = await ShowAsync(services, repository);

            try
            {
                RefBadge badge = Badge(view, "topic");
                HistoryBranchViewModel topic = Assert.IsType<HistoryBranchViewModel>(badge.DataContext);
                Assert.True(topic.CanDeleteWithRemote);

                MenuItem[] shown = Open(badge);

                MenuItem local = shown.Single(item => (item.Header as string) == "Delete \"topic\"…");
                MenuItem remote = shown.Single(item => (item.Header as string) == "Delete \"origin/topic\"…");
                MenuItem both = shown.Single(item => (item.Header as string) == "Delete \"topic\" and \"origin/topic\"…");

                Assert.Equal(Array.IndexOf(shown, local) + 1, Array.IndexOf(shown, remote));
                Assert.Equal(Array.IndexOf(shown, remote) + 1, Array.IndexOf(shown, both));
                Assert.Same(topic.Commands.DeleteWithRemote, both.Command);
                Assert.Same(topic, both.CommandParameter);
                Assert.Equal(PhosphorIcon.Trash, Assert.IsType<Icon>(both.Icon).Kind);
                Assert.True(topic.Commands.DeleteWithRemote!.CanExecute(topic));

                badge.ContextMenu!.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ALocalOnlyBadge_AndARemoteOnlyBadge_DoNotOfferIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);

            // "local" never pushed; "gone" only on the remote.
            Git(repository, "branch", "local");
            Git(repository, "branch", "gone");
            Git(repository, "push", "origin", "gone");
            Git(repository, "branch", "-D", "gone");
            (Window window, HistoryPageView view) = await ShowAsync(services, repository);

            try
            {
                foreach (string name in new[] { "local", "origin/gone" })
                {
                    RefBadge badge = Badge(view, name);
                    HistoryBranchViewModel branch = Assert.IsType<HistoryBranchViewModel>(badge.DataContext);
                    Assert.False(branch.CanDeleteWithRemote);
                    Assert.False(branch.Commands.DeleteWithRemote!.CanExecute(branch));

                    Assert.DoesNotContain(Open(badge), item => (item.Header as string)?.Contains(" and ", StringComparison.Ordinal) == true);

                    badge.ContextMenu!.Close();
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OnTheCheckedOutBranch_ItIsDisabled()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);

            HistoryBranchViewModel main = (await LineAsync(services, repository)).Branches.Single(branch => branch.Name == "main");

            Assert.True(main.IsCurrent);
            Assert.True(main.CanDeleteWithRemote);
            Assert.False(main.Commands.DeleteWithRemote!.CanExecute(main));
        });
    }

    // ---------------------------------------------------------------- deleting

    [Fact]
    public void Confirming_DeletesBoth_AfterOneQuestion_AndTheHistoryDropsTheBadge()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);
            Git(repository, "branch", "topic");
            Git(repository, "push", "--set-upstream", "origin", "topic");

            HistoryBranchViewModel topic = (await LineAsync(services, repository)).Branches.Single(branch => branch.Name == "topic");

            services.Dialogs.Result = DialogResult.Primary;
            await topic.Commands.DeleteWithRemote!.ExecuteAsync(topic);

            Assert.Single(services.Dialogs.Shown);
            Assert.Equal(string.Empty, Git(repository, "branch", "--list", "topic"));
            Assert.Equal(string.Empty, Git(repository, "ls-remote", "--heads", "origin", "topic"));

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            Assert.DoesNotContain(
                page.Rows.SelectMany(row => row.Branches),
                branch => branch.Name is "topic" or "origin/topic");
        });
    }

    [Fact]
    public void Cancelling_DeletesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);
            Git(repository, "branch", "topic");
            Git(repository, "push", "--set-upstream", "origin", "topic");

            HistoryBranchViewModel topic = (await LineAsync(services, repository)).Branches.Single(branch => branch.Name == "topic");

            services.Dialogs.Result = DialogResult.None;
            await topic.Commands.DeleteWithRemote!.ExecuteAsync(topic);

            Assert.Single(services.Dialogs.Shown);
            Assert.Equal("topic", Git(repository, "branch", "--list", "topic").TrimStart('*', ' '));
            Assert.NotEqual(string.Empty, Git(repository, "ls-remote", "--heads", "origin", "topic"));
        });
    }

    // ---------------------------------------------------------------- helpers

    private static MenuItem[] Open(RefBadge badge)
    {
        ContextMenu menu = badge.ContextMenu!;
        menu.Open(badge);
        Dispatcher.UIThread.RunJobs();

        return [.. menu.Items.OfType<MenuItem>().Where(item => item.IsVisible)];
    }

    /// <summary>
    /// A repository whose one commit is on <c>main</c>, published to a bare <c>origin</c> that
    /// <c>main</c> tracks.
    /// </summary>
    private static async Task<RepositoryHandle> BuildPublishedAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, "local"), "main");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");

        string origin = Path.Combine(root, "origin.git");
        Directory.CreateDirectory(origin);
        Git(repository, "init", "--bare", origin);
        Git(repository, "remote", "add", "origin", origin);
        Git(repository, "push", "--set-upstream", "origin", "main");

        return repository;
    }

    private static async Task<CommitRowViewModel> LineAsync(TestServices services, RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
        await page.ReloadAsync();

        return page.Rows.Single(row => row.Subject == "Add the readme");
    }

    private static async Task<(Window Window, HistoryPageView View)> ShowAsync(TestServices services, RepositoryHandle repository)
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

        return (window, view);
    }

    private static RefBadge Badge(HistoryPageView view, string name)
        => view.GetVisualDescendants()
            .OfType<RefBadge>()
            .First(badge => badge.DataContext is HistoryBranchViewModel branch && branch.Name == name);

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
