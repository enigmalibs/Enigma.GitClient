using System;
using Enigma.GitClient.Core.Hosting;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Hosting;

/// <summary>
/// When an account connected again is the one already listed (BUG-3024): the same login on the same
/// instance for the same profile, whatever its identifier and name.
/// </summary>
public sealed class HostAccountIdentityTests
{
    private static HostAccount Account(
        string userName = "ada",
        string uri = "https://github.com",
        HostKind kind = HostKind.GitHub,
        string? profileId = "work",
        string displayName = "Ada")
        => HostAccount.Create(kind, new Uri(uri), userName, displayName, profileId);

    [Fact]
    public void TheSameLogin_OnTheSameInstance_ForTheSameProfile_IsTheSameAccount()
        => Assert.True(Account().IsSameIdentityAs(Account(displayName: "Another name")));

    [Theory]
    [InlineData("ADA", "https://github.com")]
    [InlineData("ada", "https://GitHub.com/")]
    [InlineData("ada", "HTTPS://github.com")]
    public void CaseAndATrailingSlash_DoNotMakeAnotherAccount(string userName, string uri)
        => Assert.True(Account().IsSameIdentityAs(Account(userName, uri)));

    [Fact]
    public void AnInstancePath_IsPartOfIt()
    {
        HostAccount contoso = Account(uri: "https://dev.azure.com/contoso", kind: HostKind.AzureDevOps);

        Assert.True(contoso.IsSameIdentityAs(Account(uri: "https://dev.azure.com/contoso/", kind: HostKind.AzureDevOps)));
        Assert.False(contoso.IsSameIdentityAs(Account(uri: "https://dev.azure.com/fabrikam", kind: HostKind.AzureDevOps)));
    }

    [Fact]
    public void AnotherLogin_IsAnotherAccount()
        => Assert.False(Account().IsSameIdentityAs(Account(userName: "grace")));

    [Fact]
    public void AnotherInstance_IsAnotherAccount()
    {
        Assert.False(Account().IsSameIdentityAs(Account(uri: "https://github.example.com")));
        Assert.False(Account().IsSameIdentityAs(Account(uri: "https://github.com:8443")));
    }

    [Fact]
    public void AnotherProfile_IsAnotherAccount()
    {
        Assert.False(Account().IsSameIdentityAs(Account(profileId: "home")));
        Assert.False(Account().IsSameIdentityAs(Account(profileId: null)));
    }

    [Fact]
    public void AnotherKindOfHost_IsAnotherAccount()
        => Assert.False(Account().IsSameIdentityAs(Account(kind: HostKind.GitLab)));
}
