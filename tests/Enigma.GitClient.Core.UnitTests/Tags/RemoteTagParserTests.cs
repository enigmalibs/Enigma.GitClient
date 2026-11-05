using System.Collections.Generic;
using Enigma.GitClient.Core.Tags;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Tags;

/// <summary>
/// Reading what <c>git ls-remote --tags</c> says a remote has (FEATURE-0123).
/// </summary>
public sealed class RemoteTagParserTests
{
    private const string Commit = "5e986028c5d72812a6dd259148dd04fe68b71100";
    private const string TagObject = "15fd987f5a8969b5b1e9af926d50fd9a476f4ef3";
    private const string Other = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void ALightweightTag_IsItsCommit()
    {
        IReadOnlyDictionary<string, string> tags = RemoteTagParser.Parse($"{Commit}\trefs/tags/light\n");

        Assert.Equal(Commit, Assert.Contains("light", tags));
    }

    [Fact]
    public void AnAnnotatedTag_IsTheCommitItPeelsTo()
    {
        IReadOnlyDictionary<string, string> tags = RemoteTagParser.Parse(
            $"{TagObject}\trefs/tags/v1\n{Commit}\trefs/tags/v1^{{}}\n");

        Assert.Equal(Commit, Assert.Single(tags, pair => pair.Key == "v1").Value);
    }

    [Fact]
    public void ThePeeledLine_WinsWhicheverOrderTheyComeIn()
    {
        IReadOnlyDictionary<string, string> tags = RemoteTagParser.Parse(
            $"{Commit}\trefs/tags/v1^{{}}\n{TagObject}\trefs/tags/v1\n");

        Assert.Equal(Commit, tags["v1"]);
    }

    [Fact]
    public void WhatIsNotATag_OrNotALine_IsSkipped()
    {
        string output = string.Join(
            '\n',
            $"{Other}\trefs/heads/main",
            $"{Other}\tHEAD",
            "not a line at all",
            $"xyz\trefs/tags/bad-sha",
            $"{Other}\trefs/tags/",
            $"{Commit}\trefs/tags/kept\r",
            string.Empty);

        IReadOnlyDictionary<string, string> tags = RemoteTagParser.Parse(output);

        Assert.Equal(Commit, Assert.Single(tags).Value);
        Assert.Equal("kept", Assert.Single(tags).Key);
    }

    [Fact]
    public void NothingSaid_IsNoTag()
        => Assert.Empty(RemoteTagParser.Parse(string.Empty));

    [Fact]
    public void ANameWithSlashes_IsKeptWhole()
        => Assert.Equal(Commit, RemoteTagParser.Parse($"{Commit}\trefs/tags/release/6.16.0\n")["release/6.16.0"]);
}
