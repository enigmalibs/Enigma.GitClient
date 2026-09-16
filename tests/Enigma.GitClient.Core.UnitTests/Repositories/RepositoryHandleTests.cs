using System;
using System.IO;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Repositories;

public sealed class RepositoryHandleTests
{
    private static string Root => OperatingSystem.IsWindows() ? @"C:\src" : "/src";

    [Fact]
    public void Name_IsTheLastSegmentOfTheWorkTreePath()
    {
        RepositoryHandle handle = new(
            Path.Combine(Root, "my-repo"),
            Path.Combine(Root, "my-repo", ".git"));

        Assert.Equal("my-repo", handle.Name);
    }

    [Fact]
    public void Construction_NormalisesATrailingSeparator()
    {
        RepositoryHandle handle = new(
            Path.Combine(Root, "my-repo") + Path.DirectorySeparatorChar,
            Path.Combine(Root, "my-repo", ".git"));

        Assert.Equal(Path.Combine(Root, "my-repo"), handle.WorkTreePath);
        Assert.Equal("my-repo", handle.Name);
    }

    [Fact]
    public void GetGitPath_CombinesWithTheGitDirectory()
    {
        RepositoryHandle handle = new(
            Path.Combine(Root, "my-repo"),
            Path.Combine(Root, "my-repo", ".git"));

        Assert.Equal(
            Path.Combine(Root, "my-repo", ".git", "MERGE_HEAD"),
            handle.GetGitPath("MERGE_HEAD"));
    }

    [Fact]
    public void Equality_ComparesBothPaths()
    {
        RepositoryHandle first = new(Path.Combine(Root, "r"), Path.Combine(Root, "r", ".git"));
        RepositoryHandle same = new(Path.Combine(Root, "r"), Path.Combine(Root, "r", ".git"));
        RepositoryHandle other = new(Path.Combine(Root, "other"), Path.Combine(Root, "other", ".git"));

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, other);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Construction_RejectsABlankPath(string path)
        => Assert.Throws<ArgumentException>(() => new RepositoryHandle(path, Path.Combine(Root, "r", ".git")));
}
