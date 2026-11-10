using System.IO;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Checkout;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Submodules;
using Enigma.GitClient.Core.Sync;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Submodules;

/// <summary>
/// The follow after an operation that moved HEAD: the submodules whose recorded commit moved are put on
/// it, the ones not downloaded are downloaded, and nothing else moves — against real repositories, the
/// operations run by the application's own services, whose commands switch git's own submodule
/// recursion off.
/// </summary>
public sealed class SubmoduleFollowTests : IAsyncLifetime, IClassFixture<SubmoduleTemplate>
{
    private const string Alpha = SubmoduleTemplate.Alpha;
    private const string Leaf = SubmoduleTemplate.Leaf;
    private const string Beta = SubmoduleTemplate.Beta;
    private const string Gamma = SubmoduleTemplate.Gamma;

    private readonly SubmoduleTemplate _template;

    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _super = null!;
    private RepositoryHandle _handle = null!;

    public SubmoduleFollowTests(SubmoduleTemplate template) => _template = template;

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

    private string Library(string name) => Path.Combine(_workspace.RootPath, name);

    private async Task<string> GitInAsync(string directory, params string[] arguments)
        => (await GitCli.RunAsync(directory, _workspace.Environment, arguments)).Trim();

    private Task<string> GitInSubmoduleAsync(string path, params string[] arguments)
        => GitInAsync(_super.GetPath(path), arguments);

    private Task<string> HeadOfAsync(string path) => GitInSubmoduleAsync(path, "rev-parse", "HEAD");

    /// <summary>Commits a change in one of the workspace's libraries and returns the commit.</summary>
    private async Task<string> CommitInLibraryAsync(string name, string message)
    {
        File.AppendAllText(Path.Combine(Library(name), "lib.txt"), message + "\n");
        await GitInAsync(Library(name), "commit", "-q", "-am", message);

        return await GitInAsync(Library(name), "rev-parse", "HEAD");
    }

    /// <summary>Points every submodule's remote at this workspace's libraries, which the test changes.</summary>
    private Task SyncSubmoduleRemotesAsync() => _super.GitAsync("submodule", "sync", "-q", "--recursive");

    private Task CheckOutAsync(string revision)
        => _host.GetRequiredService<ICheckoutService>()
            .CheckoutAsync(_handle, revision, cancellationToken: TestContext.Current.CancellationToken);

    [Fact]
    public async Task AfterAPullThatMovesAPin_TheSubmoduleIsOnTheNewCommit_AndOneMovedOnPurposeStays()
    {
        // A remote for super, and a colleague who records a newer gamma there.
        string origin = Path.Combine(_workspace.RootPath, "origin.git");
        await GitInAsync(_workspace.RootPath, "clone", "-q", "--bare", "--", _super.Path, origin);
        await _super.GitAsync("remote", "add", "origin", origin);
        await _super.GitAsync("fetch", "-q", "origin");
        await _super.GitAsync("branch", "-q", "--set-upstream-to=origin/main", "main");
        await SyncSubmoduleRemotesAsync();

        string gammaNew = await CommitInLibraryAsync("gamma", "gamma three");

        string colleague = Path.Combine(_workspace.RootPath, "colleague");
        await GitInAsync(_workspace.RootPath, "clone", "-q", "--", origin, colleague);
        await GitInAsync(colleague, "update-index", "--cacheinfo", $"160000,{gammaNew},{Gamma}");
        await GitInAsync(colleague, "commit", "-q", "-m", "Bump gamma");
        await GitInAsync(colleague, "push", "-q", "origin", "main");

        // The user moved beta back on purpose; the pull does not touch its pin.
        await GitInSubmoduleAsync(Beta, "checkout", "-q", "HEAD~1");
        string betaOlder = await HeadOfAsync(Beta);

        string before = await _super.ResolveAsync("HEAD");
        await _host.GetRequiredService<ISyncService>()
            .PullAsync(_handle, "origin", "main", cancellationToken: TestContext.Current.CancellationToken);
        string after = await _super.ResolveAsync("HEAD");

        Assert.NotEqual(before, after);
        Assert.Equal(_template.GammaPin, await HeadOfAsync(Gamma));

        SubmoduleUpdateResult result = await Service.FollowAsync(_handle, before, after, null, TestContext.Current.CancellationToken);

        Assert.Equal([Gamma], result.Updated);
        Assert.False(result.HasProblems);
        Assert.Equal(gammaNew, await HeadOfAsync(Gamma));
        Assert.Equal(betaOlder, await HeadOfAsync(Beta));
    }

