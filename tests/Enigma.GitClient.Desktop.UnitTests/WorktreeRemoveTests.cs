using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
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
/// Removing a worktree from the application: the confirmation and what it says — the branch offered
/// ticked only when nothing is lost, the uncommitted work named, the windows still in it — what
/// confirming and declining leave behind, and the menus that offer it with the three ways to open one.
/// Against a real repository with real worktrees.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class WorktreeRemoveTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public WorktreeRemoveTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- the dialog

    [Fact]
    public void Dialog_AMergedBranch_IsOfferedTicked()
    {
        RemoveWorktreeDialogViewModel model = new("/src/repo.worktree.topic", isMissing: false, changes: 0, "topic", 0, []);

        Assert.Equal("Remove the worktree at /src/repo.worktree.topic? Its folder is deleted.", model.Question);
        Assert.True(model.HasBranch);
        Assert.True(model.DeleteBranch);
        Assert.False(model.IsUnmerged);
        Assert.Equal("Also delete the branch \"topic\"", model.DeleteBranchLabel);
        Assert.False(model.HasChanges);
        Assert.Equal("Remove", model.ConfirmText);
        Assert.Equal("Close any window working in it first.", model.CloseWindowsNote);
    }

    [Fact]
    public void Dialog_AnUnmergedBranch_IsOfferedUnticked_WithTheCommitsItHolds()
    {
        RemoveWorktreeDialogViewModel model = new("/src/w", isMissing: false, changes: 0, "topic", 12, ["Third", "Second"]);

        Assert.False(model.DeleteBranch);
        Assert.True(model.IsUnmerged);
        Assert.Equal("holds 12 commits the current branch does not; deleting it loses them", model.UnmergedNote);
        Assert.Equal(["Third", "Second", "…and 10 more"], model.UnmergedList);

        // Ticked by the reader, it goes all the same.
        model.DeleteBranch = true;
        Assert.True(model.DeleteBranch);
    }

    [Fact]
    public void Dialog_WithChanges_WarnsAndRemovesAnyway()
    {
        RemoveWorktreeDialogViewModel one = new("/src/w", isMissing: false, changes: 1, "topic", 0, []);
        RemoveWorktreeDialogViewModel three = new("/src/w", isMissing: false, changes: 3, "topic", 0, []);

        Assert.True(one.HasChanges);
        Assert.Equal("1 uncommitted change in it will be lost.", one.ChangesWarning);
        Assert.Equal("3 uncommitted changes in it will be lost.", three.ChangesWarning);
        Assert.Equal("Remove anyway", three.ConfirmText);
    }

    [Fact]
    public void Dialog_ADetachedWorktree_OffersNoBranch()
    {
        RemoveWorktreeDialogViewModel model = new("/src/w", isMissing: false, changes: 0, branch: null, 0, []);

        Assert.False(model.HasBranch);
        Assert.False(model.DeleteBranch);
        Assert.False(model.IsUnmerged);

        model.DeleteBranch = true;
        Assert.False(model.DeleteBranch);
    }

    [Fact]
    public void Dialog_AMissingFolder_SaysOnlyTheRecordGoes()
        => Assert.Equal(
            "Remove the worktree at /src/w? Its folder is already gone; this removes git's record of it.",
            new RemoveWorktreeDialogViewModel("/src/w", isMissing: true, changes: 0, "topic", 0, []).Question);

    // ---------------------------------------------------------------- a real repository

    /// <summary>
    /// <c>main</c> checked out here and <c>topic</c> in a worktree beside it, both on the same commit;
    /// opened, with the worktrees read.
    /// </summary>
    private static async Task<(RepositoryHandle Repository, string Worktree)> OpenAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, "project"), "main");

        await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        Git(repository.WorkTreePath, "add", "--all");
        Git(repository.WorkTreePath, "commit", "-m", "Add the readme");

        string worktree = WorktreePaths.DefaultFor(repository.WorkTreePath, "topic");
        await services.Get<IWorktreeService>().AddAsync(repository, worktree, "topic", "HEAD");

        await services.Get<IRepositoryContext>().OpenAsync(repository);

        return (repository, worktree);
    }

    private static GitWorktree Linked(TestServices services)
        => services.Get<IRepositoryContext>().Worktrees.Single(worktree => !worktree.IsMain);

    private static bool BranchExists(TestServices services, string name)
        => services.Get<IRepositoryContext>().Refs.LocalBranches.Any(branch => branch.ShortName == name);

    /// <summary>Answers the confirmation, and keeps what it asked.</summary>
    private static Func<RemoveWorktreeDialogViewModel?> Answer(TestServices services, DialogResult result, bool? deleteBranch = null)
    {
        RemoveWorktreeDialogViewModel? asked = null;

        services.Dialogs.OnShown = dialog =>
        {
            if (dialog.Content is Control { DataContext: RemoveWorktreeDialogViewModel model })
            {
                asked = model;

                if (deleteBranch is { } tick)
                {
                    model.DeleteBranch = tick;
                }
            }
        };
        services.Dialogs.Result = result;

        return () => asked;
    }

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

    // ---------------------------------------------------------------- removing

    [Fact]
    public void Remove_Confirmed_WithTheBoxTicked_RemovesTheFolderAndTheBranch()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (_, string worktree) = await OpenAsync(services);
            Func<RemoveWorktreeDialogViewModel?> asked = Answer(services, DialogResult.Primary);

            Assert.True(await services.Get<IWorktreeOperations>().RemoveAsync(Linked(services)));

            ContentDialog dialog = Assert.Single(services.Dialogs.Shown);
            Assert.Equal("Remove worktree", dialog.Title);
            Assert.Equal("Remove", dialog.PrimaryButtonText);
            Assert.True(asked()!.DeleteBranch);

            Assert.False(Directory.Exists(worktree));
            Assert.Single(services.Get<IRepositoryContext>().Worktrees);
            Assert.False(BranchExists(services, "topic"));

            RecordedNotification note = services.InfoBar.Shown[^1];
            Assert.Equal("Worktree removed", note.Title);
            Assert.Equal($"'{worktree}' is gone and \"topic\" is deleted.", note.Message);
            Assert.Equal(InfoBarSeverity.Success, note.Severity);
        });
    }

    [Fact]
    public void Remove_Confirmed_WithTheBoxUnticked_KeepsTheBranch_WhichCanBeDeletedAfterwards()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (_, string worktree) = await OpenAsync(services);
            Answer(services, DialogResult.Primary, deleteBranch: false);

            Assert.True(await services.Get<IWorktreeOperations>().RemoveAsync(Linked(services)));

            Assert.False(Directory.Exists(worktree));
            Assert.True(BranchExists(services, "topic"));
            Assert.Equal($"'{worktree}' is gone.", services.InfoBar.Shown[^1].Message);

            // No other working tree has it now: the delete is asked, and goes.
            services.Dialogs.OnShown = null;
            services.Dialogs.Result = DialogResult.Primary;

            Assert.True(await services.Get<IBranchOperations>().DeleteAsync("topic", isRemote: false));
            Assert.False(BranchExists(services, "topic"));
        });
    }

    [Fact]
    public void Remove_AnUnmergedBranch_IsOfferedUnticked_AndKept()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, string worktree) = await OpenAsync(services);

            await File.WriteAllTextAsync(Path.Combine(worktree, "topic.txt"), "topic\n");
            Git(worktree, "add", "--all");
            Git(worktree, "commit", "-m", "Work only topic has");
            await services.Get<IRepositoryContext>().RefreshAsync();

            Func<RemoveWorktreeDialogViewModel?> asked = Answer(services, DialogResult.Primary);

            Assert.True(await services.Get<IWorktreeOperations>().RemoveAsync(Linked(services)));

            RemoveWorktreeDialogViewModel model = asked()!;
            Assert.False(model.DeleteBranch);
            Assert.True(model.IsUnmerged);
            Assert.Equal(1, model.UnmergedCommits);
            Assert.Equal(["Work only topic has"], model.UnmergedList);

            Assert.False(Directory.Exists(worktree));
            Assert.True(BranchExists(services, "topic"));
            Assert.NotEqual(GitProbe.Sha(repository, "main"), GitProbe.Sha(repository, "topic"));
        });
    }

    [Fact]
    public void Remove_WithChanges_NamesThem_AndRemovesAnywayWithForce()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (_, string worktree) = await OpenAsync(services);
            await File.WriteAllTextAsync(Path.Combine(worktree, "README.md"), "# changed\n");
            await File.WriteAllTextAsync(Path.Combine(worktree, "new.txt"), "untracked\n");

            Func<RemoveWorktreeDialogViewModel?> asked = Answer(services, DialogResult.Primary, deleteBranch: false);

            Assert.True(await services.Get<IWorktreeOperations>().RemoveAsync(Linked(services)));

            Assert.Equal(2, asked()!.Changes);
            Assert.Equal("2 uncommitted changes in it will be lost.", asked()!.ChangesWarning);
            Assert.Equal("Remove anyway", services.Dialogs.Last!.PrimaryButtonText);

            // git refuses a worktree that is not clean without --force: it went, so force was passed.
            Assert.False(Directory.Exists(worktree));
        });
    }

    [Fact]
    public void Remove_Cancelled_RemovesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (_, string worktree) = await OpenAsync(services);
            Answer(services, DialogResult.Close);

            Assert.False(await services.Get<IWorktreeOperations>().RemoveAsync(Linked(services)));

            Assert.True(Directory.Exists(worktree));
            Assert.Equal(2, services.Get<IRepositoryContext>().Worktrees.Count);
            Assert.True(BranchExists(services, "topic"));
            Assert.Empty(services.InfoBar.Shown);
        });
    }

    [Fact]
    public void Remove_WhenTheBranchCannotBeDeletedAfterwards_StillReportsTheRemoval_AndNamesTheBranch()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, string worktree) = await OpenAsync(services);
            Answer(services, DialogResult.Primary);

            // A lock on the branch's ref: git removes the worktree, then cannot write the branch.
            string refs = Path.Combine(repository.GitDirectory, "refs", "heads");
            Directory.CreateDirectory(refs);
            await File.WriteAllTextAsync(Path.Combine(refs, "topic.lock"), string.Empty);

            try
            {
                Assert.True(await services.Get<IWorktreeOperations>().RemoveAsync(Linked(services)));

                Assert.False(Directory.Exists(worktree));

                RecordedNotification note = services.InfoBar.Shown[^1];
                Assert.Equal("Worktree removed", note.Title);
                Assert.Equal(InfoBarSeverity.Warning, note.Severity);
                Assert.Contains("\"topic\" was not deleted", note.Message, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(Path.Combine(refs, "topic.lock"));
            }
        });
    }

    [Fact]
    public void Remove_TheMainWorkingTree_IsRefusedWithoutAQuestion()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, string worktree) = await OpenAsync(services);

            RepositoryDiscoveryResult linked = await services.Get<IRepositoryLocator>().DiscoverAsync(worktree);
            await services.Get<IRepositoryContext>().OpenAsync(linked.Repository!);

            GitWorktree main = services.Get<IRepositoryContext>().Worktrees.Single(candidate => candidate.IsMain);

            Assert.False(await services.Get<IWorktreeOperations>().RemoveAsync(main));

            Assert.Empty(services.Dialogs.Shown);
            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "README.md")));

            RecordedNotification note = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Could not remove the worktree", note.Title);
            Assert.Contains("main working tree", note.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Remove_ALockedWorktree_IsRefusedNamingUnlock()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, string worktree) = await OpenAsync(services);
            Git(repository.WorkTreePath, "worktree", "lock", "--reason", "on a USB stick", worktree);
            await services.Get<IRepositoryContext>().RefreshAsync();

            Assert.False(await services.Get<IWorktreeOperations>().RemoveAsync(Linked(services)));

            Assert.Empty(services.Dialogs.Shown);
            Assert.True(Directory.Exists(worktree));
            Assert.Contains("git worktree unlock", Assert.Single(services.InfoBar.Shown).Message, StringComparison.Ordinal);
        });
    }

    // ---------------------------------------------------------------- opening

    [Fact]
    public void Open_LaunchesOnTheFolder_OrSaysItCouldNot()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (_, string worktree) = await OpenAsync(services);
            IWorktreeOperations operations = services.Get<IWorktreeOperations>();

            Assert.True(await operations.OpenAsync(Linked(services)));
            Assert.Equal([worktree], services.Launcher.Launched);

            services.Launcher.Succeeds = false;
            Assert.False(await operations.OpenAsync(Linked(services)));
            Assert.Equal("No new window", services.InfoBar.Shown[^1].Title);
        });
    }

    [Fact]
    public void OpenFolderAndTerminal_AskTheDesktopForTheWorktreesFolder()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (_, string worktree) = await OpenAsync(services);
            IWorktreeOperations operations = services.Get<IWorktreeOperations>();

            Assert.True(await operations.OpenFolderAsync(Linked(services)));
            Assert.True(await operations.OpenTerminalAsync(Linked(services)));

            Assert.Contains(worktree, services.Interop.Opened);
            Assert.Contains(worktree, services.Interop.Terminals);

            // Each says so when the desktop does not oblige.
            services.Interop.OpensPaths = false;
            services.Interop.OpensTerminals = false;

            Assert.False(await operations.OpenFolderAsync(Linked(services)));
            Assert.Equal("The folder did not open", services.InfoBar.Shown[^1].Title);

            Assert.False(await operations.OpenTerminalAsync(Linked(services)));
            Assert.Equal("No terminal started", services.InfoBar.Shown[^1].Title);
        });
    }

    // ---------------------------------------------------------------- the menus

    [Fact]
    public void TheBranchesDialog_OffersTheWorktreesItems_ForSuchABranchOnly()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            services.OrderListsByName();
            (RepositoryHandle repository, string worktree) = await OpenAsync(services);
            Git(repository.WorkTreePath, "branch", "other");
            await services.Get<IRepositoryContext>().RefreshAsync();

            BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
            await page.OnAppearingAsync();

            BranchRowViewModel Row(string name) => page.Groups.SelectMany(group => group.Rows).Single(row => row.FullName == name);

            BranchRowViewModel topic = Row("topic");
            Assert.True(topic.IsInOtherWorktree);
            Assert.True(topic.CanRemoveWorktree);
            Assert.True(topic.OpenWorktreeCommand.CanExecute(topic));
            Assert.True(topic.OpenWorktreeFolderCommand.CanExecute(topic));
            Assert.True(topic.OpenWorktreeTerminalCommand.CanExecute(topic));
            Assert.True(topic.RemoveWorktreeCommand.CanExecute(topic));

            foreach (BranchRowViewModel row in new[] { Row("main"), Row("other") })
            {
                Assert.False(row.IsInOtherWorktree);
                Assert.False(row.CanRemoveWorktree);
                Assert.False(row.OpenWorktreeCommand.CanExecute(row));
                Assert.False(row.RemoveWorktreeCommand.CanExecute(row));
            }

            // Removed from the row: the page rebuilds without the worktree.
            Answer(services, DialogResult.Primary, deleteBranch: false);
            await topic.RemoveWorktreeCommand.ExecuteAsync(topic);

            Assert.False(Directory.Exists(worktree));
            Assert.False(Row("topic").IsInOtherWorktree);
        });
    }

    [Fact]
    public void TheBranchesRowsMenu_ListsTheFourItemsInOrder_RightAfterCreateWorktree()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            services.OrderListsByName();
            await OpenAsync(services);

            BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
            await page.OnAppearingAsync();

            BranchesPageView view = services.Get<BranchesPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1100, Height = 560 };
            window.Show();

            for (int attempt = 0; attempt < 3; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }

            try
            {
                TreeView tree = view.FindControl<TreeView>("BranchTree")
                    ?? throw new InvalidOperationException("The branches page has no branch tree.");

                Grid line = tree.GetRealizedTreeContainers()
                    .OfType<TreeViewItem>()
                    .First(container => container.DataContext is BranchRowViewModel { FullName: "topic" })
                    .GetVisualDescendants()
                    .OfType<Grid>()
                    .First(grid => grid.ContextMenu is not null);

                ContextMenu menu = line.ContextMenu!;
                menu.Open(line);

                string?[] headers = [.. menu.Items.OfType<MenuItem>().Where(item => item.IsVisible).Select(item => item.Header as string)];
                int create = Array.IndexOf(headers, "Create worktree…");

                Assert.Equal(
                    ["Open worktree in a new window", "Open worktree folder", "Open a terminal in the worktree", "Remove worktree…"],
                    headers[(create + 1)..(create + 5)]);

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void SeenFromALinkedWorktree_TheMainWorkingTreesBranchOffersNoRemove()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            services.OrderListsByName();
            (_, string worktree) = await OpenAsync(services);

            RepositoryDiscoveryResult linked = await services.Get<IRepositoryLocator>().DiscoverAsync(worktree);
            await services.Get<IRepositoryContext>().OpenAsync(linked.Repository!);

            BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
            await page.OnAppearingAsync();

            BranchRowViewModel main = page.Groups.SelectMany(group => group.Rows).Single(row => row.FullName == "main");

            Assert.True(main.IsInOtherWorktree);
            Assert.False(main.CanRemoveWorktree);
            Assert.False(main.RemoveWorktreeCommand.CanExecute(main));
            Assert.True(main.OpenWorktreeCommand.CanExecute(main));
        });
    }

    [Fact]
    public void AMissingFolder_GreysTheOpenItems_AndStillOffersRemove()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            services.OrderListsByName();
            (_, string worktree) = await OpenAsync(services);
            Directory.Delete(worktree, recursive: true);
            await services.Get<IRepositoryContext>().RefreshAsync();

            BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
            await page.OnAppearingAsync();

            BranchRowViewModel topic = page.Groups.SelectMany(group => group.Rows).Single(row => row.FullName == "topic");

            Assert.True(topic.IsInOtherWorktree);
            Assert.False(topic.OpenWorktreeCommand.CanExecute(topic));
            Assert.False(topic.OpenWorktreeFolderCommand.CanExecute(topic));
            Assert.False(topic.OpenWorktreeTerminalCommand.CanExecute(topic));
            Assert.True(topic.RemoveWorktreeCommand.CanExecute(topic));

            // And removing it clears git's record.
            Func<RemoveWorktreeDialogViewModel?> asked = Answer(services, DialogResult.Primary, deleteBranch: false);
            await topic.RemoveWorktreeCommand.ExecuteAsync(topic);

            Assert.True(asked()!.IsMissing);
            Assert.Single(services.Get<IRepositoryContext>().Worktrees);
        });
    }

    [Fact]
    public void TheHistoryBadge_OffersTheWorktreesItems_ForSuchABranchOnly_AndReloadsAfterARemoval()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (_, string worktree) = await OpenAsync(services);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            HistoryBranchViewModel Badge(string name) => history.Rows.SelectMany(row => row.Branches).Single(branch => branch.Name == name);

            HistoryBranchViewModel topic = Badge("topic");
            HistoryWorktreeCommands commands = topic.Commands.Worktrees!;

            Assert.True(topic.IsInOtherWorktree);
            Assert.True(topic.CanRemoveWorktree);
            Assert.Equal(worktree, topic.Worktree!.Path);
            Assert.True(commands.Open.CanExecute(topic));
            Assert.True(commands.OpenFolder.CanExecute(topic));
            Assert.True(commands.OpenTerminal.CanExecute(topic));
            Assert.True(commands.Remove.CanExecute(topic));

            HistoryBranchViewModel main = Badge("main");
            Assert.False(main.IsInOtherWorktree);
            Assert.False(commands.Open.CanExecute(main));
            Assert.False(commands.Remove.CanExecute(main));

            Answer(services, DialogResult.Primary);
            await commands.Remove.ExecuteAsync(topic);

            Assert.False(Directory.Exists(worktree));

            // Reloaded as a branch deletion is: the branch went with its worktree.
            Assert.DoesNotContain(history.Rows.SelectMany(row => row.Branches), branch => branch.Name == "topic");
        });
    }
}
