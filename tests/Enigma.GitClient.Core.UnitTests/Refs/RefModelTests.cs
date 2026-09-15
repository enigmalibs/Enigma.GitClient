using System;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Refs;

public sealed class RefModelTests
{
    private static readonly GitSignature Ada =
        new("Ada Lovelace", "ada@example.com", DateTimeOffset.UnixEpoch);

    private static GitBranch Branch(string fullName, bool isCurrent = false, BranchTracking? tracking = null)
        => new(
            fullName,
            "1111111111111111111111111111111111111111",
            isCurrent,
            null,
            tracking ?? BranchTracking.None,
            Ada,
            DateTimeOffset.UnixEpoch,
            "S");

    [Theory]
    [InlineData("refs/heads/main", "main", false)]
    [InlineData("refs/heads/feature/nested", "feature/nested", false)]
    [InlineData("refs/remotes/origin/main", "origin/main", true)]
    [InlineData("refs/remotes/upstream/feature/nested", "upstream/feature/nested", true)]
    public void ShortName_StripsTheNamespace(string fullName, string expected, bool isRemote)
    {
        GitBranch branch = Branch(fullName);

        Assert.Equal(expected, branch.ShortName);
        Assert.Equal(isRemote, branch.IsRemote);
    }

    [Fact]
    public void BranchTracking_ReportsDivergenceAndSynchronisation()
    {
        Assert.True(BranchTracking.None.IsSynchronised);
        Assert.False(BranchTracking.None.HasDiverged);
        Assert.True(new BranchTracking(1, 1, false).HasDiverged);
        Assert.False(new BranchTracking(1, 0, false).IsSynchronised);
        Assert.False(new BranchTracking(0, 0, true).IsSynchronised);
    }

    [Fact]
    public void RefCollection_FindsTheCurrentBranchAndTheStash()
    {
        RefCollection refs = new(
            [Branch("refs/heads/other"), Branch("refs/heads/main", isCurrent: true)],
            [],
            [],
            [new GitOtherRef("refs/stash", "2222222222222222222222222222222222222222")]);

        Assert.Equal("main", refs.CurrentBranch?.ShortName);
        Assert.Equal(GitRefKind.Stash, refs.Stash?.Kind);
        Assert.Equal(3, System.Linq.Enumerable.Count(refs.All()));
    }

    [Fact]
    public void RefCollection_Empty_HasNothing()
    {
        Assert.Null(RefCollection.Empty.CurrentBranch);
        Assert.Null(RefCollection.Empty.Stash);
        Assert.Empty(RefCollection.Empty.All());
    }

    [Fact]
    public void HeadState_DescribesADetachedHead()
    {
        HeadState head = new(
            IsUnborn: false,
            IsDetached: true,
            null,
            "abcdef1234567890abcdef1234567890abcdef12",
            RepositoryOperation.None);

        Assert.Equal("detached at abcdef1", head.DisplayName);
        Assert.False(head.HasOperationInProgress);
    }

    [Fact]
    public void HeadState_DescribesABranch()
    {
        HeadState head = new(false, false, "main", "abc", RepositoryOperation.Merge);

        Assert.Equal("main", head.DisplayName);
        Assert.True(head.HasOperationInProgress);
    }

    [Fact]
    public void HeadState_Unborn_KeepsTheBranchTheFirstCommitWillCreate()
    {
        HeadState head = HeadState.Unborn("main");

        Assert.True(head.IsUnborn);
        Assert.False(head.IsDetached);
        Assert.Equal("main", head.BranchName);
        Assert.Equal(string.Empty, head.Sha);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git", "github.com")]
    [InlineData("ssh://git@gitlab.example.com/group/project.git", "gitlab.example.com")]
    [InlineData("git@github.com:owner/repo.git", "github.com")]
    [InlineData("/home/user/projects/repo", null)]
    [InlineData("", null)]
    public void GitRemote_ExtractsTheHostFromEveryUrlShape(string url, string? expected)
        => Assert.Equal(expected, GitRemote.ExtractHost(url));

    [Fact]
    public void GitRemote_ReportsASeparatePushUrl()
    {
        Assert.False(new GitRemote("origin", "https://a/b.git", "https://a/b.git").HasSeparatePushUrl);
        Assert.True(new GitRemote("origin", "https://a/b.git", "git@a:b.git").HasSeparatePushUrl);
    }

    [Fact]
    public void GitRef_EqualityComparesNameTargetAndType()
    {
        GitBranch first = Branch("refs/heads/main");
        GitBranch same = Branch("refs/heads/main");

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual<GitRef>(first, Branch("refs/heads/other"));
    }
}
