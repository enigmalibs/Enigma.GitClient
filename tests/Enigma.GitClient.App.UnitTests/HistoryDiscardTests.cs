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
using Enigma.GitClient.Core.Repositories;
using Enigma.Icons.Phosphor;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Throwing the uncommitted work away from the history's uncommitted line: offered there alone, asked
/// in red, and refused where there is nothing sound to go back to.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryDiscardTests
{
    private const string Header = "Discard uncommitted files…";

    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryDiscardTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void TheUncommittedLine_OffersTheDiscard_BesideTheStash_AndNoOtherLineDoes()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            Write(repository, "app.txt", "for the stash\n");
            Git(repository, "stash", "push", "-m", "Work");
            Write(repository, "app.txt", "uncommitted\n");

            HistoryPageViewModel page = await OpenAsync(services, repository);

            CommitRowViewModel uncommitted = Assert.Single(page.Rows, row => row.IsUncommitted);
            string[] headers = [.. uncommitted.MenuEntries.Where(entry => !entry.IsSeparator).Select(entry => entry.Header)];

            int stash = Array.IndexOf(headers, "Stash all changes…");
            Assert.True(stash >= 0);
            Assert.Equal(Header, headers[stash + 1]);
            Assert.Equal(PhosphorIcon.Trash, uncommitted.MenuEntries.Single(entry => entry.Header == Header).Icon);

            Assert.All(
                page.Rows.Where(row => !row.IsUncommitted),
                row => Assert.DoesNotContain(row.MenuEntries, entry => entry.Header == Header));
        });
    }

    [Fact]
    public void Discarding_AsksInRedNamingTheFiles_ThenLeavesTheTreeClean()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            // Modified, staged new, and untracked: three files, whichever half changed.
            Write(repository, "app.txt", "ruined\n");
            Write(repository, "staged.txt", "staged\n");
            Git(repository, "add", "staged.txt");
            Write(repository, "notes.txt", "never committed\n");

            HistoryPageViewModel page = await OpenAsync(services, repository);

            bool redWhileAsked = false;
            services.Dialogs.OnShown = dialog => redWhileAsked = dialog.Classes.Contains(ContentDialogServiceExtensions.DangerClass);
            services.Dialogs.Result = DialogResult.Primary;

            await DiscardFromTheUncommittedLineAsync(page);

            ContentDialog dialog = Assert.Single(services.Dialogs.Shown);
            string message = Assert.IsType<string>(dialog.Content);

            Assert.Equal("Discard uncommitted files", dialog.Title);
            Assert.Contains("3 files", message, StringComparison.Ordinal);
            Assert.Contains("untracked files included", message, StringComparison.Ordinal);
            Assert.Contains("cannot be undone", message, StringComparison.Ordinal);
            Assert.Equal("Discard", dialog.PrimaryButtonText);
            Assert.Equal(DefaultButton.Close, dialog.DefaultButton);
            Assert.True(redWhileAsked, "the confirm button was not red");

            // Everything went back to the last commit, and the history no longer has the line.
            Assert.Equal("one\n", File.ReadAllText(Path.Combine(repository.WorkTreePath, "app.txt")));
            Assert.False(File.Exists(Path.Combine(repository.WorkTreePath, "staged.txt")));
            Assert.False(File.Exists(Path.Combine(repository.WorkTreePath, "notes.txt")));
            Assert.Equal(string.Empty, GitOutput(repository, "status", "--porcelain"));

            Assert.DoesNotContain(page.Rows, row => row.IsUncommitted);
            Assert.Equal("Changes discarded", services.InfoBar.Last?.Title);
            Assert.Equal(InfoBarSeverity.Success, services.InfoBar.Last?.Severity);
        });
    }

    [Fact]
    public void Cancelling_ChangesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);
            Write(repository, "app.txt", "ruined\n");
            Write(repository, "notes.txt", "never committed\n");

            HistoryPageViewModel page = await OpenAsync(services, repository);
            services.Dialogs.Result = DialogResult.Close;

            await DiscardFromTheUncommittedLineAsync(page);

            Assert.Single(services.Dialogs.Shown);
            Assert.Equal("ruined\n", File.ReadAllText(Path.Combine(repository.WorkTreePath, "app.txt")));
            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "notes.txt")));
            Assert.Contains(page.Rows, row => row.IsUncommitted);
        });
    }

    [Fact]
    public void DuringAMerge_TheDiscardIsRefused()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            // Both sides change the same line, so the merge stops on the conflict.
            Git(repository, "checkout", "-b", "other");
            Write(repository, "app.txt", "theirs\n");
            Git(repository, "commit", "-a", "-m", "Theirs");
            Git(repository, "checkout", "main");
            Write(repository, "app.txt", "ours\n");
            Git(repository, "commit", "-a", "-m", "Ours");
            GitAllowingFailure(repository, "merge", "other");

            HistoryPageViewModel page = await OpenAsync(services, repository);

            Assert.True(services.Get<IRepositoryContext>().Head!.HasOperationInProgress);
            Assert.False(services.Get<IDiscardOperations>().CanDiscardUncommitted);

            CommitRowViewModel uncommitted = Assert.Single(page.Rows, row => row.IsUncommitted);
            HistoryMenuEntry entry = uncommitted.MenuEntries.Single(candidate => candidate.Header == Header);
            Assert.False(entry.Command!.CanExecute(entry.Parameter));

            // And asked all the same, it neither asks nor discards.
            Assert.False(await services.Get<IDiscardOperations>().DiscardUncommittedAsync());
            Assert.Empty(services.Dialogs.Shown);
        });
    }

    [Fact]
    public void BeforeTheFirstCommit_ThereIsNothingToGoBackTo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);

            string root = Path.Combine(services.ConfigurationRoot, "workspace");
            Directory.CreateDirectory(root);
            RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, "unborn"), "main");
            Write(repository, "first.txt", "not committed yet\n");

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            Assert.False(services.Get<IDiscardOperations>().CanDiscardUncommitted);
            Assert.False(await services.Get<IDiscardOperations>().DiscardUncommittedAsync());
            Assert.True(File.Exists(Path.Combine(repository.WorkTreePath, "first.txt")));
        });
    }

    // ---------------------------------------------------------------- helpers

    private static async Task DiscardFromTheUncommittedLineAsync(HistoryPageViewModel page)
    {
        CommitRowViewModel uncommitted = Assert.Single(page.Rows, row => row.IsUncommitted);
        HistoryMenuEntry entry = uncommitted.MenuEntries.Single(candidate => candidate.Header == Header);

        Assert.True(entry.Command!.CanExecute(entry.Parameter));
        await ((IAsyncRelayCommand)entry.Command).ExecuteAsync(entry.Parameter);
    }

    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, "discard"), "main");

        Write(repository, "app.txt", "one\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the application file");

        return repository;
    }

    private static async Task<HistoryPageViewModel> OpenAsync(TestServices services, RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
        await page.ReloadAsync();

        return page;
    }

    private static void Write(RepositoryHandle repository, string relativePath, string content)
        => File.WriteAllText(Path.Combine(repository.WorkTreePath, relativePath), content);

    private static void Git(RepositoryHandle repository, params string[] arguments)
    {
        (int exitCode, _, string error) = Run(repository, arguments);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }
    }

    private static void GitAllowingFailure(RepositoryHandle repository, params string[] arguments)
        => _ = Run(repository, arguments);

    private static string GitOutput(RepositoryHandle repository, params string[] arguments)
        => Run(repository, arguments).Output.Trim();

    private static (int ExitCode, string Output, string Error) Run(RepositoryHandle repository, string[] arguments)
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
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output, error);
    }
}
