using System;
using System.IO;
using Enigma.GitClient.Desktop.Services;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// What a change to each of a repository's files means, for a plain repository, a linked worktree and
/// a submodule. Paths only: nothing here touches a disk or an Avalonia type.
/// </summary>
public sealed class RepositoryLayoutTests
{
    /// <summary>An absolute path on this platform, from its segments.</summary>
    private static string At(params string[] segments)
        => Path.Combine([OperatingSystem.IsWindows() ? @"C:\" : "/", .. segments]);

    /// <summary>A plain repository: <c>/repo</c>, its git directory <c>/repo/.git</c>.</summary>
    private static readonly RepositoryLayout Plain = new(At("repo"), At("repo", ".git"), At("repo", ".git"));

    /// <summary>A linked worktree of <c>/main</c>, checked out in <c>/wt</c>.</summary>
    private static readonly RepositoryLayout Worktree = new(At("wt"), At("main", ".git", "worktrees", "wt"), At("main", ".git"));

    /// <summary>A submodule <c>/super/sub</c>, whose git directory is in the superproject's.</summary>
    private static readonly RepositoryLayout Submodule = new(At("super", "sub"), At("super", ".git", "modules", "sub"), At("super", ".git", "modules", "sub"));

    [Theory]
    [InlineData("src", "Program.cs")]
    [InlineData("README.md")]
    [InlineData("yarn.lock")]
    [InlineData("node_modules", "left-pad", "index.js")]
    [InlineData("docs", ".gitkeep")]
    public void AWorkTreeFile_IsAWorkingTreeChange(params string[] segments)
        => Assert.Equal(RepositoryChanges.WorkingTree, Plain.Classify(At(["repo", .. segments])));

    [Theory]
    [InlineData("HEAD")]
    [InlineData("index")]
    [InlineData("packed-refs")]
    [InlineData("MERGE_HEAD")]
    [InlineData("CHERRY_PICK_HEAD")]
    [InlineData("REVERT_HEAD")]
    [InlineData("ORIG_HEAD")]
    [InlineData("config")]
    [InlineData("refs", "heads", "main")]
    [InlineData("refs", "heads", "feature", "watcher")]
    [InlineData("refs", "remotes", "origin", "main")]
    [InlineData("refs", "tags", "6.0.0")]
    [InlineData("refs", "stash")]
    public void AReferenceFile_IsAReferenceChange(params string[] segments)
        => Assert.Equal(RepositoryChanges.References, Plain.Classify(At(["repo", ".git", .. segments])));

    [Theory]
    [InlineData("FETCH_HEAD")]
    [InlineData("HEAD.lock")]
    [InlineData("index.lock")]
    [InlineData("refs", "heads", "main.lock")]
    [InlineData("objects", "ab", "cdef0123456789")]
    [InlineData("objects", "pack", "pack-1.pack")]
    [InlineData("logs", "HEAD")]
    [InlineData("logs", "refs", "heads", "main")]
    [InlineData("hooks", "pre-commit")]
    [InlineData("COMMIT_EDITMSG")]
    [InlineData("description")]
    public void TheGitDirectorysOwnBookkeeping_IsNothing(params string[] segments)
        => Assert.Equal(RepositoryChanges.None, Plain.Classify(At(["repo", ".git", .. segments])));

    [Fact]
    public void TheGitDirectoryItself_IsNothing()
        => Assert.Equal(RepositoryChanges.None, Plain.Classify(At("repo", ".git")));

    [Theory]
    [InlineData("elsewhere", "file.txt")]
    [InlineData("repo-copy", "file.txt")]
    [InlineData("repo.git", "HEAD")]
    public void APathOutsideTheRepository_IsNothing(params string[] segments)
        => Assert.Equal(RepositoryChanges.None, Plain.Classify(At(segments)));

    [Fact]
    public void AnEmptyPath_IsNothing() => Assert.Equal(RepositoryChanges.None, Plain.Classify(string.Empty));

    // ---------------------------------------------------------------- a linked worktree

    [Fact]
    public void ALinkedWorktreesOwnHeadAndIndex_AreReferenceChanges()
    {
        Assert.Equal(RepositoryChanges.References, Worktree.Classify(At("main", ".git", "worktrees", "wt", "HEAD")));
        Assert.Equal(RepositoryChanges.References, Worktree.Classify(At("main", ".git", "worktrees", "wt", "index")));
        Assert.Equal(RepositoryChanges.References, Worktree.Classify(At("main", ".git", "worktrees", "wt", "MERGE_HEAD")));
    }

    [Fact]
    public void ALinkedWorktreesReferences_AreTheMainRepositorys()
    {
        Assert.Equal(RepositoryChanges.References, Worktree.Classify(At("main", ".git", "refs", "heads", "feature")));
        Assert.Equal(RepositoryChanges.References, Worktree.Classify(At("main", ".git", "packed-refs")));
        Assert.Equal(RepositoryChanges.References, Worktree.Classify(At("main", ".git", "config")));
        Assert.Equal(RepositoryChanges.None, Worktree.Classify(At("main", ".git", "objects", "ab", "cd")));
        Assert.Equal(RepositoryChanges.None, Worktree.Classify(At("main", ".git", "worktrees", "wt", "index.lock")));
    }

    [Fact]
    public void ALinkedWorktreesGitFile_IsNothing_AndItsFilesAreTheWorkTree()
    {
        Assert.Equal(RepositoryChanges.None, Worktree.Classify(At("wt", ".git")));
        Assert.Equal(RepositoryChanges.WorkingTree, Worktree.Classify(At("wt", "src", "a.cs")));

        // The main repository's own work tree is not this one's.
        Assert.Equal(RepositoryChanges.None, Worktree.Classify(At("main", "src", "a.cs")));
    }

    // ---------------------------------------------------------------- a submodule

    [Fact]
    public void ASubmodulesGitDirectory_IsInTheSuperproject_AndHoldsItsReferences()
    {
        Assert.Equal(RepositoryChanges.References, Submodule.Classify(At("super", ".git", "modules", "sub", "HEAD")));
        Assert.Equal(RepositoryChanges.References, Submodule.Classify(At("super", ".git", "modules", "sub", "refs", "heads", "main")));
        Assert.Equal(RepositoryChanges.None, Submodule.Classify(At("super", ".git", "modules", "sub", "objects", "ab", "cd")));
        Assert.Equal(RepositoryChanges.WorkingTree, Submodule.Classify(At("super", "sub", "lib.c")));
        Assert.Equal(RepositoryChanges.None, Submodule.Classify(At("super", "sub", ".git")));
    }
}
