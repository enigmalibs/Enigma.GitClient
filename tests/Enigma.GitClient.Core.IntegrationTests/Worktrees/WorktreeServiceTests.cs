using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
}
