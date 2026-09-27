using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Security;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Git;

public sealed class GitCredentialsTests
{
    private const string Token = "ghp_0123456789abcdefghijklmnopqrstuvwxyz";

    private static GitHostCredential Credential(string location, string userName = "x-access-token", string token = Token)
    {
        Assert.True(GitHostCredential.TryCreate(new Uri(location), userName, new SecretString(token), out GitHostCredential? credential));
        return credential!;
    }

    private static GitCommand Fetch() => new GitCommandFactory().Create("/repo", "fetch", "--all");

    // ---------------------------------------------------------------- origins

    [Theory]
    [InlineData("https://github.com", "https://github.com")]
    [InlineData("https://GitHub.com/owner/repo.git", "https://github.com")]
    [InlineData("https://someone@github.com/owner/repo.git", "https://github.com")]
    [InlineData("https://github.com:443/owner/repo.git", "https://github.com")]
    [InlineData("https://git.example.com:8443/scm/repo.git", "https://git.example.com:8443")]
    [InlineData("http://tfs.example.com/tfs/DefaultCollection", "http://tfs.example.com")]
    [InlineData("https://dev.azure.com/contoso", "https://dev.azure.com")]
    [InlineData("https://[::1]:3000/repo.git", "https://[::1]:3000")]
    public void TryGetOrigin_KeepsTheSchemeTheHostAndAPortThatIsNotTheDefault(string location, string expected)
    {
        Assert.True(GitHostCredential.TryGetOrigin(new Uri(location), out string? origin));
        Assert.Equal(expected, origin);
    }

    [Theory]
    [InlineData("ssh://git@github.com/owner/repo.git")]
    [InlineData("git://github.com/owner/repo.git")]
    [InlineData("file:///srv/git/repo.git")]
    public void TryGetOrigin_RefusesWhatIsNotHttp(string location)
        => Assert.False(GitHostCredential.TryGetOrigin(new Uri(location), out _));

    [Fact]
    public void TryGetOrigin_RefusesAHostGitsConfigurationCouldNotName()
        => Assert.False(GitHostCredential.TryGetOrigin(new Uri("https://a_b.example.com/repo.git"), out _));

    // ---------------------------------------------------------------- what is refused

    [Theory]
    [InlineData("ghp_token\npassword=forged")]
    [InlineData("ghp_token\r")]
    [InlineData("ghp_\0token")]
    [InlineData("")]
    public void TryCreate_RefusesATokenThatCouldForgeAProtocolLine(string token)
        => Assert.False(GitHostCredential.TryCreate(new Uri("https://github.com"), "x-access-token", new SecretString(token), out _));

    [Theory]
    [InlineData("")]
    [InlineData("user\nname")]
    public void TryCreate_RefusesAnEmptyOrMultiLineUserName(string userName)
        => Assert.False(GitHostCredential.TryCreate(new Uri("https://github.com"), userName, new SecretString(Token), out _));

