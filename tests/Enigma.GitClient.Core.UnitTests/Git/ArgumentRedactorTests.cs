using Enigma.GitClient.Core.Git;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Git;

public sealed class ArgumentRedactorTests
{
    [Theory]
    [InlineData(
        "https://octocat:ghp_abcdefghijklmnopqrstuvwxyz0123456789@github.com/o/r.git",
        "https://octocat:***@github.com/o/r.git")]
    [InlineData(
        "https://x-access-token:ghs_secretvalue@github.com/o/r.git",
        "https://x-access-token:***@github.com/o/r.git")]
    [InlineData(
        "https://glpat-abcdefghijklmnopqrstuvwxyz@gitlab.example.com/g/p.git",
        "https://***@gitlab.example.com/g/p.git")]
    public void Redact_RemovesCredentialsFromUrls(string value, string expected)
        => Assert.Equal(expected, ArgumentRedactor.Redact(value));

    [Theory]
    [InlineData("https://github.com/owner/repo.git")]
    [InlineData("ssh://git@github.com/owner/repo.git")]
    [InlineData("git@github.com:owner/repo.git")]
    [InlineData("/home/user/projects/repo")]
    public void Redact_LeavesUrlsWithoutSecretsAlone(string value)
        => Assert.Equal(value, ArgumentRedactor.Redact(value));

    [Theory]
    [InlineData("https://gitlab.example.com/api/v4/projects?private_token=abc123",
        "https://gitlab.example.com/api/v4/projects?private_token=***")]
    [InlineData("https://host/api?page=2&access_token=abc123",
        "https://host/api?page=2&access_token=***")]
    public void Redact_RemovesQueryStringTokens(string value, string expected)
        => Assert.Equal(expected, ArgumentRedactor.Redact(value));

    [Theory]
    [InlineData("Authorization: Bearer ghp_secret", "Authorization: Bearer ***")]
    [InlineData("PRIVATE-TOKEN: glpat-secret", "PRIVATE-TOKEN: ***")]
    [InlineData("http.extraHeader=Authorization: Basic c2VjcmV0", "http.extraHeader=Authorization: Basic ***")]
    public void Redact_RemovesAuthorizationHeaders(string value, string expected)
        => Assert.Equal(expected, ArgumentRedactor.Redact(value));

    [Theory]
    [InlineData("--password=hunter2", "--password=***")]
    [InlineData("--access-token=abc", "--access-token=***")]
    public void Redact_RemovesOptionValues(string value, string expected)
        => Assert.Equal(expected, ArgumentRedactor.Redact(value));

    [Fact]
    public void RedactCommandLine_RedactsEveryArgument()
    {
        string commandLine = ArgumentRedactor.RedactCommandLine(
        [
            "clone",
            "https://user:secret@github.com/o/r.git",
            "target",
        ]);

        Assert.Equal("clone https://user:***@github.com/o/r.git target", commandLine);
    }

    [Fact]
    public void Redact_HandlesNullAndEmpty()
    {
        Assert.Equal(string.Empty, ArgumentRedactor.Redact(null));
        Assert.Equal(string.Empty, ArgumentRedactor.Redact(string.Empty));
    }
}