    [Fact]
    public async Task CheckingOutABranchThatAddsASubmodule_DownloadsIt()
    {
        TemporaryRepository epsilon = await _workspace.InitRepositoryAsync("epsilon");
        string epsilonPin = await epsilon.CommitFileAsync("lib.txt", "epsilon one\n", "epsilon one");

        // The branch records the submodule without checking it out here.
        await _super.GitAsync("checkout", "-q", "-b", "with-epsilon");
        await _super.GitAsync("config", "-f", ".gitmodules", "submodule.libs/epsilon.path", "libs/epsilon");
        await _super.GitAsync("config", "-f", ".gitmodules", "submodule.libs/epsilon.url", "../epsilon");
        await _super.GitAsync("update-index", "--add", "--cacheinfo", $"160000,{epsilonPin},libs/epsilon");
        await _super.GitAsync("add", ".gitmodules");
        await _super.GitAsync("commit", "-q", "-m", "Add epsilon");
        await _super.GitAsync("checkout", "-q", "main");

        string before = await _super.ResolveAsync("HEAD");
        await CheckOutAsync("with-epsilon");
        string after = await _super.ResolveAsync("HEAD");

        Assert.False(File.Exists(_super.GetPath("libs/epsilon/.git")));

        SubmoduleUpdateResult result = await Service.FollowAsync(_handle, before, after, null, TestContext.Current.CancellationToken);

        Assert.Equal(["libs/epsilon"], result.Updated);
        Assert.Equal(epsilonPin, await HeadOfAsync("libs/epsilon"));
    }

    [Fact]
    public async Task WithSubmoduleRecurseConfigured_TheCheckoutLeavesTheSubmodules_AndTheFollowMovesThem()
    {
        // A branch that records gamma one commit back, prepared before the setting is on.
        await _super.GitAsync("checkout", "-q", "-b", "older-gamma");
        await GitInSubmoduleAsync(Gamma, "checkout", "-q", "HEAD~1");
        await _super.GitAsync("add", "--", Gamma);
        await _super.GitAsync("commit", "-q", "-m", "Gamma back");

        await _super.GitAsync("config", "submodule.recurse", "true");

        string before = await _super.ResolveAsync("HEAD");
        await CheckOutAsync("main");
        string after = await _super.ResolveAsync("HEAD");

        // git did not recurse: gamma is still where the branch left it.
        Assert.Equal(_template.GammaOlder, await HeadOfAsync(Gamma));

        SubmoduleUpdateResult result = await Service.FollowAsync(_handle, before, after, null, TestContext.Current.CancellationToken);

        Assert.Equal([Gamma], result.Updated);
        Assert.Equal(_template.GammaPin, await HeadOfAsync(Gamma));
    }

    [Fact]
    public async Task ASubmoduleThatMoves_TakesTheNestedOnesItsMoveChanged()
    {
        await SyncSubmoduleRemotesAsync();

        // alpha records a newer leaf; super's branch records that alpha.
        string leafNew = await CommitInLibraryAsync("leaf", "leaf three");
        await GitInAsync(Library("alpha"), "update-index", "--cacheinfo", $"160000,{leafNew},vendor/leaf");
        await GitInAsync(Library("alpha"), "commit", "-q", "-m", "Bump the leaf");
        string alphaNew = await GitInAsync(Library("alpha"), "rev-parse", "HEAD");

        await _super.GitAsync("checkout", "-q", "-b", "new-alpha");
        await _super.GitAsync("update-index", "--cacheinfo", $"160000,{alphaNew},{Alpha}");
        await _super.GitAsync("commit", "-q", "-m", "Bump alpha");
        await _super.GitAsync("checkout", "-q", "main");

        string before = await _super.ResolveAsync("HEAD");
        await CheckOutAsync("new-alpha");
        string after = await _super.ResolveAsync("HEAD");

        SubmoduleUpdateResult result = await Service.FollowAsync(_handle, before, after, null, TestContext.Current.CancellationToken);

        Assert.Equal([Alpha, Leaf], result.Updated);
        Assert.Equal(alphaNew, await HeadOfAsync(Alpha));
        Assert.Equal(leafNew, await HeadOfAsync(Leaf));
    }

    [Fact]
    public async Task ReadHeadAsync_ReadsTheCommitHeadIsOn()
        => Assert.Equal(_template.SuperHead, await Service.ReadHeadAsync(_handle, TestContext.Current.CancellationToken));
}
