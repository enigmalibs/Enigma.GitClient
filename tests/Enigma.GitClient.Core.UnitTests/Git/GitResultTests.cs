using System;
using Enigma.GitClient.Core.Git;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Git;

public sealed class GitResultTests
{
    private static GitResult Result(string standardOutput)
        => new(0, standardOutput, string.Empty, TimeSpan.Zero);

    [Fact]
    public void SplitOutput_DropsTheTrailingEmptyRecord()
        => Assert.Equal(["a", "b"], Result("a\nb\n").SplitOutput());

    [Fact]
    public void SplitOutput_KeepsInteriorEmptyRecords()
        => Assert.Equal(["a", "", "b"], Result("a\n\nb\n").SplitOutput());

    [Fact]
    public void SplitOutput_HandlesNulSeparatedOutput()
        => Assert.Equal(["a b", "c\nd"], Result("a b\0c\nd\0").SplitOutput('\0'));

    [Fact]
    public void SplitOutput_StripsCarriageReturnsOnLineSplits()
        => Assert.Equal(["a", "b"], Result("a\r\nb\r\n").SplitOutput());

    [Fact]
    public void SplitOutput_ReturnsNothingForEmptyOutput()
        => Assert.Empty(Result(string.Empty).SplitOutput());

    [Fact]
    public void TrimmedOutput_RemovesTheTrailingNewline()
        => Assert.Equal("/repo", Result("/repo\n").TrimmedOutput);

    [Fact]
    public void IsSuccess_FollowsTheExitCode()
    {
        Assert.True(new GitResult(0, string.Empty, string.Empty, TimeSpan.Zero).IsSuccess);
        Assert.False(new GitResult(1, string.Empty, string.Empty, TimeSpan.Zero).IsSuccess);
    }
}
