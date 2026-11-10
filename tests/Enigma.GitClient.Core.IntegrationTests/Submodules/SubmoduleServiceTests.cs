using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Submodules;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Submodules;

/// <summary>
/// Drives the submodule service against real repositories: the <see cref="SubmoduleTemplate"/>'s
/// <c>super</c> and its libraries, copied for each test.
/// </summary>
public sealed class SubmoduleServiceTests : IAsyncLifetime, IClassFixture<SubmoduleTemplate>
{
    private const string Alpha = SubmoduleTemplate.Alpha;
    private const string Leaf = SubmoduleTemplate.Leaf;
    private const string Beta = SubmoduleTemplate.Beta;
    private const string Delta = SubmoduleTemplate.Delta;
    private const string Gamma = SubmoduleTemplate.Gamma;

    private readonly SubmoduleTemplate _template;

    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _super = null!;
    private RepositoryHandle _handle = null!;

    public SubmoduleServiceTests(SubmoduleTemplate template) => _template = template;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _workspace.AllowLocalSubmodules();
        _host = CoreTestHost.Create(_workspace);
        _super = _template.CopyInto(_workspace);

        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_super.Path, TestContext.Current.CancellationToken);

        _handle = discovery.Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private ISubmoduleService Service => _host.GetRequiredService<ISubmoduleService>();

    private async Task<string> GitInAsync(string directory, params string[] arguments)
        => (await GitCli.RunAsync(directory, _workspace.Environment, arguments)).Trim();

    private Task<string> GitInSubmoduleAsync(string path, params string[] arguments)
        => GitInAsync(_super.GetPath(path), arguments);

    private Task<string> HeadOfAsync(string path) => GitInSubmoduleAsync(path, "rev-parse", "HEAD");

    private async Task<Dictionary<string, GitSubmodule>> ListByPathAsync()
        => (await Service.ListAsync(_handle, TestContext.Current.CancellationToken)).ToDictionary(submodule => submodule.Path);

    /// <summary>
    /// Puts gamma one commit behind, beta one commit ahead on a branch of its own, and empties delta.
    /// </summary>
    private async Task<string> ArrangeMixedStatesAsync()
    {
        await GitInSubmoduleAsync(Gamma, "checkout", "-q", "HEAD~1");

        await GitInSubmoduleAsync(Beta, "checkout", "-q", "-b", "work");
        File.AppendAllText(_super.GetPath(Beta + "/lib.txt"), "beta work\n");
        await GitInSubmoduleAsync(Beta, "commit", "-q", "-am", "beta work");

        await _super.GitAsync("submodule", "deinit", "-q", "-f", "--", Delta);

        return await HeadOfAsync(Beta);
    }

    // ---------------------------------------------------------------- listing

    [Fact]
    public async Task ListAsync_ReadsEveryStateAndTheNestedSubmodule()
    {
        string betaWork = await ArrangeMixedStatesAsync();

        IReadOnlyList<GitSubmodule> listed = await Service.ListAsync(_handle, TestContext.Current.CancellationToken);

        Assert.Equal([Alpha, Leaf, Beta, Delta, Gamma], listed.Select(submodule => submodule.Path));

        GitSubmodule alpha = listed[0];
        Assert.Equal(SubmoduleCheckoutState.UpToDate, alpha.State);
        Assert.Equal(_template.AlphaPin, alpha.ExpectedSha);
        Assert.Equal(_template.AlphaPin, alpha.CheckedOutSha);
        Assert.Equal("libs/alpha", alpha.Name);
        Assert.Equal("../alpha", alpha.Url);
        Assert.Null(alpha.ParentPath);
        Assert.Equal(Path.GetFullPath(_super.GetPath("libs/alpha")), alpha.FullPath);

        GitSubmodule leaf = listed[1];
        Assert.Equal(SubmoduleCheckoutState.UpToDate, leaf.State);
        Assert.Equal(_template.LeafPin, leaf.ExpectedSha);
        Assert.Equal("vendor/leaf", leaf.Name);
        Assert.Equal(Alpha, leaf.ParentPath);
        Assert.Equal(Path.GetFullPath(_super.GetPath("libs/alpha/vendor/leaf")), leaf.FullPath);

        GitSubmodule beta = listed[2];
        Assert.Equal(SubmoduleCheckoutState.NewCommits, beta.State);
        Assert.Equal(_template.BetaPin, beta.ExpectedSha);
        Assert.Equal(betaWork, beta.CheckedOutSha);
        Assert.Equal("../beta lib", beta.Url);

        GitSubmodule delta = listed[3];
        Assert.Equal(SubmoduleCheckoutState.NotDownloaded, delta.State);
        Assert.Equal(_template.DeltaPin, delta.ExpectedSha);
        Assert.Null(delta.CheckedOutSha);
        Assert.False(delta.IsDownloaded);

        GitSubmodule gamma = listed[4];
        Assert.Equal(SubmoduleCheckoutState.Behind, gamma.State);
        Assert.Equal(_template.GammaPin, gamma.ExpectedSha);
        Assert.Equal(_template.GammaOlder, gamma.CheckedOutSha);

        Assert.All(listed, submodule => Assert.False(submodule.HasUncommittedChanges));
    }

    [Fact]
    public async Task ListAsync_CallsACommitOnAnotherLineElsewhere()
    {
        await GitInSubmoduleAsync(Gamma, "checkout", "-q", "--detach", "HEAD~1");
        File.WriteAllText(_super.GetPath(Gamma + "/side.txt"), "side\n");
        await GitInSubmoduleAsync(Gamma, "add", "side.txt");
        await GitInSubmoduleAsync(Gamma, "commit", "-q", "-m", "side");

        Dictionary<string, GitSubmodule> listed = await ListByPathAsync();

        Assert.Equal(SubmoduleCheckoutState.Elsewhere, listed[Gamma].State);
    }

    [Fact]
    public async Task ListAsync_SaysWhichSubmodulesHoldUncommittedWork()
    {
        File.AppendAllText(_super.GetPath(Gamma + "/lib.txt"), "edited\n");
        File.WriteAllText(_super.GetPath(Alpha + "/notes.txt"), "untracked\n");

        // A nested submodule that is merely behind is not uncommitted work of the one it is in.
        await GitInSubmoduleAsync(Leaf, "checkout", "-q", "HEAD~1");

        Dictionary<string, GitSubmodule> listed = await ListByPathAsync();

        Assert.True(listed[Gamma].HasUncommittedChanges);
        Assert.False(listed[Gamma].HasUntrackedFiles);

        Assert.False(listed[Alpha].HasUncommittedChanges);
        Assert.True(listed[Alpha].HasUntrackedFiles);
        Assert.Equal(SubmoduleCheckoutState.UpToDate, listed[Alpha].State);

        Assert.Equal(SubmoduleCheckoutState.Behind, listed[Leaf].State);
        Assert.False(listed[Beta].HasUncommittedChanges);
    }

    [Fact]
    public async Task ListAsync_WithoutGitModules_ReturnsNothing()
    {
        TemporaryRepository plain = await _workspace.InitRepositoryAsync("plain");
        await plain.CommitFileAsync("README.md", "# plain\n", "Add the readme");

        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(plain.Path, TestContext.Current.CancellationToken);

        Assert.Empty(await Service.ListAsync(discovery.Repository!, TestContext.Current.CancellationToken));
        Assert.Same(
            SubmoduleUpdateResult.Nothing,
            await Service.UpdateAsync(discovery.Repository!, null, null, TestContext.Current.CancellationToken));
    }

    // ---------------------------------------------------------------- updating

    [Fact]
    public async Task UpdateAsync_DownloadsTheMissingOneAndMovesTheBehindOne_AndLeavesTheOthers()
    {
        string betaWork = await ArrangeMixedStatesAsync();
        List<SubmoduleUpdateProgress> reports = [];

        SubmoduleUpdateResult result = await Service.UpdateAsync(
            _handle,
            [Delta, Gamma],
            new SynchronousProgress<SubmoduleUpdateProgress>(reports.Add),
            TestContext.Current.CancellationToken);

        Assert.Equal([Delta, Gamma], result.Updated);
        Assert.Empty(result.Skipped);
        Assert.Empty(result.Failed);

        Assert.Equal(_template.DeltaPin, await HeadOfAsync(Delta));
        Assert.Equal(_template.GammaPin, await HeadOfAsync(Gamma));
        Assert.Equal(betaWork, await HeadOfAsync(Beta));

        Assert.Contains(reports, report => report.Path == Delta);
        Assert.Contains(reports, report => report.Path == Gamma);

        Dictionary<string, GitSubmodule> listed = await ListByPathAsync();
        Assert.Equal(SubmoduleCheckoutState.UpToDate, listed[Delta].State);
        Assert.Equal(SubmoduleCheckoutState.UpToDate, listed[Gamma].State);
        Assert.Equal(SubmoduleCheckoutState.NewCommits, listed[Beta].State);
    }

    [Fact]
    public async Task UpdateAsync_WithNoPaths_PutsEverySubmoduleOnItsExpectedCommit()
    {
        string betaWork = await ArrangeMixedStatesAsync();

        SubmoduleUpdateResult result = await Service.UpdateAsync(_handle, null, null, TestContext.Current.CancellationToken);

        Assert.Equal([Beta, Delta, Gamma], result.Updated);
        Assert.False(result.HasProblems);

        Assert.All(await Service.ListAsync(_handle, TestContext.Current.CancellationToken),
            submodule => Assert.Equal(SubmoduleCheckoutState.UpToDate, submodule.State));

        // Beta's work is still on its branch: the update only moved the checkout.
        Assert.Equal(betaWork, await GitInSubmoduleAsync(Beta, "rev-parse", "refs/heads/work"));
    }

    [Fact]
    public async Task UpdateAsync_SkipsASubmoduleWithUncommittedChanges()
    {
        await GitInSubmoduleAsync(Gamma, "checkout", "-q", "HEAD~1");
        File.AppendAllText(_super.GetPath(Gamma + "/lib.txt"), "edited\n");

        SubmoduleUpdateResult result = await Service.UpdateAsync(_handle, [Gamma], null, TestContext.Current.CancellationToken);

        Assert.Empty(result.Updated);
        SubmoduleSkip skip = Assert.Single(result.Skipped);
        Assert.Equal(Gamma, skip.Path);
        Assert.Equal(SubmoduleSkipReason.UncommittedChanges, skip.Reason);
        Assert.Contains("uncommitted changes", skip.Message, System.StringComparison.Ordinal);

        Assert.Equal(_template.GammaOlder, await HeadOfAsync(Gamma));
        Assert.EndsWith("edited\n", File.ReadAllText(_super.GetPath(Gamma + "/lib.txt")).ReplaceLineEndings("\n"), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateAsync_SkipsASubmoduleOnACommitNoRefContains()
    {
        await GitInSubmoduleAsync(Gamma, "checkout", "-q", "--detach", "HEAD~1");
        File.WriteAllText(_super.GetPath(Gamma + "/side.txt"), "side\n");
        await GitInSubmoduleAsync(Gamma, "add", "side.txt");
        await GitInSubmoduleAsync(Gamma, "commit", "-q", "-m", "side");
        string orphan = await HeadOfAsync(Gamma);

        SubmoduleUpdateResult result = await Service.UpdateAsync(_handle, [Gamma], null, TestContext.Current.CancellationToken);

        Assert.Empty(result.Updated);
        SubmoduleSkip skip = Assert.Single(result.Skipped);
        Assert.Equal(SubmoduleSkipReason.CommitOnNoBranch, skip.Reason);
        Assert.Equal(orphan, skip.CheckedOutSha);
        Assert.Equal(orphan, await HeadOfAsync(Gamma));
    }

    [Fact]
    public async Task UpdateAsync_DownloadsTheSubmodulesNestedInOneItDownloads()
    {
        await _super.GitAsync("submodule", "deinit", "-q", "-f", "--", Alpha);

        Dictionary<string, GitSubmodule> before = await ListByPathAsync();
        Assert.Equal(SubmoduleCheckoutState.NotDownloaded, before[Alpha].State);
        Assert.False(before.ContainsKey(Leaf));

        SubmoduleUpdateResult result = await Service.UpdateAsync(_handle, [Alpha], null, TestContext.Current.CancellationToken);

        Assert.Equal([Alpha, Leaf], result.Updated);
        Assert.Equal(_template.AlphaPin, await HeadOfAsync(Alpha));
        Assert.Equal(_template.LeafPin, await HeadOfAsync(Leaf));
    }

    [Fact]
    public async Task UpdateAsync_MovesANestedSubmoduleBackOnTheCommitItsParentExpects()
    {
        await GitInSubmoduleAsync(Leaf, "checkout", "-q", "HEAD~1");

        SubmoduleUpdateResult result = await Service.UpdateAsync(_handle, [Alpha, Leaf], null, TestContext.Current.CancellationToken);

        Assert.Equal([Leaf], result.Updated);
        Assert.Equal(_template.LeafPin, await HeadOfAsync(Leaf));
    }

    [Fact]
    public async Task UpdateAsync_WithRebaseConfigured_ChecksTheCommitOutAndRebasesNothing()
    {
        await GitInSubmoduleAsync(Gamma, "checkout", "-q", "-b", "topic", "HEAD~1");
        File.WriteAllText(_super.GetPath(Gamma + "/topic.txt"), "topic\n");
        await GitInSubmoduleAsync(Gamma, "add", "topic.txt");
        await GitInSubmoduleAsync(Gamma, "commit", "-q", "-m", "topic work");
        string topic = await HeadOfAsync(Gamma);

        await _super.GitAsync("config", "submodule.libs/gamma.update", "rebase");

        SubmoduleUpdateResult result = await Service.UpdateAsync(_handle, [Gamma], null, TestContext.Current.CancellationToken);

        Assert.Equal([Gamma], result.Updated);
        Assert.Equal(_template.GammaPin, await HeadOfAsync(Gamma));
        Assert.Equal("HEAD", await GitInSubmoduleAsync(Gamma, "rev-parse", "--abbrev-ref", "HEAD"));
        Assert.Equal(topic, await GitInSubmoduleAsync(Gamma, "rev-parse", "refs/heads/topic"));
    }

    [Fact]
    public async Task UpdateAsync_NamesAPathThatIsNotASubmodule()
    {
        SubmoduleUpdateResult result = await Service.UpdateAsync(_handle, ["docs"], null, TestContext.Current.CancellationToken);

        SubmoduleSkip skip = Assert.Single(result.Skipped);
        Assert.Equal("docs", skip.Path);
        Assert.Equal(SubmoduleSkipReason.NotASubmodule, skip.Reason);
    }

    // ---------------------------------------------------------------- pins and refs

    [Fact]
    public async Task ChangedPinsAsync_NamesExactlyThePinsTheCommitMoved()
    {
        string before = await _super.ResolveAsync("HEAD");

        await GitInSubmoduleAsync(Gamma, "checkout", "-q", "HEAD~1");
        await GitInSubmoduleAsync(Beta, "checkout", "-q", "HEAD~1");
        _super.WriteFile("README.md", "# super, again\n");
        await _super.GitAsync("add", "--", Gamma, "README.md");
        await _super.GitAsync("commit", "-q", "-m", "Move gamma back");
        string after = await _super.ResolveAsync("HEAD");

        Assert.Equal(
            [Gamma],
            await Service.ChangedPinsAsync(_handle, before, after, TestContext.Current.CancellationToken));

        Assert.Empty(await Service.ChangedPinsAsync(_handle, after, after, TestContext.Current.CancellationToken));

        Assert.Equal(
            [Alpha, Beta, Delta, Gamma],
            await Service.ChangedPinsAsync(_handle, null, after, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsOnAnyRefAsync_TellsACommitOnABranchFromOneOnNone()
    {
        Dictionary<string, GitSubmodule> listed = await ListByPathAsync();
        GitSubmodule gamma = listed[Gamma];

        Assert.True(await Service.IsOnAnyRefAsync(gamma, _template.GammaOlder, TestContext.Current.CancellationToken));

        await GitInSubmoduleAsync(Gamma, "checkout", "-q", "--detach");
        await GitInSubmoduleAsync(Gamma, "commit", "-q", "--allow-empty", "-m", "nowhere");
        string orphan = await HeadOfAsync(Gamma);

        Assert.False(await Service.IsOnAnyRefAsync(gamma, orphan, TestContext.Current.CancellationToken));
    }

    private sealed class SynchronousProgress<T> : System.IProgress<T>
    {
        private readonly System.Action<T> _report;

        public SynchronousProgress(System.Action<T> report) => _report = report;

        public void Report(T value) => _report(value);
    }
}
