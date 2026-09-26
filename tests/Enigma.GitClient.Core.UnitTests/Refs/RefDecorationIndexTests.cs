using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Refs;

public sealed class RefDecorationIndexTests
{
    private const string ShaOne = "1111111111111111111111111111111111111111";
    private const string ShaTwo = "2222222222222222222222222222222222222222";

    private static readonly GitSignature Ada =
        new("Ada Lovelace", "ada@example.com", DateTimeOffset.UnixEpoch);

    private static GitBranch Branch(string fullName, string sha, bool isCurrent = false)
        => new(fullName, sha, isCurrent, null, BranchTracking.None, Ada, DateTimeOffset.UnixEpoch, "S");

    private static GitTag Tag(string fullName, string sha)
        => new(fullName, sha, null, null, string.Empty, DateTimeOffset.UnixEpoch);

    [Fact]
    public void GetRefs_ReturnsEveryRefPointingAtACommit()
    {
        RefDecorationIndex index = RefDecorationIndex.Build(
        [
            Branch("refs/heads/main", ShaOne),
            Tag("refs/tags/v1.0", ShaOne),
            Branch("refs/heads/other", ShaTwo),
        ]);

        Assert.Equal(2, index.GetRefs(ShaOne).Count);
        Assert.Single(index.GetRefs(ShaTwo));
        Assert.Equal(2, index.DecoratedCommitCount);
    }

    [Fact]
    public void GetRefs_ReturnsNothingForAnUndecoratedCommit()
    {
        RefDecorationIndex index = RefDecorationIndex.Build([Branch("refs/heads/main", ShaOne)]);

        Assert.Empty(index.GetRefs(ShaTwo));
        Assert.False(index.HasRefs(ShaTwo));
        Assert.True(index.HasRefs(ShaOne));
    }

    [Fact]
    public void GetRefs_OrdersCurrentBranchThenLocalThenRemoteThenTagThenStash()
    {
        RefDecorationIndex index = RefDecorationIndex.Build(
        [
            Tag("refs/tags/v1.0", ShaOne),
            new GitOtherRef("refs/stash", ShaOne),
            Branch("refs/remotes/origin/main", ShaOne),
            Branch("refs/heads/zulu", ShaOne),
            Branch("refs/heads/main", ShaOne, isCurrent: true),
            Branch("refs/heads/alpha", ShaOne),
        ]);

        List<string> names = [.. index.GetRefs(ShaOne).Select(reference => reference.ShortName)];

        Assert.Equal(["main", "alpha", "zulu", "origin/main", "v1.0", "stash"], names);
    }

    [Fact]
    public void IsHead_FollowsTheSuppliedHeadState()
    {
        HeadState head = new(IsUnborn: false, IsDetached: true, null, ShaOne, RepositoryOperation.None);
        RefDecorationIndex index = RefDecorationIndex.Build([Branch("refs/heads/main", ShaTwo)], head);

        Assert.True(index.IsHead(ShaOne));
        Assert.False(index.IsHead(ShaTwo));
        Assert.Same(head, index.Head);
    }

    [Fact]
    public void IsHead_IsFalseWhenNoHeadStateWasSupplied()
        => Assert.False(RefDecorationIndex.Build([Branch("refs/heads/main", ShaOne)]).IsHead(ShaOne));

    [Fact]
    public void Build_SkipsARefWithNoTarget()
    {
        RefDecorationIndex index = RefDecorationIndex.Build([Branch("refs/heads/unborn", string.Empty)]);

        Assert.Equal(0, index.DecoratedCommitCount);
    }

    [Fact]
    public void Build_FromARefCollectionIncludesEveryKind()
    {
        RefCollection refs = new(
            [Branch("refs/heads/main", ShaOne, isCurrent: true)],
            [Branch("refs/remotes/origin/main", ShaOne)],
            [Tag("refs/tags/v1.0", ShaOne)],
            [new GitOtherRef("refs/stash", ShaTwo)]);

        RefDecorationIndex index = RefDecorationIndex.Build(refs);

        Assert.Equal(3, index.GetRefs(ShaOne).Count);
        Assert.Single(index.GetRefs(ShaTwo));
    }

    [Fact]
    public void Empty_HasNoDecorations()
    {
        Assert.Equal(0, RefDecorationIndex.Empty.DecoratedCommitCount);
        Assert.Empty(RefDecorationIndex.Empty.GetRefs(ShaOne));
    }
}
