using System;
using System.IO;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Identity;

/// <summary>
/// Drives the identity service against a real git whose global configuration is the workspace's
/// own throwaway file — never the developer's.
/// </summary>
public sealed class GitIdentityServiceTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _repository = null!;
    private RepositoryHandle _handle = null!;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _repository = await _workspace.InitRepositoryAsync("identity");
        await _repository.CommitInitialAsync();

        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_repository.Path, TestContext.Current.CancellationToken);

        _handle = discovery.Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private IGitIdentityService Identity => _host.GetRequiredService<IGitIdentityService>();

    private string GlobalConfigFile => _workspace.Environment["GIT_CONFIG_GLOBAL"];

    [Fact]
    public async Task GetGlobal_ReadsEmptyWhenNothingIsSet()
    {
        GitIdentity identity = await Identity.GetGlobalAsync(TestContext.Current.CancellationToken);

        Assert.Equal(GitIdentity.Empty, identity);
    }

    [Fact]
    public async Task SetGlobal_WritesTheWorkspacesGlobalFileAndReadsBack()
    {
        await Identity.SetGlobalAsync(
            new GitIdentity("  Ada King Lovelace ", " ada@example.com "),
            TestContext.Current.CancellationToken);

        GitIdentity identity = await Identity.GetGlobalAsync(TestContext.Current.CancellationToken);

        // Trimmed on the way in, and written where GIT_CONFIG_GLOBAL points.
        Assert.Equal(new GitIdentity("Ada King Lovelace", "ada@example.com"), identity);
        Assert.Contains("Ada King Lovelace", await File.ReadAllTextAsync(GlobalConfigFile, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetGlobal_KeepsTheRestOfTheFile()
    {
        await File.WriteAllTextAsync(
            GlobalConfigFile,
            "[core]\n\teditor = vim\n[user]\n\tname = Old Name\n\tsigningkey = ABC123\n",
            TestContext.Current.CancellationToken);

        await Identity.SetGlobalAsync(new GitIdentity("New Name", "new@example.com"), TestContext.Current.CancellationToken);

        string file = await File.ReadAllTextAsync(GlobalConfigFile, TestContext.Current.CancellationToken);

        Assert.Contains("editor = vim", file, StringComparison.Ordinal);
        Assert.Contains("signingkey = ABC123", file, StringComparison.Ordinal);
        Assert.DoesNotContain("Old Name", file, StringComparison.Ordinal);
        Assert.Equal(
            new GitIdentity("New Name", "new@example.com"),
            await Identity.GetGlobalAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetGlobal_ReadsOnlyTheNameAndTheEmail()
    {
        await File.WriteAllTextAsync(
            GlobalConfigFile,
            "[user]\n\tname = Ada\n\tuseConfigOnly = true\n\temail = ada@example.com\n\tsigningkey = ABC\n",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new GitIdentity("Ada", "ada@example.com"),
            await Identity.GetGlobalAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetGlobal_RefusesAnInvalidIdentityBeforeGitRuns()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Identity.SetGlobalAsync(new GitIdentity("Ada", "not-an-address"), TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Identity.SetGlobalAsync(new GitIdentity("", "ada@example.com"), TestContext.Current.CancellationToken));

        // Not even the valid half was written.
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(GlobalConfigFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetLocal_ReadsOnlyWhatTheRepositorySetsItself()
    {
        await Identity.SetGlobalAsync(new GitIdentity("Global", "global@example.com"), TestContext.Current.CancellationToken);

        // The global identity is inherited, but it is not the repository's own.
        Assert.Equal(GitIdentity.Empty, await Identity.GetLocalAsync(_handle, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetLocal_OverridesTheGlobalIdentityInThatRepository()
    {
        await Identity.SetGlobalAsync(new GitIdentity("Global", "global@example.com"), TestContext.Current.CancellationToken);

        await Identity.SetLocalAsync(_handle, new GitIdentity("Work Me", "me@work.example"), TestContext.Current.CancellationToken);

        Assert.Equal(
            new GitIdentity("Work Me", "me@work.example"),
            await Identity.GetLocalAsync(_handle, TestContext.Current.CancellationToken));

        // What git itself resolves in the repository is now the local identity…
        Assert.Equal("Work Me", await _repository.GitLineAsync("config", "--get", "user.name"));
        Assert.Equal("me@work.example", await _repository.GitLineAsync("config", "--get", "user.email"));

        // …and the global one is untouched.
        Assert.Equal(
            new GitIdentity("Global", "global@example.com"),
            await Identity.GetGlobalAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveLocal_LeavesTheRepositoryOnTheGlobalIdentity()
    {
        await Identity.SetGlobalAsync(new GitIdentity("Global", "global@example.com"), TestContext.Current.CancellationToken);
        await Identity.SetLocalAsync(_handle, new GitIdentity("Work Me", "me@work.example"), TestContext.Current.CancellationToken);

        await Identity.RemoveLocalAsync(_handle, TestContext.Current.CancellationToken);

        Assert.Equal(GitIdentity.Empty, await Identity.GetLocalAsync(_handle, TestContext.Current.CancellationToken));
        Assert.Equal("Global", await _repository.GitLineAsync("config", "--get", "user.name"));
    }

    [Fact]
    public async Task RemoveLocal_SucceedsWhenNothingIsSet()
    {
        await Identity.RemoveLocalAsync(_handle, TestContext.Current.CancellationToken);

        Assert.Equal(GitIdentity.Empty, await Identity.GetLocalAsync(_handle, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveLocal_RemovesAKeySetMoreThanOnce()
    {
        await _repository.GitAsync("config", "--local", "--add", "user.name", "First");
        await _repository.GitAsync("config", "--local", "--add", "user.name", "Second");
        await _repository.GitAsync("config", "--local", "user.email", "only@example.com");

        await Identity.RemoveLocalAsync(_handle, TestContext.Current.CancellationToken);

        Assert.Equal(GitIdentity.Empty, await Identity.GetLocalAsync(_handle, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetLocal_FailsLoudlyOutsideARepository()
    {
        string plain = _workspace.CreateDirectory("not-a-repository");
        RepositoryHandle fake = new(plain, Path.Combine(plain, ".git"));

        await Assert.ThrowsAsync<GitCommandException>(() =>
            Identity.GetLocalAsync(fake, TestContext.Current.CancellationToken));
    }
}
