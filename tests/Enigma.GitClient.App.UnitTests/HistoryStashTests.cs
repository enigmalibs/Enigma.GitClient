using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Stashes in the history, drawn and handled the way GitKraken does it, against real git.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryStashTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryStashTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- one line per stash

    [Fact]
    public void AStash_IsOneLine_AtTheCommitTheStashNames_WithTheStashBadge()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            // Tracked and untracked work, so git records the entry with an index commit and an
            // untracked-files commit besides the entry itself.
            Write(repository, "app.txt", "two\nthree\n");
            Write(repository, "notes.txt", "new\n");
            Git(repository, "stash", "push", "--include-untracked", "-m", "Half-done work");

            string stash = Git(repository, "rev-parse", "stash@{0}");
            string baseCommit = Git(repository, "rev-parse", "HEAD");

            HistoryPageViewModel page = await OpenAsync(services, repository);

            CommitRowViewModel line = Assert.Single(page.Rows, row => row.IsStash);

            Assert.Equal(stash, line.Sha);
            Assert.Equal("On main: Half-done work", line.Subject);
            Assert.Equal("stash@{0}", line.Stash!.Reference);

            RefBadgeItem badge = Assert.Single(line.Refs);
            Assert.Equal(GitRefKind.Stash, badge.Kind);
            Assert.Equal("stash@{0}", badge.Name);

            // Drawn off the commit it was made on, as an ordinary commit and not a merge.
            Assert.False(line.Row.IsMerge);
            Assert.Equal([baseCommit], line.Commit!.ParentShas);

            // git's bookkeeping is not history.
            Assert.DoesNotContain(page.Rows, row => row.Subject.StartsWith("index on ", StringComparison.Ordinal));
            Assert.DoesNotContain(page.Rows, row => row.Subject.StartsWith("untracked files on ", StringComparison.Ordinal));
            Assert.Equal(3, page.Rows.Count);
        });
    }

    [Fact]
    public void EveryStashEntry_IsALine_NotOnlyTheNewest()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "app.txt", "first\n");
            Git(repository, "stash", "push", "-m", "First");
            Write(repository, "app.txt", "second\n");
            Git(repository, "stash", "push", "-m", "Second");

            HistoryPageViewModel page = await OpenAsync(services, repository);

            CommitRowViewModel[] stashes = [.. page.Rows.Where(row => row.IsStash)];

            Assert.Equal(2, stashes.Length);
            Assert.Equal(["stash@{0}", "stash@{1}"], stashes.Select(row => row.Refs.Single().Name).Order(StringComparer.Ordinal));
            Assert.Equal(Git(repository, "rev-parse", "stash@{1}"), stashes.Single(row => row.Stash!.Index == 1).Sha);

            // One badge per entry: refs/stash does not add a second one to the newest.
            Assert.All(stashes, row => Assert.Single(row.Refs));
            Assert.DoesNotContain(page.Rows, row => row.Subject.StartsWith("index on ", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void DroppingAnOlderEntry_IsRedrawnByTheNextRefresh()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "app.txt", "first\n");
            Git(repository, "stash", "push", "-m", "First");
            Write(repository, "app.txt", "second\n");
            Git(repository, "stash", "push", "-m", "Second");

            HistoryPageViewModel page = await OpenAsync(services, repository);
            Assert.Equal(2, page.Rows.Count(row => row.IsStash));

            // No reference moves: refs/stash still names the newest entry.
            Git(repository, "stash", "drop", "stash@{1}");
            await services.Get<IRepositoryContext>().RefreshAsync();
            await page.RefreshInPlaceAsync(referencesMoved: false);

            CommitRowViewModel left = Assert.Single(page.Rows, row => row.IsStash);
            Assert.Equal("On main: Second", left.Subject);
        });
    }

    [Fact]
    public void ARefreshWithNothingNew_LeavesTheStashLinesAlone()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildRepositoryAsync(services);

            Write(repository, "app.txt", "first\n");
            Git(repository, "stash", "push", "-m", "First");

            HistoryPageViewModel page = await OpenAsync(services, repository);
            CommitRowViewModel before = Assert.Single(page.Rows, row => row.IsStash);

            await page.RefreshInPlaceAsync(referencesMoved: false);

            Assert.Same(before, Assert.Single(page.Rows, row => row.IsStash));
        });
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<HistoryPageViewModel> OpenAsync(TestServices services, RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
        await page.ReloadAsync();
        Dispatcher.UIThread.RunJobs();

        return page;
    }

    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "stashes"), "main");

        Write(repository, "README.md", "# one\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");

        Write(repository, "app.txt", "two\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the application");

        return repository;
    }

    private static void Write(RepositoryHandle repository, string path, string content)
        => File.WriteAllText(Path.Combine(repository.WorkTreePath, path), content);

    private static string Git(RepositoryHandle repository, params string[] arguments)
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

        return process.ExitCode == 0
            ? output.Trim()
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }
}
