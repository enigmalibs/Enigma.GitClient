using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.History;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.History;

public sealed class CommitLogParserTests
{
    private const char Fs = CommitLogParser.FieldSeparator;
    private const char Rs = CommitLogParser.RecordSeparator;

    private static string Record(
        string sha,
        string parents,
        string authorName = "Ada Lovelace",
        string authorEmail = "ada@example.com",
        string authorDate = "2026-03-04T10:11:12+01:00",
        string committerName = "Ada Lovelace",
        string committerEmail = "ada@example.com",
        string committerDate = "2026-03-04T10:11:12+01:00",
        string subject = "A subject",
        string body = "")
        => string.Join(
            Fs,
            sha,
            parents,
            authorName,
            authorEmail,
            authorDate,
            committerName,
            committerEmail,
            committerDate,
            subject,
            body) + Rs;

    [Fact]
    public void Parse_ReadsASingleCommit()
    {
        IReadOnlyList<GitCommit> commits = CommitLogParser.Parse(
            Record("1111111111111111111111111111111111111111", "2222222222222222222222222222222222222222"));

        GitCommit commit = Assert.Single(commits);
        Assert.Equal("1111111111111111111111111111111111111111", commit.Sha);
        Assert.Equal("1111111", commit.ShortSha);
        Assert.Equal(["2222222222222222222222222222222222222222"], commit.ParentShas);
        Assert.Equal("Ada Lovelace", commit.Author.Name);
        Assert.Equal("ada@example.com", commit.Author.Email);
        Assert.Equal("A subject", commit.Subject);
        Assert.Equal(string.Empty, commit.Body);
        Assert.False(commit.IsMerge);
        Assert.False(commit.IsRoot);
    }

    [Fact]
    public void Parse_ReadsSeveralRecordsSeparatedByGitsOwnNewline()
    {
        string payload = string.Concat(
            Record("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", subject: "Second"),
            "\n",
            Record("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", string.Empty, subject: "First"));

        IReadOnlyList<GitCommit> commits = CommitLogParser.Parse(payload);

        Assert.Equal(2, commits.Count);
        Assert.Equal("Second", commits[0].Subject);
        Assert.Equal("First", commits[1].Subject);
        Assert.True(commits[1].IsRoot);
    }

    [Fact]
    public void Parse_KeepsAMultiLineBodyIntact()
    {
        const string body = "First body line.\n\nA second paragraph.\n\n- a bullet\n- another bullet";

        IReadOnlyList<GitCommit> commits = CommitLogParser.Parse(
            Record("cccccccccccccccccccccccccccccccccccccccc", string.Empty, subject: "Subject", body: body));

        Assert.Equal(body, Assert.Single(commits).Body);
    }

    [Fact]
    public void Parse_ReadsAMergeWithTwoParents()
    {
        GitCommit commit = Assert.Single(CommitLogParser.Parse(Record(
            "dddddddddddddddddddddddddddddddddddddddd",
            "1111111111111111111111111111111111111111 2222222222222222222222222222222222222222")));

        Assert.True(commit.IsMerge);
        Assert.Equal(2, commit.ParentShas.Count);
    }

    [Fact]
    public void Parse_ReadsAnOctopusMergeWithThreeParents()
    {
        GitCommit commit = Assert.Single(CommitLogParser.Parse(Record(
            "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            "1111111111111111111111111111111111111111 2222222222222222222222222222222222222222 "
            + "3333333333333333333333333333333333333333")));

        Assert.True(commit.IsMerge);
        Assert.Equal(3, commit.ParentShas.Count);
    }

    [Fact]
    public void Parse_ReadsNonAsciiIdentities()
    {
        GitCommit commit = Assert.Single(CommitLogParser.Parse(Record(
            "ffffffffffffffffffffffffffffffffffffffff",
            string.Empty,
            authorName: "Josué Clément",
            authorEmail: "josué@exämple.ch",
            subject: "Ajout du rapport financier")));

        Assert.Equal("Josué Clément", commit.Author.Name);
        Assert.Equal("josué@exämple.ch", commit.Author.Email);
        Assert.Equal("Ajout du rapport financier", commit.Subject);
    }

