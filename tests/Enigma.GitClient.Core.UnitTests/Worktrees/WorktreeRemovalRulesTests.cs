using System;
using System.Collections.Generic;
using System.IO;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Worktrees;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Worktrees;

/// <summary>
/// Covers which worktree a removal is for and the ones it refuses, before git is asked: the main
/// working tree, the one the repository is open in, a locked one, and a folder that is not a worktree.
/// </summary>
public sealed class WorktreeRemovalRulesTests
{
    private static string Root => OperatingSystem.IsWindows() ? @"C:\src" : "/src";

    private static readonly string MainPath = Path.Combine(Root, "repo");
    private static readonly string TopicPath = Path.Combine(Root, "repo.worktree.topic");
    private static readonly string LockedPath = Path.Combine(Root, "repo.worktree.locked");

    private static readonly RepositoryHandle FromMain = new(MainPath, Path.Combine(MainPath, ".git"));
    private static readonly RepositoryHandle FromTopic = new(TopicPath, Path.Combine(MainPath, ".git", "worktrees", "topic"));

    private static readonly IReadOnlyList<GitWorktree> Worktrees =
    [
        new GitWorktree { Path = MainPath, Branch = "main", IsMain = true },
        new GitWorktree { Path = TopicPath, Branch = "topic" },
        new GitWorktree { Path = LockedPath, Branch = "locked", IsLocked = true, LockReason = "on a USB stick" },
    ];

    [Fact]
    public void FindRemovable_ALinkedWorktree()
        => Assert.Same(Worktrees[1], WorktreeService.FindRemovable(FromMain, Worktrees, TopicPath));

    [Fact]
    public void FindRemovable_IgnoresATrailingSeparator()
        => Assert.Same(
            Worktrees[1],
            WorktreeService.FindRemovable(FromMain, Worktrees, TopicPath + Path.DirectorySeparatorChar));

    [Fact]
    public void FindRemovable_OnWindows_IgnoresCase()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Folder names are case-sensitive here.");

        Assert.Same(Worktrees[1], WorktreeService.FindRemovable(FromMain, Worktrees, TopicPath.ToUpperInvariant()));
    }

    [Fact]
    public void FindRemovable_RefusesTheMainWorkingTree()
    {
        GitOperationRefusedException refusal = Assert.Throws<GitOperationRefusedException>(
            () => WorktreeService.FindRemovable(FromTopic, Worktrees, MainPath));

        Assert.Contains("main working tree", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindRemovable_RefusesTheWorkingTreeTheRepositoryIsOpenIn()
    {
        GitOperationRefusedException refusal = Assert.Throws<GitOperationRefusedException>(
            () => WorktreeService.FindRemovable(FromTopic, Worktrees, TopicPath));

        Assert.Contains("open in", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindRemovable_RefusesALockedWorktree_NamingItsReasonAndUnlock()
    {
        GitOperationRefusedException refusal = Assert.Throws<GitOperationRefusedException>(
            () => WorktreeService.FindRemovable(FromMain, Worktrees, LockedPath));

        Assert.Contains("on a USB stick", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("git worktree unlock", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindRemovable_RefusesALockedWorktreeWithoutAReason()
    {
        IReadOnlyList<GitWorktree> worktrees = [Worktrees[0], Worktrees[2] with { LockReason = string.Empty }];

        GitOperationRefusedException refusal = Assert.Throws<GitOperationRefusedException>(
            () => WorktreeService.FindRemovable(FromMain, worktrees, LockedPath));

        Assert.Contains("is locked. Unlock it", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("elsewhere")]
    [InlineData("repo.worktree.topic")]
    public void FindRemovable_RefusesAFolderThatIsNotAWorktree(string folder)
    {
        string path = folder == "elsewhere" ? Path.Combine(Root, "elsewhere") : folder;

        GitOperationRefusedException refusal = Assert.Throws<GitOperationRefusedException>(
            () => WorktreeService.FindRemovable(FromMain, Worktrees, path));

        Assert.Contains("is not a worktree of this repository", refusal.Message, StringComparison.Ordinal);
    }
}
