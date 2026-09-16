using System;
using Enigma.GitClient.Core.History;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.History;

public sealed class GitCommitTests
{
    private static readonly GitSignature Ada =
        new("Ada Lovelace", "ada@example.com", DateTimeOffset.UnixEpoch);

    private static GitCommit Commit(string sha, string[] parents, string subject = "S", string body = "")
        => new(sha, parents, Ada, Ada, subject, body);

    [Fact]
    public void ShortSha_IsSevenCharacters()
        => Assert.Equal("abc1234", Commit("abc1234567890abcdef1234567890abcdef123456", []).ShortSha);

    [Fact]
    public void ShortSha_HandlesAnAlreadyShortSha()
        => Assert.Equal("abc", Commit("abc", []).ShortSha);

    [Fact]
    public void IsMerge_IsTrueFromTwoParents()
    {
        Assert.False(Commit("a", []).IsMerge);
        Assert.False(Commit("a", ["p1"]).IsMerge);
        Assert.True(Commit("a", ["p1", "p2"]).IsMerge);
        Assert.True(Commit("a", ["p1", "p2", "p3"]).IsMerge);
    }

    [Fact]
    public void IsRoot_IsTrueWithNoParents()
    {
        Assert.True(Commit("a", []).IsRoot);
        Assert.False(Commit("a", ["p1"]).IsRoot);
    }

    [Fact]
    public void Message_JoinsTheSubjectAndBodyWithABlankLine()
    {
        Assert.Equal("Subject", Commit("a", [], "Subject").Message);
        Assert.Equal("Subject\n\nBody", Commit("a", [], "Subject", "Body").Message);
    }

    [Fact]
    public void ParentShas_AreCopiedSoLaterMutationCannotAffectTheCommit()
    {
        string[] parents = ["p1"];
        GitCommit commit = Commit("a", parents);

        parents[0] = "changed";

        Assert.Equal("p1", commit.ParentShas[0]);
    }

    [Fact]
    public void Equality_IsByShaAlone()
    {
        GitCommit first = Commit("a", [], "One");
        GitCommit second = Commit("a", ["p"], "Two");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, Commit("b", []));
    }

    [Theory]
    [InlineData("Ada Lovelace", "ada@example.com", "AL")]
    [InlineData("Ada", "ada@example.com", "A")]
    [InlineData("ada-lovelace", "ada@example.com", "AL")]
    [InlineData("Jean-Luc de la Fontaine", "jl@example.com", "JF")]
    [InlineData("", "grace@example.com", "G")]
    [InlineData("", "", "?")]
    public void Initials_AreDerivedFromTheNameThenTheEmail(string name, string email, string expected)
        => Assert.Equal(expected, new GitSignature(name, email, DateTimeOffset.UnixEpoch).Initials);

    [Fact]
    public void SignatureToString_RendersNameAndEmail()
        => Assert.Equal("Ada Lovelace <ada@example.com>", Ada.ToString());

    [Fact]
    public void Construction_RejectsABlankSha()
        => Assert.Throws<ArgumentException>(() => Commit("  ", []));
}
