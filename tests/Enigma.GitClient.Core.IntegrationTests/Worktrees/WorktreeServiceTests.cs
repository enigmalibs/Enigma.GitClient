using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Branches;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Worktrees;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Worktrees;

/// <summary>
/// Drives the worktree service against a real repository: where the worktree lands, what it has
/// checked out, what the repository it came from keeps — and every refusal, checked against what is
/// left on disk and in the references afterwards.
/// </summary>
public sealed class WorktreeServiceTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _repository = null!;
    private RepositoryHandle _handle = null!;

    private string _firstSha = string.Empty;
    private string _secondSha = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _repository = await _workspace.InitRepositoryAsync("project");

        _firstSha = await _repository.CommitFileAsync("README.md", "# one\n", "Add the readme");
        _secondSha = await _repository.CommitFileAsync("src/app.txt", "one\n", "Add the application file");

        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_repository.Path, TestContext.Current.CancellationToken);

        _handle = discovery.Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private IWorktreeService Service => _host.GetRequiredService<IWorktreeService>();

    private Task AddAsync(string path, string branch, string startPoint)
        => Service.AddAsync(_handle, path, branch, startPoint, TestContext.Current.CancellationToken);

    private async Task<bool> BranchExistsAsync(string name)
    {
        RefCollection refs = await _host.GetRequiredService<IRefReader>()
            .GetRefsAsync(_handle, TestContext.Current.CancellationToken);

        return refs.LocalBranches.Any(branch => branch.ShortName == name);
    }

    private Task<string> GitInAsync(string directory, params string[] arguments)
        => GitCli.RunAsync(directory, _workspace.Environment, arguments);

    // ---------------------------------------------------------------- creating

    [Fact]
    public async Task AddAsync_CreatesTheWorktreeBesideTheRepositoryOnTheNewBranch()
    {
        string folder = WorktreePaths.DefaultFor(_handle.WorkTreePath, "feature/login");

        await AddAsync(folder, "feature/login", _firstSha);

        Assert.Equal(Path.Combine(_workspace.RootPath, "project.worktree.feature-login"), folder);
        Assert.True(File.Exists(Path.Combine(folder, "README.md")));

        // The worktree is on the new branch, at the start point: the second commit's file is not there.
        Assert.Equal("feature/login", (await GitInAsync(folder, "rev-parse", "--abbrev-ref", "HEAD")).Trim());
        Assert.Equal(_firstSha, (await GitInAsync(folder, "rev-parse", "HEAD")).Trim());
        Assert.False(File.Exists(Path.Combine(folder, "src", "app.txt")));

        // The repository it came from keeps its HEAD, and sees the branch.
        Assert.Equal("main", (await _repository.GitLineAsync("rev-parse", "--abbrev-ref", "HEAD")).Trim());
        Assert.Equal(_secondSha, await _repository.ResolveAsync("HEAD"));
        Assert.True(await BranchExistsAsync("feature/login"));

        string list = await _repository.GitAsync("worktree", "list", "--porcelain");
        Assert.Contains("branch refs/heads/feature/login", list, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddAsync_StartsFromABranchByName()
    {
        await _repository.GitAsync("branch", "older", _firstSha);
        string folder = WorktreePaths.DefaultFor(_handle.WorkTreePath, "from-older");

        await AddAsync(folder, "from-older", "older");

        Assert.Equal(_firstSha, (await GitInAsync(folder, "rev-parse", "HEAD")).Trim());
    }

    [Fact]
    public async Task AddAsync_FromARemoteBranch_TracksIt()
    {
        string remote = _workspace.CreateDirectory("remote.git");
        await _repository.GitAsync("remote", "add", "origin", remote);
        await _repository.GitAsync("update-ref", "refs/remotes/origin/release", _firstSha);
        string folder = WorktreePaths.DefaultFor(_handle.WorkTreePath, "release");

        await AddAsync(folder, "release", "origin/release");

        Assert.Equal("origin/release", (await _repository.GitLineAsync("rev-parse", "--abbrev-ref", "release@{upstream}")).Trim());
    }

    [Fact]
    public async Task AddAsync_AcceptsAnEmptyFolder()
    {
        string folder = _workspace.CreateDirectory("ready");

        await AddAsync(folder, "topic", "HEAD");

        Assert.True(File.Exists(Path.Combine(folder, "README.md")));
    }

    // ---------------------------------------------------------------- refusing

    [Fact]
    public async Task AddAsync_RefusesAnExistingBranchAndCreatesNothing()
    {
        await _repository.GitAsync("branch", "topic");
        string folder = WorktreePaths.DefaultFor(_handle.WorkTreePath, "topic");

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => AddAsync(folder, "topic", "HEAD"));

        Assert.Contains("already exists", refusal.Message, System.StringComparison.Ordinal);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task AddAsync_RefusesAnInvalidBranchName()
    {
        string folder = WorktreePaths.DefaultFor(_handle.WorkTreePath, "bad");

        await Assert.ThrowsAsync<GitOperationRefusedException>(() => AddAsync(folder, "bad name", "HEAD"));

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task AddAsync_RefusesAFolderInsideTheRepository()
    {
        string folder = Path.Combine(_handle.WorkTreePath, "worktrees", "topic");

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => AddAsync(folder, "topic", "HEAD"));

        Assert.Contains("inside the repository", refusal.Message, System.StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_handle.WorkTreePath, "worktrees")));
        Assert.False(await BranchExistsAsync("topic"));
    }

    [Fact]
    public async Task AddAsync_RefusesAFolderThatIsNotEmpty()
    {
        string folder = _workspace.CreateDirectory("busy");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "keep me\n");

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => AddAsync(folder, "topic", "HEAD"));

        Assert.Contains("not empty", refusal.Message, System.StringComparison.Ordinal);

        // git itself would have created the branch before refusing the folder.
        Assert.False(await BranchExistsAsync("topic"));
        Assert.Equal("keep me\n", File.ReadAllText(Path.Combine(folder, "notes.txt")));
    }

    [Fact]
    public async Task AddAsync_RefusesAFile()
    {
        string file = Path.Combine(_workspace.RootPath, "a-file");
        File.WriteAllText(file, "x\n");

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => AddAsync(file, "topic", "HEAD"));

        Assert.Contains("is a file", refusal.Message, System.StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- when git fails

    [Fact]
    public async Task AddAsync_WhenGitCannotMakeTheFolder_LeavesNoBranchBehind()
    {
        // A folder under a file: nothing the folder rules look at, and git fails only after it has
        // created the branch.
        string file = Path.Combine(_workspace.RootPath, "in-the-way");
        File.WriteAllText(file, "x\n");

        await Assert.ThrowsAsync<GitCommandException>(() => AddAsync(Path.Combine(file, "topic"), "topic", "HEAD"));

        Assert.False(await BranchExistsAsync("topic"));
    }

    [Fact]
    public async Task AddAsync_WithAStartPointThatDoesNotExist_FailsAndCreatesNothing()
    {
        string folder = WorktreePaths.DefaultFor(_handle.WorkTreePath, "topic");

        await Assert.ThrowsAsync<GitCommandException>(() => AddAsync(folder, "topic", "no-such-revision"));

        Assert.False(await BranchExistsAsync("topic"));
        Assert.False(Directory.Exists(folder));
    }

    // ---------------------------------------------------------------- listing

    private Task<IReadOnlyList<GitWorktree>> ListAsync(RepositoryHandle? repository = null)
        => Service.ListAsync(repository ?? _handle, TestContext.Current.CancellationToken);

    private Task RemoveAsync(string path, bool force = false, RepositoryHandle? repository = null)
        => Service.RemoveAsync(repository ?? _handle, path, force, TestContext.Current.CancellationToken);

    private async Task<string> AddTopicAsync(string branch = "topic")
    {
        string folder = WorktreePaths.DefaultFor(_handle.WorkTreePath, branch);
        await AddAsync(folder, branch, _firstSha);
        return folder;
    }

    private async Task<RepositoryHandle> OpenAsync(string folder)
    {
        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(folder, TestContext.Current.CancellationToken);

        return discovery.Repository!;
    }

    [Fact]
    public async Task ListAsync_WithNoOtherWorktree_ListsTheMainOne()
    {
        GitWorktree main = Assert.Single(await ListAsync());

        Assert.True(main.IsMain);
        Assert.Equal(_handle.WorkTreePath, main.Path);
        Assert.Equal("main", main.Branch);
        Assert.Equal(_secondSha, main.HeadSha);
        Assert.False(main.IsMissing);
    }

    [Fact]
    public async Task ListAsync_AfterAdd_HasTheMainOneFirstAndTheNewOneWithItsBranchAndSha()
    {
        string folder = await AddTopicAsync("feature/login");

        IReadOnlyList<GitWorktree> worktrees = await ListAsync();

        Assert.Equal(2, worktrees.Count);
        Assert.True(worktrees[0].IsMain);
        Assert.Equal(_handle.WorkTreePath, worktrees[0].Path);

        GitWorktree linked = worktrees[1];
        Assert.False(linked.IsMain);
        Assert.Equal(folder, linked.Path);
        Assert.Equal("feature/login", linked.Branch);
        Assert.Equal(_firstSha, linked.HeadSha);
        Assert.False(linked.IsDetached);
        Assert.False(linked.IsLocked);
        Assert.False(linked.IsMissing);
    }

    [Fact]
    public async Task ListAsync_FromALinkedWorktree_StillListsTheMainOneFirst()
    {
        string folder = await AddTopicAsync();

        IReadOnlyList<GitWorktree> worktrees = await ListAsync(await OpenAsync(folder));

        Assert.Equal([_handle.WorkTreePath, folder], worktrees.Select(worktree => worktree.Path));
        Assert.True(worktrees[0].IsMain);
    }

    [Fact]
    public async Task ListAsync_ADetachedWorktree_HasNoBranch()
    {
        string folder = await AddTopicAsync();
        await GitInAsync(folder, "checkout", "--detach");

        GitWorktree linked = (await ListAsync())[1];

        Assert.True(linked.IsDetached);
        Assert.Null(linked.Branch);
    }

    [Fact]
    public async Task ListAsync_ALockedWorktree_HasItsReason()
    {
        string folder = await AddTopicAsync();
        await _repository.GitAsync("worktree", "lock", "--reason", "on a USB stick", folder);

        GitWorktree linked = (await ListAsync())[1];

        Assert.True(linked.IsLocked);
        Assert.Equal("on a USB stick", linked.LockReason);
    }

    // ---------------------------------------------------------------- counting changes

    [Fact]
    public async Task CountChangesAsync_ACleanWorktree_HasNone()
    {
        await AddTopicAsync();

        Assert.Equal(0, await Service.CountChangesAsync((await ListAsync())[1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CountChangesAsync_IgnoredFilesDoNotCount()
    {
        string folder = await AddTopicAsync();
        File.WriteAllText(Path.Combine(folder, ".gitignore"), "*.log\n");
        await GitInAsync(folder, "add", ".gitignore");
        await GitInAsync(folder, "commit", "-m", "Ignore the logs");
        File.WriteAllText(Path.Combine(folder, "build.log"), "noise\n");

        GitWorktree linked = (await ListAsync())[1];

        Assert.Equal(0, await Service.CountChangesAsync(linked, TestContext.Current.CancellationToken));

        // And git removes it without being forced, the ignored file with it.
        await RemoveAsync(folder);
        Assert.False(Directory.Exists(folder));
    }

    // ---------------------------------------------------------------- removing

    [Fact]
    public async Task RemoveAsync_ACleanWorktree_DeletesTheFolderAndTheRecordAndKeepsTheBranch()
    {
        string folder = await AddTopicAsync();

        await RemoveAsync(folder);

        Assert.False(Directory.Exists(folder));
        Assert.Single(await ListAsync());
        Assert.True(await BranchExistsAsync("topic"));

        // The branch is no longer checked out anywhere, so it can go now.
        await _host.GetRequiredService<IBranchService>()
            .DeleteAsync(_handle, "topic", force: true, TestContext.Current.CancellationToken);

        Assert.False(await BranchExistsAsync("topic"));
    }

    [Fact]
    public async Task RemoveAsync_WithChanges_IsRefusedByGitUnlessForced()
    {
        string folder = await AddTopicAsync();
        File.WriteAllText(Path.Combine(folder, "README.md"), "# changed\n");
        File.WriteAllText(Path.Combine(folder, "new.txt"), "untracked\n");

        GitWorktree linked = (await ListAsync())[1];
        Assert.Equal(2, await Service.CountChangesAsync(linked, TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<GitCommandException>(() => RemoveAsync(folder));
        Assert.True(File.Exists(Path.Combine(folder, "new.txt")));
        Assert.Equal(2, (await ListAsync()).Count);

        await RemoveAsync(folder, force: true);

        Assert.False(Directory.Exists(folder));
        Assert.Single(await ListAsync());
        Assert.True(await BranchExistsAsync("topic"));
    }

    [Fact]
    public async Task RemoveAsync_AFolderDeletedByHand_IsMissingAndRemovingClearsTheRecord()
    {
        string folder = await AddTopicAsync();
        DirectoryCleanup.Delete(folder);

        GitWorktree linked = (await ListAsync())[1];
        Assert.True(linked.IsMissing);
        Assert.Equal(0, await Service.CountChangesAsync(linked, TestContext.Current.CancellationToken));

        await RemoveAsync(folder);

        Assert.Single(await ListAsync());
        Assert.True(await BranchExistsAsync("topic"));
    }

    [Fact]
    public async Task RemoveAsync_TheMainWorkingTree_IsRefusedAndNothingIsRemoved()
    {
        string folder = await AddTopicAsync();
        RepositoryHandle fromLinked = await OpenAsync(folder);

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => RemoveAsync(_handle.WorkTreePath, repository: fromLinked));

        Assert.Contains("main working tree", refusal.Message, System.StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_handle.WorkTreePath, "README.md")));
        Assert.Equal(2, (await ListAsync()).Count);
    }

    [Fact]
    public async Task RemoveAsync_TheWorkingTreeTheRepositoryIsOpenIn_IsRefusedAndNothingIsRemoved()
    {
        string folder = await AddTopicAsync();
        RepositoryHandle fromLinked = await OpenAsync(folder);

        await Assert.ThrowsAsync<GitOperationRefusedException>(() => RemoveAsync(folder, repository: fromLinked));

        Assert.True(File.Exists(Path.Combine(folder, "README.md")));
        Assert.Equal(2, (await ListAsync()).Count);
    }

    [Fact]
    public async Task RemoveAsync_ALockedWorktree_IsRefusedNamingUnlockAndNothingIsRemoved()
    {
        string folder = await AddTopicAsync();
        await _repository.GitAsync("worktree", "lock", "--reason", "on a USB stick", folder);

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => RemoveAsync(folder, force: true));

        Assert.Contains("git worktree unlock", refusal.Message, System.StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, "README.md")));
        Assert.Equal(2, (await ListAsync()).Count);
    }

    [Fact]
    public async Task RemoveAsync_AFolderThatIsNotAWorktree_IsRefused()
    {
        string folder = _workspace.CreateDirectory("not-a-worktree");

        await Assert.ThrowsAsync<GitOperationRefusedException>(() => RemoveAsync(folder));

        Assert.True(Directory.Exists(folder));
    }
}