    [Fact]
    public void Parse_PreservesTheUtcOffsetOfTheRecordedDates()
    {
        GitCommit commit = Assert.Single(CommitLogParser.Parse(Record(
            "1010101010101010101010101010101010101010",
            string.Empty,
            authorDate: "2026-03-04T10:11:12+05:30",
            committerDate: "2026-03-04T06:11:12+01:00")));

        Assert.Equal(TimeSpan.FromMinutes(330), commit.Author.When.Offset);
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 10, 11, 12, TimeSpan.FromMinutes(330)), commit.Author.When);
        Assert.Equal(TimeSpan.FromHours(1), commit.Committer.When.Offset);
    }

    [Fact]
    public void Parse_KeepsTheAuthorAndCommitterApart()
    {
        GitCommit commit = Assert.Single(CommitLogParser.Parse(Record(
            "2020202020202020202020202020202020202020",
            string.Empty,
            authorName: "Ada",
            authorEmail: "ada@example.com",
            committerName: "Grace",
            committerEmail: "grace@example.com")));

        Assert.Equal("Ada", commit.Author.Name);
        Assert.Equal("Grace", commit.Committer.Name);
    }

    [Fact]
    public void Parse_KeepsASubjectContainingSeparatorNeighbourCharacters()
    {
        const string subject = "Fix: handle ,  and  in the parser";

        GitCommit commit = Assert.Single(CommitLogParser.Parse(
            Record("3030303030303030303030303030303030303030", string.Empty, subject: subject)));

        Assert.Equal(subject, commit.Subject);
    }

    [Fact]
    public void Parse_TreatsALaterFieldSeparatorAsPartOfTheBody()
    {
        // The body is the last field, so a stray separator inside it is re-joined rather than
        // making the record look malformed.
        string body = $"before{Fs}after";

        GitCommit commit = Assert.Single(CommitLogParser.Parse(
            Record("4040404040404040404040404040404040404040", string.Empty, body: body)));

        Assert.Equal(body, commit.Body);
    }

    [Fact]
    public void Parse_ReturnsNothingForAnEmptyPayload()
        => Assert.Empty(CommitLogParser.Parse(string.Empty));

    [Fact]
    public void ParseRecord_RejectsATruncatedRecord()
        => Assert.Throws<FormatException>(() => CommitLogParser.ParseRecord($"sha{Fs}parents{Fs}name"));

    [Fact]
    public void Parse_FallsBackToTheMinimumDateWhenGitReportsSomethingUnparseable()
    {
        GitCommit commit = Assert.Single(CommitLogParser.Parse(
            Record("5050505050505050505050505050505050505050", string.Empty, authorDate: "not-a-date")));

        Assert.Equal(DateTimeOffset.MinValue, commit.Author.When);
    }

    [Fact]
    public void FormatTemplate_ProducesTenFields()
    {
        // Nine field separators means ten fields; the parser depends on exactly that.
        Assert.Equal(9, CommitLogParser.FormatTemplate.Split("%x1f").Length - 1);
        Assert.EndsWith("%x00", CommitLogParser.FormatTemplate, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_HandlesAPayloadWithATrailingRecordSeparator()
    {
        string payload = Record("6060606060606060606060606060606060606060", string.Empty);

        Assert.Single(CommitLogParser.Parse(payload));
        Assert.Single(CommitLogParser.Parse(payload + "\n"));
    }

    [Fact]
    public void Parse_ReadsALongPayloadWithoutLosingOrder()
    {
        string payload = string.Join(
            "\n",
            Enumerable.Range(0, 50).Select(index => Record(
                index.ToString("D40", System.Globalization.CultureInfo.InvariantCulture),
                string.Empty,
                subject: $"Commit {index.ToString(System.Globalization.CultureInfo.InvariantCulture)}")));

        IReadOnlyList<GitCommit> commits = CommitLogParser.Parse(payload);

        Assert.Equal(50, commits.Count);
        Assert.Equal("Commit 0", commits[0].Subject);
        Assert.Equal("Commit 49", commits[49].Subject);
    }
}
