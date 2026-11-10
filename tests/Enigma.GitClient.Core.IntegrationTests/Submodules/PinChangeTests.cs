using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Staging;
using Enigma.GitClient.Core.Submodules;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Submodules;

/// <summary>
/// A submodule's pin change against real repositories: which way it goes and the commits between, the
/// changes a commit would record, and a discard that puts the submodule back — or refuses to, with work
/// inside.
/// </summary>
public sealed class PinChangeTests : IAsyncLifetime, IClassFixture<SubmoduleTemplate>
{
    private const string Gamma = SubmoduleTemplate.Gamma;
    private const string Delta = SubmoduleTemplate.Delta;

    private readonly SubmoduleTemplate _template;

    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _super = null!;
    private RepositoryHandle _handle = null!;

    public PinChangeTests(SubmoduleTemplate template) => _template = template;

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

    private async Task<string> GitInSubmoduleAsync(string path, params string[] arguments)
        => (await GitCli.RunAsync(_super.GetPath(path), _workspace.Environment, arguments)).Trim();

    private Task<string> HeadOfAsync(string path) => GitInSubmoduleAsync(path, "rev-parse", "HEAD");

    private Task<PinChange> DescribeAsync(string path, string oldSha, string newSha)
        => Service.DescribePinChangeAsync(_handle, path, oldSha, newSha, TestContext.Current.CancellationToken);

    private async Task<string> CommitOnTheSideAsync()
    {
        await GitInSubmoduleAsync(Gamma, "checkout", "-q", "--detach", _template.GammaOlder);
        File.WriteAllText(_super.GetPath(Gamma + "/side.txt"), "side\n");
        await GitInSubmoduleAsync(Gamma, "add", "side.txt");
        await GitInSubmoduleAsync(Gamma, "commit", "-q", "-m", "gamma side");

        return await HeadOfAsync(Gamma);
    }

    // ---------------------------------------------------------------- which way

    [Fact]
    public async Task Forward_ListsTheCommitsItBringsIn()
    {
        PinChange change = await DescribeAsync(Gamma, _template.GammaOlder, _template.GammaPin);

        Assert.Equal(PinDirection.Forward, change.Direction);
        Assert.Equal(1, change.AddedCount);
        Assert.Equal("gamma two", Assert.Single(change.Added).Title);
        Assert.Empty(change.Removed);
        Assert.Equal("Enigma Test", change.Added[0].Author);
    }

    [Fact]
    public async Task Back_ListsTheCommitsItTakesBack()
    {
        PinChange change = await DescribeAsync(Gamma, _template.GammaPin, _template.GammaOlder);

        Assert.Equal(PinDirection.Back, change.Direction);
        Assert.Equal(1, change.RemovedCount);
        Assert.Equal("gamma two", Assert.Single(change.Removed).Title);
        Assert.Empty(change.Added);
    }

    [Fact]
    public async Task Diverged_ListsBothSides()
    {
        string side = await CommitOnTheSideAsync();

        PinChange change = await DescribeAsync(Gamma, _template.GammaPin, side);

        Assert.Equal(PinDirection.Diverged, change.Direction);
        Assert.Equal("gamma side", Assert.Single(change.Added).Title);
        Assert.Equal("gamma two", Assert.Single(change.Removed).Title);
    }

