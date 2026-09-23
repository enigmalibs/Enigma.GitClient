using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Resetting the branch that is checked out from a history line's menu, soft or hard — against a real
/// repository, asserting where the branch ended up and what the work tree holds afterwards.
/// </summary>
/// <remarks>
/// A hard reset throws work away, so much of what is asserted is what the client asked before it
/// acted, and that it did nothing at all when the answer was no.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryResetTests
{
    private const string SoftSuffix = " to this commit - Soft (keep all changes)";
    private const string HardSuffix = " to this commit - Hard (discard all changes)";

    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryResetTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// <c>main</c>, checked out, three commits long.
    /// </summary>
    /// <param name="prepare">What to do to the repository before the history reads it.</param>
    private static async Task<(TestServices Services, RepositoryHandle Repository, HistoryPageViewModel History)> OpenAsync(
        Action<RepositoryHandle>? prepare = null)
    {
        TestServices services = TestServices.Build(useRealRefReader: true);

        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "reset"), "main");

        Commit(repository, "README.md", "# one\n", "Add the readme");
        Commit(repository, "src/app.txt", "one\n", "Add the application file");
        Commit(repository, "src/app.txt", "one\ntwo\n", "Extend the application file");

        prepare?.Invoke(repository);

        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
        await history.OnAppearingAsync();
        await history.ReloadAsync();

        return (services, repository, history);
    }

    private static void Commit(RepositoryHandle repository, string path, string content, string message)
    {
        Write(repository, path, content);

        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", message);
    }

    private static void Write(RepositoryHandle repository, string path, string content)
    {
        string full = Path.Combine(repository.WorkTreePath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static string Read(RepositoryHandle repository, string path)
        => File.ReadAllText(Path.Combine(repository.WorkTreePath, path));

    private static string Git(RepositoryHandle repository, params string[] arguments)
    {
        (int exitCode, string output, string error) = TryGit(repository, arguments);

        return exitCode == 0
            ? output
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }

    private static (int ExitCode, string Output, string Error) TryGit(RepositoryHandle repository, params string[] arguments)
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
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output, error);
    }

    private static string Sha(RepositoryHandle repository, string revision)
        => Git(repository, "rev-parse", revision).Trim();

    private static CommitRowViewModel Line(HistoryPageViewModel history, string subject)
        => history.Rows.Single(row => row.Subject == subject);

    private static string[] Headers(CommitRowViewModel row)
        => [.. row.MenuEntries.Where(entry => !entry.IsSeparator).Select(entry => entry.Header)];

    private static HistoryMenuEntry Entry(CommitRowViewModel row, string suffix)
        => row.MenuEntries.Single(entry => entry.Header.EndsWith(suffix, StringComparison.Ordinal));

    private static bool CanRun(HistoryMenuEntry entry) => entry.Command!.CanExecute(entry.Parameter);

    private static Task RunAsync(HistoryMenuEntry entry)
        => ((AsyncRelayCommand<HistoryResetRequest>)entry.Command!).ExecuteAsync((HistoryResetRequest)entry.Parameter!);

    // ---------------------------------------------------------------- the menu

    [Fact]
    public void ALinesMenu_NamesTheCurrentBranchInBothItemsRightAfterTheCheckout()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            string[] headers = Headers(Line(history, "Add the readme"));
            int checkout = Array.IndexOf(headers, "Check out this commit (detaches HEAD)");

            Assert.True(checkout >= 0);
            Assert.Equal("Reset \"main\" to this commit - Soft (keep all changes)", headers[checkout + 1]);
            Assert.Equal("Reset \"main\" to this commit - Hard (discard all changes)", headers[checkout + 2]);
        });
    }

    [Fact]
    public void OnADetachedHead_NoResetIsOffered()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync(
                repository => Git(repository, "checkout", "--detach", "HEAD~1"));
            using TestServices scope = services;

            Assert.All(history.Rows, row => Assert.DoesNotContain(
                Headers(row),
                header => header.StartsWith("Reset ", StringComparison.Ordinal)));
        });
    }

    [Fact]
    public void TheItems_AreGreyedOutWhereTheyWouldDoNothing()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync(
                repository => Write(repository, "README.md", "edited but not committed\n"));
            using TestServices scope = services;

            // The uncommitted row has no commit to move to.
            CommitRowViewModel uncommitted = history.Rows.Single(row => row.IsUncommitted);
            Assert.False(CanRun(Entry(uncommitted, SoftSuffix)));
            Assert.False(CanRun(Entry(uncommitted, HardSuffix)));

            // Soft onto the commit HEAD is at does nothing; hard there throws the uncommitted work away.
            CommitRowViewModel head = history.Rows.Single(row => row.IsHead);
            Assert.False(CanRun(Entry(head, SoftSuffix)));
            Assert.True(CanRun(Entry(head, HardSuffix)));

            CommitRowViewModel older = Line(history, "Add the readme");
            Assert.True(CanRun(Entry(older, SoftSuffix)));
            Assert.True(CanRun(Entry(older, HardSuffix)));
        });
    }

    // ---------------------------------------------------------------- soft

    [Fact]
    public void Soft_MovesTheBranchKeepsEveryChangeStagedAndAsksNothing()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            string target = Sha(repository, "HEAD~2");

            await RunAsync(Entry(Line(history, "Add the readme"), SoftSuffix));

            Assert.Equal(target, Sha(repository, "main"));
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);
            Assert.Empty(services.Dialogs.Shown);

            // The two later commits' work is all still there, staged.
            Assert.Equal("one\ntwo\n", Read(repository, "src/app.txt"));
            Assert.Equal("A  src/app.txt", Git(repository, "status", "--porcelain").TrimEnd());

            // The history was read again: the branch's old tip is gone from it, and the kept work is
            // the row at its top.
            Assert.True(history.Rows[0].IsUncommitted);
            Assert.DoesNotContain(history.Rows, row => row.Subject == "Extend the application file");
            Assert.Equal(InfoBarSeverity.Success, services.InfoBar.Last?.Severity);
        });
    }

    // ---------------------------------------------------------------- hard

    [Fact]
    public void Hard_AsksFirstAndCancellingChangesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync(
                repository => Write(repository, "README.md", "edited but not committed\n"));
            using TestServices scope = services;

            string before = Sha(repository, "main");
            services.Dialogs.Result = DialogResult.None;

            await RunAsync(Entry(Line(history, "Add the readme"), HardSuffix));

            ContentDialog dialog = Assert.Single(services.Dialogs.Shown);
            Assert.Equal("Discard all changes?", dialog.Title);
            Assert.Equal(DefaultButton.Close, dialog.DefaultButton);

            Assert.Equal(before, Sha(repository, "main"));
            Assert.Equal("edited but not committed\n", Read(repository, "README.md"));
        });
    }

    [Fact]
    public void Hard_OnceConfirmedDiscardsTheTrackedChangesAndKeepsUntrackedFiles()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync(
                repository =>
                {
                    Write(repository, "README.md", "edited but not committed\n");
                    Write(repository, "notes.txt", "never added\n");
                });
            using TestServices scope = services;

            string target = Sha(repository, "HEAD~1");
            services.Dialogs.Result = DialogResult.Primary;

            await RunAsync(Entry(Line(history, "Add the application file"), HardSuffix));

            // The question named the file whose work it takes, and not the one it leaves alone.
            string question = (string)services.Dialogs.Last!.Content!;
            Assert.Contains("README.md", question, StringComparison.Ordinal);
            Assert.DoesNotContain("notes.txt", question, StringComparison.Ordinal);
            Assert.Contains("Untracked files are left where they are.", question, StringComparison.Ordinal);

            Assert.Equal(target, Sha(repository, "main"));
            Assert.Equal("# one\n", Read(repository, "README.md"));
            Assert.Equal("one\n", Read(repository, "src/app.txt"));
            Assert.Equal("never added\n", Read(repository, "notes.txt"));
            Assert.Equal("?? notes.txt", Git(repository, "status", "--porcelain").TrimEnd());
            Assert.Equal(InfoBarSeverity.Success, services.InfoBar.Last?.Severity);
        });
    }

    // ---------------------------------------------------------------- refusals

    [Fact]
    public void AReset_IsRefusedWhenHeadHasLeftTheBranchTheMenuNamed()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            // The menu is opened while HEAD is on main...
            HistoryMenuEntry soft = Entry(Line(history, "Add the readme"), SoftSuffix);
            string before = Sha(repository, "main");

            // ...and HEAD moves to another branch before the item is clicked.
            Git(repository, "checkout", "-b", "other");
            await services.Get<IRepositoryContext>().RefreshAsync();

            await RunAsync(soft);

            Assert.Equal(before, Sha(repository, "main"));
            Assert.Equal(before, Sha(repository, "other"));
            Assert.Equal(InfoBarSeverity.Warning, services.InfoBar.Last?.Severity);
            Assert.Contains("\"other\"", services.InfoBar.Last!.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AReset_IsRefusedWhileAMergeIsInProgress()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync(
                repository =>
                {
                    Git(repository, "checkout", "-b", "side", "HEAD~1");
                    Commit(repository, "src/app.txt", "one\nside\n", "Change it on the side");
                    Git(repository, "checkout", "main");

                    // Both branches changed the same line: the merge stops on the conflict.
                    Assert.NotEqual(0, TryGit(repository, "merge", "side").ExitCode);
                });
            using TestServices scope = services;

            Assert.Equal(RepositoryOperation.Merge, services.Get<IRepositoryContext>().Head?.Operation);

            string before = Sha(repository, "main");
            services.Dialogs.Result = DialogResult.Primary;

            await RunAsync(Entry(Line(history, "Add the readme"), HardSuffix));

            Assert.Empty(services.Dialogs.Shown);
            Assert.Equal(before, Sha(repository, "main"));
            Assert.True(File.Exists(Path.Combine(repository.GitDirectory, "MERGE_HEAD")));
            Assert.Equal(InfoBarSeverity.Warning, services.InfoBar.Last?.Severity);
        });
    }

    [Theory]
    [InlineData(false, false, "main", RepositoryOperation.None, null)]
    [InlineData(true, false, "main", RepositoryOperation.None, "no commit yet")]
    [InlineData(false, true, null, RepositoryOperation.None, "HEAD is detached")]
    [InlineData(false, false, "other", RepositoryOperation.None, "on \"other\" now")]
    [InlineData(false, false, "main", RepositoryOperation.Merge, "A merge is in progress")]
    [InlineData(false, false, "main", RepositoryOperation.CherryPick, "A cherry-pick is in progress")]
    [InlineData(false, false, "main", RepositoryOperation.Revert, "A revert is in progress")]
    [InlineData(false, false, "main", RepositoryOperation.Bisect, "A bisect session is running")]
    [InlineData(false, false, "main", RepositoryOperation.Rebase, "left a rebase in progress")]
    [InlineData(false, false, "main", RepositoryOperation.ApplyMailbox, "An 'am' session is in progress")]
    public void Refuse_SaysWhyAResetCannotGoAhead(
        bool unborn,
        bool detached,
        string? current,
        RepositoryOperation operation,
        string? expected)
    {
        HeadState head = new(unborn, detached, current, unborn ? string.Empty : "0123456789abcdef0123456789abcdef01234567", operation);

        string? refusal = ResetOperations.Refuse(head, "main");

        if (expected is null)
        {
            Assert.Null(refusal);
        }
        else
        {
            Assert.Contains(expected, refusal, StringComparison.Ordinal);
        }
    }
}
