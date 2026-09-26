using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Identity;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Identity;

/// <summary>
/// Covers the <c>git config</c> invocations the identity service runs, and how it reads what git
/// prints back.
/// </summary>
public sealed class GitIdentityArgumentsTests
{
    [Theory]
    [InlineData(GitConfigScope.Global, "--global")]
    [InlineData(GitConfigScope.Local, "--local")]
    public void BuildReadArguments_AsksForTheTwoKeysOfOneScope(GitConfigScope scope, string flag)
        => Assert.Equal(
            ["config", "-z", flag, "--get-regexp", @"^user\.(name|email)$"],
            GitIdentityService.BuildReadArguments(scope));

    [Theory]
    [InlineData(GitConfigScope.Global, "--global")]
    [InlineData(GitConfigScope.Local, "--local")]
    public void BuildWriteArguments_SetsOneKeyInOneScope(GitConfigScope scope, string flag)
        => Assert.Equal(
            ["config", flag, "user.name", "Ada Lovelace"],
            GitIdentityService.BuildWriteArguments(scope, "user.name", "Ada Lovelace"));

    [Fact]
    public void BuildWriteArguments_KeepsAValueThatStartsWithADashAfterTheKey()
    {
        // git config reads options only up to the key, so this is written as a value.
        List<string> arguments = GitIdentityService.BuildWriteArguments(GitConfigScope.Global, "user.name", "-n");

        Assert.Equal(["config", "--global", "user.name", "-n"], arguments);
    }

    [Fact]
    public void BuildRemoveArguments_RemovesEveryLineOfTheKeyFromTheRepository()
        => Assert.Equal(
            ["config", "--local", "--unset-all", "user.email"],
            GitIdentityService.BuildRemoveArguments("user.email"));

    [Fact]
    public void Arguments_RefuseAScopeThatDoesNotExist()
        => Assert.Throws<ArgumentOutOfRangeException>(() => GitIdentityService.BuildReadArguments((GitConfigScope)7));

    [Fact]
    public void Arguments_AreAcceptedByTheCommandFactory()
    {
        GitCommandFactory factory = new();

        // The forbidden-operation check reads every vector; none of these may trip it.
        Assert.NotNull(factory.Create(".", GitIdentityService.BuildReadArguments(GitConfigScope.Global)));
        Assert.NotNull(factory.Create(".", GitIdentityService.BuildWriteArguments(GitConfigScope.Local, "user.email", "a@b")));
        Assert.NotNull(factory.Create(".", GitIdentityService.BuildRemoveArguments("user.name")));
    }

    [Fact]
    public void Parse_ReadsANameWithSpacesAndAnEmail()
        => Assert.Equal(
            new GitIdentity("Ada King Lovelace", "ada@example.com"),
            GitIdentityService.Parse("user.name\nAda King Lovelace\0user.email\nada@example.com\0"));

    [Fact]
    public void Parse_TakesTheLastValueOfAKeySetTwice()
        => Assert.Equal(
            new GitIdentity("Second", "ada@example.com"),
            GitIdentityService.Parse("user.name\nFirst\0user.email\nada@example.com\0user.name\nSecond\0"));

    [Fact]
    public void Parse_LeavesWhatIsNotSetEmpty()
    {
        Assert.Equal(GitIdentity.Empty, GitIdentityService.Parse(string.Empty));
        Assert.Equal(new GitIdentity(string.Empty, "ada@example.com"), GitIdentityService.Parse("user.email\nada@example.com\0"));
    }

    [Fact]
    public void Parse_ReadsAKeyWithNoValueAsNotSet()
        => Assert.Equal(new GitIdentity(string.Empty, "a@b"), GitIdentityService.Parse("user.name\0user.email\na@b\0"));

    [Fact]
    public void Parse_IgnoresTheCaseOfTheKey()
        => Assert.Equal(new GitIdentity("Ada", "a@b"), GitIdentityService.Parse("USER.NAME\nAda\0User.Email\na@b\0"));
}
