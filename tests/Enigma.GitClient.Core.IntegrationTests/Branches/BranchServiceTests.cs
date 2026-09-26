using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Branches;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Branches;

/// <summary>
/// Drives the branch service against a real repository. Every guard it claims to have is asserted
/// here against git's own answer, because a refusal that is only in the ViewModel is a refusal a
/// second caller can walk straight past.
/// </summary>
public sealed class BranchServiceTests : IAsyncLifetime
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
        _repository = await _workspace.InitRepositoryAsync("branches");

        _firstSha = await _repository.CommitFileAsync("README.md", "# one\n", "Add the readme");
        await _repository.CommitFileAsync("src/app.txt", "one\n", "Add the application file");

        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_repository.Path, TestContext.Current.CancellationToken);

        _handle = discovery.Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private IBranchService Service => _host.GetRequiredService<IBranchService>();

    private Task<RefCollection> RefsAsync()
        => _host.GetRequiredService<IRefReader>().GetRefsAsync(_handle, TestContext.Current.CancellationToken);

    private Task<HeadState> HeadAsync()
        => _host.GetRequiredService<IRefReader>().GetHeadStateAsync(_handle, TestContext.Current.CancellationToken);

    // ---------------------------------------------------------------- create

    [Fact]
    public async Task CreateAsync_BranchesFromHeadByDefault()
    {
        await Service.CreateAsync(_handle, "topic", cancellationToken: TestContext.Current.CancellationToken);

        RefCollection refs = await RefsAsync();
        GitBranch topic = refs.LocalBranches.Single(branch => branch.ShortName == "topic");

        Assert.Equal(await _repository.ResolveAsync("HEAD"), topic.TargetSha);

        // Creating a branch does not move onto it.
        Assert.Equal("main", (await HeadAsync()).BranchName);
    }

    [Fact]
    public async Task CreateAsync_BranchesFromAnyStartPoint()
    {
        await Service.CreateAsync(
            _handle,
            "from-first",
            _firstSha,
            cancellationToken: TestContext.Current.CancellationToken);

        GitBranch branch = (await RefsAsync()).LocalBranches.Single(candidate => candidate.ShortName == "from-first");

        Assert.Equal(_firstSha, branch.TargetSha);
    }

    [Fact]
    public async Task CreateAsync_CanCheckTheNewBranchOut()
    {
        await Service.CreateAsync(
            _handle,
            "feature/login",
            checkout: true,
            cancellationToken: TestContext.Current.CancellationToken);

        HeadState head = await HeadAsync();

        Assert.Equal("feature/login", head.BranchName);
        Assert.False(head.IsDetached);
    }

    [Fact]
    public async Task CreateAsync_RefusesANameThatAlreadyExists()
    {
        await Service.CreateAsync(_handle, "topic", cancellationToken: TestContext.Current.CancellationToken);

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Service.CreateAsync(_handle, "topic", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("already exists", refusal.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_RefusesAnInvalidNameWithoutRunningGit()
    {
        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Service.CreateAsync(_handle, "bad..name", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("\"..\"", refusal.Message, System.StringComparison.Ordinal);
        Assert.DoesNotContain((await RefsAsync()).LocalBranches, branch => branch.ShortName.Contains("bad"));
    }

    // ---------------------------------------------------------------- rename

    [Fact]
    public async Task RenameAsync_MovesTheBranchToItsNewName()
    {
        await Service.CreateAsync(_handle, "old-name", cancellationToken: TestContext.Current.CancellationToken);
        await Service.RenameAsync(_handle, "old-name", "new-name", cancellationToken: TestContext.Current.CancellationToken);

        IReadOnlyList<string> names = [.. (await RefsAsync()).LocalBranches.Select(branch => branch.ShortName)];

        Assert.Contains("new-name", names);
        Assert.DoesNotContain("old-name", names);
    }

    [Fact]
    public async Task RenameAsync_CanRenameTheCheckedOutBranch()
    {
        await Service.RenameAsync(_handle, "main", "trunk", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("trunk", (await HeadAsync()).BranchName);
    }

    [Fact]
    public async Task RenameAsync_RefusesToOverwriteAnExistingBranch()
    {
        await Service.CreateAsync(_handle, "one", cancellationToken: TestContext.Current.CancellationToken);
        await Service.CreateAsync(_handle, "two", _firstSha, cancellationToken: TestContext.Current.CancellationToken);

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Service.RenameAsync(_handle, "one", "two", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("already exists", refusal.Message, System.StringComparison.Ordinal);

        // Both are still there, and "two" still points where it did.
        Assert.Equal(_firstSha, (await RefsAsync()).LocalBranches.Single(branch => branch.ShortName == "two").TargetSha);
    }

    [Fact]
    public async Task RenameAsync_OverwritesWhenForcedDeliberately()
    {
        await Service.CreateAsync(_handle, "one", cancellationToken: TestContext.Current.CancellationToken);
        await Service.CreateAsync(_handle, "two", _firstSha, cancellationToken: TestContext.Current.CancellationToken);

        await Service.RenameAsync(_handle, "one", "two", force: true, TestContext.Current.CancellationToken);

        RefCollection refs = await RefsAsync();

        Assert.DoesNotContain(refs.LocalBranches, branch => branch.ShortName == "one");
        Assert.Equal(await _repository.ResolveAsync("HEAD"), refs.LocalBranches.Single(b => b.ShortName == "two").TargetSha);
    }

    [Fact]
    public async Task RenameAsync_ToTheSameNameDoesNothing()
    {
        await Service.CreateAsync(_handle, "same", cancellationToken: TestContext.Current.CancellationToken);
        await Service.RenameAsync(_handle, "same", "same", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains((await RefsAsync()).LocalBranches, branch => branch.ShortName == "same");
    }

    // ---------------------------------------------------------------- delete

    [Fact]
    public async Task DeleteAsync_RemovesAMergedBranch()
    {
        await Service.CreateAsync(_handle, "merged", _firstSha, cancellationToken: TestContext.Current.CancellationToken);

        await Service.DeleteAsync(_handle, "merged", cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain((await RefsAsync()).LocalBranches, branch => branch.ShortName == "merged");
    }

    [Fact]
    public async Task DeleteAsync_RefusesAnUnmergedBranchUntilItIsForced()
    {
        await BuildUnmergedBranchAsync();

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Service.DeleteAsync(_handle, "unmerged", cancellationToken: TestContext.Current.CancellationToken));

        // The message has to say what is at risk; "cannot delete" alone teaches nothing.
        Assert.Contains("would lose", refusal.Message, System.StringComparison.Ordinal);
        Assert.Contains((await RefsAsync()).LocalBranches, branch => branch.ShortName == "unmerged");

        await Service.DeleteAsync(_handle, "unmerged", force: true, TestContext.Current.CancellationToken);

        Assert.DoesNotContain((await RefsAsync()).LocalBranches, branch => branch.ShortName == "unmerged");
    }

    [Fact]
    public async Task DeleteAsync_RefusesTheBranchThatIsCheckedOut()
    {
        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Service.DeleteAsync(_handle, "main", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("checked out", refusal.Message, System.StringComparison.Ordinal);
        Assert.Contains((await RefsAsync()).LocalBranches, branch => branch.ShortName == "main");
    }

    [Fact]
    public async Task DeleteAsync_RefusesTheCheckedOutBranchEvenWhenForced()
    {
        await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Service.DeleteAsync(_handle, "main", force: true, TestContext.Current.CancellationToken));
    }

    // ---------------------------------------------------------------- merged-ness

    [Fact]
    public async Task IsMergedAsync_AnswersBothWaysRound()
    {
        await BuildUnmergedBranchAsync();

        Assert.False(await Service.IsMergedAsync(_handle, "unmerged", "main", TestContext.Current.CancellationToken));
        Assert.True(await Service.IsMergedAsync(_handle, "main", "unmerged", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsMergedAsync_DefaultsToHead()
    {
        await Service.CreateAsync(_handle, "behind", _firstSha, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await Service.IsMergedAsync(_handle, "behind", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetUnmergedCommitsAsync_NamesWhatWouldBeLost()
    {
        await BuildUnmergedBranchAsync();

        IReadOnlyList<string> subjects = await Service.GetUnmergedCommitsAsync(
            _handle,
            "unmerged",
            "main",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Work only on the branch"], subjects);
    }

    [Fact]
    public async Task GetUnmergedCommitsAsync_ReturnsNothingForAMergedBranch()
    {
        await Service.CreateAsync(_handle, "merged", _firstSha, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(await Service.GetUnmergedCommitsAsync(
            _handle,
            "merged",
            "main",
            cancellationToken: TestContext.Current.CancellationToken));
    }

    // ---------------------------------------------------------------- checkout

    [Fact]
    public async Task CheckoutAsync_MovesHeadOntoTheBranch()
    {
        await Service.CreateAsync(_handle, "topic", cancellationToken: TestContext.Current.CancellationToken);
        await Service.CheckoutAsync(_handle, "topic", TestContext.Current.CancellationToken);

        HeadState head = await HeadAsync();

        Assert.Equal("topic", head.BranchName);
        Assert.False(head.IsDetached);
    }

    [Fact]
    public async Task CheckoutRemoteAsync_CreatesATrackingBranch()
    {
        await BuildRemoteAsync();

        string local = await Service.CheckoutRemoteAsync(
            _handle,
            "origin/published",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("published", local);

        GitBranch branch = (await RefsAsync()).LocalBranches.Single(candidate => candidate.ShortName == "published");

        Assert.Equal("origin/published", branch.UpstreamShortName);
        Assert.Equal("published", (await HeadAsync()).BranchName);
    }

    [Fact]
    public async Task CheckoutRemoteAsync_CanNameTheLocalBranchItself()
    {
        await BuildRemoteAsync();

        string local = await Service.CheckoutRemoteAsync(
            _handle,
            "origin/published",
            "their-work",
            TestContext.Current.CancellationToken);

        Assert.Equal("their-work", local);
        Assert.Equal(
            "origin/published",
            (await RefsAsync()).LocalBranches.Single(branch => branch.ShortName == "their-work").UpstreamShortName);
    }

    [Fact]
    public async Task CheckoutRemoteAsync_RefusesWhenTheLocalNameIsTaken()
    {
        await BuildRemoteAsync();
        await Service.CreateAsync(_handle, "published", cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Service.CheckoutRemoteAsync(
                _handle,
                "origin/published",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    // ---------------------------------------------------------------- upstream

    [Fact]
    public async Task SetUpstreamAsync_PointsABranchAtARemoteOneAndBack()
    {
        await BuildRemoteAsync();
        await Service.CreateAsync(_handle, "local-work", cancellationToken: TestContext.Current.CancellationToken);

        await Service.SetUpstreamAsync(_handle, "local-work", "origin/published", TestContext.Current.CancellationToken);

        Assert.Equal(
            "origin/published",
            (await RefsAsync()).LocalBranches.Single(branch => branch.ShortName == "local-work").UpstreamShortName);

        await Service.SetUpstreamAsync(_handle, "local-work", null, TestContext.Current.CancellationToken);

        Assert.Null((await RefsAsync()).LocalBranches.Single(branch => branch.ShortName == "local-work").UpstreamShortName);
    }

    [Fact]
    public async Task DeleteRemoteAsync_RemovesTheBranchFromTheRemote()
    {
        TemporaryRepository origin = await BuildRemoteAsync();

        await Service.DeleteRemoteAsync(_handle, "origin", "published", TestContext.Current.CancellationToken);

        IReadOnlyList<string> remaining = await origin.GitLinesAsync("for-each-ref", "--format=%(refname:short)", "refs/heads/");

        Assert.DoesNotContain("published", remaining);
    }

    // ---------------------------------------------------------------- existence

    [Fact]
    public async Task ExistsAsync_KnowsWhatIsThere()
    {
        Assert.True(await Service.ExistsAsync(_handle, "main", TestContext.Current.CancellationToken));
        Assert.False(await Service.ExistsAsync(_handle, "nothing-like-this", TestContext.Current.CancellationToken));
        Assert.False(await Service.ExistsAsync(_handle, " ", TestContext.Current.CancellationToken));
    }

    // ---------------------------------------------------------------- fixtures

    /// <summary>
    /// Builds a branch holding one commit that is not on <c>main</c>, and leaves HEAD on main.
    /// </summary>
    private async Task BuildUnmergedBranchAsync()
    {
        await _repository.GitAsync("checkout", "-b", "unmerged");
        await _repository.CommitFileAsync("src/branch.txt", "branch only\n", "Work only on the branch");
        await _repository.GitAsync("checkout", "main");
    }

    /// <summary>
    /// Builds a bare repository, registers it as <c>origin</c>, and publishes a branch to it.
    /// </summary>
    private async Task<TemporaryRepository> BuildRemoteAsync()
    {
        TemporaryRepository origin = await _workspace.InitRepositoryAsync("origin", bare: true);

        await _repository.GitAsync("remote", "add", "origin", origin.Path);
        await _repository.GitAsync("checkout", "-b", "published");
        await _repository.CommitFileAsync("src/published.txt", "published\n", "Publish some work");
        await _repository.GitAsync("push", "origin", "published");
        await _repository.GitAsync("checkout", "main");
        await _repository.GitAsync("branch", "-D", "published");
        await _repository.GitAsync("fetch", "origin");

        return origin;
    }
}
