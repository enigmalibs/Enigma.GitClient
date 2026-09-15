using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.Core.Merging;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives merging from the places a user starts one: the branches page, the graph's row menu and
/// the shell's banner.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class MergeOperationTests
{
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

            Assert.True(row.CanMergeBranch);
            Assert.Contains("theirs", row.MergeHeader, StringComparison.Ordinal);

            await row.Commands!.MergeBranch.ExecuteAsync(row);

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

            Assert.Equal("main", head.BranchName);
            Assert.False(head.CanMergeBranch);
            Assert.False(head.Commands!.MergeBranch.CanExecute(head));
        });
    }
}
