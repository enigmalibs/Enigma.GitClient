using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Worktrees;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The deletes and checkouts git would refuse with "used by worktree", refused before it is asked: the
/// delete says where the branch is, alone or in a batch; the checkout offers to open that worktree — from
/// the branches dialog's rows, the history's badges, a drop-merge and a revision's checkout alike.
/// Against a real repository with a real worktree.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class WorktreeGuardTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public WorktreeGuardTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// <c>main</c> checked out here, <c>topic</c> in a worktree beside it, and <c>other</c> — one commit
    /// ahead — nowhere; opened, with the worktrees read.
    /// </summary>
    private static async Task<(RepositoryHandle Repository, string Worktree)> OpenAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, "project"), "main");

        await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        Git(repository.WorkTreePath, "add", "--all");
        Git(repository.WorkTreePath, "commit", "-m", "Add the readme");

        Git(repository.WorkTreePath, "checkout", "-b", "other");
        await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "other.txt"), "other\n");
        Git(repository.WorkTreePath, "add", "--all");
        Git(repository.WorkTreePath, "commit", "-m", "Work on other");
        Git(repository.WorkTreePath, "checkout", "main");

        string worktree = WorktreePaths.DefaultFor(repository.WorkTreePath, "topic");
        await services.Get<IWorktreeService>().AddAsync(repository, worktree, "topic", "HEAD");

        await services.Get<IRepositoryContext>().OpenAsync(repository);

        return (repository, worktree);
    }

    private static bool BranchExists(TestServices services, string name)
        => services.Get<IRepositoryContext>().Refs.LocalBranches.Any(branch => branch.ShortName == name);

    private static string Sha(RepositoryHandle repository, string revision) => GitProbe.Sha(repository, revision);

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

    // ---------------------------------------------------------------- deleting

    [Fact]
    public void ALoneDelete_IsRefusedWithWhereTheBranchIs_AndRunsNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (_, string worktree) = await OpenAsync(services);

            bool deleted = await services.Get<IBranchOperations>().DeleteAsync("topic", isRemote: false);

            Assert.False(deleted);
            Assert.Empty(services.Dialogs.Shown);
            Assert.True(BranchExists(services, "topic"));

            RecordedNotification note = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Could not delete \"topic\"", note.Title);
            Assert.Equal($"\"topic\" is checked out in the worktree at {worktree}. Remove the worktree first.", note.Message);
            Assert.Equal(InfoBarSeverity.Warning, note.Severity);
        });
    }

    [Fact]
    public void ABatch_SkipsTheBranchInAWorktree_AndDeletesTheRest()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (_, string worktree) = await OpenAsync(services);
            services.Dialogs.Result = DialogResult.Primary;

            bool deleted = await services.Get<IBranchOperations>().DeleteAsync(
                [new BranchToDelete("topic", false), new BranchToDelete("other", false)]);

            Assert.True(deleted);

            // The question names it as left out, and why.
            string question = Assert.IsType<string>(services.Dialogs.Last!.Content);
            Assert.Contains($"topic — skipped: it is checked out in the worktree at {worktree}", question, StringComparison.Ordinal);

            Assert.True(BranchExists(services, "topic"));
            Assert.False(BranchExists(services, "other"));

            RecordedNotification note = services.InfoBar.Shown[^1];
            Assert.Contains($"topic — it is checked out in the worktree at {worktree}", note.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ABatchOfOnlyRefusedBranches_AsksNothing_AndSaysWhyForEach()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (_, string worktree) = await OpenAsync(services);

            bool deleted = await services.Get<IBranchOperations>().DeleteAsync(
                [new BranchToDelete("main", false), new BranchToDelete("topic", false)]);

            Assert.False(deleted);
            Assert.Empty(services.Dialogs.Shown);

            RecordedNotification note = Assert.Single(services.InfoBar.Shown);
            Assert.Equal(InfoBarSeverity.Warning, note.Severity);
            Assert.Contains("main — it is checked out", note.Message, StringComparison.Ordinal);
            Assert.Contains($"topic — it is checked out in the worktree at {worktree}", note.Message, StringComparison.Ordinal);
        });
    }

    // ---------------------------------------------------------------- checking out

    [Fact]
    public void ACheckout_IsRefusedWithTheOffer_AndOpenLaunchesOnTheFolder()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, string worktree) = await OpenAsync(services);
            services.Dialogs.Result = DialogResult.Primary;

            bool moved = await services.Get<IBranchOperations>().CheckoutAsync("topic", isRemote: false);

            Assert.False(moved);

            ContentDialog dialog = Assert.Single(services.Dialogs.Shown);
            Assert.Equal("Branch in a worktree", dialog.Title);
            Assert.Equal($"\"topic\" is checked out in the worktree at {worktree}. Open it in a new window?", dialog.Content);
            Assert.Equal("Open", dialog.PrimaryButtonText);
            Assert.Equal("Cancel", dialog.CloseButtonText);

            Assert.Equal([worktree], services.Launcher.Launched);
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);
            Assert.Equal(Sha(repository, "main"), Sha(repository, "HEAD"));
        });
    }

    [Fact]
    public void ACheckout_Cancelled_LaunchesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await OpenAsync(services);
            services.Dialogs.Result = DialogResult.Close;

            Assert.False(await services.Get<IBranchOperations>().CheckoutAsync("topic", isRemote: false));

            Assert.Empty(services.Launcher.Launched);
            Assert.Empty(services.InfoBar.Shown);
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);
        });
    }

    [Fact]
    public void ACheckout_WhoseWindowCannotStart_SaysSo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (_, string worktree) = await OpenAsync(services);
            services.Dialogs.Result = DialogResult.Primary;
            services.Launcher.Succeeds = false;

            Assert.False(await services.Get<IBranchOperations>().CheckoutAsync("topic", isRemote: false));

            RecordedNotification note = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("No new window", note.Title);
            Assert.Equal(InfoBarSeverity.Error, note.Severity);
            Assert.Contains(worktree, note.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ACheckoutOfARevision_NamingTheBranch_IsRefusedTheSameWay()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (_, string worktree) = await OpenAsync(services);
            services.Dialogs.Result = DialogResult.Primary;

            Assert.False(await services.Get<ICheckoutOperations>().CheckoutAsync("topic"));

            ContentDialog dialog = Assert.Single(services.Dialogs.Shown);
            Assert.Equal("Branch in a worktree", dialog.Title);
            Assert.Equal([worktree], services.Launcher.Launched);
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);
        });
    }

    [Fact]
    public void ADetachedCheckoutOfTheBranchesCommit_IsNotRefused()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, _) = await OpenAsync(services);

            // The detach warning is the only question, answered "check out anyway".
            services.Dialogs.Result = DialogResult.Primary;

            Assert.True(await services.Get<ICheckoutOperations>().CheckoutAsync("topic", detach: true));

            Assert.DoesNotContain(services.Dialogs.Shown, dialog => dialog.Title == "Branch in a worktree");
            Assert.Empty(services.Launcher.Launched);
            Assert.True(services.Get<IRepositoryContext>().Head?.IsDetached);
            Assert.Equal(Sha(repository, "topic"), Sha(repository, "HEAD"));
        });
    }

    [Fact]
    public void ARemoteBranchWhoseLocalBranchIsInAWorktree_IsRefused()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, string worktree) = await OpenAsync(services);

            string origin = Path.Combine(services.ConfigurationRoot, "workspace", "origin.git");
            Directory.CreateDirectory(origin);
            Git(repository.WorkTreePath, "init", "--bare", origin);
            Git(repository.WorkTreePath, "remote", "add", "origin", origin);
            Git(repository.WorkTreePath, "push", "origin", "topic");
            await services.Get<IRepositoryContext>().RefreshAsync();

            services.Dialogs.Result = DialogResult.Close;

            Assert.False(await services.Get<IBranchOperations>().CheckoutAsync("origin/topic", isRemote: true));

            Assert.Equal("Branch in a worktree", Assert.Single(services.Dialogs.Shown).Title);
            Assert.Contains(worktree, Assert.IsType<string>(services.Dialogs.Last!.Content), StringComparison.Ordinal);
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);
        });
    }

    [Fact]
    public void ADropMergeOntoABranchInAWorktree_IsRefusedBeforeAnythingMoves()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, _) = await OpenAsync(services);
            string topic = Sha(repository, "topic");
            services.Dialogs.Result = DialogResult.Close;

            bool changed = await services.Get<IBranchDropOperations>().DropAsync(
                new BranchDropRequest("other", SourceIsRemote: false, "topic", TargetIsRemote: false, TargetIsCurrent: false));

            Assert.False(changed);
            Assert.Equal("Branch in a worktree", Assert.Single(services.Dialogs.Shown).Title);
            Assert.Equal(topic, Sha(repository, "topic"));
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);
        });
    }

    [Fact]
    public void AWorktreeWhoseFolderIsGone_GetsTheMessageOnly()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (_, string worktree) = await OpenAsync(services);
            Directory.Delete(worktree, recursive: true);
            await services.Get<IRepositoryContext>().RefreshAsync();

            services.Dialogs.Result = DialogResult.Primary;

            Assert.False(await services.Get<IBranchOperations>().CheckoutAsync("topic", isRemote: false));

            ContentDialog dialog = Assert.Single(services.Dialogs.Shown);
            Assert.Equal("Branch in a worktree", dialog.Title);
            Assert.True(string.IsNullOrEmpty(dialog.PrimaryButtonText), "a missing folder is offered to be opened");
            Assert.Equal(
                $"\"topic\" is checked out in the worktree at {worktree}. Its folder is missing: remove the worktree to free the branch.",
                dialog.Content);
            Assert.Empty(services.Launcher.Launched);
        });
    }

    [Fact]
    public void ABranchNoOtherWorktreeHas_IsCheckedOutAsBefore()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await OpenAsync(services);

            Assert.True(await services.Get<IBranchOperations>().CheckoutAsync("other", isRemote: false));

            Assert.DoesNotContain(services.Dialogs.Shown, dialog => dialog.Title == "Branch in a worktree");
            Assert.Equal("other", services.Get<IRepositoryContext>().Head?.BranchName);
        });
    }
}