    [Fact]
    public void ALoginNeverPrintsItsToken()
    {
        GitHostCredential credential = Credential("https://github.com");

        Assert.DoesNotContain(Token, credential.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Token, GitCredentials.For([credential]).ToString(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the configuration

    [Fact]
    public void BuildConfigArguments_ResetsTheHelperListBeforeAddingItsOwn_ScopedToTheOrigin()
    {
        GitCredentials credentials = GitCredentials.For([Credential("https://github.com")]);

        IReadOnlyList<string> arguments = credentials.BuildConfigArguments();

        Assert.Equal(4, arguments.Count);
        Assert.Equal("-c", arguments[0]);
        Assert.Equal("credential.https://github.com.helper=", arguments[1]);
        Assert.Equal("-c", arguments[2]);
        Assert.StartsWith("credential.https://github.com.helper=!", arguments[3], StringComparison.Ordinal);
    }

    [Fact]
    public void TheHelperAnswersOnlyGet_FromTheEnvironment()
    {
        string helper = GitCredentials.For([Credential("https://github.com")]).BuildConfigArguments()[3];

        Assert.Contains("test \"$1\" = get || exit 0", helper, StringComparison.Ordinal);
        Assert.Contains("\"$ENIGMA_GIT_USERNAME_0\" \"$ENIGMA_GIT_PASSWORD_0\"", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoOrigins_GetTheirOwnHelperAndTheirOwnVariables()
    {
        GitCredentials credentials = GitCredentials.For(
        [
            Credential("https://github.com"),
            Credential("https://gitlab.example.com", "oauth2", "glpat-other-token-value"),
        ]);

        IReadOnlyList<string> arguments = credentials.BuildConfigArguments();
        IReadOnlyDictionary<string, string> environment = credentials.BuildEnvironment();

        Assert.Equal(8, arguments.Count);
        Assert.Contains("ENIGMA_GIT_PASSWORD_0", arguments[3], StringComparison.Ordinal);
        Assert.Equal("credential.https://gitlab.example.com.helper=", arguments[5]);
        Assert.Contains("ENIGMA_GIT_PASSWORD_1", arguments[7], StringComparison.Ordinal);

        Assert.Equal("x-access-token", environment["ENIGMA_GIT_USERNAME_0"]);
        Assert.Equal(Token, environment["ENIGMA_GIT_PASSWORD_0"]);
        Assert.Equal("oauth2", environment["ENIGMA_GIT_USERNAME_1"]);
        Assert.Equal("glpat-other-token-value", environment["ENIGMA_GIT_PASSWORD_1"]);
    }

    [Fact]
    public void For_KeepsTheFirstLoginOfAnOrigin()
    {
        GitCredentials credentials = GitCredentials.For(
        [
            Credential("https://github.com/first"),
            Credential("https://github.com/second", token: "ghp_second-token-value"),
        ]);

        GitHostCredential kept = Assert.Single(credentials.Credentials);
        Assert.Equal(Token, kept.Token.Reveal());
    }

    [Fact]
    public void For_NothingGivesNone()
    {
        Assert.Same(GitCredentials.None, GitCredentials.For([]));
        Assert.True(GitCredentials.None.IsEmpty);
        Assert.Empty(GitCredentials.None.BuildConfigArguments());
        Assert.Empty(GitCredentials.None.BuildEnvironment());
    }

    // ---------------------------------------------------------------- on a command

    [Fact]
    public void WithCredentials_PutsTheConfigurationFirstAndKeepsTheVerb()
    {
        GitCommand command = Fetch().WithCredentials(GitCredentials.For([Credential("https://github.com")]));

        Assert.Equal("fetch", command.Verb);
        Assert.Equal("-c", command.Arguments[0]);
        Assert.Equal("credential.https://github.com.helper=", command.Arguments[1]);
        Assert.Equal(Fetch().Arguments, command.Arguments.Skip(4));
    }

    [Fact]
    public void WithCredentials_TheTokenIsInTheEnvironmentAndNowhereInTheArguments()
    {
        GitCommand command = Fetch().WithCredentials(GitCredentials.For([Credential("https://github.com")]));

        Assert.Equal(Token, command.Environment["ENIGMA_GIT_PASSWORD_0"]);
        Assert.DoesNotContain(command.Arguments, argument => argument.Contains(Token, StringComparison.Ordinal));
        Assert.DoesNotContain(Token, command.ToString(), StringComparison.Ordinal);

        GitCommandException failure = new(command, 128, "fatal: Authentication failed", string.Empty);
        Assert.DoesNotContain(Token, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(failure.RedactedArguments, argument => argument.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public void WithCredentials_KeepsTheCommandsOwnEnvironmentAndInput()
    {
        GitCommand original = new("/repo", ["credential", "fill"], new Dictionary<string, string> { ["GIT_TRACE"] = "0" }, "protocol=https\n");

        GitCommand command = original.WithCredentials(GitCredentials.For([Credential("https://github.com")]));

        Assert.Equal("0", command.Environment["GIT_TRACE"]);
        Assert.Equal("protocol=https\n", command.StandardInput);
        Assert.Equal("/repo", command.WorkingDirectory);
    }

    [Fact]
    public void WithCredentials_NoneLeavesTheCommandAsItIs()
    {
        GitCommand command = Fetch();

        Assert.Same(command, command.WithCredentials(GitCredentials.None));
    }

    [Fact]
    public void WithCredentials_StillPassesTheForbiddenOperationCheck()
    {
        GitCommand command = Fetch().WithCredentials(GitCredentials.For([Credential("https://github.com")]));

        ForbiddenGitOperations.EnsureAllowed(command.Arguments);
    }
}
