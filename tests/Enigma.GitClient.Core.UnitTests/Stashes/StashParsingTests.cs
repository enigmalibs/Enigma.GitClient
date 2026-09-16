using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.Stashes;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Stashes;

/// <summary>
/// Covers the stash listing's format. A stash message is free text a user typed, so the separator
/// has to be one they cannot type by accident — which is the whole reason the listing is not read
/// in its human-readable form.
/// </summary>
public sealed class StashParsingTests
{
    private const char Unit = (char)31;

    private static string Line(string reference, string sha, string subject, string date)
        => string.Join(Unit, reference, sha, subject, date);

    [Fact]
    public void Parse_ReadsAnEntry()
    {
        StashEntry entry = Assert.Single(StashParsingTests.ParseOne(
            "stash@{0}",
            "1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d",
            "WIP on main: 9f8e7d6 Add the readme",
            "2026-09-15T18:30:00+02:00"));

        Assert.Equal(0, entry.Index);
        Assert.Equal("1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d", entry.Sha);
        Assert.Equal("1a2b3c4", entry.ShortSha);
        Assert.Equal("main", entry.Branch);
        Assert.Equal("9f8e7d6 Add the readme", entry.Message);
        Assert.Equal("stash@{0}", entry.Reference);
    }

    private static IReadOnlyList<StashEntry> ParseOne(string reference, string sha, string subject, string date)
        => StashService.Parse(Line(reference, sha, subject, date));

    [Fact]
    public void Parse_ReadsAWholeListInOrder()
    {
        string payload = string.Join('\n',
        [
            Line("stash@{0}", "aaaa", "On feature/login: half the login form", "2026-09-15T18:30:00+02:00"),
            Line("stash@{1}", "bbbb", "WIP on main: 1234567 Add the readme", "2026-09-14T10:00:00+02:00"),
            Line("stash@{2}", "cccc", "On main: the thing I was doing", "2026-09-13T09:00:00+02:00"),
        ]);

        IReadOnlyList<StashEntry> entries = StashService.Parse(payload);

        Assert.Equal(3, entries.Count);
        Assert.Equal([0, 1, 2], entries.Select(entry => entry.Index));
        Assert.Equal("feature/login", entries[0].Branch);
        Assert.Equal("half the login form", entries[0].Message);
        Assert.Equal("the thing I was doing", entries[2].Message);
    }

    [Fact]
    public void Parse_KeepsAMessageThatContainsTheHumanSeparator()
    {
        StashEntry entry = Assert.Single(ParseOne(
            "stash@{0}",
            "aaaa",
            "On main: refactor: split the parser: properly",
            "2026-09-15T18:30:00+02:00"));

        // The human-readable listing separates on ": ", which this message contains twice. That is
        // exactly why the machine-readable format exists.
        Assert.Equal("main", entry.Branch);
        Assert.Equal("refactor: split the parser: properly", entry.Message);
    }

    [Fact]
    public void Parse_KeepsAMessageWithNoRecognisedPrefix()
    {
        StashEntry entry = Assert.Single(ParseOne(
            "stash@{0}",
            "aaaa",
            "something git wrote differently",
            "2026-09-15T18:30:00+02:00"));

        Assert.Equal(string.Empty, entry.Branch);
        Assert.Equal("something git wrote differently", entry.Message);
    }

    [Fact]
    public void Parse_ReadsTheDate()
    {
        StashEntry entry = Assert.Single(ParseOne(
            "stash@{0}",
            "aaaa",
            "On main: work",
            "2026-09-15T18:30:00+02:00"));

        Assert.Equal(new DateTimeOffset(2026, 9, 15, 18, 30, 0, TimeSpan.FromHours(2)), entry.When);
    }

    [Fact]
    public void Parse_SurvivesADateItCannotRead()
    {
        StashEntry entry = Assert.Single(ParseOne("stash@{0}", "aaaa", "On main: work", "not a date"));

        Assert.Equal(DateTimeOffset.MinValue, entry.When);
    }

    [Fact]
    public void Parse_SkipsATruncatedLine()
    {
        string payload = string.Join('\n',
        [
            "stash@{0}" + Unit + "aaaa",
            Line("stash@{1}", "bbbb", "On main: fine", "2026-09-15T18:30:00+02:00"),
        ]);

        Assert.Equal(1, StashService.Parse(payload).Single().Index);
    }

    [Fact]
    public void Parse_HandlesAnEmptyStash()
        => Assert.Empty(StashService.Parse(string.Empty));

    [Fact]
    public void Parse_RejectsNull()
        => Assert.Throws<ArgumentNullException>(() => StashService.Parse(null!));

    [Theory]
    [InlineData("stash@{0}", 0)]
    [InlineData("stash@{7}", 7)]
    [InlineData("stash@{42}", 42)]
    [InlineData("refs/stash", 0)]
    [InlineData("stash@{}", 0)]
    [InlineData("nonsense", 0)]
    public void ParseIndex_ReadsThePositionOutOfAReference(string reference, int expected)
        => Assert.Equal(expected, StashService.ParseIndex(reference));

    [Theory]
    [InlineData("WIP on main: 1234567 Subject", "main", "1234567 Subject")]
    [InlineData("On feature/x: what I typed", "feature/x", "what I typed")]
    [InlineData("On main", "", "main")]
    [InlineData("just a message", "", "just a message")]
    public void CleanMessage_SplitsTheBranchOffTheMessage(string subject, string branch, string message)
    {
        Assert.Equal(message, StashService.CleanMessage(subject, out string parsed));
        Assert.Equal(branch, parsed);
    }

    [Theory]
    [InlineData(0, "stash@{0}")]
    [InlineData(3, "stash@{3}")]
    public void Reference_BuildsWhatEveryStashCommandTakes(int index, string expected)
        => Assert.Equal(expected, StashService.Reference(index));

    [Fact]
    public void Reference_RefusesANegativePosition()
        => Assert.Throws<ArgumentOutOfRangeException>(() => StashService.Reference(-1));

    [Fact]
    public void FormatTemplate_SeparatesOnSomethingAMessageCannotContain()
    {
        // %x1f is the unit separator: a user cannot type it into a stash message, and a colon or a
        // tab they certainly can.
        Assert.Contains("%x1f", StashService.FormatTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain(":", StashService.FormatTemplate, StringComparison.Ordinal);
    }
}
