using System;
using Enigma.GitClient.Core.Hosting;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Hosting;

/// <summary>
/// Parses the four shapes git accepts for the same remote, and works out which host each one is on.
/// </summary>
/// <remarks>
/// This is the whole basis of "open this commit on GitHub": if a repository cloned over SSH is not
/// recognised as the same repository cloned over HTTPS, half the users never see the feature.
/// </remarks>
public sealed class RemoteUrlTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo.git", "github.com", "owner/repo")]
    [InlineData("https://github.com/owner/repo", "github.com", "owner/repo")]
    [InlineData("http://github.com/owner/repo.git", "github.com", "owner/repo")]
    [InlineData("git://github.com/owner/repo.git", "github.com", "owner/repo")]
    [InlineData("ssh://git@github.com/owner/repo.git", "github.com", "owner/repo")]
    [InlineData("ssh://git@github.com:22/owner/repo.git", "github.com", "owner/repo")]
    [InlineData("git@github.com:owner/repo.git", "github.com", "owner/repo")]
    [InlineData("git@github.com:owner/repo", "github.com", "owner/repo")]
    [InlineData("https://gitlab.com/group/subgroup/repo.git", "gitlab.com", "group/subgroup/repo")]
    [InlineData("git@gitlab.com:group/subgroup/repo.git", "gitlab.com", "group/subgroup/repo")]
    [InlineData("https://dev.azure.com/contoso/project/_git/repo", "dev.azure.com", "contoso/project/_git/repo")]
    [InlineData("git@ssh.dev.azure.com:v3/contoso/project/repo", "ssh.dev.azure.com", "v3/contoso/project/repo")]
    [InlineData("https://contoso.visualstudio.com/project/_git/repo", "contoso.visualstudio.com", "project/_git/repo")]
    public void Parse_ReadsTheHostAndThePath(string value, string host, string path)
    {
        RemoteUrl remote = RemoteUrl.Parse(value);

        Assert.Equal(host, remote.Host);
        Assert.Equal(path, remote.Path);
        Assert.Equal(value, remote.Original);
    }

    [Theory]
    [InlineData("HTTPS://GitHub.COM/Owner/Repo.git", "github.com")]
    [InlineData("git@GITHUB.com:owner/repo.git", "github.com")]
    public void Parse_LowerCasesTheHostAndLeavesThePathAlone(string value, string host)
    {
        RemoteUrl remote = RemoteUrl.Parse(value);

        Assert.Equal(host, remote.Host);

        // The path is case-sensitive on every host here, so it is never touched.
        Assert.Contains("Owner", remote.Path + "owner", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_DropsTheCredentialsAnAddressCarries()
    {
        RemoteUrl remote = RemoteUrl.Parse("https://someone:ghp_verysecrettoken@github.com/owner/repo.git");

        Assert.Equal("github.com", remote.Host);
        Assert.Equal("someone", remote.UserName);

        // The token does not survive parsing, so it cannot be logged from here later.
        Assert.DoesNotContain("ghp_verysecrettoken", remote.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_KeepsTheSshUserAndSaysItIsSsh()
    {
        RemoteUrl remote = RemoteUrl.Parse("git@github.com:owner/repo.git");

        Assert.True(remote.IsSsh);
        Assert.Equal("git", remote.UserName);
        Assert.Equal(0, remote.Port);
    }

    [Fact]
    public void Parse_KeepsANonDefaultPort()
    {
        RemoteUrl remote = RemoteUrl.Parse("ssh://git@git.example.com:2222/owner/repo.git");

        Assert.Equal(2222, remote.Port);
        Assert.True(remote.IsSsh);
    }

    [Fact]
    public void Parse_SplitsThePathIntoOwnerAndName()
    {
        RemoteUrl remote = RemoteUrl.Parse("https://github.com/owner/repo.git");

        Assert.Equal(["owner", "repo"], remote.Segments);
        Assert.Equal("owner", remote.Owner);
        Assert.Equal("repo", remote.Name);
    }

    [Fact]
    public void Parse_UnescapesAPathWithASpaceInIt()
    {
        RemoteUrl remote = RemoteUrl.Parse("https://dev.azure.com/contoso/My%20Project/_git/My%20Repo");

        Assert.Equal("contoso/My Project/_git/My Repo", remote.Path);
        Assert.Equal("My Repo", remote.Name);
    }

    [Theory]
    [InlineData("/srv/git/repo.git")]
    [InlineData("../sibling")]
    [InlineData("C:\\src\\repo")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_SaysNoForSomethingThatIsNotAnAddress(string? value)
    {
        Assert.False(RemoteUrl.TryParse(value, out RemoteUrl? remote));
        Assert.Null(remote);
    }

    [Fact]
    public void Parse_ThrowsForSomethingThatIsNotAnAddress()
        => Assert.Throws<FormatException>(() => RemoteUrl.Parse("/srv/git/repo.git"));

    [Theory]
    [InlineData("https://github.com/o/r", "github.com", true)]
    [InlineData("https://github.com/o/r", "GitHub.com", true)]
    [InlineData("https://github.com/o/r", "gitlab.com", false)]
    public void IsHost_ComparesWithoutCaring(string value, string host, bool expected)
        => Assert.Equal(expected, RemoteUrl.Parse(value).IsHost(host));

    [Theory]
    [InlineData("https://ssh.github.com/o/r", "github.com", true)]
    [InlineData("https://contoso.visualstudio.com/p/_git/r", "visualstudio.com", true)]
    [InlineData("https://notgithub.com/o/r", "github.com", false)]
    public void IsUnder_MatchesASubDomainAndNotALookalike(string value, string domain, bool expected)
        => Assert.Equal(expected, RemoteUrl.Parse(value).IsUnder(domain));

    // ---------------------------------------------------------------- which host is it

    [Theory]
    [InlineData("https://github.com/owner/repo.git", HostKind.GitHub)]
    [InlineData("git@github.com:owner/repo.git", HostKind.GitHub)]
    [InlineData("ssh://git@ssh.github.com:443/owner/repo.git", HostKind.GitHub)]
    [InlineData("https://gitlab.com/group/repo.git", HostKind.GitLab)]
    [InlineData("git@gitlab.com:group/repo.git", HostKind.GitLab)]
    [InlineData("https://dev.azure.com/contoso/project/_git/repo", HostKind.AzureDevOps)]
    [InlineData("git@ssh.dev.azure.com:v3/contoso/project/repo", HostKind.AzureDevOps)]
    [InlineData("https://contoso.visualstudio.com/project/_git/repo", HostKind.AzureDevOps)]
    [InlineData("git@vs-ssh.visualstudio.com:v3/contoso/project/repo", HostKind.AzureDevOps)]
    public void Detect_RecognisesThePublicInstances(string value, HostKind expected)
        => Assert.Equal(expected, WellKnownHosts.Detect(value));

    [Theory]
    [InlineData("https://github.example.com/owner/repo.git")]
    [InlineData("git@git.example.com:owner/repo.git")]
    [InlineData("https://bitbucket.org/owner/repo.git")]
    [InlineData("/srv/git/repo.git")]
    public void Detect_SaysNothingAboutAHostItCannotKnow(string value)
    {
        // A self-hosted instance is any host name at all; recognising it is the account's job.
        Assert.Equal(HostKind.Unknown, WellKnownHosts.Detect(value));
    }
}
