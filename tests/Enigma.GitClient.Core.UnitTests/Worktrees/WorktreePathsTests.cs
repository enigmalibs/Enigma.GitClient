using System;
using System.IO;
using Enigma.GitClient.Core.Worktrees;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Worktrees;

/// <summary>
/// Covers where a new worktree is proposed — beside the repository, never inside it — and the rules a
/// folder is judged by that need no file system. The rules about what is already on disk are asserted
/// against a real one, in the integration suite.
/// </summary>
public sealed class WorktreePathsTests
{
    private static string Root => OperatingSystem.IsWindows() ? @"C:\src" : "/src";

    // Under a folder no machine has, so the file-system rules never fire here.
    private static readonly string Parent = Path.Combine(Root, "no-such-" + Guid.NewGuid().ToString("N"));

    private static readonly string Repository = Path.Combine(Parent, "my-repo");

    [Fact]
    public void DefaultFor_IsBesideTheRepositoryAndNamedAfterItAndTheBranch()
        => Assert.Equal(Path.Combine(Parent, "my-repo.worktree.topic"), WorktreePaths.DefaultFor(Repository, "topic"));

    [Fact]
    public void DefaultFor_IsNeverInsideTheRepository()
    {
        string folder = WorktreePaths.DefaultFor(Repository, "topic");

        Assert.Equal(Parent, Path.GetDirectoryName(folder));
        Assert.Empty(WorktreePaths.Validate(Repository, folder));
    }

    [Fact]
    public void DefaultFor_IgnoresATrailingSeparatorOnTheRepository()
        => Assert.Equal(
            Path.Combine(Parent, "my-repo.worktree.topic"),
            WorktreePaths.DefaultFor(Repository + Path.DirectorySeparatorChar, "topic"));

    [Fact]
    public void DefaultFor_MakesOneFolderOfABranchWithSlashes()
        => Assert.Equal(
            Path.Combine(Parent, "my-repo.worktree.feature-login-form"),
            WorktreePaths.DefaultFor(Repository, "feature/login/form"));

    [Fact]
    public void DefaultFor_WithNoBranchYet_EndsWithTheInfix()
        => Assert.Equal(Path.Combine(Parent, "my-repo" + WorktreePaths.Infix), WorktreePaths.DefaultFor(Repository, ""));

    [Fact]
    public void DefaultFor_ARepositoryAtTheRootHasNothingBesideIt()
    {
        string root = Path.GetPathRoot(Path.GetFullPath(Root))!;

        Assert.Empty(WorktreePaths.DefaultFor(root, "topic"));
    }

    [Theory]
    [InlineData("topic", "topic")]
    [InlineData("feature/login", "feature-login")]
    [InlineData(@"a\b", "a-b")]
    [InlineData("a<b>c", "a-b-c")]
    [InlineData("a\"b|c", "a-b-c")]
    [InlineData("what?*:", "what---")]
    [InlineData("tab\there", "tab-here")]
    [InlineData("été-2026", "été-2026")]
    public void ToFolderName_ReplacesWhatAFolderNameCannotHold(string branch, string expected)
        => Assert.Equal(expected, WorktreePaths.ToFolderName(branch));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RefusesNoFolder(string path)
        => Assert.Equal("Choose the folder the worktree goes in.", WorktreePaths.Validate(Repository, path));

    [Theory]
    [InlineData("worktree")]
    [InlineData("../worktree")]
    public void Validate_RefusesARelativeFolder(string path)
        => Assert.Contains("full path", WorktreePaths.Validate(Repository, path), StringComparison.Ordinal);

    [Fact]
    public void Validate_RefusesTheRepositoryItself()
    {
        Assert.Contains("own folder", WorktreePaths.Validate(Repository, Repository), StringComparison.Ordinal);
        Assert.Contains(
            "own folder",
            WorktreePaths.Validate(Repository, Repository + Path.DirectorySeparatorChar),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RefusesAFolderInsideTheRepository()
    {
        Assert.Contains(
            "inside the repository",
            WorktreePaths.Validate(Repository, Path.Combine(Repository, "worktrees", "topic")),
            StringComparison.Ordinal);

        // Through a "..", still inside.
        Assert.Contains(
            "inside the repository",
            WorktreePaths.Validate(Repository, Path.Combine(Parent, "other", "..", "my-repo", "topic")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AcceptsAFolderWhoseNameOnlyStartsLikeTheRepository()
        => Assert.Empty(WorktreePaths.Validate(Repository, Path.Combine(Parent, "my-repo-topic")));

    [Fact]
    public void Validate_AcceptsAFolderThatDoesNotExistYet()
        => Assert.Empty(WorktreePaths.Validate(Repository, Path.Combine(Parent, "elsewhere", "topic")));

    [Fact]
    public void Validate_OnWindows_ComparesFoldersWithoutCase()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Folder names are case-sensitive on this platform.");

        Assert.Contains(
            "inside the repository",
            WorktreePaths.Validate(Repository, Path.Combine(Repository.ToUpperInvariant(), "topic")),
            StringComparison.Ordinal);
    }
}
