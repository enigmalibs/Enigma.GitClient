using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Refs;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Refs;

public sealed class RefParserTests
{
    private const char Fs = RefParser.FieldSeparator;

    /// <summary>
    /// Builds a record in exactly the field order of <see cref="RefParser.FormatTemplate"/>.
    /// </summary>
    private static string Record(
        string refName,
        string objectType = "commit",
        string objectName = "1111111111111111111111111111111111111111",
        string dereferenced = "",
        string head = " ",
        string upstreamShort = "",
        string upstreamTrack = "",
        string authorName = "Ada Lovelace",
        string authorEmail = "<ada@example.com>",
        string committerDate = "2026-02-01T09:00:00+00:00",
        string derefAuthorName = "",
        string derefAuthorEmail = "",
        string derefCommitterDate = "",
        string taggerName = "",
        string taggerEmail = "",
        string taggerDate = "",
        string subject = "A subject")
        => string.Join(
            Fs,
            refName,
            objectType,
            objectName,
            dereferenced,
            head,
            upstreamShort,
            upstreamTrack,
            authorName,
            authorEmail,
            committerDate,
            derefAuthorName,
            derefAuthorEmail,
            derefCommitterDate,
            taggerName,
            taggerEmail,
            taggerDate,
            subject);

    [Fact]
    public void ParseRecord_ReadsALocalBranchWithAnUpstreamAndTracking()
    {
        GitBranch branch = Assert.IsType<GitBranch>(RefParser.ParseRecord(Record(
            "refs/heads/main",
            head: "*",
            upstreamShort: "origin/main",
            upstreamTrack: "[ahead 2, behind 1]",
            subject: "Extend the application file")));

        Assert.Equal("main", branch.ShortName);
        Assert.Equal(GitRefKind.LocalBranch, branch.Kind);
        Assert.False(branch.IsRemote);
        Assert.True(branch.IsCurrent);
        Assert.Equal("origin/main", branch.UpstreamShortName);
        Assert.Equal(2, branch.Tracking.Ahead);
        Assert.Equal(1, branch.Tracking.Behind);
        Assert.True(branch.Tracking.HasDiverged);
        Assert.Equal("Ada Lovelace", branch.TipAuthor.Name);
        Assert.Equal("ada@example.com", branch.TipAuthor.Email);
        Assert.Equal("Extend the application file", branch.TipSubject);
        Assert.Null(branch.RemoteName);
    }

    [Fact]
    public void ParseRecord_ReadsALocalBranchWithNoUpstream()
    {
        GitBranch branch = Assert.IsType<GitBranch>(RefParser.ParseRecord(Record("refs/heads/topic")));

        Assert.Null(branch.UpstreamShortName);
        Assert.Equal(BranchTracking.None, branch.Tracking);
        Assert.True(branch.Tracking.IsSynchronised);
        Assert.False(branch.IsCurrent);
    }

    [Fact]
    public void ParseRecord_ReadsARemoteTrackingBranch()
    {
        GitBranch branch = Assert.IsType<GitBranch>(
            RefParser.ParseRecord(Record("refs/remotes/origin/feature/nested-name")));

        Assert.True(branch.IsRemote);
        Assert.Equal(GitRefKind.RemoteBranch, branch.Kind);
        Assert.Equal("origin/feature/nested-name", branch.ShortName);
        Assert.Equal("origin", branch.RemoteName);
        Assert.Equal("feature/nested-name", branch.NameWithoutRemote);
    }

    [Fact]
    public void ParseRecord_ReadsALightweightTag()
    {
        GitTag tag = Assert.IsType<GitTag>(RefParser.ParseRecord(Record(
            "refs/tags/v0.9",
            objectType: "commit",
            objectName: "2222222222222222222222222222222222222222",
            subject: "The tagged commit subject")));

        Assert.Equal("v0.9", tag.ShortName);
        Assert.Equal(GitRefKind.Tag, tag.Kind);
        Assert.False(tag.IsAnnotated);
        Assert.Null(tag.TagObjectSha);
        Assert.Null(tag.Tagger);
        Assert.Equal(string.Empty, tag.Message);
        Assert.Equal("2222222222222222222222222222222222222222", tag.TargetSha);
    }

