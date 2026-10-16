using System;
using System.Collections.Generic;
using System.IO;
using Enigma.GitClient.Core.Worktrees;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Worktrees;

/// <summary>
/// Covers the reading of <c>git worktree list --porcelain</c>: the main working tree first, every
/// state a linked one can be in, and the shapes of output older and newer gits write.
/// </summary>
public sealed class WorktreeListParserTests
{
    private const string MainSha = "0123456789abcdef0123456789abcdef01234567";
    private const string TopicSha = "89abcdef0123456789abcdef0123456789abcdef";

    private static string Root => OperatingSystem.IsWindows() ? @"C:\src" : "/src";

    private static string Main => Path.Combine(Root, "repo");

    private static string Topic => Path.Combine(Root, "repo.worktree.topic");

    // git writes forward slashes on Windows too.
    private static string AsGit(string path) => path.Replace('\\', '/');

    [Fact]
    public void Parse_MainOnly()
    {
        IReadOnlyList<GitWorktree> worktrees = WorktreeListParser.Parse(
            $"worktree {AsGit(Main)}\nHEAD {MainSha}\nbranch refs/heads/main\n\n");

        GitWorktree main = Assert.Single(worktrees);
        Assert.Equal(Main, main.Path);
        Assert.Equal(MainSha, main.HeadSha);
        Assert.Equal("main", main.Branch);
        Assert.True(main.IsMain);
        Assert.False(main.IsBare);
        Assert.False(main.IsDetached);
        Assert.False(main.IsLocked);
        Assert.False(main.IsMissing);
    }

    [Fact]
    public void Parse_MainAndLinked_TheMainFirst()
    {
        IReadOnlyList<GitWorktree> worktrees = WorktreeListParser.Parse(
            $"worktree {AsGit(Main)}\nHEAD {MainSha}\nbranch refs/heads/main\n\n" +
            $"worktree {AsGit(Topic)}\nHEAD {TopicSha}\nbranch refs/heads/feature/topic\n\n");

        Assert.Equal(2, worktrees.Count);
        Assert.True(worktrees[0].IsMain);
        Assert.Equal(Main, worktrees[0].Path);

        GitWorktree topic = worktrees[1];
        Assert.False(topic.IsMain);
        Assert.Equal(Topic, topic.Path);
        Assert.Equal(TopicSha, topic.HeadSha);
        Assert.Equal("feature/topic", topic.Branch);
    }

    [Fact]
    public void Parse_Detached_HasNoBranch()
    {
        IReadOnlyList<GitWorktree> worktrees = WorktreeListParser.Parse(
            $"worktree {AsGit(Main)}\nHEAD {MainSha}\nbranch refs/heads/main\n\n" +
            $"worktree {AsGit(Topic)}\nHEAD {TopicSha}\ndetached\n\n");

        GitWorktree topic = worktrees[1];
        Assert.True(topic.IsDetached);
        Assert.Null(topic.Branch);
        Assert.Equal(TopicSha, topic.HeadSha);
    }

    [Fact]
    public void Parse_Bare_HasNoHeadAndNoBranch()
    {
        IReadOnlyList<GitWorktree> worktrees = WorktreeListParser.Parse(
            $"worktree {AsGit(Main)}\nbare\n\n" +
            $"worktree {AsGit(Topic)}\nHEAD {TopicSha}\nbranch refs/heads/topic\n\n");

        GitWorktree main = worktrees[0];
        Assert.True(main.IsMain);
        Assert.True(main.IsBare);
        Assert.Empty(main.HeadSha);
        Assert.Null(main.Branch);
        Assert.Equal("topic", worktrees[1].Branch);
    }

    [Fact]
    public void Parse_LockedWithAReason()
    {
        IReadOnlyList<GitWorktree> worktrees = WorktreeListParser.Parse(
            $"worktree {AsGit(Main)}\nHEAD {MainSha}\nbranch refs/heads/main\n\n" +
            $"worktree {AsGit(Topic)}\nHEAD {TopicSha}\nbranch refs/heads/topic\nlocked on a USB stick\n\n");

        GitWorktree topic = worktrees[1];
        Assert.True(topic.IsLocked);
        Assert.Equal("on a USB stick", topic.LockReason);
    }

