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
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Squashing from the history's lines — against a real repository shaped like the phased workflow
/// that motivated it: the base's flag and chip, the menu in each state, and a squash that creates the
/// new branch, shows its commit and leaves every existing branch where it was.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistorySquashTests
{
    private const string BaseSubject = "Plan the feature";
    private const string TipSubject = "Merge phase3 into feature";

    private readonly HeadlessAvaloniaFixture _fixture;

    public HistorySquashTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A readme, the base, then three phases each committed on its own branch and merged back into
    /// <c>feature</c> with <c>--no-ff</c>; <c>feature</c> is checked out.
    /// </summary>
    /// <param name="prepare">What to do to the repository before the history reads it.</param>
    private static async Task<(TestServices Services, RepositoryHandle Repository, HistoryPageViewModel History)> OpenAsync(
        Action<RepositoryHandle>? prepare = null)
    {
        TestServices services = TestServices.Build(useRealRefReader: true);

        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "squash"), "main");

        // The squash commit is one the application makes: it needs an identity of the repository's own.
        Git(repository, "config", "user.name", "Ada Lovelace");
        Git(repository, "config", "user.email", "ada@example.com");

        Commit(repository, "README.md", "# one\n", "Add the readme");
        Commit(repository, "docs/plan.md", "plan\n", BaseSubject);
        Git(repository, "checkout", "-b", "feature");

        for (int phase = 1; phase <= 3; phase++)
        {
            Git(repository, "checkout", "-b", $"phase{phase}");
            Commit(repository, $"docs/phase{phase}.md", $"phase {phase}\n", $"Add phase {phase}");
            Git(repository, "checkout", "feature");
            Git(repository, "merge", "--no-ff", $"phase{phase}", "-m", $"Merge phase{phase} into feature");
        }

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

    private static bool BranchExists(RepositoryHandle repository, string name)
        => TryGit(repository, "show-ref", "--verify", "--quiet", $"refs/heads/{name}").ExitCode == 0;

    private static CommitRowViewModel Line(HistoryPageViewModel history, string subject)
        => history.Rows.Single(row => row.Subject == subject);

    private static string[] Headers(CommitRowViewModel row)
        => [.. row.MenuEntries.Where(entry => !entry.IsSeparator).Select(entry => entry.Header)];

    private static HistoryMenuEntry Entry(CommitRowViewModel row, Func<string, bool> header)
        => row.MenuEntries.Single(entry => !entry.IsSeparator && header(entry.Header));

    private static HistoryMenuEntry SquashEntry(CommitRowViewModel row)
        => Entry(row, header => header.StartsWith("Squash ", StringComparison.Ordinal));

    private static bool CanRun(HistoryMenuEntry entry) => entry.Command!.CanExecute(entry.Parameter);

    private static void SetBase(HistoryPageViewModel history, string subject)
        => history.SquashCommands.SetBase.Execute(Line(history, subject));

    private static Task SquashAsync(HistoryMenuEntry entry)
        => ((AsyncRelayCommand<HistorySquashRequest>)entry.Command!).ExecuteAsync((HistorySquashRequest)entry.Parameter!);

    /// <summary>
    /// Types a title into the squash dialog as it is shown, which is what makes it confirmable.
    /// </summary>
    private static void TitleTheDialog(TestServices services, string title)
        => services.Dialogs.OnShown = dialog =>
        {
            SquashDialogViewModel model = (SquashDialogViewModel)((Control)dialog.Content!).DataContext!;
            model.Message = title + model.Message;
        };

    // ---------------------------------------------------------------- the menu

    [Fact]
    public void WithNoBase_EveryCommitLineOffersToSetIt()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            Assert.All(history.Rows.Where(row => row.Commit is not null), row =>
            {
                string[] headers = Headers(row);

                Assert.Contains(CommitRowViewModel.SetSquashBaseHeader, headers);
                Assert.DoesNotContain(headers, header => header.StartsWith("Squash ", StringComparison.Ordinal));
                Assert.True(CanRun(Entry(row, header => header == CommitRowViewModel.SetSquashBaseHeader)));
            });
        });
    }

    [Fact]
    public void TheGroup_ComesRightAfterTheRevert()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            SetBase(history, BaseSubject);
            string[] headers = Headers(Line(history, TipSubject));
            int revert = Array.IndexOf(headers, CommitRowViewModel.RevertHeader);

            Assert.True(revert >= 0);
            Assert.Equal(CommitRowViewModel.SetSquashBaseHeader, headers[revert + 1]);
            Assert.StartsWith("Squash ", headers[revert + 2], StringComparison.Ordinal);
            Assert.Equal(CommitRowViewModel.ClearSquashBaseHeader, headers[revert + 3]);
            Assert.True(SquashEntry(Line(history, TipSubject)).HasIcon);
        });
    }

    [Fact]
    public void TheBase_IsFlaggedShownInTheToolbarAndOffersToBeCleared()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            SetBase(history, BaseSubject);
            CommitRowViewModel line = Line(history, BaseSubject);

            Assert.True(line.IsSquashBase);
            Assert.Single(history.Rows, row => row.IsSquashBase);
            Assert.True(history.HasSquashBase);
            Assert.Equal($"Squash base: {line.ShortSha}", history.SquashBaseSummary);

            string[] headers = Headers(line);
            Assert.Contains(CommitRowViewModel.ClearSquashBaseHeader, headers);
            Assert.DoesNotContain(CommitRowViewModel.SetSquashBaseHeader, headers);
        });
    }

    [Fact]
    public void TheTip_OffersToSquashTheCommitsCountedFromTheLoadedLines()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            SetBase(history, BaseSubject);
            CommitRowViewModel baseLine = Line(history, BaseSubject);
            HistoryMenuEntry squash = SquashEntry(Line(history, TipSubject));

            Assert.Equal(CommitRowViewModel.SquashHeader(6, baseLine.ShortSha), squash.Header);
            Assert.Equal($"Squash 6 commits since {baseLine.ShortSha} into a new branch…", squash.Header);
            Assert.True(CanRun(squash));
        });
    }

    [Fact]
    public void ALineTheBaseIsNotBelowOnItsMainLine_IsGreyedOut()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            // A phase commit is the second parent of its merge: not on the tip's main line.
            SetBase(history, "Add phase 1");
            CommitRowViewModel baseLine = Line(history, "Add phase 1");
            HistoryMenuEntry squash = SquashEntry(Line(history, TipSubject));

            Assert.Equal(CommitRowViewModel.UnmeasuredSquashHeader(baseLine.ShortSha), squash.Header);
            Assert.False(CanRun(squash));

            // Nor is anything below the base.
            Assert.False(CanRun(SquashEntry(Line(history, "Add the readme"))));
        });
    }

    [Fact]
    public void TheUncommittedLine_ListsTheItemsGreyedOut()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync(
                repository => Write(repository, "README.md", "edited but not committed\n"));
            using TestServices scope = services;

            CommitRowViewModel uncommitted = history.Rows.Single(row => row.IsUncommitted);

            Assert.False(CanRun(Entry(uncommitted, header => header == CommitRowViewModel.SetSquashBaseHeader)));

            SetBase(history, BaseSubject);

            Assert.False(CanRun(SquashEntry(uncommitted)));
        });
    }

    [Fact]
    public void ClearingTheBase_FromTheChipTakesTheFlagAway()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            SetBase(history, BaseSubject);
            history.ClearSquashBaseCommand.Execute(null);

            Assert.False(history.HasSquashBase);
            Assert.DoesNotContain(history.Rows, row => row.IsSquashBase);
            Assert.False(history.ClearSquashBaseCommand.CanExecute(null));
        });
    }

    [Fact]
    public void TheBase_SurvivesAReloadAndIsForgottenWhenAnotherRepositoryOpens()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, _, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            SetBase(history, BaseSubject);
            await history.ReloadAsync();

            Assert.True(Line(history, BaseSubject).IsSquashBase);

            RepositoryHandle other = await services.Get<IRepositoryService>()
                .InitAsync(Path.Combine(services.ConfigurationRoot, "workspace", "other"), "main");
            Git(other, "config", "user.name", "Ada Lovelace");
            Git(other, "config", "user.email", "ada@example.com");
            Commit(other, "README.md", "# other\n", "Start the other repository");

            await services.Get<IRepositoryContext>().OpenAsync(other);

            Assert.False(history.HasSquashBase);
        });
    }

    // ---------------------------------------------------------------- the squash

    [Fact]
    public void ASquash_CancelledChangesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            SetBase(history, BaseSubject);
            services.Dialogs.Result = DialogResult.None;

            await SquashAsync(SquashEntry(Line(history, TipSubject)));

            ContentDialog dialog = Assert.Single(services.Dialogs.Shown);
            Assert.Equal("Squash 6 commits into a new branch", dialog.Title);
            Assert.Equal("Squash", dialog.PrimaryButtonText);

            // The default message has no title yet, so the dialog opens unconfirmable.
            Assert.False(dialog.IsPrimaryButtonEnabled);

            Assert.False(BranchExists(repository, "feature-squashed"));
            Assert.True(history.HasSquashBase);
        });
    }

    [Fact]
    public void ASquash_OnceConfirmedCreatesTheBranchShowsItsCommitAndForgetsTheBase()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync();
            using TestServices scope = services;

            string baseSha = Sha(repository, "main");
            string tip = Sha(repository, "feature");

            SetBase(history, BaseSubject);
            TitleTheDialog(services, "Squash the feature");
            services.Dialogs.Result = DialogResult.Primary;

            await SquashAsync(SquashEntry(Line(history, TipSubject)));

            // One commit with the tip's files on the base, on the new branch — and HEAD on it, since
            // HEAD was at the tip.
            string squashed = Sha(repository, "feature-squashed");
            Assert.Equal(baseSha, Sha(repository, "feature-squashed~1"));
            Assert.Equal(string.Empty, Git(repository, "diff", tip, squashed));
            Assert.Equal("feature-squashed", Git(repository, "symbolic-ref", "--short", "HEAD").Trim());
            Assert.Equal("Squash the feature", Git(repository, "log", "-1", "--format=%s", squashed).Trim());

            // Nothing that existed moved.
            Assert.Equal(tip, Sha(repository, "feature"));
            Assert.Equal(baseSha, Sha(repository, "main"));

            // The history shows the squash commit, and the base has served its squash.
            Assert.Equal(squashed, history.SelectedRow?.Sha);
            Assert.False(history.HasSquashBase);

            RecordedNotification report = services.InfoBar.Last!;
            Assert.Equal(InfoBarSeverity.Success, report.Severity);
            Assert.Contains("\"feature\" is untouched", report.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ASquash_IsRefusedDuringAMergeAndWritesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            (TestServices services, RepositoryHandle repository, HistoryPageViewModel history) = await OpenAsync(
                repository =>
                {
                    Git(repository, "checkout", "-b", "other", "main");
                    Commit(repository, "docs/plan.md", "theirs\n", "Plan it otherwise");
                    Git(repository, "checkout", "feature");
                    Commit(repository, "docs/plan.md", "ours\n", "Plan it our way");
                    TryGit(repository, "merge", "other");
                });
            using TestServices scope = services;

            SetBase(history, BaseSubject);
            services.Dialogs.Result = DialogResult.Primary;

            await SquashAsync(SquashEntry(Line(history, TipSubject)));

            Assert.Empty(services.Dialogs.Shown);
            Assert.False(BranchExists(repository, "feature-squashed"));
            Assert.Equal(InfoBarSeverity.Warning, services.InfoBar.Last?.Severity);
            Assert.Contains("merge is in progress", services.InfoBar.Last!.Message, StringComparison.Ordinal);
        });
    }
}
