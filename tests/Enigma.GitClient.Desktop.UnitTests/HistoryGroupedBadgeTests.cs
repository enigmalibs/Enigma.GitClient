using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.Core.Refs;
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
/// A local branch and its upstream on the same commit are one badge in the history, as GitKraken
/// draws them: both icons, the local branch's name, and one menu reaching both. Against real git, with
/// a bare <c>origin</c>.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryGroupedBadgeTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryGroupedBadgeTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- which badges are one

    [Fact]
    public void ABranchAndItsUpstreamOnOneCommit_AreOneBadge_TheLocalOnes()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);

            CommitRowViewModel row = await LineAsync(services, repository, "Add the readme");

            HistoryBranchViewModel main = Assert.IsType<HistoryBranchViewModel>(Assert.Single(row.Badges));
            Assert.Equal("main", main.Name);
            Assert.True(main.IsCurrent);
            Assert.True(main.HasRemote);
            Assert.Equal("origin/main", main.Remote!.Name);
            Assert.True(main.Remote.IsRemote);
            Assert.Equal("origin/main", main.Badge.Upstream);
            Assert.Equal("Delete \"origin/main\"…", main.DeleteRemoteHeader);

            // The line's own menu still names both.
            Assert.Equal(["main", "origin/main"], row.Branches.Select(branch => branch.Name));
            Assert.Equal([new RefBadgeItem(GitRefKind.LocalBranch, "main", true, "origin/main")], row.Refs);
        });
    }

    [Fact]
    public void AnUpstreamOnAnotherCommit_IsABadgeOfItsOwn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);
            Commit(repository, "src/ahead.txt", "ahead\n", "Work not pushed yet");

            CommitRowViewModel newer = await LineAsync(services, repository, "Work not pushed yet");
            HistoryBranchViewModel main = Assert.IsType<HistoryBranchViewModel>(Assert.Single(newer.Badges));
            Assert.False(main.HasRemote);
            Assert.Null(main.Badge.Upstream);

            CommitRowViewModel older = Line(services, "Add the readme");
            HistoryBranchViewModel remote = Assert.IsType<HistoryBranchViewModel>(Assert.Single(older.Badges));
            Assert.Equal("origin/main", remote.Name);
        });
    }

    [Fact]
    public void ARemoteBranchNothingTracks_IsABadgeOfItsOwn_OnTheSameCommit()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);

            // Published, but without recording an upstream.
            Git(repository, "branch", "topic");
            Git(repository, "push", "origin", "topic");

            CommitRowViewModel row = await LineAsync(services, repository, "Add the readme");

            Assert.Equal(
                ["main", "origin/topic", "topic"],
                row.Badges.OfType<HistoryBranchViewModel>().Select(branch => branch.Name).Order(StringComparer.Ordinal));
            Assert.False(row.Badges.OfType<HistoryBranchViewModel>().Single(branch => branch.Name == "topic").HasRemote);
            Assert.True(row.Badges.OfType<HistoryBranchViewModel>().Single(branch => branch.Name == "main").HasRemote);
        });
    }

    [Fact]
    public void OnlyTheConfiguredUpstreamJoins_AnotherRemotesBranchOfTheSameNameKeepsItsBadge()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);

            string upstream = Path.Combine(Path.GetDirectoryName(repository.WorkTreePath)!, "upstream.git");
            Directory.CreateDirectory(upstream);
            Git(repository, "init", "--bare", upstream);
            Git(repository, "remote", "add", "upstream", upstream);
            Git(repository, "push", "upstream", "main");

            CommitRowViewModel row = await LineAsync(services, repository, "Add the readme");
            HistoryBranchViewModel[] badges = [.. row.Badges.OfType<HistoryBranchViewModel>()];

            Assert.Equal(2, badges.Length);
            Assert.Equal("origin/main", badges.Single(badge => badge.Name == "main").Remote?.Name);
            Assert.False(badges.Single(badge => badge.Name == "upstream/main").HasRemote);
        });
    }

    [Fact]
    public void TheGroupedBadge_IsRingedWhenEitherOfItsBranchesIsTheMergeSource()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);

            CommitRowViewModel row = await LineAsync(services, repository, "Add the readme");
            HistoryBranchViewModel main = row.Branches.Single(branch => branch.Name == "main");
            Assert.False(main.IsDrawnAsMergeSource);

            // The line's menu can pick the remote by name.
            main.Commands.SetAsMergeSource.Execute(main.Remote);

            Assert.False(main.IsMergeSource);
            Assert.True(main.IsDrawnAsMergeSource);
        });
    }

    // ---------------------------------------------------------------- what is drawn

    [Fact]
    public void TheGroupedBadge_DrawsBothIcons_TheHeadLook_AndNamesBoth()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);
            (Window window, HistoryPageView view) = await ShowAsync(services, repository);

            try
            {
                RefBadge badge = Badge(view, "main");

                Assert.True(badge.HasUpstream);
                Assert.Contains("head", badge.Classes);
                Assert.Equal("main and origin/main", badge.Description);

                // The checked-out branch's check first, then the branch's own icon and the remote's.
                Icon[] icons = [.. badge.GetVisualDescendants().OfType<Icon>()];
                Assert.Equal([PhosphorIcon.Check, PhosphorIcon.GitBranch, PhosphorIcon.CloudArrowDown], icons.Select(icon => icon.Kind));
                Assert.All(icons, icon => Assert.True(icon.IsVisible));

                Border pill = badge.GetVisualDescendants().OfType<Border>().First();
                Assert.Equal("main and origin/main", ToolTip.GetTip(pill));
                Assert.Equal("main and origin/main", global::Avalonia.Automation.AutomationProperties.GetName(pill));

                // No origin/main badge of its own.
                Assert.DoesNotContain(
                    view.GetVisualDescendants().OfType<RefBadge>(),
                    candidate => candidate.Text == "origin/main");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheMetrics_CountTheSecondIcon_AsTheTemplateDrawsIt()
    {
        _fixture.Run(() =>
        {
            RefBadge plain = new() { Kind = GitRefKind.LocalBranch, Text = "main" };
            RefBadge grouped = new() { Kind = GitRefKind.LocalBranch, Text = "main", Upstream = "origin/main" };

            Window window = new()
            {
                Content = new StackPanel { Children = { plain, grouped } },
                Width = 400,
                Height = 200,
            };
            window.Show();
            window.UpdateLayout();

            try
            {
                double drawn = grouped.Bounds.Width - plain.Bounds.Width;
                double measured = RefBadgeMetrics.MeasureBadge("main", withUpstream: true) - RefBadgeMetrics.MeasureBadge("main");

                Assert.Equal(RefBadgeMetrics.IconSize + RefBadgeMetrics.IconSpacing, drawn, 3);
                Assert.Equal(drawn, measured, 3);

                Assert.Equal(
                    RefBadgeMetrics.MeasureBadge("main", withUpstream: true),
                    RefBadgeMetrics.Measure([new RefBadgeItem(GitRefKind.LocalBranch, "main", false, "origin/main")]));

                // A badge of its own draws no second icon.
                Assert.False(plain.HasUpstream);
                Assert.False(plain.GetVisualDescendants().OfType<Icon>().Single(icon => icon.Name == "UpstreamIcon").IsVisible);
                Assert.Equal("main", plain.Description);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- one menu for both

    [Fact]
    public void TheGroupedBadgesMenu_IsTheLocalOnes_PlusTheRemotesDelete()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);
            (Window window, HistoryPageView view) = await ShowAsync(services, repository);

            try
            {
                RefBadge badge = Badge(view, "main");
                HistoryBranchViewModel main = Assert.IsType<HistoryBranchViewModel>(badge.DataContext);

                ContextMenu menu = badge.ContextMenu!;
                menu.Open(badge);
                Dispatcher.UIThread.RunJobs();

                MenuItem[] shown = [.. menu.Items.OfType<MenuItem>().Where(item => item.IsVisible)];
                string?[] headers = [.. shown.Select(item => item.Header as string)];

                Assert.Contains("Check out \"main\"", headers);
                Assert.Contains("Pull \"main\"", headers);
                Assert.Contains("Push \"main\"", headers);

                MenuItem local = shown.Single(item => (item.Header as string) == "Delete \"main\"…");
                MenuItem remote = shown.Single(item => (item.Header as string) == "Delete \"origin/main\"…");

                // Side by side, the local first.
                Assert.Equal(Array.IndexOf(shown, local) + 1, Array.IndexOf(shown, remote));
                Assert.Same(main, local.CommandParameter);
                Assert.Same(main.Remote, remote.CommandParameter);
                Assert.Same(main.Commands.Delete, remote.Command);
                Assert.Equal(PhosphorIcon.CloudX, Assert.IsType<Icon>(remote.Icon).Kind);

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheRemotesDelete_DeletesTheRemoteBranch_AndKeepsTheLocalOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildPublishedAsync(services);
            Git(repository, "branch", "topic");
            Git(repository, "push", "--set-upstream", "origin", "topic");

            CommitRowViewModel row = await LineAsync(services, repository, "Add the readme");
            HistoryBranchViewModel topic = row.Branches.Single(branch => branch.Name == "topic");
            Assert.Equal("origin/topic", topic.Remote?.Name);

            services.Dialogs.Result = DialogResult.Primary;
            await topic.Commands.Delete.ExecuteAsync(topic.Remote);

            Assert.Equal(string.Empty, Git(repository, "ls-remote", "--heads", "origin", "topic"));
            Assert.Equal("topic", Git(repository, "branch", "--list", "topic").TrimStart('*', ' '));
        });
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// A repository whose one commit is on <c>main</c>, published to a bare <c>origin</c> that
    /// <c>main</c> tracks.
    /// </summary>
    private static async Task<RepositoryHandle> BuildPublishedAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "local"), "main");

        Commit(repository, "README.md", "# one\n", "Add the readme");

        string origin = Path.Combine(root, "origin.git");
        Directory.CreateDirectory(origin);
        Git(repository, "init", "--bare", origin);
        Git(repository, "remote", "add", "origin", origin);
        Git(repository, "push", "--set-upstream", "origin", "main");

        return repository;
    }

    private static async Task<CommitRowViewModel> LineAsync(TestServices services, RepositoryHandle repository, string subject)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
        await page.ReloadAsync();

        return Line(services, subject);
    }

    private static CommitRowViewModel Line(TestServices services, string subject)
        => services.Get<HistoryPageViewModel>().Rows.Single(row => row.Subject == subject);

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

    private static void Commit(RepositoryHandle repository, string path, string content, string message)
    {
        string full = Path.Combine(repository.WorkTreePath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);

        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", message);
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
