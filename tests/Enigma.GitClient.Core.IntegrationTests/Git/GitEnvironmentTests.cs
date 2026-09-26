using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Git;

public sealed class GitEnvironmentTests : IAsyncLifetime
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

    [Fact]
    public async Task GetAvailabilityAsync_ReportsTheInstalledGitAsUsable()
    {
        GitAvailability availability = await _host.GetRequiredService<IGitEnvironment>()
            .GetAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.Equal(GitAvailabilityStatus.Available, availability.Status);
        Assert.True(availability.IsUsable);
        Assert.Equal(string.Empty, availability.Message);
        Assert.NotNull(availability.Version);
        Assert.True(availability.Version!.Value >= GitVersion.Minimum);
    }

    [Fact]
    public async Task GetVersionAsync_ReadsTheInstalledVersion()
    {
        GitVersion version = await _host.GetRequiredService<IGitEnvironment>()
            .GetVersionAsync(TestContext.Current.CancellationToken);

        Assert.True(version.Major >= 2);
        Assert.NotEqual(string.Empty, version.Raw);
    }

    [Fact]
    public async Task GetAvailabilityAsync_CachesTheProbe()
    {
        IGitEnvironment environment = _host.GetRequiredService<IGitEnvironment>();

        GitAvailability first = await environment.GetAvailabilityAsync(TestContext.Current.CancellationToken);
        GitAvailability second = await environment.GetAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetAvailabilityAsync_ReportsAMissingExecutableInsteadOfThrowing()
    {
        using CoreTestHost host = CoreTestHostFactory.WithExecutablePath(
            _workspace,
            System.IO.Path.Combine(_workspace.RootPath, "no-such-git"));

        GitAvailability availability = await host.GetRequiredService<IGitEnvironment>()
            .GetAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.Equal(GitAvailabilityStatus.NotFound, availability.Status);
        Assert.False(availability.IsUsable);
        Assert.NotEqual(string.Empty, availability.Message);
    }
}
