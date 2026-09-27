using System;
using System.IO;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Security;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Git;

/// <summary>
/// The inline credential helper, against a real git and through the product's own runner. git's
/// <c>credential</c> plumbing is exactly what a fetch or a push asks, so no network is needed.
/// </summary>
public sealed class GitCredentialHelperTests : IAsyncLifetime
{
    private const string Token = "ghp_0123456789abcdefghijklmnopqrstuvwxyz";

    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _repository = null!;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _repository = await _workspace.InitRepositoryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private IGitProcessRunner Runner => _host.GetRequiredService<IGitProcessRunner>();

    private IGitCommandFactory Factory => _host.GetRequiredService<IGitCommandFactory>();

    private static GitCredentials GitHub()
    {
        Assert.True(GitHostCredential.TryCreate(
            new Uri("https://github.com"), "x-access-token", new SecretString(Token), out GitHostCredential? credential));

        return GitCredentials.For([credential!]);
    }

    private Task<GitResult> CredentialAsync(string operation, string input, GitCredentials credentials)
        => Runner.RunAsync(
            Factory.CreateWithInput(_repository.Path, input, ["credential", operation]).WithCredentials(credentials),
            throwOnError: false,
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task Fill_AnswersWithTheUserNameAndTheTokenForItsOrigin()
    {
        GitResult result = await CredentialAsync("fill", "protocol=https\nhost=github.com\n\n", GitHub());

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Contains("username=x-access-token\n", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains($"password={Token}\n", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fill_AnotherHostGetsNothing()
    {
        GitResult result = await CredentialAsync("fill", "protocol=https\nhost=gitlab.com\n\n", GitHub());

        // Prompts are disabled, so a host with no answer fails rather than asks.
        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(Token, result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fill_AHelperTheUserConfiguredDoesNotAnswerFirst()
    {
        await _repository.GitAsync("config", "credential.helper", "!f() { echo username=someone; echo password=stale; }; f");

        GitResult own = await CredentialAsync("fill", "protocol=https\nhost=github.com\n\n", GitCredentials.None);
        GitResult injected = await CredentialAsync("fill", "protocol=https\nhost=github.com\n\n", GitHub());

        Assert.Contains("password=stale", own.StandardOutput, StringComparison.Ordinal);
        Assert.Contains($"password={Token}\n", injected.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("stale", injected.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approve_NeverReachesAHelperThatWouldStoreTheToken()
    {
        string store = Path.Combine(_workspace.RootPath, "stored-credentials");
        await _repository.GitAsync("config", "credential.helper", $"store --file=\"{store.Replace('\\', '/')}\"");

        string approval = $"protocol=https\nhost=github.com\nusername=x-access-token\npassword={Token}\n\n";

        GitResult injected = await CredentialAsync("approve", approval, GitHub());

        Assert.True(injected.IsSuccess, injected.StandardError);
        Assert.False(File.Exists(store));

        // The same approval without the injected helper does store: the helper above is a live one.
        await CredentialAsync("approve", approval, GitCredentials.None);
        Assert.True(File.Exists(store));
    }
}
