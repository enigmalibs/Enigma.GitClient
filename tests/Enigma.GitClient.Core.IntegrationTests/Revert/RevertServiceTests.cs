using System.IO;
using System.Threading.Tasks;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Revert;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Revert;

/// <summary>
/// Drives the revert service against a real repository: the commit it records, a merge reverted
/// against the branch it went into, and every way a revert stops without leaving one in progress.
/// </summary>
public sealed class RevertServiceTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _repository = null!;
    private RepositoryHandle _handle = null!;

    private string _secondSha = string.Empty;
    private string _thirdSha = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _repository = await _workspace.InitRepositoryAsync("revert");

        await _repository.CommitFileAsync("README.md", "# one\n", "Add the readme");
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

    private IRevertService Revert => _host.GetRequiredService<IRevertService>();

    private bool IsReverting => File.Exists(Path.Combine(_repository.Path, ".git", "REVERT_HEAD"));

    private Task<HeadState> HeadAsync()
        => _host.GetRequiredService<IRefReader>().GetHeadStateAsync(_handle, TestContext.Current.CancellationToken);

    private Task<RevertOutcome> RevertAsync(string revision, int? mainline = null)
        => Revert.RevertAsync(_handle, revision, mainline, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Revert_RecordsACommitThatUndoesTheChangeWithGitsMessage()
    {
        RevertOutcome outcome = await RevertAsync(_thirdSha);

        Assert.Equal(RevertResultKind.Reverted, outcome.Kind);

        HeadState head = await HeadAsync();

        Assert.Equal(outcome.CommitSha, head.Sha);
        Assert.Equal("main", head.BranchName);
        Assert.False(head.IsDetached);

        // One new commit on top of the one it undoes, which stays in the history.
        Assert.Equal(_thirdSha, await _repository.ResolveAsync("HEAD~1"));
        Assert.Equal("one\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
        Assert.Equal("Revert \"Extend the application file\"", await _repository.GitLineAsync("log", "-1", "--format=%s"));
        Assert.Contains(_thirdSha, await _repository.GitAsync("log", "-1", "--format=%b"), System.StringComparison.Ordinal);
        Assert.Equal(string.Empty, await _repository.GitAsync("status", "--porcelain"));
    }

    [Fact]
    public async Task Revert_OfAMergeUndoesWhatItBroughtInAndKeepsTheBranchItWentInto()
    {
        await _repository.GitAsync("checkout", "-b", "side", _secondSha);
        await _repository.CommitFileAsync("src/side.txt", "side\n", "Work on the side");
        await _repository.GitAsync("checkout", "main");
        await _repository.GitAsync("merge", "--no-ff", "side", "-m", "Merge side");
        string merge = await _repository.ResolveAsync("HEAD");

        RevertOutcome outcome = await RevertAsync(merge, mainline: 1);

        Assert.Equal(RevertResultKind.Reverted, outcome.Kind);
        Assert.False(File.Exists(_repository.GetPath("src/side.txt")));
        Assert.Equal("one\ntwo\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
        Assert.Equal(merge, await _repository.ResolveAsync("HEAD~1"));
    }

    [Fact]
    public async Task Revert_ThatConflictsIsAbandonedAndNamesTheFile()
    {
        string middle = await _repository.CommitFileAsync("src/app.txt", "one\nTWO\n", "Shout the second line");
        await _repository.CommitFileAsync("src/app.txt", "one\nTwo\n", "Settle the second line");
        string before = await _repository.ResolveAsync("HEAD");

        RevertOutcome outcome = await RevertAsync(middle);

        Assert.Equal(RevertResultKind.Conflicted, outcome.Kind);
        Assert.Equal(["src/app.txt"], outcome.ConflictedPaths);

        // Nothing is left behind: no revert in progress, the branch and the files as they were.
        Assert.False(IsReverting);
        Assert.Equal(before, await _repository.ResolveAsync("HEAD"));
        Assert.Equal("one\nTwo\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
        Assert.Equal(string.Empty, await _repository.GitAsync("status", "--porcelain"));
    }

    [Fact]
    public async Task Revert_ThatConflictsKeepsAnUnrelatedEdit()
    {
        string middle = await _repository.CommitFileAsync("src/app.txt", "one\nTWO\n", "Shout the second line");
        await _repository.CommitFileAsync("src/app.txt", "one\nTwo\n", "Settle the second line");
        _repository.WriteFile("README.md", "# edited\n");

        RevertOutcome outcome = await RevertAsync(middle);

        Assert.Equal(RevertResultKind.Conflicted, outcome.Kind);
        Assert.False(IsReverting);
        Assert.Equal("# edited\n", File.ReadAllText(_repository.GetPath("README.md")));
        Assert.Equal(" M README.md", await _repository.GitLineAsync("status", "--porcelain"));
    }

    [Fact]
    public async Task Revert_OfWhatIsAlreadyUndoneCommitsNothing()
    {
        await RevertAsync(_thirdSha);
        string before = await _repository.ResolveAsync("HEAD");

        RevertOutcome outcome = await RevertAsync(_thirdSha);

        Assert.Equal(RevertResultKind.NothingToRevert, outcome.Kind);
        Assert.Equal(before, await _repository.ResolveAsync("HEAD"));
        Assert.False(IsReverting);
    }

    [Fact]
    public async Task Revert_WithStagedWorkInTheWayFailsAndLeavesItAlone()
    {
        _repository.WriteFile("README.md", "# staged\n");
        await _repository.GitAsync("add", "README.md");

        RevertOutcome outcome = await RevertAsync(_thirdSha);

        Assert.Equal(RevertResultKind.Failed, outcome.Kind);
        Assert.Equal("Uncommitted work is in the way. Commit it or stash it, then revert.", outcome.Message);
        Assert.Equal(_thirdSha, await _repository.ResolveAsync("HEAD"));
        Assert.False(IsReverting);
        Assert.Equal("M  README.md", await _repository.GitLineAsync("status", "--porcelain"));
    }

    [Fact]
    public async Task Revert_GoesAheadPastAnUnrelatedEditAndKeepsIt()
    {
        _repository.WriteFile("README.md", "# edited\n");

        RevertOutcome outcome = await RevertAsync(_thirdSha);

        Assert.Equal(RevertResultKind.Reverted, outcome.Kind);
        Assert.Equal("one\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
        Assert.Equal(" M README.md", await _repository.GitLineAsync("status", "--porcelain"));
    }
}
