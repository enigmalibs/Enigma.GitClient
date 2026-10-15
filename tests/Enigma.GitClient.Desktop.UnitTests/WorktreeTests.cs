using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Worktrees;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.Icons.Phosphor;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The "create a worktree" dialog and the flow behind it: the folder it proposes next to the
/// repository, what it refuses and why, what confirming, declining and a window that cannot start
/// each leave behind, and the three menus that offer it — checked against a real repository.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class WorktreeTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public WorktreeTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static string RepositoryPath(TestServices services) => Path.Combine(services.ConfigurationRoot, "workspace", "project");

    private static CreateWorktreeDialogViewModel Dialog(TestServices services, params string[] existingNames)
        => new(services.Folders, RepositoryPath(services), "1a2b3c4", existingNames);

    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(RepositoryPath(services), "main");

        await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");

        await services.Get<IRepositoryContext>().OpenAsync(repository);

        return repository;
    }

    private static void Git(RepositoryHandle repository, params string[] arguments)
    {
        ProcessStartInfo start = new()
        {
            FileName = "git",
            WorkingDirectory = repository.WorkTreePath,
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

    // ---------------------------------------------------------------- the dialog

    [Fact]
    public void Dialog_ProposesAFolderBesideTheRepositoryThatFollowsTheBranch()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            CreateWorktreeDialogViewModel model = Dialog(services);
            string beside = Path.Combine(services.ConfigurationRoot, "workspace");

            Assert.Equal(Path.Combine(beside, "project" + WorktreePaths.Infix), model.WorkingDirectory);

            model.Name = "feature/login";

            Assert.Equal(Path.Combine(beside, "project.worktree.feature-login"), model.WorkingDirectory);
            Assert.True(model.OpenAfterCreate);
            Assert.Equal("1a2b3c4", model.StartPointLabel);
        });
    }

    [Fact]
    public void Dialog_StopsFollowingTheBranchOnceTheFolderIsEdited()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            CreateWorktreeDialogViewModel model = Dialog(services);
            string chosen = Path.Combine(services.ConfigurationRoot, "elsewhere", "mine");

            model.Name = "topic";
            model.WorkingDirectory = chosen;
            model.Name = "another";

            Assert.Equal(chosen, model.WorkingDirectory);
        });
    }

    [Fact]
    public void Dialog_SaysWhyItCannotBeConfirmed()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            CreateWorktreeDialogViewModel model = Dialog(services, "main");

            // No name yet.
            Assert.False(model.IsValid);
            Assert.True(model.HasValidationMessage);

            model.Name = "bad name";
            Assert.Contains("space", model.ValidationMessage, StringComparison.Ordinal);

            model.Name = "main";
            Assert.Equal("A branch called \"main\" already exists.", model.ValidationMessage);

            model.Name = "topic";
            Assert.True(model.IsValid);

            model.WorkingDirectory = Path.Combine(RepositoryPath(services), "inside");
            Assert.Contains("inside the repository", model.ValidationMessage, StringComparison.Ordinal);

            string busy = Directory.CreateDirectory(Path.Combine(services.ConfigurationRoot, "busy")).FullName;
            File.WriteAllText(Path.Combine(busy, "notes.txt"), "keep me\n");
            model.WorkingDirectory = busy;
            Assert.Contains("not empty", model.ValidationMessage, StringComparison.Ordinal);

            model.WorkingDirectory = "relative";
            Assert.Contains("full path", model.ValidationMessage, StringComparison.Ordinal);

            model.WorkingDirectory = Path.Combine(services.ConfigurationRoot, "free");
            Assert.True(model.IsValid);
            Assert.False(model.HasValidationMessage);
        });
    }

    [Fact]
    public void Dialog_RaisesValidationChangedAsItIsTyped()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            CreateWorktreeDialogViewModel model = Dialog(services);
            int raised = 0;
            model.ValidationChanged += (_, _) => raised++;

            model.Name = "t";
            model.WorkingDirectory = Path.Combine(services.ConfigurationRoot, "free");

            Assert.True(raised >= 2);
        });
    }

    [Fact]
    public void Browse_PutsTheWorktreesFolderInTheChosenOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            CreateWorktreeDialogViewModel model = Dialog(services);
            string chosen = Directory.CreateDirectory(Path.Combine(services.ConfigurationRoot, "trees")).FullName;

            model.Name = "topic";
            services.Folders.Answer = chosen;
            await model.BrowseCommand.ExecuteAsync(null);

            Assert.Equal(Path.Combine(chosen, "project.worktree.topic"), model.WorkingDirectory);

            // Still following the branch, in the chosen folder.
            model.Name = "other";
            Assert.Equal(Path.Combine(chosen, "project.worktree.other"), model.WorkingDirectory);

            // A cancelled picker leaves the field alone.
            services.Folders.Answer = null;
            await model.BrowseCommand.ExecuteAsync(null);
            Assert.Equal(Path.Combine(chosen, "project.worktree.other"), model.WorkingDirectory);
        });
    }

    [Fact]
    public void Browse_KeepsTheFolderNameTheReaderTyped()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            CreateWorktreeDialogViewModel model = Dialog(services);
            string chosen = Directory.CreateDirectory(Path.Combine(services.ConfigurationRoot, "trees")).FullName;

            model.Name = "topic";
            model.WorkingDirectory = Path.Combine(services.ConfigurationRoot, "somewhere", "my-tree");
            services.Folders.Answer = chosen;
            await model.BrowseCommand.ExecuteAsync(null);

            Assert.Equal(Path.Combine(chosen, "my-tree"), model.WorkingDirectory);
        });
    }

    // ---------------------------------------------------------------- the flow

    [Fact]
    public void Create_MakesTheWorktreeAndOpensItInANewWindow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            string sha = GitProbe.Sha(repository, "HEAD");

            bool disabledUntilNamed = false;
            bool enabledOnceNamed = false;
            string? proposed = null;

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: CreateWorktreeDialogViewModel model })
                {
                    disabledUntilNamed = !dialog.IsPrimaryButtonEnabled;
                    model.Name = "feature/login";
                    enabledOnceNamed = dialog.IsPrimaryButtonEnabled;
                    proposed = model.WorkingDirectory;
                }
            };
            services.Dialogs.Result = DialogResult.Primary;

            bool created = await services.Get<IWorktreeOperations>().CreateAsync(sha, sha[..7]);

            string folder = Path.Combine(services.ConfigurationRoot, "workspace", "project.worktree.feature-login");

            Assert.True(created);
            Assert.True(disabledUntilNamed);
            Assert.True(enabledOnceNamed);
            Assert.Equal("Create a worktree", services.Dialogs.Last?.Title);
            Assert.Equal(folder, proposed);

            Assert.True(File.Exists(Path.Combine(folder, "README.md")));
            Assert.Equal([folder], services.Launcher.Launched);
            Assert.Contains(
                services.Get<IRepositoryContext>().Refs.LocalBranches,
                branch => branch.ShortName == "feature/login");

            // The repository it came from stays where it was.
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);

            Assert.Contains(
                services.InfoBar.Shown,
                note => note.Title == "Worktree created" && note.Severity == InfoBarSeverity.Success);
        });
    }

    [Fact]
    public void Create_WithoutOpening_StartsNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: CreateWorktreeDialogViewModel model })
                {
                    model.Name = "topic";
                    model.OpenAfterCreate = false;
                }
            };
            services.Dialogs.Result = DialogResult.Primary;

            Assert.True(await services.Get<IWorktreeOperations>().CreateAsync("main", "main"));

            Assert.True(Directory.Exists(WorktreePaths.DefaultFor(repository.WorkTreePath, "topic")));
            Assert.Empty(services.Launcher.Launched);
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Worktree created");
        });
    }

    [Fact]
    public void Create_Declined_CreatesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: CreateWorktreeDialogViewModel model })
                {
                    model.Name = "topic";
                }
            };
            services.Dialogs.Result = DialogResult.None;

            Assert.False(await services.Get<IWorktreeOperations>().CreateAsync("main", "main"));

            Assert.False(Directory.Exists(WorktreePaths.DefaultFor(repository.WorkTreePath, "topic")));
            Assert.DoesNotContain(
                services.Get<IRepositoryContext>().Refs.LocalBranches,
                branch => branch.ShortName == "topic");
            Assert.Empty(services.Launcher.Launched);
            Assert.DoesNotContain(services.InfoBar.Shown, note => note.Title == "Worktree created");
        });
    }

    [Fact]
    public void Create_WhenTheWindowCannotStart_SaysWhereTheWorktreeIs()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            services.Launcher.Succeeds = false;

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: CreateWorktreeDialogViewModel model })
                {
                    model.Name = "topic";
                }
            };
            services.Dialogs.Result = DialogResult.Primary;

            Assert.True(await services.Get<IWorktreeOperations>().CreateAsync("main", "main"));

            string folder = WorktreePaths.DefaultFor(repository.WorkTreePath, "topic");
            RecordedNotification note = Assert.Single(services.InfoBar.Shown, shown => shown.Title == "No new window");

            Assert.True(Directory.Exists(folder));
            Assert.Equal(InfoBarSeverity.Error, note.Severity);
            Assert.DoesNotContain(services.InfoBar.Shown, shown => shown.Title == "Worktree created");
            Assert.Contains(folder, note.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Create_WithNoRepositoryOpen_AsksNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            Assert.False(await services.Get<IWorktreeOperations>().CreateAsync("main", "main"));
            Assert.Empty(services.Dialogs.Shown);
        });
    }

    // ---------------------------------------------------------------- the menus

    /// <summary>
    /// Two commits, a stash on top of them and uncommitted work — every kind of line the history has —
    /// opened, with the history read.
    /// </summary>
    private static async Task<(RepositoryHandle Repository, HistoryPageViewModel History)> OpenEveryLineAsync(TestServices services)
    {
        RepositoryHandle repository = await BuildRepositoryAsync(services);

        await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "README.md"), "# two\n");
        Git(repository, "commit", "-a", "-m", "Say it twice");

        await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "README.md"), "# stashed\n");
        Git(repository, "stash", "push", "-m", "Work");

        await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "README.md"), "# uncommitted\n");

        await services.Get<IRepositoryContext>().RefreshAsync();

        HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
        await history.ReloadAsync();

        return (repository, history);
    }

    /// <summary>
    /// Answers the dialog with a branch name, and keeps what it said the branch starts from.
    /// </summary>
    private static Func<string?> AnswerWith(TestServices services, string branch, bool open = false)
    {
        string? label = null;

        services.Dialogs.OnShown = dialog =>
        {
            if (dialog.Content is Control { DataContext: CreateWorktreeDialogViewModel model })
            {
                label = model.StartPointLabel;
                model.Name = branch;
                model.OpenAfterCreate = open;
            }
        };
        services.Dialogs.Result = DialogResult.Primary;

        return () => label;
    }

    private static string HeadOf(string worktree)
        => GitProbe.Sha(new RepositoryHandle(worktree, Path.Combine(worktree, ".git")), "HEAD");

    [Fact]
    public void ALinesMenu_OffersAWorktreeRightAfterTheBranch_AndStartsItAtTheLinesCommit()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, HistoryPageViewModel history) = await OpenEveryLineAsync(services);

            CommitRowViewModel older = history.Rows.Single(row => row.Subject == "Add the readme");
            string[] headers = [.. older.MenuEntries.Where(entry => !entry.IsSeparator).Select(entry => entry.Header)];
            HistoryMenuEntry entry = older.MenuEntries.Single(candidate => candidate.Header == "Create worktree here…");

            Assert.Equal(Array.IndexOf(headers, "Create branch here…") + 1, Array.IndexOf(headers, "Create worktree here…"));
            Assert.Equal(PhosphorIcon.FolderPlus, entry.Icon);
            Assert.True(entry.Command!.CanExecute(entry.Parameter));

            Func<string?> label = AnswerWith(services, "from-the-line");
            await ((IAsyncRelayCommand)entry.Command).ExecuteAsync(entry.Parameter);

            string folder = WorktreePaths.DefaultFor(repository.WorkTreePath, "from-the-line");

            Assert.Equal(older.ShortSha, label());
            Assert.Equal(older.Sha, HeadOf(folder));
        });
    }

    [Fact]
    public void ALinesMenu_GreysTheWorktreeOnTheUncommittedLine_AndLeavesItOffAStash()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (_, HistoryPageViewModel history) = await OpenEveryLineAsync(services);

            CommitRowViewModel uncommitted = Assert.Single(history.Rows, row => row.IsUncommitted);
            CommitRowViewModel stash = Assert.Single(history.Rows, row => row.IsStash);

            HistoryMenuEntry entry = uncommitted.MenuEntries.Single(candidate => candidate.Header == "Create worktree here…");

            Assert.False(entry.Command!.CanExecute(entry.Parameter));
            Assert.DoesNotContain(stash.MenuEntries, candidate => candidate.Header == "Create worktree here…");
        });
    }

    [Fact]
    public void ABranchBadge_OffersAWorktreeStartingFromThatBranch()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, HistoryPageViewModel history) = await OpenEveryLineAsync(services);

            HistoryBranchViewModel main = history.Rows.SelectMany(row => row.Branches).Single(branch => branch.Name == "main");

            Assert.True(main.CanCreateWorktree);
            Assert.Equal("Create worktree from \"main\"…", main.CreateWorktreeHeader);

            Func<string?> label = AnswerWith(services, "from-the-badge", open: true);
            await main.Commands.CreateWorktree!.ExecuteAsync(main);

            string folder = WorktreePaths.DefaultFor(repository.WorkTreePath, "from-the-badge");

            Assert.Equal("main", label());
            Assert.Equal(GitProbe.Sha(repository, "main"), HeadOf(folder));
            Assert.Equal([folder], services.Launcher.Launched);
        });
    }

    [Fact]
    public void ABranchRow_OffersAWorktreeStartingFromThatBranch()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            services.OrderListsByName();
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            Git(repository, "branch", "older", "HEAD");
            await services.Get<IRepositoryContext>().RefreshAsync();

            BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
            await page.OnAppearingAsync();

            BranchRowViewModel older = page.Groups.SelectMany(group => group.Rows).Single(row => row.FullName == "older");

            Assert.True(older.CreateWorktreeCommand.CanExecute(older));

            Func<string?> label = AnswerWith(services, "from-the-row");
            await older.CreateWorktreeCommand.ExecuteAsync(older);

            string folder = WorktreePaths.DefaultFor(repository.WorkTreePath, "from-the-row");

            Assert.Equal("older", label());
            Assert.Equal(GitProbe.Sha(repository, "older"), HeadOf(folder));
            Assert.Contains(page.Groups.SelectMany(group => group.Rows), row => row.FullName == "from-the-row");
        });
    }
}
