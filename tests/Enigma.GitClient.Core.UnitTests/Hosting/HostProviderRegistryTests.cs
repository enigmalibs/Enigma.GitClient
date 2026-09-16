using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Security;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Hosting;

/// <summary>
/// A provider that answers only the questions the registry asks it. The real ones arrive with their
/// own phases; what is being tested here is the resolution, not any host's API.
/// </summary>
internal sealed class StubProvider : IRepositoryHostProvider
{
    public StubProvider(HostKind kind, string? extraHost = null)
    {
        Kind = kind;
        ExtraHost = extraHost;
        DefaultBaseUri = new Uri("https://" + kind.ToString().ToLowerInvariant() + ".example");
    }

    public HostKind Kind { get; }

    public string DisplayName => Kind.ToString();

    public Uri DefaultBaseUri { get; }

    public string TokenScopeHint => "a scope";

    /// <summary>A host this provider claims beyond the well-known table.</summary>
    public string? ExtraHost { get; }

    public bool MatchesRemote(RemoteUrl remote)
        => ExtraHost is not null && remote.IsHost(ExtraHost);

    public Task<HostIdentity> ValidateCredentialAsync(
        HostAccount account,
        SecretString token,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new HostIdentity("stub", "Stub"));

    public Task<HostRepositoryPage> ListRepositoriesAsync(
        HostAccount account,
        SecretString token,
        HostRepositoryQuery query,
        CancellationToken cancellationToken = default)
        => Task.FromResult(HostRepositoryPage.Empty);

    public string? BuildCommitUrl(RemoteUrl remote, string sha) => null;

    public string? BuildBranchUrl(RemoteUrl remote, string branch) => null;

    public string? BuildFileUrl(RemoteUrl remote, string reference, string path, int? line = null) => null;
}

/// <summary>
/// Resolves a remote address to the host that handles it, and to the account that can speak to it.
/// </summary>
public sealed class HostProviderRegistryTests
{
    private static HostProviderRegistry Build(params IRepositoryHostProvider[] providers)
        => new(providers);

    private static HostProviderRegistry All()
        => Build(
            new StubProvider(HostKind.GitHub),
            new StubProvider(HostKind.GitLab),
            new StubProvider(HostKind.AzureDevOps));

    private static HostAccount Account(HostKind kind, string baseUri)
        => HostAccount.Create(kind, new Uri(baseUri), "someone");

    [Theory]
    [InlineData("https://github.com/owner/repo.git", HostKind.GitHub)]
    [InlineData("git@github.com:owner/repo.git", HostKind.GitHub)]
    [InlineData("https://gitlab.com/group/repo.git", HostKind.GitLab)]
    [InlineData("git@gitlab.com:group/repo.git", HostKind.GitLab)]
    [InlineData("https://dev.azure.com/contoso/project/_git/repo", HostKind.AzureDevOps)]
    [InlineData("https://contoso.visualstudio.com/project/_git/repo", HostKind.AzureDevOps)]
    public void Match_ResolvesThePublicInstancesWithNoAccountAtAll(string remote, HostKind expected)
    {
        HostMatch match = Assert.IsType<HostMatch>(All().Match(remote));

        Assert.Equal(expected, match.Provider.Kind);

        // Enough to build a deep link, not enough to call an API, and the match says which.
        Assert.False(match.HasAccount);
    }

    [Theory]
    [InlineData("https://bitbucket.org/owner/repo.git")]
    [InlineData("https://git.example.com/owner/repo.git")]
    [InlineData("/srv/git/repo.git")]
    [InlineData(null)]
    public void Match_SaysNothingForAHostNobodyClaims(string? remote)
        => Assert.Null(All().Match(remote));

    [Fact]
    public void Match_RecognisesASelfHostedInstanceThroughItsAccount()
    {
        HostAccount account = Account(HostKind.GitLab, "https://git.example.com");

        HostMatch match = Assert.IsType<HostMatch>(
            All().Match("https://git.example.com/group/repo.git", [account]));

        Assert.Equal(HostKind.GitLab, match.Provider.Kind);
        Assert.Same(account, match.Account);
    }

    [Fact]
    public void Match_RecognisesAGitHubEnterpriseServerThroughItsAccount()
    {
        HostAccount account = Account(HostKind.GitHub, "https://github.contoso.com");

        HostMatch match = Assert.IsType<HostMatch>(
            All().Match("git@github.contoso.com:team/repo.git", [account]));

        Assert.Equal(HostKind.GitHub, match.Provider.Kind);
        Assert.Same(account, match.Account);
    }

