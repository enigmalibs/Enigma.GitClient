using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Squash;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Squash;

/// <summary>
/// Covers the commit the squash writes — the argument vector, the message it starts from and the
/// cleanup it records — and the sentence a switch blocked by local changes becomes.
/// </summary>
public sealed class SquashArgumentsTests
{
    private const string Tip = "0123456789abcdef0123456789abcdef01234567";
    private const string Base = "89abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void BuildCommitTreeArguments_RecordsTheTipsTreeOnTheBase()
    {
        List<string> arguments = SquashService.BuildCommitTreeArguments(Tip, Base, "msg.txt");

        Assert.Equal(["commit-tree", $"{Tip}^{{tree}}", "-p", Base, "-F", "msg.txt"], arguments);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("--all")]
    [InlineData("-p")]
    public void BuildCommitTreeArguments_RefusesATipThatWouldBeReadAsAnOption(string tip)
        => Assert.Throws<ArgumentException>(() => SquashService.BuildCommitTreeArguments(tip, Base, "msg.txt"));

    [Theory]
    [InlineData("-")]
    [InlineData("--all")]
    public void BuildCommitTreeArguments_RefusesABaseThatWouldBeReadAsAnOption(string baseRevision)
        => Assert.Throws<ArgumentException>(() => SquashService.BuildCommitTreeArguments(Tip, baseRevision, "msg.txt"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildCommitTreeArguments_RefusesABlankRevision(string revision)
    {
        Assert.Throws<ArgumentException>(() => SquashService.BuildCommitTreeArguments(revision, Base, "msg.txt"));
        Assert.Throws<ArgumentException>(() => SquashService.BuildCommitTreeArguments(Tip, revision, "msg.txt"));
    }

    [Fact]
    public void ComposeDefaultMessage_LeavesTheTitleEmptyAndListsTheSubjects()
    {
        string message = SquashService.ComposeDefaultMessage(["Add the overview (PHASE01)", "Add the startup (PHASE02)"]);

        Assert.Equal("\n\n- Add the overview (PHASE01)\n- Add the startup (PHASE02)", message);
        Assert.Equal(string.Empty, SquashService.TitleOf(message));
    }

    [Fact]
    public void ComposeDefaultMessage_WithNoSubjectIsEmpty()
        => Assert.Equal(string.Empty, SquashService.ComposeDefaultMessage([]));

    [Theory]
    [InlineData("Architecture docs\n\n- one", "Architecture docs")]
    [InlineData("  Architecture docs  \r\n\r\n- one", "Architecture docs")]
    [InlineData("Only a title", "Only a title")]
    [InlineData("\nArchitecture docs", "")]
    [InlineData("   \n- one", "")]
    [InlineData("", "")]
    public void TitleOf_IsTheFirstLineAsTyped(string message, string title)
        => Assert.Equal(title, SquashService.TitleOf(message));

    [Fact]
    public void CleanUpMessage_DoesWhatGitCommitDoesByDefault()
    {
        string cleaned = SquashService.CleanUpMessage("Title  \r\n\r\n\r\n- one \n- two\t\n\n\n");

        // LF endings, no trailing spaces, one blank line at most, one newline at the end.
        Assert.Equal("Title\n\n- one\n- two\n", cleaned);
    }

    [Fact]
    public void CleanUpMessage_KeepsCharactersOutsideAscii()
        => Assert.Equal("Ajouté l'été\n\n- déjà\n", SquashService.CleanUpMessage("Ajouté l'été\n\n- déjà"));

    [Fact]
    public void DescribeBlockedSwitch_NamesTheFilesGitListed()
    {
        string sentence = SquashService.DescribeBlockedSwitch(
            "error: Your local changes to the following files would be overwritten by checkout:\n"
            + "\tdocs/a.md\n\tsrc/b.cs\n"
            + "Please commit your changes or stash them before you switch branches.\nAborting\n");

        Assert.StartsWith("Uncommitted changes to docs/a.md, src/b.cs are in the way", sentence, StringComparison.Ordinal);
        Assert.Contains("nothing was created", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeBlockedSwitch_WithoutAFileStillSaysNothingWasCreated()
    {
        string sentence = SquashService.DescribeBlockedSwitch("error: something would be overwritten\n");

        Assert.StartsWith("Uncommitted changes are in the way", sentence, StringComparison.Ordinal);
        Assert.Contains("nothing was created", sentence, StringComparison.Ordinal);
    }
}
