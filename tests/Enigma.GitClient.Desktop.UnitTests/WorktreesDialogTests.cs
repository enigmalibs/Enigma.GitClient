using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Worktrees;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The Worktrees dialog: every working tree, main first, with its state; what each line's menu offers,
/// and why what it does not offer is greyed; the empty state; and each action reaching the worktree
/// operations — from a listed state, then against real worktrees.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class WorktreesDialogTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    private readonly HeadlessAvaloniaFixture _fixture;

    public WorktreesDialogTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static string Folder(string name) => OperatingSystem.IsWindows() ? $@"C:\src\{name}" : $"/src/{name}";

    private static RepositoryHandle HandleAt(string path) => new(path, Path.Combine(path, ".git"));

    private static readonly GitWorktree Main = new() { Path = Folder("project"), HeadSha = Sha, Branch = "main", IsMain = true };
    private static readonly GitWorktree Topic = new() { Path = Folder("project.worktree.topic"), HeadSha = Sha, Branch = "topic" };
    private static readonly GitWorktree Locked = new() { Path = Folder("project.worktree.away"), HeadSha = Sha, Branch = "away", IsLocked = true, LockReason = "on a USB stick" };
    private static readonly GitWorktree Gone = new() { Path = Folder("project.worktree.gone"), HeadSha = Sha, Branch = "gone", IsMissing = true };
    private static readonly GitWorktree Detached = new() { Path = Folder("project.worktree.bisect"), HeadSha = Sha, IsDetached = true };

    /// <summary>
    /// The dialog's page over a listed state — git lists the worktrees in the order they were made —
    /// opened from <paramref name="openFrom"/>.
    /// </summary>
    private static async Task<WorktreesPageViewModel> ListAsync(TestServices services, string openFrom, params GitWorktree[] worktrees)
    {
        ((FakeWorktreeService)services.Get<IWorktreeService>()).Worktrees = worktrees;
        await services.Get<IRepositoryContext>().OpenAsync(HandleAt(openFrom));

        WorktreesPageViewModel page = services.Get<WorktreesPageViewModel>();
        await page.OnAppearingAsync();

        return page;
    }

    // ---------------------------------------------------------------- the rows

    [Fact]
    public void TheRows_HaveTheMainOneFirst_ThenTheOthersByFolder_WithWhatEachHasCheckedOut()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            WorktreesPageViewModel page = await ListAsync(services, Main.Path, Main, Topic, Locked, Gone, Detached);

            Assert.Equal("Worktrees", page.Title);
            Assert.False(page.IsEmpty);
            Assert.Equal(
                [Main.Path, Locked.Path, Detached.Path, Gone.Path, Topic.Path],
                page.Rows.Select(row => row.Path));

            WorktreeRowViewModel main = page.Rows[0];
            Assert.Equal("project", main.Name);
            Assert.Equal("main", main.Checkout);

            Assert.Equal("project.worktree.topic", page.Rows.Single(row => row.Path == Topic.Path).Name);
            Assert.Equal("detached at 0123456", page.Rows.Single(row => row.Path == Detached.Path).Checkout);
        });
    }

    [Fact]
    public void TheBadges_SayMainThisWindowLockedAndFolderMissing_EachWhereItApplies()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            WorktreesPageViewModel page = await ListAsync(services, Main.Path, Main, Topic, Locked, Gone);

            WorktreeRowViewModel Row(GitWorktree worktree) => page.Rows.Single(row => row.Path == worktree.Path);

            Assert.True(Row(Main).IsMain);
            Assert.True(Row(Main).IsThisWindow);
            Assert.False(Row(Main).IsLocked);
            Assert.False(Row(Main).IsMissing);

            Assert.False(Row(Topic).IsMain);
            Assert.False(Row(Topic).IsThisWindow);

            Assert.True(Row(Locked).IsLocked);
            Assert.Equal("Locked: on a USB stick", Row(Locked).LockTip);

            Assert.True(Row(Gone).IsMissing);
            Assert.False(Row(Gone).IsLocked);
        });
    }

    [Fact]
    public void FromALinkedWorktree_ItIsThisWindow_AndTheMainOneIsNot()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            WorktreesPageViewModel page = await ListAsync(services, Topic.Path, Main, Topic);

            Assert.False(page.Rows[0].IsThisWindow);
            Assert.True(page.Rows[0].IsMain);
            Assert.True(page.Rows[1].IsThisWindow);
        });
    }

    // ---------------------------------------------------------------- what the menu offers

    [Fact]
    public void Remove_IsGreyed_WithWhy_ForTheMainOne_ThisWindow_AndALockedOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            WorktreesPageViewModel page = await ListAsync(services, Topic.Path, Main, Topic, Locked, Gone);

            WorktreeRowViewModel Row(GitWorktree worktree) => page.Rows.Single(row => row.Path == worktree.Path);

            Assert.False(page.RemoveCommand.CanExecute(Row(Main)));
            Assert.Equal("The main working tree cannot be removed", Row(Main).RemoveTip);

            Assert.False(page.RemoveCommand.CanExecute(Row(Topic)));
            Assert.Equal("Remove it from another window of this repository", Row(Topic).RemoveTip);

            Assert.False(page.RemoveCommand.CanExecute(Row(Locked)));
            Assert.Equal("Locked: on a USB stick — unlock it with git worktree unlock", Row(Locked).RemoveTip);

            // A worktree whose folder is gone can still be removed: that clears git's record.
            Assert.True(page.RemoveCommand.CanExecute(Row(Gone)));
            Assert.Empty(Row(Gone).RemoveTip);
        });
    }

    [Fact]
    public void TheOpenItems_AreGreyedForAMissingFolder_AndOnlyThere()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            WorktreesPageViewModel page = await ListAsync(services, Main.Path, Main, Topic, Gone);

            WorktreeRowViewModel gone = page.Rows.Single(row => row.Path == Gone.Path);
            WorktreeRowViewModel topic = page.Rows.Single(row => row.Path == Topic.Path);

            Assert.False(page.OpenCommand.CanExecute(gone));
            Assert.False(page.OpenFolderCommand.CanExecute(gone));
            Assert.False(page.OpenTerminalCommand.CanExecute(gone));
            Assert.Equal("Its folder is missing", gone.OpenTip);

            Assert.True(page.OpenCommand.CanExecute(topic));
            Assert.True(page.OpenFolderCommand.CanExecute(topic));
            Assert.True(page.OpenTerminalCommand.CanExecute(topic));
            Assert.Empty(topic.OpenTip);
        });
    }

    [Fact]
    public void TheOpenItems_ReachTheWorktreeOperations()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            WorktreesPageViewModel page = await ListAsync(services, Main.Path, Main, Topic);
            WorktreeRowViewModel topic = page.Rows.Single(row => row.Path == Topic.Path);

            await topic.OpenCommand.ExecuteAsync(topic);
            await topic.OpenFolderCommand.ExecuteAsync(topic);
            await topic.OpenTerminalCommand.ExecuteAsync(topic);

            Assert.Equal([Topic.Path], services.Launcher.Launched);
            Assert.Contains(Topic.Path, services.Interop.Opened);
            Assert.Contains(Topic.Path, services.Interop.Terminals);
        });
    }

    // ---------------------------------------------------------------- empty

    [Fact]
    public void WithOnlyTheMainWorkingTree_ItSaysHowToMakeOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            WorktreesPageViewModel page = await ListAsync(services, Main.Path, Main);

            Assert.True(page.IsEmpty);
            Assert.Equal("No other worktrees. Create one from a branch's or a commit's menu.", page.EmptyMessage);
        });
    }

    [Fact]
    public void WithNoRepositoryOpen_ItSaysToOpenOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            WorktreesPageViewModel page = services.Get<WorktreesPageViewModel>();
            await page.OnAppearingAsync();

            Assert.True(page.IsEmpty);
            Assert.Empty(page.Rows);
            Assert.Equal("Open a repository to see its worktrees.", page.EmptyMessage);
        });
    }

    // ---------------------------------------------------------------- the view

    [Fact]
    public void TheView_DrawsTheBadges_NamedByTheirTips_AndADoubleClickOpensTheWorktree()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            WorktreesPageViewModel page = await ListAsync(services, Main.Path, Main, Topic, Locked);

            WorktreesPageView view = services.Get<WorktreesPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 900, Height = 500 };
            window.Show();

            for (int attempt = 0; attempt < 3; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }

            try
            {
                ListBox list = view.FindControl<ListBox>("WorktreeList")
                    ?? throw new InvalidOperationException("The page has no worktree list.");

                ListBoxItem Item(GitWorktree worktree)
                    => list.GetRealizedContainers().OfType<ListBoxItem>().Single(item => item.DataContext is WorktreeRowViewModel row && row.Path == worktree.Path);

                string[] Badges(GitWorktree worktree)
                    => [.. Item(worktree).GetVisualDescendants()
                        .OfType<StackPanel>()
                        .Where(badge => badge.Classes.Contains("badge") && badge.IsEffectivelyVisible)
                        .Select(badge => string.Join(' ', badge.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)))];

                Assert.Equal(["main", "this window"], Badges(Main));
                Assert.Empty(Badges(Topic));
                Assert.Equal(["locked"], Badges(Locked));

                StackPanel locked = Item(Locked).GetVisualDescendants().OfType<StackPanel>()
                    .Single(badge => badge.Classes.Contains("badge") && badge.IsEffectivelyVisible);
                Assert.Equal("Locked: on a USB stick", ToolTip.GetTip(locked));
                Assert.Equal("Locked: on a USB stick", AutomationProperties.GetName(locked));

                // The name's tooltip is the whole path.
                TextBlock name = Item(Topic).GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "project.worktree.topic");
                Assert.Equal(Topic.Path, ToolTip.GetTip(name));

                Point centre = name.TranslatePoint(new Point(name.Bounds.Width / 2, name.Bounds.Height / 2), window)!.Value;
                window.MouseDown(centre, MouseButton.Left);
                window.MouseUp(centre, MouseButton.Left);
                window.MouseDown(centre, MouseButton.Left);
                window.MouseUp(centre, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal([Topic.Path], services.Launcher.Launched);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- real worktrees

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

    [Fact]
    public void Remove_FromTheDialog_RemovesTheWorktree_AndTheRowsFollow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);

            string root = Path.Combine(services.ConfigurationRoot, "workspace");
            Directory.CreateDirectory(root);
            RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, "project"), "main");
            await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
            Git(repository.WorkTreePath, "add", "--all");
            Git(repository.WorkTreePath, "commit", "-m", "Add the readme");

            string worktree = WorktreePaths.DefaultFor(repository.WorkTreePath, "topic");
            await services.Get<IWorktreeService>().AddAsync(repository, worktree, "topic", "HEAD");
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            WorktreesPageViewModel page = services.Get<WorktreesPageViewModel>();
            await page.OnAppearingAsync();

            Assert.Equal([repository.WorkTreePath, worktree], page.Rows.Select(row => row.Path));

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: RemoveWorktreeDialogViewModel model })
                {
                    model.DeleteBranch = false;
                }
            };
            services.Dialogs.Result = DialogResult.Primary;

            WorktreeRowViewModel topic = page.Rows[1];
            await topic.RemoveCommand.ExecuteAsync(topic);

            Assert.Equal("Remove worktree", services.Dialogs.Last?.Title);
            Assert.False(Directory.Exists(worktree));
            Assert.Equal([repository.WorkTreePath], page.Rows.Select(row => row.Path));
            Assert.True(page.IsEmpty);
        });
    }
}
