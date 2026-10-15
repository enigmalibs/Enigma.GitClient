using System;
using System.Collections.Generic;
using System.IO;
using Enigma.GitClient.Core.Worktrees;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Worktrees;

/// <summary>
/// Covers the argument vector that creates a worktree, and what it refuses before git is asked.
/// </summary>
public sealed class WorktreeArgumentsTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    private static string Folder => OperatingSystem.IsWindows() ? @"C:\src\repo.worktree.topic" : "/src/repo.worktree.topic";

    [Fact]
    public void BuildAddArguments_CreatesTheBranchAndEndsTheOptionsBeforeTheFolder()
    {
        List<string> arguments = WorktreeService.BuildAddArguments(Folder, "topic", Sha);

        Assert.Equal(["worktree", "add", "-b", "topic", "--", Folder, Sha], arguments);
    }

    [Fact]
    public void BuildAddArguments_TakesABranchNameAsTheStartPoint()
        => Assert.Equal(
            ["worktree", "add", "-b", "topic", "--", Folder, "origin/main"],
            WorktreeService.BuildAddArguments(Folder, "topic", "origin/main"));

    [Fact]
    public void BuildAddArguments_HandsAFolderWithSpacesOverAsOneArgument()
    {
        string folder = Path.Combine(Path.GetDirectoryName(Folder)!, "my repo.worktree.topic");

        Assert.Equal(folder, WorktreeService.BuildAddArguments(folder, "topic", Sha)[5]);
    }

    [Theory]
    [InlineData("repo.worktree.topic")]
    [InlineData("../repo.worktree.topic")]
    public void BuildAddArguments_RefusesARelativeFolder(string folder)
        => Assert.Throws<ArgumentException>(() => WorktreeService.BuildAddArguments(folder, "topic", Sha));

    [Theory]
    [InlineData("-")]
    [InlineData("--force")]
    public void BuildAddArguments_RefusesABranchThatWouldBeReadAsAnOption(string branch)
        => Assert.Throws<ArgumentException>(() => WorktreeService.BuildAddArguments(Folder, branch, Sha));

    [Theory]
    [InlineData("-")]
    [InlineData("--detach")]
    public void BuildAddArguments_RefusesAStartPointThatWouldBeReadAsAnOption(string startPoint)
        => Assert.Throws<ArgumentException>(() => WorktreeService.BuildAddArguments(Folder, "topic", startPoint));

    [Theory]
    [InlineData("", "topic", Sha)]
    [InlineData(" ", "topic", Sha)]
    [InlineData("folder", "", Sha)]
    [InlineData("folder", "topic", "")]
    [InlineData("folder", "topic", "  ")]
    public void BuildAddArguments_RefusesABlankArgument(string folder, string branch, string startPoint)
    {
        string path = folder == "folder" ? Folder : folder;

        Assert.Throws<ArgumentException>(() => WorktreeService.BuildAddArguments(path, branch, startPoint));
    }
}
