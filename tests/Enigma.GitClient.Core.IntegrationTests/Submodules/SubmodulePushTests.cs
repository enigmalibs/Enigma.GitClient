using System.IO;
using System.Threading.Tasks;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Sync;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Submodules;

/// <summary>
/// A push that records a submodule commit no remote has is refused, naming the submodule, and goes
/// through once the submodule is pushed — against real remotes of the repository and of the submodule.
/// </summary>
public sealed class SubmodulePushTests : IAsyncLifetime, IClassFixture<SubmoduleTemplate>
{
    private const string Gamma = SubmoduleTemplate.Gamma;

    private readonly SubmoduleTemplate _template;

    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _super = null!;
    private RepositoryHandle _handle = null!;

    public SubmodulePushTests(SubmoduleTemplate template) => _template = template;

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

    private async Task<string> GitInAsync(string directory, params string[] arguments)
        => (await GitCli.RunAsync(directory, _workspace.Environment, arguments)).Trim();

    [Fact]
    public async Task APushRecordingAnUnpushedSubmoduleCommit_IsRefused_ThenGoesThroughOnceTheSubmoduleIsPushed()
    {
        ISyncService sync = _host.GetRequiredService<ISyncService>();

        // A bare remote for super, and one for gamma that the submodule fetches from.
        string origin = Path.Combine(_workspace.RootPath, "origin.git");
        await GitInAsync(_workspace.RootPath, "clone", "-q", "--bare", "--", _super.Path, origin);
        await _super.GitAsync("remote", "add", "origin", origin);
        await _super.GitAsync("fetch", "-q", "origin");

        string gammaRemote = Path.Combine(_workspace.RootPath, "gamma.git");
        await GitInAsync(_workspace.RootPath, "clone", "-q", "--bare", "--", Path.Combine(_workspace.RootPath, "gamma"), gammaRemote);

        string gamma = _super.GetPath(Gamma);
        await GitInAsync(gamma, "remote", "set-url", "origin", gammaRemote);
        await GitInAsync(gamma, "fetch", "-q", "origin");

        // Work in gamma that no remote has, recorded by super.
        await GitInAsync(gamma, "checkout", "-q", "-b", "work");
        File.WriteAllText(Path.Combine(gamma, "work.txt"), "work\n");
        await GitInAsync(gamma, "add", "work.txt");
        await GitInAsync(gamma, "commit", "-q", "-m", "gamma work");
        await _super.GitAsync("add", "--", Gamma);
        await _super.GitAsync("commit", "-q", "-m", "Record gamma's work");

        PushRequest push = new() { Remote = "origin", Branch = "main" };

        SyncException refused = await Assert.ThrowsAsync<SyncException>(
            () => sync.PushAsync(_handle, push, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(SyncFailureKind.UnpushedSubmodule, refused.Failure.Kind);
        Assert.Equal([Gamma], refused.Failure.SubmodulePaths);
        Assert.NotEqual(await _super.ResolveAsync("HEAD"), await GitInAsync(origin, "rev-parse", "main"));

        RepositoryHandle gammaHandle = (await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(gamma, TestContext.Current.CancellationToken)).Repository!;

        await sync.PushAsync(
            gammaHandle,
            new PushRequest { Remote = "origin", Branch = "work", SetUpstream = true },
            cancellationToken: TestContext.Current.CancellationToken);

        await sync.PushAsync(_handle, push, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(await _super.ResolveAsync("HEAD"), await GitInAsync(origin, "rev-parse", "main"));
        Assert.Equal(await GitInAsync(gamma, "rev-parse", "HEAD"), await GitInAsync(gammaRemote, "rev-parse", "work"));
    }
}