    [Fact]
    public void Match_CarriesThePublicAccountWhenThereIsOneForThatInstance()
    {
        HostAccount account = Account(HostKind.GitHub, "https://github.com");

        HostMatch match = Assert.IsType<HostMatch>(
            All().Match("https://github.com/owner/repo.git", [account]));

        Assert.Same(account, match.Account);
    }

    [Fact]
    public void Match_NeverLendsOneInstancesAccountToAnother()
    {
        HostAccount enterprise = Account(HostKind.GitHub, "https://github.contoso.com");

        HostMatch match = Assert.IsType<HostMatch>(
            All().Match("https://github.com/owner/repo.git", [enterprise]));

        // The host is recognised, but an enterprise token is not a github.com token.
        Assert.Equal(HostKind.GitHub, match.Provider.Kind);
        Assert.Null(match.Account);
    }

    [Fact]
    public void Match_FindsAnInstancesSshEndpoint()
    {
        HostAccount account = Account(HostKind.GitLab, "https://git.example.com");

        HostMatch match = Assert.IsType<HostMatch>(
            All().Match("git@ssh.git.example.com:group/repo.git", [account]));

        Assert.Same(account, match.Account);
    }

    [Fact]
    public void Match_LetsAProviderClaimAHostTheTableDoesNotKnow()
    {
        HostProviderRegistry registry = Build(new StubProvider(HostKind.GitLab, extraHost: "code.example.org"));

        HostMatch match = Assert.IsType<HostMatch>(registry.Match("https://code.example.org/g/r.git"));

        Assert.Equal(HostKind.GitLab, match.Provider.Kind);
    }

    [Fact]
    public void Match_SaysNothingWhenThePublicHostHasNoProviderRegistered()
    {
        // A build with only the GitLab provider recognises gitlab.com and nothing else.
        HostProviderRegistry registry = Build(new StubProvider(HostKind.GitLab));

        Assert.Null(registry.Match("https://github.com/owner/repo.git"));
        Assert.NotNull(registry.Match("https://gitlab.com/group/repo.git"));
    }

    [Fact]
    public void Find_ReturnsTheProviderForAKindAndNothingForOneItDoesNotHave()
    {
        HostProviderRegistry registry = Build(new StubProvider(HostKind.GitHub));

        Assert.NotNull(registry.Find(HostKind.GitHub));
        Assert.Null(registry.Find(HostKind.GitLab));
        Assert.Null(registry.Find(HostKind.Unknown));
    }

    [Fact]
    public void Providers_ListsWhatWasRegistered()
    {
        IReadOnlyList<IRepositoryHostProvider> providers = All().Providers;

        Assert.Equal(3, providers.Count);
    }

    // ---------------------------------------------------------------- accounts

    [Fact]
    public void AnAccountKnowsWhichRemotesAreItsOwn()
    {
        HostAccount account = Account(HostKind.GitHub, "https://github.com");

        Assert.True(account.Owns(RemoteUrl.Parse("https://github.com/owner/repo.git")));
        Assert.True(account.Owns(RemoteUrl.Parse("git@github.com:owner/repo.git")));
        Assert.True(account.Owns(RemoteUrl.Parse("ssh://git@ssh.github.com/owner/repo.git")));
        Assert.False(account.Owns(RemoteUrl.Parse("https://gitlab.com/owner/repo.git")));
    }

    [Fact]
    public void AnAccountsTokenKeyIsDerivedFromItsIdAndNothingElse()
    {
        HostAccount account = Account(HostKind.GitHub, "https://github.com");

        Assert.Equal("host:" + account.Id, account.TokenKey);

        // Two accounts on the same instance are two accounts, with two keys.
        Assert.NotEqual(account.TokenKey, Account(HostKind.GitHub, "https://github.com").TokenKey);
    }

    [Fact]
    public void AnAccountNamesItselfAfterTheInstanceWhenNobodyNamesIt()
    {
        HostAccount account = HostAccount.Create(HostKind.GitLab, new Uri("https://git.example.com"), "someone");

        Assert.Equal("git.example.com", account.DisplayName);
        Assert.Equal("git.example.com", account.Host);
    }
}
