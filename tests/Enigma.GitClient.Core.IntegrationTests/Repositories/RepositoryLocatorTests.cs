using System.IO;
using System.Threading.Tasks;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Repositories;

public sealed class RepositoryLocatorTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;

    public ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private IRepositoryLocator Locator => _host.GetRequiredService<IRepositoryLocator>();

    [Fact]
    public async Task DiscoverAsync_FindsTheRepositoryFromItsRoot()
    {
        TemporaryRepository repository = await _workspace.InitRepositoryAsync("work");
        await repository.CommitInitialAsync();

        RepositoryDiscoveryResult result =
            await Locator.DiscoverAsync(repository.Path, TestContext.Current.CancellationToken);

        Assert.True(result.IsFound);
        Assert.NotNull(result.Repository);
        Assert.Equal(Path.GetFullPath(repository.Path), result.Repository!.WorkTreePath);
        Assert.Equal("work", result.Repository.Name);
        Assert.True(Directory.Exists(result.Repository.GitDirectory));
    }

    [Fact]
    public async Task DiscoverAsync_WalksUpFromANestedDirectory()
    {
        TemporaryRepository repository = await _workspace.InitRepositoryAsync("work");
        await repository.CommitInitialAsync();
        string nested = repository.CreateDirectory(Path.Combine("src", "deep", "nested"));

        RepositoryDiscoveryResult result =
            await Locator.DiscoverAsync(nested, TestContext.Current.CancellationToken);

        Assert.True(result.IsFound);
        Assert.Equal(Path.GetFullPath(repository.Path), result.Repository!.WorkTreePath);
    }

    [Fact]
    public async Task DiscoverAsync_WalksUpFromAFilePath()
    {
        TemporaryRepository repository = await _workspace.InitRepositoryAsync("work");
        await repository.CommitInitialAsync();

        RepositoryDiscoveryResult result =
            await Locator.DiscoverAsync(repository.GetPath("README.md"), TestContext.Current.CancellationToken);

        Assert.True(result.IsFound);
        Assert.Equal(Path.GetFullPath(repository.Path), result.Repository!.WorkTreePath);
    }

    [Fact]
    public async Task DiscoverAsync_FindsNothingOutsideARepository()
    {
        string plain = _workspace.CreateDirectory("not-a-repository");

        RepositoryDiscoveryResult result =
            await Locator.DiscoverAsync(plain, TestContext.Current.CancellationToken);

        Assert.Equal(RepositoryDiscoveryStatus.NotARepository, result.Status);
        Assert.Null(result.Repository);
        Assert.Contains("not inside a git repository", result.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverAsync_RejectsABareRepository()
    {
        TemporaryRepository bare = await _workspace.InitRepositoryAsync("mirror.git", bare: true);

        RepositoryDiscoveryResult result =
            await Locator.DiscoverAsync(bare.Path, TestContext.Current.CancellationToken);

        Assert.Equal(RepositoryDiscoveryStatus.BareRepository, result.Status);
        Assert.Null(result.Repository);
        Assert.Contains("bare repository", result.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverAsync_FindsARepositoryWithNoCommitsYet()
    {
        TemporaryRepository repository = await _workspace.InitRepositoryAsync("unborn");

        RepositoryDiscoveryResult result =
            await Locator.DiscoverAsync(repository.Path, TestContext.Current.CancellationToken);

        Assert.True(result.IsFound);
    }

    [Fact]
    public async Task DiscoverAsync_FindsNothingForAPathThatDoesNotExist()
    {
        RepositoryDiscoveryResult result = await Locator.DiscoverAsync(
            Path.Combine(_workspace.RootPath, "no", "such", "path"),
            TestContext.Current.CancellationToken);

        Assert.Equal(RepositoryDiscoveryStatus.NotARepository, result.Status);
    }

    [Fact]
    public async Task DiscoverAsync_ResolvesTheGitDirectoryOfALinkedWorktree()
    {
        TemporaryRepository repository = await _workspace.InitRepositoryAsync("main-tree");
        await repository.CommitInitialAsync();

        string linkedPath = Path.Combine(_workspace.RootPath, "linked-tree");
        await repository.GitAsync("worktree", "add", linkedPath, "-b", "linked");

        RepositoryDiscoveryResult result =
            await Locator.DiscoverAsync(linkedPath, TestContext.Current.CancellationToken);

        Assert.True(result.IsFound);
        Assert.Equal(Path.GetFullPath(linkedPath), result.Repository!.WorkTreePath);

        // A linked worktree's git directory lives under the main repository, not beside the tree.
        Assert.Contains("worktrees", result.Repository.GitDirectory, System.StringComparison.Ordinal);
    }
}
