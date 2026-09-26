using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Reset;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Reset;

/// <summary>
/// Covers the argument vector a reset runs with: the mode's flag, the revision, and the guard that
/// keeps a revision from being read as an option.
/// </summary>
public sealed class ResetArgumentsTests
{
    [Theory]
    [InlineData(ResetMode.Soft, "--soft")]
    [InlineData(ResetMode.Hard, "--hard")]
    public void BuildArguments_NamesTheModeAndEndsTheRevisions(ResetMode mode, string flag)
    {
        List<string> arguments = ResetService.BuildArguments("0123456789abcdef0123456789abcdef01234567", mode);

        // The trailing "--" keeps a revision that is also a file's name from being read as a path.
        Assert.Equal(["reset", flag, "0123456789abcdef0123456789abcdef01234567", "--"], arguments);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("--hard")]
    [InlineData("-q")]
    public void BuildArguments_RefusesARevisionThatWouldBeReadAsAnOption(string revision)
        => Assert.Throws<ArgumentException>(() => ResetService.BuildArguments(revision, ResetMode.Soft));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildArguments_RefusesABlankRevision(string revision)
        => Assert.Throws<ArgumentException>(() => ResetService.BuildArguments(revision, ResetMode.Hard));

    [Fact]
    public void BuildArguments_RefusesAModeThatDoesNotExist()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ResetService.BuildArguments("HEAD~1", (ResetMode)42));
}