    [Fact]
    public void Parse_LockedWithoutAReason()
    {
        IReadOnlyList<GitWorktree> worktrees = WorktreeListParser.Parse(
            $"worktree {AsGit(Main)}\nHEAD {MainSha}\nbranch refs/heads/main\n\n" +
            $"worktree {AsGit(Topic)}\nHEAD {TopicSha}\nbranch refs/heads/topic\nlocked\n\n");

        GitWorktree topic = worktrees[1];
        Assert.True(topic.IsLocked);
        Assert.Empty(topic.LockReason);
    }

    [Fact]
    public void Parse_LockedWithAQuotedReason_IsUnquoted()
    {
        IReadOnlyList<GitWorktree> worktrees = WorktreeListParser.Parse(
            $"worktree {AsGit(Main)}\nHEAD {MainSha}\nbranch refs/heads/main\n\n" +
            $"worktree {AsGit(Topic)}\nHEAD {TopicSha}\nbranch refs/heads/topic\nlocked \"two\\nlines\"\n\n");

        Assert.Equal("two\nlines", worktrees[1].LockReason);
    }

    [Theory]
    [InlineData("prunable gitdir file points to non-existent location")]
    [InlineData("prunable")]
    public void Parse_Prunable_IsMissing(string line)
    {
        IReadOnlyList<GitWorktree> worktrees = WorktreeListParser.Parse(
            $"worktree {AsGit(Main)}\nHEAD {MainSha}\nbranch refs/heads/main\n\n" +
            $"worktree {AsGit(Topic)}\nHEAD {TopicSha}\nbranch refs/heads/topic\n{line}\n\n");

        Assert.False(worktrees[0].IsMissing);
        Assert.True(worktrees[1].IsMissing);
    }

    [Fact]
    public void Parse_ForwardSlashes_BecomeThePlatformsSeparators()
    {
        GitWorktree topic = WorktreeListParser.Parse($"worktree {AsGit(Topic)}/\nHEAD {TopicSha}\ndetached\n")[0];

        Assert.Equal(Topic, topic.Path);
        Assert.DoesNotContain(OperatingSystem.IsWindows() ? '/' : '\\', topic.Path);
    }

    [Fact]
    public void Parse_AFolderWithSpaces_KeepsThemAll()
    {
        string folder = Path.Combine(Root, "my repo  copy");

        Assert.Equal(folder, WorktreeListParser.Parse($"worktree {AsGit(folder)}\nbare\n")[0].Path);
    }

    [Fact]
    public void Parse_CrlfAndTrailingBlankLines()
    {
        IReadOnlyList<GitWorktree> worktrees = WorktreeListParser.Parse(
            $"worktree {AsGit(Main)}\r\nHEAD {MainSha}\r\nbranch refs/heads/main\r\n\r\n" +
            $"worktree {AsGit(Topic)}\r\nHEAD {TopicSha}\r\nbranch refs/heads/topic\r\nlocked why\r\n\r\n\r\n\r\n");

        Assert.Equal(2, worktrees.Count);
        Assert.Equal("main", worktrees[0].Branch);
        Assert.Equal(MainSha, worktrees[0].HeadSha);
        Assert.Equal("topic", worktrees[1].Branch);
        Assert.Equal("why", worktrees[1].LockReason);
    }

    [Fact]
    public void Parse_WithoutATrailingBlankLine_KeepsTheLastRecord()
    {
        IReadOnlyList<GitWorktree> worktrees = WorktreeListParser.Parse(
            $"worktree {AsGit(Main)}\nHEAD {MainSha}\nbranch refs/heads/main\n\nworktree {AsGit(Topic)}\nHEAD {TopicSha}\ndetached");

        Assert.Equal(2, worktrees.Count);
        Assert.True(worktrees[1].IsDetached);
    }

    [Fact]
    public void Parse_UnknownLines_AreIgnored()
    {
        IReadOnlyList<GitWorktree> worktrees = WorktreeListParser.Parse(
            $"worktree {AsGit(Main)}\nHEAD {MainSha}\nsomething-new with a value\nbranch refs/heads/main\n\n");

        GitWorktree main = Assert.Single(worktrees);
        Assert.Equal("main", main.Branch);
    }

    [Fact]
    public void Parse_LinesBeforeTheFirstRecord_AreIgnored()
        => Assert.Single(WorktreeListParser.Parse($"HEAD {MainSha}\n\nworktree {AsGit(Main)}\nbare\n"));

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n\r\n")]
    public void Parse_NoOutput_NoWorktrees(string porcelain)
        => Assert.Empty(WorktreeListParser.Parse(porcelain));
}
