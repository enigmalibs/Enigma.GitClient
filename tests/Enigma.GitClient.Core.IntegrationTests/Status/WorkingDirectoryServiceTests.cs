using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Commits;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Staging;
using Enigma.GitClient.Core.Status;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Status;

/// <summary>
/// Drives the status, staging and commit services against a real repository — the half of a git
/// client that writes, so every assertion is against what git recorded rather than what was asked.
/// </summary>
public sealed class WorkingDirectoryServiceTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _repository = null!;
    private RepositoryHandle _handle = null!;

    private string _firstSha = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _repository = await _workspace.InitRepositoryAsync("work");

        _repository.WriteFile("README.md", "# one\n");
        _repository.WriteFile("src/app.txt", "one\ntwo\n");
        _firstSha = await _repository.CommitAllAsync("Add the initial files");

        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_repository.Path, TestContext.Current.CancellationToken);

        _handle = discovery.Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private IStatusService Status => _host.GetRequiredService<IStatusService>();

    private IStagingService Staging => _host.GetRequiredService<IStagingService>();

    private ICommitService Commits => _host.GetRequiredService<ICommitService>();

    private IGitIgnoreService Ignore => _host.GetRequiredService<IGitIgnoreService>();

    private Task<WorkingTreeStatus> ReadAsync(bool includeIgnored = false)
        => Status.GetStatusAsync(_handle, includeIgnored, TestContext.Current.CancellationToken);

    // ---------------------------------------------------------------- reading status

    [Fact]
    public async Task GetStatusAsync_ReportsACleanTree()
    {
        WorkingTreeStatus status = await ReadAsync();

        Assert.True(status.IsClean);
        Assert.Equal("main", status.Branch.Head);
        Assert.Equal(await _repository.ResolveAsync("HEAD"), status.Branch.Oid);
    }

    [Fact]
    public async Task GetStatusAsync_SeparatesStagedFromUnstagedAndUntracked()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo changed\n");
        await _repository.GitAsync("add", "src/app.txt");

        _repository.WriteFile("README.md", "# edited\n");
        _repository.WriteFile("notes.txt", "brand new\n");

        WorkingTreeStatus status = await ReadAsync();

        Assert.Equal("src/app.txt", Assert.Single(status.Staged).Path);
        Assert.Equal("README.md", Assert.Single(status.Unstaged).Path);
        Assert.Equal("notes.txt", Assert.Single(status.Untracked).Path);
        Assert.False(status.IsClean);
    }

    [Fact]
    public async Task GetStatusAsync_ListsFilesInsideAnUntrackedDirectory()
    {
        _repository.WriteFile("fresh/one.txt", "one\n");
        _repository.WriteFile("fresh/two.txt", "two\n");

        WorkingTreeStatus status = await ReadAsync();

        // --untracked-files=all is what makes each of them separately stageable.
        Assert.Equal(["fresh/one.txt", "fresh/two.txt"], status.Untracked.Select(file => file.Path).Order());
    }

    [Fact]
    public async Task GetStatusAsync_ReportsARenameWithItsOriginalPath()
    {
        await _repository.GitAsync("mv", "src/app.txt", "src/renamed.txt");

        ChangedFile renamed = Assert.Single((await ReadAsync()).Staged);

        Assert.Equal("src/renamed.txt", renamed.Path);
        Assert.Equal("src/app.txt", renamed.OldPath);
        Assert.Equal(FileChangeKind.Renamed, renamed.ChangeKind);
    }

    [Fact]
    public async Task GetStatusAsync_ReportsTheUpstreamAndHowFarApart()
    {
        TemporaryRepository origin = await _workspace.InitRepositoryAsync("origin", bare: true);
        await _repository.GitAsync("remote", "add", "origin", origin.Path);
        await _repository.GitAsync("push", "--set-upstream", "origin", "main");
        await _repository.CommitFileAsync("src/ahead.txt", "ahead\n", "Move ahead");

        WorkingTreeStatus status = await ReadAsync();

        Assert.Equal("origin/main", status.Branch.Upstream);
        Assert.Equal(1, status.Branch.Ahead);
        Assert.Equal(0, status.Branch.Behind);
    }

    [Fact]
    public async Task GetStatusAsync_ListsIgnoredFilesOnlyWhenAsked()
    {
        _repository.WriteFile(".gitignore", "build/\n");
        _repository.WriteFile("build/output.bin", "binary-ish\n");

        Assert.Empty((await ReadAsync()).Ignored);
        Assert.NotEmpty((await ReadAsync(includeIgnored: true)).Ignored);
    }

    [Fact]
    public async Task GetStatusAsync_HandlesAPathWithSpacesAndAccents()
    {
        _repository.WriteFile("a file with spaces.txt", "keep\n");
        _repository.WriteFile("rapport-écrit.txt", "accentué\n");

        WorkingTreeStatus status = await ReadAsync();

        Assert.Contains(status.Untracked, file => file.Path == "a file with spaces.txt");
        Assert.Contains(status.Untracked, file => file.Path == "rapport-écrit.txt");
    }

    // ---------------------------------------------------------------- staging

    [Fact]
    public async Task StageAsync_MovesOneFileIntoTheIndex()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo changed\n");
        _repository.WriteFile("README.md", "# edited\n");

        await Staging.StageAsync(_handle, ["src/app.txt"], TestContext.Current.CancellationToken);

        WorkingTreeStatus status = await ReadAsync();

        Assert.Equal("src/app.txt", Assert.Single(status.Staged).Path);
        Assert.Equal("README.md", Assert.Single(status.Unstaged).Path);
    }

    [Fact]
    public async Task StageAsync_StagesAnUntrackedFileToo()
    {
        _repository.WriteFile("notes.txt", "brand new\n");

        await Staging.StageAsync(_handle, ["notes.txt"], TestContext.Current.CancellationToken);

        WorkingTreeStatus status = await ReadAsync();

        Assert.Equal(FileChangeKind.Added, Assert.Single(status.Staged).ChangeKind);
        Assert.Empty(status.Untracked);
    }

    [Fact]
    public async Task StageAllAsync_TakesEverythingIncludingDeletionsAndNewFiles()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo changed\n");
        _repository.WriteFile("notes.txt", "brand new\n");
        _repository.DeleteFile("README.md");

        await Staging.StageAllAsync(_handle, TestContext.Current.CancellationToken);

        WorkingTreeStatus status = await ReadAsync();

        Assert.Equal(3, status.Staged.Count);
        Assert.Empty(status.Unstaged);
        Assert.Empty(status.Untracked);
        Assert.Contains(status.Staged, file => file is { Path: "README.md", ChangeKind: FileChangeKind.Deleted });
    }

    [Fact]
    public async Task StageAsync_DoesNothingForAnEmptySelection()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo changed\n");

        await Staging.StageAsync(_handle, [], TestContext.Current.CancellationToken);

        // An empty pathspec means "everything" to git, which is the opposite of an empty selection.
        Assert.Empty((await ReadAsync()).Staged);
    }

    [Fact]
    public async Task UnstageAsync_TakesAFileBackOutWithoutTouchingTheWorkTree()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo changed\n");
        await Staging.StageAllAsync(_handle, TestContext.Current.CancellationToken);

        await Staging.UnstageAsync(_handle, ["src/app.txt"], TestContext.Current.CancellationToken);

        WorkingTreeStatus status = await ReadAsync();

        Assert.Empty(status.Staged);
        Assert.Equal("src/app.txt", Assert.Single(status.Unstaged).Path);
        Assert.Equal("one\ntwo changed\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
    }

    [Fact]
    public async Task UnstageAllAsync_EmptiesTheIndexAndKeepsTheEdits()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo changed\n");
        _repository.WriteFile("notes.txt", "brand new\n");
        await Staging.StageAllAsync(_handle, TestContext.Current.CancellationToken);

        await Staging.UnstageAllAsync(_handle, TestContext.Current.CancellationToken);

        WorkingTreeStatus status = await ReadAsync();

        Assert.Empty(status.Staged);
        Assert.Equal("src/app.txt", Assert.Single(status.Unstaged).Path);
        Assert.Equal("notes.txt", Assert.Single(status.Untracked).Path);
    }

    // ---------------------------------------------------------------- discarding

    [Fact]
    public async Task DiscardAsync_PutsATrackedFileBackAsItWas()
    {
        _repository.WriteFile("src/app.txt", "ruined\n");

        await Staging.DiscardAsync(_handle, ["src/app.txt"], TestContext.Current.CancellationToken);

        Assert.Equal("one\ntwo\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
        Assert.True((await ReadAsync()).IsClean);
    }

    [Fact]
    public async Task DiscardAsync_DeletesAnUntrackedFileBecauseThereIsNothingToRestore()
    {
        _repository.WriteFile("notes.txt", "never committed\n");

        await Staging.DiscardAsync(_handle, ["notes.txt"], TestContext.Current.CancellationToken);

        Assert.False(File.Exists(_repository.GetPath("notes.txt")));
        Assert.True((await ReadAsync()).IsClean);
    }

    [Fact]
    public async Task DiscardAsync_TakesTheStagedHalfWithIt()
    {
        _repository.WriteFile("src/app.txt", "staged change\n");
        await Staging.StageAllAsync(_handle, TestContext.Current.CancellationToken);
        _repository.WriteFile("src/app.txt", "and an unstaged one\n");

        await Staging.DiscardAsync(_handle, ["src/app.txt"], TestContext.Current.CancellationToken);

        // Discarding a file means the file goes back to HEAD, not part-way back to the index.
        Assert.Equal("one\ntwo\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
        Assert.True((await ReadAsync()).IsClean);
    }

    [Fact]
    public async Task DiscardAsync_HandlesAMixOfTrackedAndUntracked()
    {
        _repository.WriteFile("src/app.txt", "ruined\n");
        _repository.WriteFile("notes.txt", "never committed\n");

        await Staging.DiscardAsync(
            _handle,
            ["src/app.txt", "notes.txt"],
            TestContext.Current.CancellationToken);

        Assert.Equal("one\ntwo\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
        Assert.False(File.Exists(_repository.GetPath("notes.txt")));
    }

    [Fact]
    public async Task RemoveAsync_StopsTrackingAFileAndCanLeaveItOnDisk()
    {
        await Staging.RemoveAsync(
            _handle,
            ["README.md"],
            keepOnDisk: true,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(_repository.GetPath("README.md")));

        WorkingTreeStatus status = await ReadAsync();

        Assert.Equal(FileChangeKind.Deleted, Assert.Single(status.Staged).ChangeKind);
        Assert.Equal("README.md", Assert.Single(status.Untracked).Path);
    }

    // ---------------------------------------------------------------- committing

    [Fact]
    public async Task CommitAsync_RecordsWhatWasStaged()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo changed\n");
        await Staging.StageAllAsync(_handle, TestContext.Current.CancellationToken);

        string sha = await Commits.CommitAsync(
            _handle,
            new CommitRequest { Message = "Change the application file" },
            TestContext.Current.CancellationToken);

        Assert.Equal(await _repository.ResolveAsync("HEAD"), sha);
        Assert.Equal("Change the application file", await _repository.GitLineAsync("log", "-1", "--format=%s"));
        Assert.Equal(_firstSha, await _repository.GitLineAsync("rev-parse", "HEAD^"));
        Assert.True((await ReadAsync()).IsClean);
    }

    [Fact]
    public async Task CommitAsync_KeepsAMultiLineNonAsciiMessageExactly()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo changed\n");
        await Staging.StageAllAsync(_handle, TestContext.Current.CancellationToken);

        const string message = "Réécrire l'entête\n\nCe commit touche à l'accentuation — et aux tirets longs.\nDeuxième ligne du corps.";

        await Commits.CommitAsync(
            _handle,
            new CommitRequest { Message = message },
            TestContext.Current.CancellationToken);

        // "%B" adds a separating newline of its own, which is git's formatting rather than part of
        // the message.
        string recorded = (await _repository.GitAsync("log", "-1", "--format=%B")).TrimEnd('\n');

        // A message through argv would hit a length limit and mangle the encoding; through a file
        // it is exactly what was typed.
        Assert.Equal(message, recorded);
        Assert.Contains("Réécrire l'entête", recorded, StringComparison.Ordinal);
        Assert.Contains("tirets longs", recorded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitAsync_RefusesWhenNothingIsStaged()
    {
        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Commits.CommitAsync(
                _handle,
                new CommitRequest { Message = "Nothing to say about" },
                TestContext.Current.CancellationToken));

        Assert.Contains("Nothing is staged", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitAsync_RefusesAnEmptyMessage()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo changed\n");
        await Staging.StageAllAsync(_handle, TestContext.Current.CancellationToken);

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Commits.CommitAsync(
                _handle,
                new CommitRequest { Message = "   \n  " },
                TestContext.Current.CancellationToken));

        Assert.Contains("commit message", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitAsync_CanRecordAnEmptyCommitWhenAsked()
    {
        string sha = await Commits.CommitAsync(
            _handle,
            new CommitRequest { Message = "Mark the release point", AllowEmpty = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(await _repository.ResolveAsync("HEAD"), sha);
        Assert.Equal(_firstSha, await _repository.GitLineAsync("rev-parse", "HEAD^"));
    }

    [Fact]
    public async Task CommitAsync_AmendReplacesTheTipRatherThanAddingToIt()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo changed\n");
        await Staging.StageAllAsync(_handle, TestContext.Current.CancellationToken);

        string sha = await Commits.CommitAsync(
            _handle,
            new CommitRequest { Message = "Say it better", Amend = true },
            TestContext.Current.CancellationToken);

        Assert.NotEqual(_firstSha, sha);
        Assert.Equal("Say it better", await _repository.GitLineAsync("log", "-1", "--format=%s"));

        // The amended commit replaced the only one there was, so the history is still one deep.
        Assert.Single(await _repository.GitLinesAsync("log", "--format=%H"));
    }

    [Fact]
    public async Task CommitAsync_CanRecordAnotherAuthor()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo changed\n");
        await Staging.StageAllAsync(_handle, TestContext.Current.CancellationToken);

        await Commits.CommitAsync(
            _handle,
            new CommitRequest { Message = "On behalf of someone else", Author = "Grace Hopper <grace@example.com>" },
            TestContext.Current.CancellationToken);

        Assert.Equal("Grace Hopper", await _repository.GitLineAsync("log", "-1", "--format=%an"));
        Assert.Equal("grace@example.com", await _repository.GitLineAsync("log", "-1", "--format=%ae"));
    }

    [Fact]
    public async Task CommitAsync_CanSignOff()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo changed\n");
        await Staging.StageAllAsync(_handle, TestContext.Current.CancellationToken);

        await Commits.CommitAsync(
            _handle,
            new CommitRequest { Message = "Signed work", SignOff = true },
            TestContext.Current.CancellationToken);

        Assert.Contains("Signed-off-by:", await _repository.GitAsync("log", "-1", "--format=%B"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitAsync_CanStageEverythingItself()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo changed\n");

        await Commits.CommitAsync(
            _handle,
            new CommitRequest { Message = "Commit the lot", StageEverythingFirst = true },
            TestContext.Current.CancellationToken);

        Assert.True((await ReadAsync()).IsClean);
    }

    [Fact]
    public async Task GetLastCommitMessageAsync_ReadsWhatAnAmendWouldStartFrom()
    {
        Assert.Equal("Add the initial files", await Commits.GetLastCommitMessageAsync(
            _handle,
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("subject\r\nbody\r\n", "subject\nbody\n")]
    [InlineData("subject\n\n\n", "subject\n")]
    [InlineData("subject", "subject\n")]
    public void Normalise_WritesTheMessageTheWayGitRecordsIt(string message, string expected)
        => Assert.Equal(expected, CommitService.Normalise(message));

    // ---------------------------------------------------------------- ignore rules

    [Fact]
    public async Task GetIgnoreRuleAsync_NamesTheRuleThatCoversAPath()
    {
        _repository.WriteFile(".gitignore", "# build output\nbuild/\n*.log\n");

        IgnoreRule? rule = await Ignore.GetIgnoreRuleAsync(
            _handle,
            "build/output.bin",
            TestContext.Current.CancellationToken);

        Assert.NotNull(rule);
        Assert.Equal("build/", rule!.Pattern);
        Assert.Equal(2, rule.Line);
        Assert.Contains(".gitignore", rule.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetIgnoreRuleAsync_ReturnsNothingForAPathNobodyIgnores()
    {
        _repository.WriteFile(".gitignore", "build/\n");

        Assert.Null(await Ignore.GetIgnoreRuleAsync(
            _handle,
            "src/app.txt",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddPatternAsync_AppendsOnceAndOnlyOnce()
    {
        await Ignore.AddPatternAsync(_handle, "*.log", TestContext.Current.CancellationToken);
        await Ignore.AddPatternAsync(_handle, "*.log", TestContext.Current.CancellationToken);

        string content = File.ReadAllText(_repository.GetPath(".gitignore"));

        Assert.Equal("*.log\n", content);
    }

    [Fact]
    public async Task AddPatternAsync_KeepsWhatWasAlreadyThere()
    {
        _repository.WriteFile(".gitignore", "build/");

        await Ignore.AddPatternAsync(_handle, "*.log", TestContext.Current.CancellationToken);

        // The existing file had no trailing newline; appending must not run the two together.
        Assert.Equal("build/\n*.log\n", File.ReadAllText(_repository.GetPath(".gitignore")));
    }

    [Fact]
    public async Task AddPatternAsync_MakesTheFileActuallyIgnored()
    {
        _repository.WriteFile("debug.log", "noise\n");

        Assert.Contains((await ReadAsync()).Untracked, file => file.Path == "debug.log");

        await Ignore.AddPatternAsync(_handle, "*.log", TestContext.Current.CancellationToken);

        Assert.DoesNotContain((await ReadAsync()).Untracked, file => file.Path == "debug.log");
    }
}
