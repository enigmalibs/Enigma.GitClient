using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Identity;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Hosting;

/// <summary>
/// Which pushes a profile allows: only where one of its integrations leads.
/// </summary>
public sealed class ProfilePushRuleTests
{
    private static readonly GitIdentity WorkIdentity = new("Ada Lovelace", "ada@work.example");
    private static readonly GitIdentity HomeIdentity = new("Ada", "ada@home.example");

    private static readonly IdentityProfile Work = IdentityProfile.Create("Work", WorkIdentity);
    private static readonly IdentityProfile Home = IdentityProfile.Create("Home", HomeIdentity);

    private static readonly IReadOnlyList<IdentityProfile> Profiles = [Work, Home];

    private static HostAccount GitHubFor(IdentityProfile? profile, string uri = "https://github.com")
        => HostAccount.Create(HostKind.GitHub, new Uri(uri), "ada", "GitHub", profile?.Id);

    [Fact]
    public void WithoutAMatchingProfileNothingDecides()
    {
        PushPermission permission = ProfilePushRule.Evaluate(
            Profiles,
            [],
            new GitIdentity("Someone Else", "someone@example.com"),
            "https://github.com/contoso/project.git");

        Assert.True(permission.IsAllowed);
        Assert.Equal(PushPermissionReason.NoProfile, permission.Reason);
        Assert.Null(permission.Profile);
    }

    [Fact]
    public void WithoutAnyProfileNothingDecides()
    {
        PushPermission permission = ProfilePushRule.Evaluate([], [], WorkIdentity, "/srv/git/project.git");

        Assert.True(permission.IsAllowed);
        Assert.Equal(PushPermissionReason.NoProfile, permission.Reason);
    }

    [Fact]
    public void AProfileWithoutIntegrationsNeverPushes()
    {
        PushPermission permission = ProfilePushRule.Evaluate(
            Profiles,
            [GitHubFor(Home)],
            WorkIdentity,
            "https://github.com/contoso/project.git");

        Assert.False(permission.IsAllowed);
        Assert.Equal(PushPermissionReason.NoIntegration, permission.Reason);
        Assert.Same(Work, permission.Profile);
        Assert.Equal("github.com", permission.Target);
    }

    [Theory]
    [InlineData("https://github.com/contoso/project.git")]
    [InlineData("git@github.com:contoso/project.git")]
    [InlineData("ssh://git@ssh.github.com:443/contoso/project.git")]
    public void AnIntegrationOnTheRemotesHostAllowsThePushWhateverTheAddressShape(string pushUrl)
    {
        HostAccount work = GitHubFor(Work);

        PushPermission permission = ProfilePushRule.Evaluate(Profiles, [work], WorkIdentity, pushUrl);

        Assert.True(permission.IsAllowed);
        Assert.Equal(PushPermissionReason.Integration, permission.Reason);
        Assert.Same(Work, permission.Profile);
        Assert.Same(work, permission.Account);
    }

    [Fact]
    public void AnIntegrationOnAnotherHostDoesNotReachThisOne()
    {
        PushPermission permission = ProfilePushRule.Evaluate(
            Profiles,
            [HostAccount.Create(HostKind.GitLab, new Uri("https://gitlab.com"), "ada", "GitLab", Work.Id)],
            WorkIdentity,
            "https://github.com/contoso/project.git");

        Assert.False(permission.IsAllowed);
        Assert.Equal("github.com", permission.Target);
    }

    [Fact]
    public void AnEnterpriseIntegrationIsNotAPublicOne()
    {
        PushPermission permission = ProfilePushRule.Evaluate(
            Profiles,
            [GitHubFor(Work, "https://github.example.com")],
            WorkIdentity,
            "https://github.com/contoso/project.git");

        Assert.False(permission.IsAllowed);
    }

    [Fact]
    public void AnEarlierIntegrationThatBelongsToNoProfileCountsForNone()
    {
        PushPermission permission = ProfilePushRule.Evaluate(
            Profiles,
            [GitHubFor(null)],
            WorkIdentity,
            "https://github.com/contoso/project.git");

        Assert.False(permission.IsAllowed);
    }

    [Fact]
    public void AnotherProfilesIntegrationDoesNotCount()
    {
        PushPermission permission = ProfilePushRule.Evaluate(
            Profiles,
            [GitHubFor(Home)],
            WorkIdentity,
            "git@github.com:contoso/project.git");

        Assert.False(permission.IsAllowed);
        Assert.Same(Work, permission.Profile);
    }

    [Fact]
    public void APathOnThisMachineIsNoHostAnIntegrationCouldOwn()
    {
        PushPermission permission = ProfilePushRule.Evaluate(
            Profiles,
            [GitHubFor(Work)],
            WorkIdentity,
            "/srv/git/project.git");

        Assert.False(permission.IsAllowed);
        Assert.Equal("/srv/git/project.git", permission.Target);
    }

    [Fact]
    public void AnEmailMatchesWhateverItsCase()
    {
        PushPermission permission = ProfilePushRule.Evaluate(
            Profiles,
            [],
            new GitIdentity("Ada Lovelace", "ADA@Work.Example"),
            "https://github.com/contoso/project.git");

        Assert.Same(Work, permission.Profile);
        Assert.False(permission.IsAllowed);
    }

    [Fact]
    public void TheFirstMatchingProfileDecides()
    {
        IdentityProfile twin = IdentityProfile.Create("Work again", WorkIdentity);

        PushPermission permission = ProfilePushRule.Evaluate(
            [Work, twin],
            [GitHubFor(twin)],
            WorkIdentity,
            "https://github.com/contoso/project.git");

        // The page marks the first one current, and the rule follows the same one.
        Assert.Same(Work, permission.Profile);
        Assert.False(permission.IsAllowed);
    }

    [Fact]
    public void ARefusalNeverCarriesACredential()
    {
        PushPermission permission = ProfilePushRule.Evaluate(
            Profiles,
            [],
            WorkIdentity,
            "https://ada:ghp_secret@github.com/contoso/project.git");

        Assert.DoesNotContain("ghp_secret", permission.Target, StringComparison.Ordinal);
    }
}
