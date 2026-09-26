using System.IO;
using System.Threading.Tasks;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Reset;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Reset;

/// <summary>
/// Drives the reset service against a real repository, asserting where the branch ended up and what
/// the index and the work tree hold afterwards.
/// </summary>
public sealed class ResetServiceTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _repository = null!;
    private RepositoryHandle _handle = null!;

    private string _firstSha = string.Empty;
    private string _secondSha = string.Empty;
    private string _thirdSha = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _repository = await _workspace.InitRepositoryAsync("reset");

        _firstSha = await _repository.CommitFileAsync("README.md", "# one\n", "Add the readme");
        _secondSha = await _repository.CommitFileAsync("src/app.txt", "one\n", "Add the application file");
        _thirdSha = await _repository.CommitFileAsync("src/app.txt", "one\ntwo\n", "Extend the application file");

        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_repository.Path, TestContext.Current.CancellationToken);

        _handle = discovery.Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private IResetService Reset => _host.GetRequiredService<IResetService>();

    private Task<HeadState> HeadAsync()
        => _host.GetRequiredService<IRefReader>().GetHeadStateAsync(_handle, TestContext.Current.CancellationToken);

    private Task ResetAsync(string revision, ResetMode mode)
        => Reset.ResetAsync(_handle, revision, mode, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Soft_MovesTheBranchAndKeepsWhatTheLaterCommitsChangedStaged()
    {
        await ResetAsync(_firstSha, ResetMode.Soft);

        HeadState head = await HeadAsync();

        Assert.Equal(_firstSha, head.Sha);
        Assert.Equal("main", head.BranchName);
        Assert.False(head.IsDetached);

        // Everything the two later commits did is still there, staged and ready to commit again —
        // and nothing is left unstaged.
        Assert.Equal("one\ntwo\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
        Assert.Equal("A  src/app.txt", await _repository.GitLineAsync("status", "--porcelain"));
    }

    [Fact]
    public async Task Soft_KeepsUncommittedWorkExactlyAsItWas()
    {
        _repository.WriteFile("src/app.txt", "edited but not committed\n");

        await ResetAsync(_secondSha, ResetMode.Soft);

        Assert.Equal(_secondSha, (await HeadAsync()).Sha);
        Assert.Equal("edited but not committed\n", File.ReadAllText(_repository.GetPath("src/app.txt")));

        // The third commit's change is staged; the edit on top of it is not.
        Assert.Equal("MM src/app.txt", await _repository.GitLineAsync("status", "--porcelain"));
    }

    [Fact]
    public async Task Hard_MovesTheBranchAndMakesTheTrackedFilesThatCommits()
    {
        _repository.WriteFile("src/app.txt", "edited but not committed\n");
        _repository.WriteFile("notes.txt", "never added\n");

        await ResetAsync(_secondSha, ResetMode.Hard);

        HeadState head = await HeadAsync();

        Assert.Equal(_secondSha, head.Sha);
        Assert.Equal("main", head.BranchName);
        Assert.False(head.IsDetached);

        // The tracked file is the commit's; the edit and the third commit's change are gone.
        Assert.Equal("one\n", File.ReadAllText(_repository.GetPath("src/app.txt")));

        // An untracked file is not the reset's business, and is the only thing git still reports.
        Assert.Equal("never added\n", File.ReadAllText(_repository.GetPath("notes.txt")));
        Assert.Equal("?? notes.txt", await _repository.GitLineAsync("status", "--porcelain"));
    }

    [Fact]
    public async Task Hard_ToHeadThrowsAwayOnlyTheUncommittedWork()
    {
        _repository.WriteFile("README.md", "edited but not committed\n");

        await ResetAsync(_thirdSha, ResetMode.Hard);

        Assert.Equal(_thirdSha, (await HeadAsync()).Sha);
        Assert.Equal("# one\n", File.ReadAllText(_repository.GetPath("README.md")));
        Assert.Equal(string.Empty, await _repository.GitAsync("status", "--porcelain"));
    }

    [Fact]
    public async Task Reset_MovesTheBranchToACommitThatIsNotAnAncestor()
    {
        await _repository.GitAsync("checkout", "-b", "side", _firstSha);
        string sideSha = await _repository.CommitFileAsync("src/side.txt", "side\n", "Work on the side");
        await _repository.GitAsync("checkout", "main");

        await ResetAsync(sideSha, ResetMode.Hard);

        HeadState head = await HeadAsync();

        Assert.Equal(sideSha, head.Sha);
        Assert.Equal("main", head.BranchName);
        Assert.True(File.Exists(_repository.GetPath("src/side.txt")));
        Assert.False(File.Exists(_repository.GetPath("src/app.txt")));
    }

    [Fact]
    public async Task Reset_ReadsARevisionThatIsAlsoAFileNameAsTheCommit()
    {
        // A branch called like a file in the tree: without the trailing "--", git would have to guess.
        await _repository.GitAsync("branch", "README.md", _firstSha);

        await ResetAsync("README.md", ResetMode.Soft);

        Assert.Equal(_firstSha, (await HeadAsync()).Sha);
    }
}