    [Fact]
    public async Task ASubmoduleNotDownloaded_OrACommitItLacks_IsUnknown_AndNothingIsFetched()
    {
        await _super.GitAsync("submodule", "deinit", "-q", "-f", "--", Delta);

        PinChange empty = await DescribeAsync(Delta, _template.DeltaPin, _template.GammaPin);
        Assert.Equal(PinDirection.Unknown, empty.Direction);
        Assert.Contains("not downloaded", empty.UnknownReason, System.StringComparison.Ordinal);

        PinChange lacking = await DescribeAsync(Gamma, _template.GammaPin, _template.DeltaPin);
        Assert.Equal(PinDirection.Unknown, lacking.Direction);
        Assert.Contains("does not have", lacking.UnknownReason, System.StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- what a commit would record

    [Fact]
    public async Task ReadStagedPinsAsync_NamesThePinTheIndexMoves()
    {
        await GitInSubmoduleAsync(Gamma, "checkout", "-q", _template.GammaOlder);
        await _super.GitAsync("add", "--", Gamma);

        StagedPin pin = Assert.Single(await Service.ReadStagedPinsAsync(_handle, TestContext.Current.CancellationToken));

        Assert.Equal(new StagedPin(Gamma, _template.GammaPin, _template.GammaOlder), pin);
    }

    // ---------------------------------------------------------------- a discard

    [Fact]
    public async Task DiscardingASubmodule_PutsItBackOnHeadsCommit()
    {
        await GitInSubmoduleAsync(Gamma, "checkout", "-q", _template.GammaOlder);
        await _super.GitAsync("add", "--", Gamma);

        SubmoduleDiscard plan = Assert.Single(await Service.PlanDiscardAsync(_handle, [Gamma], TestContext.Current.CancellationToken));
        Assert.Equal(new SubmoduleDiscard(Gamma, _template.GammaPin, _template.GammaOlder, false, 0), plan);

        await _host.GetRequiredService<IStagingService>().DiscardAsync(_handle, [Gamma], TestContext.Current.CancellationToken);
        SubmoduleUpdateResult result = await Service.PutBackAsync(_handle, [Gamma], null, TestContext.Current.CancellationToken);

        Assert.Equal([Gamma], result.Updated);
        Assert.Equal(_template.GammaPin, await HeadOfAsync(Gamma));
        Assert.Equal(string.Empty, (await _super.GitAsync("status", "--porcelain", "--ignore-submodules=none")).Trim());
    }

    [Fact]
    public async Task DiscardingASubmoduleOnACommitNoRefContains_SaysSo_AndMovesItAllTheSame()
    {
        string side = await CommitOnTheSideAsync();

        SubmoduleDiscard plan = Assert.Single(await Service.PlanDiscardAsync(_handle, [Gamma], TestContext.Current.CancellationToken));
        Assert.Equal(1, plan.CommitsLeftBehind);
        Assert.Equal(side, plan.CheckedOutSha);

        await _host.GetRequiredService<IStagingService>().DiscardAsync(_handle, [Gamma], TestContext.Current.CancellationToken);
        SubmoduleUpdateResult result = await Service.PutBackAsync(_handle, [Gamma], null, TestContext.Current.CancellationToken);

        Assert.Equal([Gamma], result.Updated);
        Assert.Equal(_template.GammaPin, await HeadOfAsync(Gamma));
    }

    [Fact]
    public async Task ADiscardIsRefused_WithUncommittedChangesInTheSubmodule()
    {
        await GitInSubmoduleAsync(Gamma, "checkout", "-q", _template.GammaOlder);
        File.AppendAllText(_super.GetPath(Gamma + "/lib.txt"), "edited\n");

        SubmoduleDiscard plan = Assert.Single(await Service.PlanDiscardAsync(_handle, [Gamma], TestContext.Current.CancellationToken));
        Assert.True(plan.HasUncommittedChanges);

        SubmoduleUpdateResult result = await Service.PutBackAsync(_handle, [Gamma], null, TestContext.Current.CancellationToken);

        Assert.Empty(result.Updated);
        Assert.Equal(SubmoduleSkipReason.UncommittedChanges, Assert.Single(result.Skipped).Reason);
        Assert.Equal(_template.GammaOlder, await HeadOfAsync(Gamma));
    }

    [Fact]
    public async Task APlan_LeavesOutWhatIsNotASubmodule()
    {
        IReadOnlyList<SubmoduleDiscard> plans = await Service.PlanDiscardAsync(_handle, ["README.md"], TestContext.Current.CancellationToken);

        Assert.Empty(plans);
    }
}
