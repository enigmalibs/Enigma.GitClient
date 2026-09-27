using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Identity;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Hosting;

/// <summary>
/// Which of a profile's integrations git signs in with, for which origin.
/// </summary>
public sealed class ProfileCredentialRuleTests
{
    private static readonly IdentityProfile Work = IdentityProfile.Create("Work", new GitIdentity("Ada Lovelace", "ada@work.example"));
    private static readonly IdentityProfile Home = IdentityProfile.Create("Home", new GitIdentity("Ada", "ada@home.example"));

    private static HostAccount Account(IdentityProfile? profile, string baseUri = "https://github.com", HostKind kind = HostKind.GitHub)
        => HostAccount.Create(kind, new Uri(baseUri), "ada", profileId: profile?.Id);

    private static IReadOnlyList<HostSignIn> Select(IReadOnlyList<HostAccount> accounts, params string[] urls)
        => ProfileCredentialRule.Select(Work, accounts, urls);

    [Fact]
    public void AProfileWithoutIntegrations_SignsNothingIn()
        => Assert.Empty(Select([], "https://github.com/owner/repo.git"));

    [Fact]
    public void AnHttpsRemoteOwnedByTheProfilesIntegration_SignsInWithIt()
    {
        HostAccount github = Account(Work);

        HostSignIn signIn = Assert.Single(Select([github], "https://github.com/owner/repo.git"));

        Assert.Equal("https://github.com", signIn.Origin);
        Assert.Same(github, signIn.Account);
    }

    [Theory]
    [InlineData("git@github.com:owner/repo.git")]
    [InlineData("ssh://git@github.com/owner/repo.git")]
    [InlineData("ssh://git@ssh.github.com:443/owner/repo.git")]
    [InlineData("/srv/git/repo.git")]
    [InlineData("")]
    public void AnAddressATokenCannotSignIn_GetsNothing(string url)
        => Assert.Empty(Select([Account(Work)], url));

    [Fact]
    public void AnotherProfilesIntegration_DoesNotCount()
        => Assert.Empty(Select([Account(Home)], "https://github.com/owner/repo.git"));

    [Fact]
    public void AnEarlierIntegrationThatBelongsToNoProfile_DoesNotCount()
        => Assert.Empty(Select([Account(null)], "https://github.com/owner/repo.git"));

    [Fact]
    public void AnIntegrationOnAnotherInstance_DoesNotCount()
        => Assert.Empty(Select([Account(Work, "https://github.contoso.com")], "https://github.com/owner/repo.git"));

    [Fact]
    public void TwoIntegrationsOnOneOrigin_TheFirstAnswers()
    {
        HostAccount first = Account(Work);
        HostAccount second = Account(Work);

        HostSignIn signIn = Assert.Single(Select([first, second], "https://github.com/a/one.git", "https://github.com/b/two.git"));

        Assert.Same(first, signIn.Account);
    }

    [Fact]
    public void TwoHosts_EachGetTheirOwnIntegration_InTheOrderOfTheRemotes()
    {
        HostAccount github = Account(Work);
        HostAccount gitlab = Account(Work, "https://gitlab.example.com", HostKind.GitLab);

        IReadOnlyList<HostSignIn> signIns = Select(
            [github, gitlab],
            "https://gitlab.example.com/group/repo.git",
            "git@github.com:owner/repo.git",
            "https://github.com/owner/repo.git");

        Assert.Collection(
            signIns,
            signIn => Assert.Equal(("https://gitlab.example.com", gitlab), (signIn.Origin, signIn.Account)),
            signIn => Assert.Equal(("https://github.com", github), (signIn.Origin, signIn.Account)));
    }

    [Fact]
    public void TheFetchAndPushAddressesOfOneRemote_GiveOneLogin()
        => Assert.Single(Select([Account(Work)], "https://github.com/owner/repo.git", "https://github.com/owner/repo.git"));

    [Fact]
    public void AnHttpsIntegrationsToken_NeverGoesToAnHttpAddress()
        => Assert.Empty(Select([Account(Work, "https://git.example.com", HostKind.GitLab)], "http://git.example.com/group/repo.git"));

    [Fact]
    public void AnHttpIntegration_SignsInToItsHttpInstance()
    {
        HostAccount server = Account(Work, "http://tfs.example.com/tfs/DefaultCollection", HostKind.AzureDevOps);

        HostSignIn signIn = Assert.Single(Select([server], "http://tfs.example.com/tfs/DefaultCollection/_git/repo"));

        Assert.Equal("http://tfs.example.com", signIn.Origin);
    }

    [Fact]
    public void TheOriginIsTheRemotesOwn_PortIncluded()
    {
        HostSignIn signIn = Assert.Single(Select(
            [Account(Work, "https://git.example.com", HostKind.GitLab)],
            "https://someone@git.example.com:8443/group/repo.git"));

        Assert.Equal("https://git.example.com:8443", signIn.Origin);
    }

    [Fact]
    public void AnAzureDevOpsOrganisation_SignsInToDevAzureCom()
    {
        HostSignIn signIn = Assert.Single(Select(
            [Account(Work, "https://dev.azure.com/contoso", HostKind.AzureDevOps)],
            "https://contoso@dev.azure.com/contoso/project/_git/repo"));

        Assert.Equal("https://dev.azure.com", signIn.Origin);
    }

    [Fact]
    public void ForAccount_AnswersForOneIntegrationRegardlessOfProfile()
    {
        HostAccount earlier = Account(null);

        Assert.Equal("https://github.com", ProfileCredentialRule.ForAccount(earlier, "https://github.com/owner/repo.git")?.Origin);
        Assert.Null(ProfileCredentialRule.ForAccount(earlier, "https://gitlab.com/group/repo.git"));
    }

    [Fact]
    public void FirstMatching_IsTheFirstProfileWithTheIdentity()
    {
        IdentityProfile twin = IdentityProfile.Create("Work again", Work.Identity);

        Assert.Same(Work, IdentityProfile.FirstMatching([Home, Work, twin], new GitIdentity("Ada Lovelace", "ADA@work.example")));
        Assert.Null(IdentityProfile.FirstMatching([Home, Work], new GitIdentity("Someone", "someone@example.com")));
    }
}
