using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Reverting a commit from a history line's menu — against a real repository, asserting the commit it
/// records, the question it asks first, and that a revert which cannot go through leaves everything as
/// it was.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryRevertTests
{
    private const string HardSuffix = " to this commit - Hard (discard all changes)";

    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryRevertTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

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
            .InitAsync(Path.Combine(root, "revert"), "main");

        // The revert is a commit the application makes: it needs an identity of the repository's own,
        // whatever the machine running the tests has configured.
        Git(repository, "config", "user.name", "Ada Lovelace");
        Git(repository, "config", "user.email", "ada@example.com");

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

    private static bool IsReverting(RepositoryHandle repository)
        => File.Exists(Path.Combine(repository.GitDirectory, "REVERT_HEAD"));

    private static CommitRowViewModel Line(HistoryPageViewModel history, string subject)
        => history.Rows.Single(row => row.Subject == subject);

    private static string[] Headers(CommitRowViewModel row)
        => [.. row.MenuEntries.Where(entry => !entry.IsSeparator).Select(entry => entry.Header)];

    private static HistoryMenuEntry RevertEntry(CommitRowViewModel row)
        => row.MenuEntries.Single(entry => entry.Header == CommitRowViewModel.RevertHeader);

    private static bool CanRun(HistoryMenuEntry entry) => entry.Command!.CanExecute(entry.Parameter);

    private static Task RunAsync(HistoryMenuEntry entry)
        => ((AsyncRelayCommand<CommitRowViewModel>)entry.Command!).ExecuteAsync((CommitRowViewModel)entry.Parameter!);

    // ---------------------------------------------------------------- the menu

    [Fact]
    public void ALinesMenu_OffersTheRevertRightAfterTheResets()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            CommitRowViewModel line = Line(history, "Add the readme");
            string[] headers = Headers(line);
            int hard = Array.FindIndex(headers, header => header.EndsWith(HardSuffix, StringComparison.Ordinal));

            Assert.True(hard >= 0);
            Assert.Equal("Revert this commit…", headers[hard + 1]);
            Assert.True(RevertEntry(line).HasIcon);
        });
    }

    [Fact]
    public void OnADetachedHead_NoRevertIsOffered()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync(
                repository => Git(repository, "checkout", "--detach", "HEAD~1"));
            using TestServices scope = services;

            Assert.All(history.Rows, row => Assert.DoesNotContain(CommitRowViewModel.RevertHeader, Headers(row)));
        });
    }

    [Fact]
    public void TheItem_IsGreyedOutOnTheUncommittedLineOnly()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync(
                repository => Write(repository, "README.md", "edited but not committed\n"));
            using TestServices scope = services;

            Assert.False(CanRun(RevertEntry(history.Rows.Single(row => row.IsUncommitted))));
            Assert.True(CanRun(RevertEntry(history.Rows.Single(row => row.IsHead))));
            Assert.True(CanRun(RevertEntry(Line(history, "Add the readme"))));
        });
    }

    // ---------------------------------------------------------------- the revert

    [Fact]
    public void ARevert_AsksNamingTheBranchAndCancellingChangesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            string before = Sha(repository, "main");
            CommitRowViewModel line = Line(history, "Extend the application file");
            services.Dialogs.Result = DialogResult.None;

            await RunAsync(RevertEntry(line));

            ContentDialog dialog = Assert.Single(services.Dialogs.Shown);
            Assert.Equal($"Revert {line.ShortSha}?", dialog.Title);
            Assert.Equal("Revert", dialog.PrimaryButtonText);
            Assert.Equal(DefaultButton.Primary, dialog.DefaultButton);

            string question = (string)dialog.Content!;
            Assert.Contains("\"main\"", question, StringComparison.Ordinal);
            Assert.Contains("\"Extend the application file\"", question, StringComparison.Ordinal);
            Assert.Contains("stays in the history", question, StringComparison.Ordinal);

            Assert.Equal(before, Sha(repository, "main"));
            Assert.Equal("one\ntwo\n", Read(repository, "src/app.txt"));
        });
    }

    [Fact]
    public void ARevert_OnceConfirmedRecordsTheUndoingCommitAndTheHistoryShowsIt()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            string before = Sha(repository, "main");
            services.Dialogs.Result = DialogResult.Primary;

            await RunAsync(RevertEntry(Line(history, "Extend the application file")));

            // One commit on top, undoing the change, on the branch that was checked out.
            Assert.Equal(before, Sha(repository, "main~1"));
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);
            Assert.Equal("one\n", Read(repository, "src/app.txt"));
            Assert.Equal(string.Empty, Git(repository, "status", "--porcelain"));

            // The history was read again, with the revert at its top and the reverted commit kept.
            Assert.Equal("Revert \"Extend the application file\"", history.Rows[0].Subject);
            Assert.Contains(history.Rows, row => row.Subject == "Extend the application file");
            Assert.Equal(InfoBarSeverity.Success, services.InfoBar.Last?.Severity);
        });
    }

    [Fact]
    public void ARevert_OfAMergeUndoesWhatItBroughtIn()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync(
                repository =>
                {
                    Git(repository, "checkout", "-b", "side", "HEAD~1");
                    Commit(repository, "src/side.txt", "side\n", "Work on the side");
                    Git(repository, "checkout", "main");
                    Git(repository, "merge", "--no-ff", "side", "-m", "Merge side");
                });
            using TestServices scope = services;

            services.Dialogs.Result = DialogResult.Primary;

            await RunAsync(RevertEntry(Line(history, "Merge side")));

            Assert.Contains("It is a merge", (string)services.Dialogs.Last!.Content!, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(repository.WorkTreePath, "src", "side.txt")));
            Assert.Equal("one\ntwo\n", Read(repository, "src/app.txt"));
            Assert.Equal(InfoBarSeverity.Success, services.InfoBar.Last?.Severity);
        });
    }

    [Fact]
    public void ARevertThatConflicts_ChangesNothingAndNamesTheFile()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync(
                repository =>
                {
                    Commit(repository, "src/app.txt", "one\nTWO\n", "Shout the second line");
                    Commit(repository, "src/app.txt", "one\nTwo\n", "Settle the second line");
                });
            using TestServices scope = services;

            string before = Sha(repository, "main");
            services.Dialogs.Result = DialogResult.Primary;

            await RunAsync(RevertEntry(Line(history, "Shout the second line")));

            Assert.Equal(before, Sha(repository, "main"));
            Assert.False(IsReverting(repository));
            Assert.Equal("one\nTwo\n", Read(repository, "src/app.txt"));
            Assert.Equal(string.Empty, Git(repository, "status", "--porcelain"));

            Assert.Equal(InfoBarSeverity.Warning, services.InfoBar.Last?.Severity);
            Assert.Contains("src/app.txt", services.InfoBar.Last!.Message, StringComparison.Ordinal);
            Assert.Equal(RepositoryOperation.None, services.Get<IRepositoryContext>().Head?.Operation);
        });
    }

    // ---------------------------------------------------------------- refusals

    [Fact]
    public void ARevert_IsRefusedWhileAMergeIsInProgress()
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

            string before = Sha(repository, "main");
            services.Dialogs.Result = DialogResult.Primary;

            await RunAsync(RevertEntry(Line(history, "Add the readme")));

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
    [InlineData(false, false, "main", RepositoryOperation.Merge, "A merge is in progress")]
    [InlineData(false, false, "main", RepositoryOperation.Revert, "A revert is in progress")]
    [InlineData(false, false, "main", RepositoryOperation.Rebase, "left a rebase in progress")]
    public void Refuse_SaysWhyARevertCannotGoAhead(
        bool unborn,
        bool detached,
        string? current,
        RepositoryOperation operation,
        string? expected)
    {
        HeadState head = new(unborn, detached, current, unborn ? string.Empty : "0123456789abcdef0123456789abcdef01234567", operation);

        string? refusal = RevertOperations.Refuse(head);

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
