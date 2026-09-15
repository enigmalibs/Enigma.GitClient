using System.Collections.Generic;
using Enigma.GitClient.Core.Refs;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Refs;

public sealed class RemoteReaderParseTests
{
    [Fact]
    public void Parse_ReadsASingleRemote()
    {
        IReadOnlyList<GitRemote> remotes = RemoteReader.Parse(
            "origin\thttps://github.com/owner/repo.git (fetch)\n" +
            "origin\thttps://github.com/owner/repo.git (push)\n");

        GitRemote remote = Assert.Single(remotes);
        Assert.Equal("origin", remote.Name);
        Assert.Equal("https://github.com/owner/repo.git", remote.FetchUrl);
        Assert.Equal("https://github.com/owner/repo.git", remote.PushUrl);
        Assert.False(remote.HasSeparatePushUrl);
    }

    [Fact]
    public void Parse_ReadsASeparatePushUrl()
    {
        IReadOnlyList<GitRemote> remotes = RemoteReader.Parse(
            "origin\thttps://github.com/owner/repo.git (fetch)\n" +
            "origin\tgit@github.com:owner/repo.git (push)\n");

        GitRemote remote = Assert.Single(remotes);
        Assert.Equal("https://github.com/owner/repo.git", remote.FetchUrl);
        Assert.Equal("git@github.com:owner/repo.git", remote.PushUrl);
        Assert.True(remote.HasSeparatePushUrl);
    }

    [Fact]
    public void Parse_ReadsSeveralRemotesOrderedByName()
    {
        IReadOnlyList<GitRemote> remotes = RemoteReader.Parse(
            "upstream\thttps://example.com/u.git (fetch)\n" +
            "upstream\thttps://example.com/u.git (push)\n" +
            "origin\thttps://example.com/o.git (fetch)\n" +
            "origin\thttps://example.com/o.git (push)\n");

        Assert.Equal(2, remotes.Count);
        Assert.Equal("origin", remotes[0].Name);
        Assert.Equal("upstream", remotes[1].Name);
    }

    [Fact]
    public void Parse_HandlesAUrlContainingSpaces()
    {
        IReadOnlyList<GitRemote> remotes = RemoteReader.Parse(
            "local\t/home/user/my projects/repo (fetch)\n" +
            "local\t/home/user/my projects/repo (push)\n");

        Assert.Equal("/home/user/my projects/repo", Assert.Single(remotes).FetchUrl);
    }

    [Fact]
    public void Parse_ReturnsNothingForARepositoryWithNoRemotes()
        => Assert.Empty(RemoteReader.Parse(string.Empty));

    [Fact]
    public void Parse_IgnoresMalformedLines()
        => Assert.Empty(RemoteReader.Parse("not a remote line\n\n"));
}