    [Fact]
    public void ParseRecord_ReadsAnAnnotatedTag()
    {
        GitTag tag = Assert.IsType<GitTag>(RefParser.ParseRecord(Record(
            "refs/tags/v1.0",
            objectType: "tag",
            objectName: "3333333333333333333333333333333333333333",
            dereferenced: "4444444444444444444444444444444444444444",
            derefCommitterDate: "2026-02-02T12:00:00+00:00",
            taggerName: "Grace Hopper",
            taggerEmail: "<grace@example.com>",
            taggerDate: "2026-02-03T08:30:00+02:00",
            subject: "First release with several words")));

        Assert.True(tag.IsAnnotated);
        Assert.Equal("3333333333333333333333333333333333333333", tag.TagObjectSha);
        Assert.Equal("4444444444444444444444444444444444444444", tag.TargetSha);
        Assert.Equal("Grace Hopper", tag.Tagger!.Name);
        Assert.Equal("grace@example.com", tag.Tagger.Email);
        Assert.Equal(TimeSpan.FromHours(2), tag.Tagger.When.Offset);
        Assert.Equal("First release with several words", tag.Message);
        Assert.Equal(
            new DateTimeOffset(2026, 2, 2, 12, 0, 0, TimeSpan.Zero),
            tag.TargetDate.ToUniversalTime());
    }

    [Fact]
    public void ParseRecord_ReadsTheStash()
    {
        GitRef reference = RefParser.ParseRecord(Record("refs/stash"));

        Assert.Equal(GitRefKind.Stash, reference.Kind);
        Assert.Equal("stash", reference.ShortName);
    }

    [Fact]
    public void ParseRecord_ReadsANoteRef()
        => Assert.Equal(GitRefKind.Note, RefParser.ParseRecord(Record("refs/notes/commits")).Kind);

    [Fact]
    public void ParseRecord_ReadsAnUnknownNamespaceAsOther()
        => Assert.Equal(GitRefKind.Other, RefParser.ParseRecord(Record("refs/pull/42/head")).Kind);

    [Fact]
    public void Parse_ReadsEveryRecordOfAPayload()
    {
        string payload = string.Join(
            "\n",
            Record("refs/heads/main", head: "*"),
            Record("refs/heads/topic"),
            Record("refs/remotes/origin/main"),
            Record("refs/tags/v0.9"));

        IReadOnlyList<GitRef> refs = RefParser.Parse(payload + "\n");

        Assert.Equal(4, refs.Count);
    }

    [Fact]
    public void Parse_IgnoresBlankLines()
        => Assert.Single(RefParser.Parse("\n" + Record("refs/heads/main") + "\n\n"));

    [Fact]
    public void ParseRecord_RejectsATruncatedRecord()
        => Assert.Throws<FormatException>(() => RefParser.ParseRecord($"refs/heads/main{Fs}commit"));

    [Theory]
    [InlineData("", 0, 0, false)]
    [InlineData("   ", 0, 0, false)]
    [InlineData("[ahead 3]", 3, 0, false)]
    [InlineData("[behind 4]", 0, 4, false)]
    [InlineData("[ahead 2, behind 1]", 2, 1, false)]
    [InlineData("[behind 1, ahead 2]", 2, 1, false)]
    [InlineData("[gone]", 0, 0, true)]
    [InlineData("[GONE]", 0, 0, true)]
    [InlineData("[something unexpected]", 0, 0, false)]
    public void ParseTracking_ReadsGitsTrackAtom(string track, int ahead, int behind, bool gone)
    {
        BranchTracking tracking = RefParser.ParseTracking(track);

        Assert.Equal(ahead, tracking.Ahead);
        Assert.Equal(behind, tracking.Behind);
        Assert.Equal(gone, tracking.IsUpstreamGone);
    }

    [Fact]
    public void ParseRecord_ReadsANonAsciiBranchNameAndAuthor()
    {
        GitBranch branch = Assert.IsType<GitBranch>(RefParser.ParseRecord(Record(
            "refs/heads/fonctionnalité/été",
            authorName: "Josué Clément",
            authorEmail: "<josué@exämple.ch>")));

        Assert.Equal("fonctionnalité/été", branch.ShortName);
        Assert.Equal("Josué Clément", branch.TipAuthor.Name);
        Assert.Equal("josué@exämple.ch", branch.TipAuthor.Email);
    }

    [Fact]
    public void FormatTemplate_ProducesSeventeenFields()
        => Assert.Equal(16, RefParser.FormatTemplate.Split("%00").Length - 1);
}
