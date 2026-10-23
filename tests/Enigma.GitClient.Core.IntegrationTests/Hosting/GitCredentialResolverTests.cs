using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Security;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Hosting;

/// <summary>
/// The logins a repository signs in with, read from real profiles, accounts, tokens and git
/// configuration — and handed to a real git's credential plumbing at the end.
/// </summary>
public sealed class GitCredentialResolverTests : IAsyncLifetime
{
    private const string Token = "ghp_0123456789abcdefghijklmnopqrstuvwxyz";

    private static readonly GitIdentity Ada = new("Ada Lovelace", "ada@work.example");

    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _repository = null!;
    private RepositoryHandle _handle = null!;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _repository = await _workspace.InitRepositoryAsync();

        await _repository.GitAsync("config", "user.name", Ada.Name);
        await _repository.GitAsync("config", "user.email", Ada.Email);
        await _repository.GitAsync("remote", "add", "origin", "https://github.com/ada/engine.git");

        _handle = (await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_repository.Path, TestContext.Current.CancellationToken)).Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private IGitCredentialResolver Resolver => _host.GetRequiredService<IGitCredentialResolver>();

    private async Task<IdentityProfile> AddProfileAsync(GitIdentity identity)
        => await _host.GetRequiredService<IIdentityProfileStore>()
            .SaveAsync(IdentityProfile.Create("Work", identity), Cancellation);

    private async Task<HostAccount> ConnectAsync(IdentityProfile profile, HostKind kind, string baseUri, string token = Token)
        => await _host.GetRequiredService<IHostAccountService>().AddAsync(
            HostAccount.Create(kind, new Uri(baseUri), "ada", profileId: profile.Id),
            new SecretString(token),
            Cancellation);

    [Fact]
    public async Task WithoutProfiles_GitKeepsItsOwnCredentials()
        => Assert.Same(GitCredentials.None, await Resolver.ForRepositoryAsync(_handle, Cancellation));

    [Fact]
    public async Task AnHttpsRemote_SignsInWithTheProfilesIntegration()
    {
        IdentityProfile work = await AddProfileAsync(Ada);
        await ConnectAsync(work, HostKind.GitHub, "https://github.com");

        GitCredentials credentials = await Resolver.ForRepositoryAsync(_handle, Cancellation);

        GitHostCredential credential = Assert.Single(credentials.Credentials);
        Assert.Equal("https://github.com", credential.Origin);
        Assert.Equal("x-access-token", credential.UserName);
        Assert.Equal(Token, credential.Token.Reveal());
    }

    [Fact]
    public async Task AnIdentityNoProfileMatches_SignsNothingIn()
    {
        IdentityProfile other = await AddProfileAsync(new GitIdentity("Someone Else", "someone@example.com"));
        await ConnectAsync(other, HostKind.GitHub, "https://github.com");

        Assert.True((await Resolver.ForRepositoryAsync(_handle, Cancellation)).IsEmpty);
    }

    [Fact]
    public async Task AnSshRemote_SignsNothingIn()
    {
        IdentityProfile work = await AddProfileAsync(Ada);
        await ConnectAsync(work, HostKind.GitHub, "https://github.com");
        await _repository.GitAsync("remote", "set-url", "origin", "git@github.com:ada/engine.git");

        Assert.True((await Resolver.ForRepositoryAsync(_handle, Cancellation)).IsEmpty);
    }

    [Fact]
    public async Task APushUrlOnAnotherHost_GetsThatHostsIntegrationToo()
    {
        IdentityProfile work = await AddProfileAsync(Ada);
        await ConnectAsync(work, HostKind.GitHub, "https://github.com");
        await ConnectAsync(work, HostKind.GitLab, "https://gitlab.example.com", "glpat-another-token-value");
        await _repository.GitAsync("remote", "set-url", "--push", "origin", "https://gitlab.example.com/ada/engine.git");

        GitCredentials credentials = await Resolver.ForRepositoryAsync(_handle, Cancellation);

        Assert.Collection(
            credentials.Credentials,
            github => Assert.Equal(("https://github.com", "x-access-token", Token), (github.Origin, github.UserName, github.Token.Reveal())),
            gitlab => Assert.Equal(("https://gitlab.example.com", "oauth2", "glpat-another-token-value"), (gitlab.Origin, gitlab.UserName, gitlab.Token.Reveal())));
    }

    [Fact]
    public async Task AnUnreadableTokenStore_LeavesGitItsOwnCredentialsInsteadOfFailing()
    {
        IdentityProfile work = await AddProfileAsync(Ada);
        await ConnectAsync(work, HostKind.GitHub, "https://github.com");

        await File.WriteAllTextAsync(
            Path.Combine(_workspace.RootPath, "config", FileTokenStore.FileName), "{ not json", Cancellation);

        Assert.Same(GitCredentials.None, await Resolver.ForRepositoryAsync(_handle, Cancellation));
    }

    // ---------------------------------------------------------------- a token that cannot be read (BUG-3024)

    private string ConfigFile(string name) => Path.Combine(_workspace.RootPath, "config", name);

    [Fact]
    public async Task OneTokenThatCannotBeRead_LeavesGitItsOwnSignInThereOnly_AndSaysSo()
    {
        IdentityProfile work = await AddProfileAsync(Ada);
        HostAccount github = await ConnectAsync(work, HostKind.GitHub, "https://github.com");
        await ConnectAsync(work, HostKind.GitLab, "https://gitlab.example.com", "glpat-another-token-value");
        await _repository.GitAsync("remote", "set-url", "--push", "origin", "https://gitlab.example.com/ada/engine.git");

        // GitHub's entry damaged: authentication fails, the entry is unreadable.
        JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(ConfigFile(FileTokenStore.FileName), Cancellation))!;
        JsonNode entry = document["entries"]![github.TokenKey]!;
        byte[] tag = Convert.FromBase64String(entry["tag"]!.GetValue<string>());
        tag[0] ^= 0xFF;
        entry["tag"] = Convert.ToBase64String(tag);
        await File.WriteAllTextAsync(ConfigFile(FileTokenStore.FileName), document.ToJsonString(), Cancellation);

        List<TokenUnreadableEventArgs> unreadable = [];
        Resolver.TokenUnreadable += (_, e) => unreadable.Add(e);

        GitCredentials credentials = await Resolver.ForRepositoryAsync(_handle, Cancellation);

        // GitLab still signs in; GitHub is git's own again, and the event says why.
        Assert.Equal("https://gitlab.example.com", Assert.Single(credentials.Credentials).Origin);

        TokenUnreadableEventArgs said = Assert.Single(unreadable);
        Assert.Equal(github.Id, said.Account.Id);
        Assert.Equal("https://github.com", said.Origin);
        Assert.Equal(TokenProtectionReason.EntryUnreadable, said.Reason);
    }

    [Fact]
    public async Task AKeyThatCannotBeOpened_SaysSo_ForTheIntegrationGitWouldHaveUsed()
    {
        IdentityProfile work = await AddProfileAsync(Ada);
        await ConnectAsync(work, HostKind.GitHub, "https://github.com");

        // What a Windows password reset leaves behind: a data key this account cannot open.
        await File.WriteAllBytesAsync(ConfigFile(FileTokenStore.KeyFileName), [1, 2, 3], Cancellation);

        List<TokenUnreadableEventArgs> unreadable = [];
        Resolver.TokenUnreadable += (_, e) => unreadable.Add(e);

        Assert.Same(GitCredentials.None, await Resolver.ForRepositoryAsync(_handle, Cancellation));
        Assert.Equal(TokenProtectionReason.KeyUnavailable, Assert.Single(unreadable).Reason);
    }

    [Fact]
    public async Task AClone_WhoseTokenCannotBeRead_SaysSoToo()
    {
        IdentityProfile work = await AddProfileAsync(Ada);
        HostAccount picked = await ConnectAsync(work, HostKind.GitHub, "https://github.com");
        await File.WriteAllBytesAsync(ConfigFile(FileTokenStore.KeyFileName), [1, 2, 3], Cancellation);

        List<TokenUnreadableEventArgs> unreadable = [];
        Resolver.TokenUnreadable += (_, e) => unreadable.Add(e);

        Assert.Same(GitCredentials.None, await Resolver.ForCloneAsync("https://github.com/ada/engine.git", picked, Cancellation));
        Assert.Equal(picked.Id, Assert.Single(unreadable).Account.Id);
    }

    // ---------------------------------------------------------------- clone

    [Fact]
    public async Task AClonePickedFromAnIntegration_SignsInWithIt_WhateverTheIdentity()
    {
        IdentityProfile other = await AddProfileAsync(new GitIdentity("Someone Else", "someone@example.com"));
        HostAccount picked = await ConnectAsync(other, HostKind.GitHub, "https://github.com");

        GitCredentials credentials = await Resolver.ForCloneAsync("https://github.com/ada/engine.git", picked, Cancellation);

        Assert.Equal(Token, Assert.Single(credentials.Credentials).Token.Reveal());
    }

    [Fact]
    public async Task AClonePickedFromAnIntegration_OfAnotherHost_SignsNothingIn()
    {
        IdentityProfile work = await AddProfileAsync(Ada);
        HostAccount picked = await ConnectAsync(work, HostKind.GitHub, "https://github.com");

        Assert.True((await Resolver.ForCloneAsync("https://gitlab.com/ada/engine.git", picked, Cancellation)).IsEmpty);
    }

    [Fact]
    public async Task ACloneFromAUrl_SignsInWithTheCurrentProfilesIntegration()
    {
        await _repository.GitAsync("config", "--global", "user.name", Ada.Name);
        await _repository.GitAsync("config", "--global", "user.email", Ada.Email);

        IdentityProfile work = await AddProfileAsync(Ada);
        await ConnectAsync(work, HostKind.GitHub, "https://github.com");

        GitCredentials credentials = await Resolver.ForCloneAsync("https://github.com/ada/engine.git", null, Cancellation);
        GitCredentials ssh = await Resolver.ForCloneAsync("git@github.com:ada/engine.git", null, Cancellation);

        Assert.Equal("https://github.com", Assert.Single(credentials.Credentials).Origin);
        Assert.True(ssh.IsEmpty);
    }

    [Fact]
    public async Task ACloneFromAUrl_WhenNoProfileIsCurrent_SignsNothingIn()
    {
        // The repository's own identity is Ada; the global one, which a clone goes by, is nobody.
        IdentityProfile work = await AddProfileAsync(Ada);
        await ConnectAsync(work, HostKind.GitHub, "https://github.com");

        Assert.True((await Resolver.ForCloneAsync("https://github.com/ada/engine.git", null, Cancellation)).IsEmpty);
    }

    // ---------------------------------------------------------------- git's answer

    [Fact]
    public async Task WhatTheResolverGives_IsWhatGitAnswersWith()
    {
        IdentityProfile work = await AddProfileAsync(Ada);
        await ConnectAsync(work, HostKind.GitHub, "https://github.com");

        GitCredentials credentials = await Resolver.ForRepositoryAsync(_handle, Cancellation);

        GitCommand fill = _host.GetRequiredService<IGitCommandFactory>()
            .CreateWithInput(_repository.Path, "protocol=https\nhost=github.com\npath=ada/engine.git\n\n", ["credential", "fill"])
            .WithCredentials(credentials);

        GitResult result = await _host.GetRequiredService<IGitProcessRunner>()
            .RunAsync(fill, throwOnError: false, Cancellation);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Contains($"password={Token}\n", result.StandardOutput, StringComparison.Ordinal);
    }
}
