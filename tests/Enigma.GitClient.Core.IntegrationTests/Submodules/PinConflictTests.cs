using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Merging;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Submodules;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Submodules;

/// <summary>
/// A pin conflict against real repositories: two branches move gamma to divergent commits, the merge
/// reports a submodule conflict with both commits, and taking theirs resolves it, checks the submodule
/// out on it and lets the merge commit.
/// </summary>
public sealed class PinConflictTests : IAsyncLifetime, IClassFixture<SubmoduleTemplate>
{
    private const string Gamma = SubmoduleTemplate.Gamma;

    private readonly SubmoduleTemplate _template;

    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _super = null!;
    private RepositoryHandle _handle = null!;

    public PinConflictTests(SubmoduleTemplate template) => _template = template;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _workspace.AllowLocalSubmodules();
        _host = CoreTestHost.Create(_workspace);
        _super = _template.CopyInto(_workspace);

        _handle = (await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_super.Path, TestContext.Current.CancellationToken)).Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private async Task<string> GitInGammaAsync(params string[] arguments)
        => (await GitCli.RunAsync(_super.GetPath(Gamma), _workspace.Environment, arguments)).Trim();

    /// <summary>Records gamma on a new commit made from <paramref name="from"/>, on the branch checked out.</summary>
    private async Task<string> RecordGammaCommitAsync(string from, string file)
    {
        await GitInGammaAsync("checkout", "-q", "--detach", from);
        File.WriteAllText(_super.GetPath($"{Gamma}/{file}"), file + "\n");
        await GitInGammaAsync("add", file);
        await GitInGammaAsync("commit", "-q", "-m", "gamma " + file);
        string sha = await GitInGammaAsync("rev-parse", "HEAD");

        // Kept reachable, so moving gamma off it later is not refused for a commit no ref contains.
        await GitInGammaAsync("branch", "-f", "keep-" + file, sha);

        await _super.GitAsync("add", "--", Gamma);
        await _super.GitAsync("commit", "-q", "-m", "Record gamma " + file);

        return sha;
    }

    [Fact]
    public async Task TwoBranchesMovingThePin_Conflict_AndTakingTheirsResolvesItAndTheMergeCommits()
    {
        IConflictService conflicts = _host.GetRequiredService<IConflictService>();

        await _super.GitAsync("checkout", "-q", "-b", "theirs");
        string theirs = await RecordGammaCommitAsync(_template.GammaOlder, "theirs.txt");

        await _super.GitAsync("checkout", "-q", "main");
        await GitInGammaAsync("checkout", "-q", "--detach", _template.GammaPin);
        string ours = await RecordGammaCommitAsync(_template.GammaPin, "ours.txt");

        MergeOutcome outcome = await _host.GetRequiredService<IMergeService>()
            .MergeAsync(_handle, new MergeRequest { Source = "theirs" }, TestContext.Current.CancellationToken);

        Assert.True(outcome.NeedsResolution);

        IReadOnlyList<ConflictFile> files = await conflicts.GetConflictsAsync(_handle, TestContext.Current.CancellationToken);
        ConflictFile pin = Assert.Single(files);

        Assert.Equal(Gamma, pin.Path);
        Assert.True(pin.IsSubmodule);
        Assert.True(pin.IsPinConflict);
        Assert.Equal(ours, pin.OursSha);
        Assert.Equal(theirs, pin.TheirsSha);
        Assert.False(pin.HasThreeWayContent);
        Assert.Null(await conflicts.GetDocumentAsync(_handle, Gamma, TestContext.Current.CancellationToken));

        SubmoduleUpdateResult moved = await conflicts.ResolveSubmoduleAsync(_handle, Gamma, theirs, TestContext.Current.CancellationToken);

        Assert.Equal([Gamma], moved.Updated);
        Assert.Equal(theirs, await GitInGammaAsync("rev-parse", "HEAD"));
        Assert.Empty(await conflicts.GetConflictsAsync(_handle, TestContext.Current.CancellationToken));

        string merge = await _host.GetRequiredService<IMergeService>().ContinueAsync(_handle, null, TestContext.Current.CancellationToken);

        Assert.Equal(theirs, (await _super.GitAsync("rev-parse", $"{merge}:{Gamma}")).Trim());
    }

    [Fact]
    public async Task ResolvingAllWithOurs_TakesOurCommitForThePin()
    {
        await _super.GitAsync("checkout", "-q", "-b", "theirs");
        await RecordGammaCommitAsync(_template.GammaOlder, "theirs.txt");

        await _super.GitAsync("checkout", "-q", "main");
        await GitInGammaAsync("checkout", "-q", "--detach", _template.GammaPin);
        string ours = await RecordGammaCommitAsync(_template.GammaPin, "ours.txt");

        await _host.GetRequiredService<IMergeService>()
            .MergeAsync(_handle, new MergeRequest { Source = "theirs" }, TestContext.Current.CancellationToken);

        IReadOnlyList<string> resolved = await _host.GetRequiredService<IConflictService>()
            .ResolveAllWithAsync(_handle, ConflictSide.Ours, TestContext.Current.CancellationToken);

        Assert.Equal([Gamma], resolved);
        Assert.Equal(ours, (await _super.GitAsync("rev-parse", $":{Gamma}")).Trim());
    }
}
