using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.Core.Merging;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Drives merging from the places a user starts one: the branches page, the graph's row menu and
/// the shell's banner.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class MergeOperationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly HeadlessAvaloniaFixture _fixture;

    public MergeOperationTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "merges"), "main");

        Write(repository, "README.md", "# one\n");
        Write(repository, "src/app.txt", "one\ntwo\nthree\n");

        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "Add the initial files");

        return repository;
    }

    /// <summary>
    /// Builds a branch that changed a different file, so it merges cleanly.
    /// </summary>
    private static async Task BuildCleanBranchAsync(RepositoryHandle repository)
    {
        await GitAsync(repository, "checkout", "-b", "theirs");
        Write(repository, "src/theirs.txt", "from them\n");
        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "Their work");

        await GitAsync(repository, "checkout", "main");
        Write(repository, "src/ours.txt", "from us\n");
        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "Our work");
    }

    /// <summary>
    /// Builds a branch one commit ahead of <c>main</c>, which has not moved — the shape git on its
    /// own would fast-forward.
    /// </summary>
    private static async Task BuildAheadBranchAsync(RepositoryHandle repository)
    {
        await GitAsync(repository, "checkout", "-b", "ahead");
        Write(repository, "src/ahead.txt", "further on\n");
        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "Work ahead");

        await GitAsync(repository, "checkout", "main");
    }

    /// <summary>
    /// Builds a branch that changed the same line, so it conflicts.
    /// </summary>
    private static async Task BuildConflictingBranchAsync(RepositoryHandle repository)
    {
        await GitAsync(repository, "checkout", "-b", "theirs");
        Write(repository, "src/app.txt", "one\ntheir version\nthree\n");
        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "Their version");

        await GitAsync(repository, "checkout", "main");
        Write(repository, "src/app.txt", "one\nour version\nthree\n");
        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "Our version");
    }

    private static void Write(RepositoryHandle repository, string relativePath, string content)
    {
        string full = Path.Combine(repository.WorkTreePath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static Task GitAsync(RepositoryHandle repository, params string[] arguments)
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
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? Task.CompletedTask
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }

    private static async Task<BranchesPageViewModel> OpenBranchesAsync(
        TestServices services,
        RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
        await page.OnAppearingAsync();

        return page;
    }

    private static BranchRowViewModel Row(BranchesPageViewModel page, string name)
        => page.Groups.SelectMany(group => group.Rows).Single(row => row.FullName == name);

    // ---------------------------------------------------------------- from the branches page

    [Fact]
    public void Page_MergesABranchIntoTheCurrentOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await BuildCleanBranchAsync(repository);

            BranchesPageViewModel page = await OpenBranchesAsync(services, repository);

            await page.MergeCommand.ExecuteAsync(Row(page, "theirs"));

            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "src", "theirs.txt")));
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Merged");
        });
    }

    [Fact]
    public void Page_NeverOffersToMergeTheBranchYouAreOn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await BuildCleanBranchAsync(repository);

            BranchesPageViewModel page = await OpenBranchesAsync(services, repository);

            Assert.False(page.MergeCommand.CanExecute(Row(page, "main")));
            Assert.True(page.MergeCommand.CanExecute(Row(page, "theirs")));
        });
    }

    [Fact]
    public void Page_SaysSoWhenThereIsNothingToMerge()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await GitAsync(repository, "branch", "behind", "HEAD");

            BranchesPageViewModel page = await OpenBranchesAsync(services, repository);

            await page.MergeCommand.ExecuteAsync(Row(page, "behind"));

            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Nothing to merge");
        });
    }

    [Fact]
    public void Page_RecordsAMergeCommitEvenWhenAFastForwardWouldDo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await BuildAheadBranchAsync(repository);

            BranchesPageViewModel page = await OpenBranchesAsync(services, repository);

            await page.MergeCommand.ExecuteAsync(Row(page, "ahead"));

            Assert.Equal(2, GitProbe.ParentCount(repository));
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Merged");
        });
    }

    [Fact]
    public void Merge_RecordsAMergeCommitUnlessAFastForwardIsAskedFor()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await BuildAheadBranchAsync(repository);

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            MergeOutcome outcome = await services.Get<IMergeOperations>().MergeAsync("ahead");

            Assert.Equal(MergeResultKind.Merged, outcome.Kind);
            Assert.Equal(2, GitProbe.ParentCount(repository));
        });
    }

    // ---------------------------------------------------------------- conflicts

    [Fact]
    public void Merge_ReportsAConflictAndLeavesTheMergeInProgress()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await BuildConflictingBranchAsync(repository);

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            MergeOutcome outcome = await services.Get<IMergeOperations>().MergeAsync("theirs");

            Assert.Equal(MergeResultKind.Conflicted, outcome.Kind);
            Assert.Equal(["src/app.txt"], outcome.ConflictedPaths);

            RecordedNotification note = Assert.Single(
                services.InfoBar.Shown,
                shown => shown.Title == "The merge stopped on conflicts");

            Assert.Contains("1 file", note.Message, StringComparison.Ordinal);
            Assert.Equal(InfoBarSeverity.Warning, note.Severity);
        });
    }

    [Fact]
    public void Shell_ShowsTheWayOutOfAMergeThatConflicted()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await BuildConflictingBranchAsync(repository);

            MainWindowViewModel shell = services.Get<MainWindowViewModel>();
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            Assert.False(shell.IsMergeInProgress);

            await services.Get<IMergeOperations>().MergeAsync("theirs");
            await services.Get<IRepositoryContext>().RefreshAsync();

            Assert.True(shell.IsMergeInProgress);
            Assert.True(shell.HasOperationInProgress);
            Assert.True(shell.AbortMergeCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Shell_AsksBeforeAbandoningAMerge()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await BuildConflictingBranchAsync(repository);

            MainWindowViewModel shell = services.Get<MainWindowViewModel>();
            await services.Get<IRepositoryContext>().OpenAsync(repository);
            await services.Get<IMergeOperations>().MergeAsync("theirs");
            await services.Get<IRepositoryContext>().RefreshAsync();

            services.Dialogs.Result = DialogResult.Close;

            await shell.AbortMergeCommand.ExecuteAsync(null);

            string message = Assert.IsType<string>(services.Dialogs.Last!.Content);

            // The question says what survives, because "abandon" sounds like it might not.
            Assert.Contains("Commits on either branch are not", message, StringComparison.Ordinal);
            Assert.Equal(DefaultButton.Close, services.Dialogs.Last.DefaultButton);
            Assert.True(shell.IsMergeInProgress);
        });
    }

    [Fact]
    public void Shell_AbandonsTheMergeOnceConfirmed()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await BuildConflictingBranchAsync(repository);

            MainWindowViewModel shell = services.Get<MainWindowViewModel>();
            IRepositoryContext context = services.Get<IRepositoryContext>();

            await context.OpenAsync(repository);
            await services.Get<IMergeOperations>().MergeAsync("theirs");
            await context.RefreshAsync();

            services.Dialogs.Result = DialogResult.Primary;

            await shell.AbortMergeCommand.ExecuteAsync(null);
            await context.RefreshAsync();

            Assert.False(shell.IsMergeInProgress);
            Assert.Equal(
                "one\nour version\nthree\n",
                await File.ReadAllTextAsync(Path.Combine(repository.WorkTreePath, "src", "app.txt")));
        });
    }

    [Fact]
    public void Merge_CommitsOnceEverythingIsResolved()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await BuildConflictingBranchAsync(repository);

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            IMergeOperations merges = services.Get<IMergeOperations>();

            await merges.MergeAsync("theirs");

            // Until the conflict is resolved, committing is refused rather than attempted.
            Assert.False(await merges.ContinueAsync());
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Not ready to commit");

            Write(repository, "src/app.txt", "one\nthe resolved version\nthree\n");
            await GitAsync(repository, "add", "src/app.txt");

            Assert.True(await merges.ContinueAsync("Resolve the two versions"));
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Merge committed");
        });
    }

    // ---------------------------------------------------------------- reports never block

    [Fact]
    public void AMerge_ReturnsWhileItsReportIsStillOpen()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await BuildCleanBranchAsync(repository);

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            // The bar stays open until it is closed, as the real one does: an operation that waited
            // for it would never hand its result back to the page that refreshes on it.
            services.InfoBar.HoldsOpen = true;

            MergeOutcome outcome = await services.Get<IMergeOperations>().MergeAsync("theirs").WaitAsync(Patience);

            Assert.Equal(MergeResultKind.Merged, outcome.Kind);
            Assert.True(services.InfoBar.IsOpen);

            RecordedNotification note = services.InfoBar.Last!;
            Assert.Equal("Merged", note.Title);
            Assert.Equal(InfoBarServiceExtensions.TransientDisplayDuration, note.DisplayDuration);
        });
    }

    [Fact]
    public void AnAbandonedMerge_ReturnsWhileItsReportIsStillOpen()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await BuildConflictingBranchAsync(repository);

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            services.InfoBar.HoldsOpen = true;
            IMergeOperations merges = services.Get<IMergeOperations>();

            await merges.MergeAsync("theirs").WaitAsync(Patience);

            // The conflict is a warning: it stays until it is closed, and still holds nothing up.
            Assert.Null(services.InfoBar.Last!.DisplayDuration);

            services.Dialogs.Result = DialogResult.Primary;

            Assert.True(await merges.AbortAsync().WaitAsync(Patience));
            Assert.True(services.InfoBar.IsOpen);
            Assert.Equal("Merge abandoned", services.InfoBar.Last!.Title);
            Assert.Equal(InfoBarServiceExtensions.TransientDisplayDuration, services.InfoBar.Last.DisplayDuration);
        });
    }

    // ---------------------------------------------------------------- from the graph

    [Fact]
    public void History_MergesTheBranchOnARow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await BuildCleanBranchAsync(repository);

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            CommitRowViewModel row = history.Rows.Single(candidate => candidate.Subject == "Their work");

            HistoryBranchViewModel theirs = row.Branches.Single(branch => branch.IsLocal);
            Assert.True(theirs.CanMergeIntoCurrent);
            Assert.Contains("theirs", theirs.MergeIntoCurrentHeader, StringComparison.Ordinal);

            await theirs.Commands.MergeIntoCurrent.ExecuteAsync(theirs);

            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "src", "theirs.txt")));
        });
    }

    [Fact]
    public void History_OffersNoMergeOnTheBranchYouAreOn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            await BuildCleanBranchAsync(repository);

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            CommitRowViewModel head = history.Rows.Single(row => row.IsHead);

            HistoryBranchViewModel main = head.Branches.Single(branch => branch.IsLocal);
            Assert.Equal("main", main.Name);
            Assert.False(main.CanMergeIntoCurrent);
            Assert.False(main.Commands.MergeIntoCurrent.CanExecute(main));
        });
    }
}
