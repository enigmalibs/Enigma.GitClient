using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Worktrees;
using Enigma.GitClient.Desktop.Controls;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The tree that marks a branch checked out in another working tree — on the branches dialog's rows and
/// on the history's pills — and the repository strip's mark for a linked worktree, against real
/// worktrees; and what the repository context reads them from.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class WorktreeMarkTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public WorktreeMarkTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- a real repository

    /// <summary>
    /// <c>main</c> checked out here, <c>topic</c> in a worktree beside it, and <c>other</c> nowhere.
    /// </summary>
    private static async Task<(RepositoryHandle Repository, string Worktree)> BuildAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, "project"), "main");

        await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        Git(repository.WorkTreePath, "add", "--all");
        Git(repository.WorkTreePath, "commit", "-m", "Add the readme");
        Git(repository.WorkTreePath, "branch", "other");

        string worktree = WorktreePaths.DefaultFor(repository.WorkTreePath, "topic");
        await services.Get<IWorktreeService>().AddAsync(repository, worktree, "topic", "HEAD");

        return (repository, worktree);
    }

    private static async Task<RepositoryHandle> DiscoverAsync(TestServices services, string folder)
    {
        RepositoryDiscoveryResult discovery = await services.Get<IRepositoryLocator>().DiscoverAsync(folder);
        return discovery.Repository!;
    }

    private static async Task<BranchesPageViewModel> BranchesAsync(TestServices services, RepositoryHandle repository)
    {
        services.OrderListsByName();
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
        await page.OnAppearingAsync();

        return page;
    }

    private static BranchRowViewModel Row(BranchesPageViewModel page, string name)
        => page.Groups.SelectMany(group => group.Rows).Single(row => row.FullName == name);

    private static void Git(string directory, params string[] arguments)
    {
        ProcessStartInfo start = new()
        {
            FileName = "git",
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.Environment["GIT_AUTHOR_NAME"] = "Ada Lovelace";
        start.Environment["GIT_AUTHOR_EMAIL"] = "ada@example.com";
        start.Environment["GIT_COMMITTER_NAME"] = "Ada Lovelace";
        start.Environment["GIT_COMMITTER_EMAIL"] = "ada@example.com";

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }
    }

    // ---------------------------------------------------------------- the branches dialog

    [Fact]
    public void ABranchInAnotherWorktree_CarriesTheBadge_AndNoOtherRowDoes()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, string worktree) = await BuildAsync(services);

            BranchesPageViewModel page = await BranchesAsync(services, repository);

            BranchRowViewModel topic = Row(page, "topic");
            Assert.True(topic.IsInOtherWorktree);
            Assert.Equal(worktree, topic.WorktreePath);
            Assert.Equal($"Checked out in the worktree at {worktree}", topic.WorktreeTip);

            // The branch this window has checked out keeps its "checked out" badge, never the tree.
            BranchRowViewModel main = Row(page, "main");
            Assert.True(main.IsCurrent);
            Assert.False(main.IsInOtherWorktree);
            Assert.Empty(main.WorktreeTip);

            Assert.False(Row(page, "other").IsInOtherWorktree);
        });
    }

    [Fact]
    public void SeenFromALinkedWorktree_TheMainWorkingTreesBranchCarriesTheBadge()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, string worktree) = await BuildAsync(services);

            BranchesPageViewModel page = await BranchesAsync(services, await DiscoverAsync(services, worktree));

            BranchRowViewModel main = Row(page, "main");
            Assert.True(main.IsInOtherWorktree);
            Assert.Equal($"Checked out in the main working tree at {repository.WorkTreePath}", main.WorktreeTip);

            // Here, topic is the branch checked out.
            BranchRowViewModel topic = Row(page, "topic");
            Assert.True(topic.IsCurrent);
            Assert.False(topic.IsInOtherWorktree);

            Assert.False(Row(page, "other").IsInOtherWorktree);
        });
    }

    [Fact]
    public void AWorktreeWhoseFolderIsGone_SaysSoInTheTip()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, string worktree) = await BuildAsync(services);
            Directory.Delete(worktree, recursive: true);

            BranchesPageViewModel page = await BranchesAsync(services, repository);

            Assert.Equal(
                $"Checked out in the worktree at {worktree} (its folder is missing)",
                Row(page, "topic").WorktreeTip);
        });
    }

    [Fact]
    public void TheRowsBadge_IsTheTreeAndTheWord_NamedByItsTip()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, string worktree) = await BuildAsync(services);
            BranchesPageViewModel page = await BranchesAsync(services, repository);

            BranchesPageView view = services.Get<BranchesPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1100, Height = 560 };
            window.Show();
            window.UpdateLayout();

            try
            {
                TreeView list = view.FindControl<TreeView>("BranchTree")
                    ?? throw new InvalidOperationException("The branches page has no branch tree.");

                StackPanel[] Badges(string branch) =>
                    [.. list.GetRealizedTreeContainers()
                        .OfType<TreeViewItem>()
                        .First(container => container.DataContext is BranchRowViewModel row && row.FullName == branch)
                        .GetVisualDescendants()
                        .OfType<StackPanel>()
                        .Where(badge => badge.Classes.Contains("badge") && badge.IsEffectivelyVisible)];

                StackPanel badge = Assert.Single(Badges("topic"), candidate => candidate.GetVisualDescendants().OfType<Icon>().Any(icon => icon.Kind == PhosphorIcon.Tree));

                Assert.Equal("worktree", badge.GetVisualDescendants().OfType<TextBlock>().Single().Text);
                Assert.Equal($"Checked out in the worktree at {worktree}", ToolTip.GetTip(badge));
                Assert.Equal($"Checked out in the worktree at {worktree}", AutomationProperties.GetName(badge));

                // Right after "checked out" in the strip's order, and on no other line.
                Assert.DoesNotContain(Badges("main"), candidate => candidate.GetVisualDescendants().OfType<Icon>().Any(icon => icon.Kind == PhosphorIcon.Tree));
                Assert.DoesNotContain(Badges("other"), candidate => candidate.GetVisualDescendants().OfType<Icon>().Any(icon => icon.Kind == PhosphorIcon.Tree));
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- the history

    [Fact]
    public void AHistoryPill_ForABranchInAnotherWorktree_CarriesTheTreeAndItsTip()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, string worktree) = await BuildAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            CommitRowViewModel line = page.Rows.Single(row => row.Subject == "Add the readme");

            RefBadgeItem topic = line.Refs.Single(badge => badge.Name == "topic");
            Assert.True(topic.IsWorktree);
            Assert.Equal($"Checked out in the worktree at {worktree}", topic.Worktree);

            Assert.False(line.Refs.Single(badge => badge.Name == "main").IsWorktree);
            Assert.False(line.Refs.Single(badge => badge.Name == "other").IsWorktree);

            // The column is still seeded to fit the badges, tree included.
            Assert.Equal(Math.Min(RefBadgeMetrics.Measure(line.Refs), HistoryPageViewModel.MaximumRefColumnWidth), page.RefColumnWidth);
        });
    }

    [Fact]
    public void AHistoryPill_DrawsTheTree_OnlyForTheBranchInAnotherWorktree()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, string worktree) = await BuildAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            HistoryPageView view = services.Get<HistoryPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1400, Height = 700 };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            try
            {
                RefBadge Pill(string name) => view.GetVisualDescendants()
                    .OfType<RefBadge>()
                    .First(badge => badge.Text == name);

                static Icon Tree(RefBadge badge)
                    => badge.GetVisualDescendants().OfType<Icon>().Single(icon => icon.Name == "WorktreeIcon");

                RefBadge topic = Pill("topic");
                Assert.True(topic.IsWorktree);
                Assert.True(Tree(topic).IsVisible);
                Assert.Equal(PhosphorIcon.Tree, Tree(topic).Kind);
                Assert.Equal($"topic\nChecked out in the worktree at {worktree}", topic.Description);

                Assert.False(Tree(Pill("main")).IsVisible);
                Assert.False(Tree(Pill("other")).IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ThePillsTree_SitsAfterTheCheckAndBeforeTheBranchIcon_AndIsMeasured()
    {
        _fixture.Run(() =>
        {
            RefBadge plain = new() { Kind = GitRefKind.LocalBranch, Text = "topic" };
            RefBadge tree = new() { Kind = GitRefKind.LocalBranch, Text = "topic", IsWorktree = true, WorktreeTip = "Checked out in the worktree at x" };
            RefBadge both = new() { Kind = GitRefKind.LocalBranch, Text = "topic", IsWorktree = true, IsCurrent = true };

            Window window = new()
            {
                Content = new StackPanel { Children = { plain, tree, both } },
                Width = 400,
                Height = 200,
            };
            window.Show();
            window.UpdateLayout();

            try
            {
                static Icon Named(RefBadge badge, string name)
                    => badge.GetVisualDescendants().OfType<Icon>().Single(icon => icon.Name == name);

                static double X(RefBadge badge, string name)
                    => Named(badge, name).TranslatePoint(new Point(0, 0), badge)!.Value.X;

                Assert.True(X(both, "CurrentIcon") < X(both, "WorktreeIcon"), "the tree is not after the check");
                Assert.True(X(both, "WorktreeIcon") < X(both, "KindIcon"), "the tree is not before the branch icon");
                Assert.Equal(RefBadgeMetrics.IconSize, Named(tree, "WorktreeIcon").Size);
                Assert.False(Named(plain, "WorktreeIcon").IsVisible);

                // One more icon and one more gap, as drawn and as measured.
                double drawn = tree.Bounds.Width - plain.Bounds.Width;

                Assert.Equal(RefBadgeMetrics.IconSize + RefBadgeMetrics.IconSpacing, drawn, 3);
                Assert.Equal(drawn, RefBadgeMetrics.MeasureBadge("topic", isWorktree: true) - RefBadgeMetrics.MeasureBadge("topic"), 3);
                Assert.Equal(
                    RefBadgeMetrics.MeasureBadge("topic", isWorktree: true),
                    RefBadgeMetrics.Measure([new RefBadgeItem(GitRefKind.LocalBranch, "topic", false, null, "Checked out in the worktree at x")]));

                // The tip names the folder, under the branch's name.
                Assert.Equal("topic\nChecked out in the worktree at x", tree.Description);
                Assert.Equal("topic", plain.Description);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- the repository strip

    private static GitWorktree MainAt(string path) => new() { Path = path, Branch = "main", IsMain = true };

    private static GitWorktree LinkedAt(string path) => new() { Path = path, Branch = "topic" };

    private static string Folder(string name) => OperatingSystem.IsWindows() ? $@"C:\src\{name}" : $"/src/{name}";

    private static RepositoryHandle HandleAt(string path) => new(path, Path.Combine(path, ".git"));

    [Fact]
    public void TheStrip_MarksALinkedWorktree_AndSaysWhoseItIs()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            FakeWorktreeService worktrees = (FakeWorktreeService)services.Get<IWorktreeService>();
            worktrees.Worktrees = [MainAt(Folder("project")), LinkedAt(Folder("project.worktree.topic"))];

            MainWindowViewModel viewModel = services.Get<MainWindowViewModel>();
            MainWindow window = services.Get<MainWindow>();
            window.DataContext = viewModel;
            window.Measure(new Size(1400, 900));
            window.Arrange(new Rect(0, 0, 1400, 900));

            Border mark = window.FindControl<Border>("LinkedWorktreeMark")
                ?? throw new InvalidOperationException("The strip has no worktree mark.");

            // The main working tree: no mark.
            await services.Get<IRepositoryContext>().OpenAsync(HandleAt(Folder("project")));
            Assert.False(viewModel.IsLinkedWorktree);
            Assert.False(mark.IsVisible);

            // A linked worktree: the tree, naming the main working tree.
            await services.Get<IRepositoryContext>().OpenAsync(HandleAt(Folder("project.worktree.topic")));
            Assert.True(viewModel.IsLinkedWorktree);
            Assert.True(mark.IsVisible);
            Assert.Equal($"A worktree of {Folder("project")}", viewModel.LinkedWorktreeTip);
            Assert.Equal($"A worktree of {Folder("project")}", ToolTip.GetTip(mark));
            Assert.Equal($"A worktree of {Folder("project")}", AutomationProperties.GetName(mark));
            Assert.Equal(PhosphorIcon.Tree, Assert.IsType<Icon>(mark.Child).Kind);

            // Closed: nothing.
            services.Get<IRepositoryContext>().Close();
            Assert.False(viewModel.IsLinkedWorktree);
        });
    }

    // ---------------------------------------------------------------- what the context reads

    private static (RepositoryContext Context, FakeWorktreeService Worktrees, FakeRefReader Reader) Context()
    {
        FakeRefReader reader = new();
        FakeWorktreeService worktrees = new();

        return (new RepositoryContext(reader, worktrees, NullLogger<RepositoryContext>.Instance), worktrees, reader);
    }

    [Fact]
    public async Task TheContext_FindsTheOtherWorktreeOfABranch_NeverTheOpenOne()
    {
        (RepositoryContext context, FakeWorktreeService worktrees, _) = Context();
        using RepositoryContext disposing = context;
        worktrees.Worktrees = [MainAt(Folder("project")), LinkedAt(Folder("project.worktree.topic"))];

        await context.OpenAsync(HandleAt(Folder("project")), TestContext.Current.CancellationToken);

        Assert.Equal(2, context.Worktrees.Count);
        Assert.Equal(Folder("project.worktree.topic"), context.WorktreeOf("topic")?.Path);
        Assert.Null(context.WorktreeOf("main"));
        Assert.Null(context.WorktreeOf("other"));
        Assert.False(context.IsLinkedWorktree);
        Assert.Equal(Folder("project"), context.MainWorktreePath);

        // From the linked worktree, it is the main one that has a branch elsewhere.
        await context.OpenAsync(HandleAt(Folder("project.worktree.topic")), TestContext.Current.CancellationToken);

        Assert.Equal(Folder("project"), context.WorktreeOf("main")?.Path);
        Assert.Null(context.WorktreeOf("topic"));
        Assert.True(context.IsLinkedWorktree);
    }

    [Fact]
    public async Task TheContext_WhenTheWorktreesCannotBeListed_StillPublishesTheRefs()
    {
        (RepositoryContext context, FakeWorktreeService worktrees, FakeRefReader reader) = Context();
        using RepositoryContext disposing = context;
        worktrees.ListFailure = new InvalidOperationException("git is unwell");
        reader.Head = HeadState.Unborn("trunk");

        bool refreshed = false;
        context.StateRefreshed += (_, _) => refreshed = true;

        await context.OpenAsync(HandleAt(Folder("project")), TestContext.Current.CancellationToken);

        Assert.True(refreshed);
        Assert.Equal("trunk", context.Head?.BranchName);
        Assert.Empty(context.Worktrees);
        Assert.Null(context.MainWorktreePath);
        Assert.False(context.IsLinkedWorktree);
    }

    [Fact]
    public async Task TheContext_ForgetsTheWorktreesWithTheRepository()
    {
        (RepositoryContext context, FakeWorktreeService worktrees, _) = Context();
        using RepositoryContext disposing = context;
        worktrees.Worktrees = [MainAt(Folder("project")), LinkedAt(Folder("project.worktree.topic"))];

        await context.OpenAsync(HandleAt(Folder("project")), TestContext.Current.CancellationToken);
        context.Close();

        Assert.Empty(context.Worktrees);
        Assert.Null(context.WorktreeOf("topic"));
    }

    [Fact]
    public async Task TheStamp_ChangesWhenAWorktreeComesOrGoes()
    {
        (RepositoryContext context, FakeWorktreeService worktrees, _) = Context();
        using RepositoryContext disposing = context;
        worktrees.Worktrees = [MainAt(Folder("project"))];

        await context.OpenAsync(HandleAt(Folder("project")), TestContext.Current.CancellationToken);
        RepositoryStateStamp before = RepositoryStateStamp.Of(context);

        await context.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before, RepositoryStateStamp.Of(context));

        // The pills' trees are drawn from it, so the history has something new to draw.
        worktrees.Worktrees = [MainAt(Folder("project")), LinkedAt(Folder("project.worktree.topic"))];
        await context.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(before, RepositoryStateStamp.Of(context));
    }
}
