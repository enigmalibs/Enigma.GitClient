using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Revert;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Revert;

/// <summary>
/// Covers the argument vector a revert runs with, and how git's answer is read: a conflict, nothing
/// left to undo, work in the way and any other refusal each say something different.
/// </summary>
public sealed class RevertArgumentsTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void BuildArguments_CommitsWithGitsMessageAndEndsTheRevisions()
    {
        List<string> arguments = RevertService.BuildArguments(Sha);

        // The trailing "--" keeps a revision that is also a file's name from being read as a path.
        Assert.Equal(["revert", "--no-edit", Sha, "--"], arguments);
    }

    [Fact]
    public void BuildArguments_NamesTheParentToKeepForAMerge()
        => Assert.Equal(["revert", "--no-edit", "--mainline", "1", Sha, "--"], RevertService.BuildArguments(Sha, 1));

    [Theory]
    [InlineData("-")]
    [InlineData("--abort")]
    [InlineData("-n")]
    public void BuildArguments_RefusesARevisionThatWouldBeReadAsAnOption(string revision)
        => Assert.Throws<ArgumentException>(() => RevertService.BuildArguments(revision));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildArguments_RefusesABlankRevision(string revision)
        => Assert.Throws<ArgumentException>(() => RevertService.BuildArguments(revision));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BuildArguments_RefusesAParentThatDoesNotExist(int mainline)
        => Assert.Throws<ArgumentOutOfRangeException>(() => RevertService.BuildArguments(Sha, mainline));

    [Fact]
    public void Classify_ASuccessIsARevert()
    {
        RevertOutcome outcome = RevertService.Classify(0, "[main 445b06a] Revert \"Add c\"\n", string.Empty);

        Assert.Equal(RevertResultKind.Reverted, outcome.Kind);
        Assert.True(outcome.CommittedAnything);
    }

    [Fact]
    public void Classify_ReadsAConflict()
    {
        RevertOutcome outcome = RevertService.Classify(
            1,
            "Auto-merging a\nCONFLICT (content): Merge conflict in a\n",
            "error: could not revert eed7a94... two\nhint: After resolving the conflicts, mark them with\n");

        Assert.Equal(RevertResultKind.Conflicted, outcome.Kind);
        Assert.False(outcome.CommittedAnything);
        Assert.Contains("nothing changed", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_ReadsAChangeThatIsAlreadyUndone()
    {
        RevertOutcome outcome = RevertService.Classify(1, "On branch main\nnothing to commit, working tree clean\n", string.Empty);

        Assert.Equal(RevertResultKind.NothingToRevert, outcome.Kind);
    }

    [Theory]
    [InlineData("error: your local changes would be overwritten by revert.\nhint: commit your changes or stash them to proceed.\nfatal: revert failed\n")]
    [InlineData("error: Your local changes to the following files would be overwritten by merge:\n\tc\nPlease commit your changes or stash them before you merge.\nAborting\nfatal: revert failed\n")]
    public void Classify_ReadsWorkInTheWay(string standardError)
    {
        RevertOutcome outcome = RevertService.Classify(128, string.Empty, standardError);

        Assert.Equal(RevertResultKind.Failed, outcome.Kind);
        Assert.Equal("Uncommitted work is in the way. Commit it or stash it, then revert.", outcome.Message);
    }

    [Fact]
    public void Classify_ReadsAMergeWithNoParentNamed()
    {
        RevertOutcome outcome = RevertService.Classify(
            128,
            string.Empty,
            "error: commit f3786fbccb296fd875c1e0ce9188af8fe8c57c99 is a merge but no -m option was given.\nfatal: revert failed\n");

        Assert.Equal(RevertResultKind.Failed, outcome.Kind);
        Assert.Contains("merge", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_PassesAnyOtherRefusalOnAsGitsFirstLine()
    {
        RevertOutcome outcome = RevertService.Classify(128, string.Empty, "fatal: bad revision 'nope'\nmore\n");

        Assert.Equal(RevertResultKind.Failed, outcome.Kind);
        Assert.Equal("fatal: bad revision 'nope'", outcome.Message);
    }
}
