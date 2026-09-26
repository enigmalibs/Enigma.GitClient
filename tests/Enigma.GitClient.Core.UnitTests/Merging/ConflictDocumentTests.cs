using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.Merging;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Merging;

/// <summary>
/// Covers the conflict document: how a three-way file becomes regions, and what resolving them
/// writes back.
/// </summary>
/// <remarks>
/// The preview and the bytes that reach disk come from the same method, so these tests are the
/// preview's correctness proof as much as the model's. Two of them render a whole file by choosing
/// one side everywhere and compare it against that side verbatim, which is the strongest statement
/// available: the document did not touch anything it was not asked to.
/// </remarks>
public sealed class ConflictDocumentTests
{
    private const string SimpleMerged =
        "one\n"
        + "<<<<<<< ours\n"
        + "our version\n"
        + "||||||| base\n"
        + "base version\n"
        + "=======\n"
        + "their version\n"
        + ">>>>>>> theirs\n"
        + "three\n";

    // ---------------------------------------------------------------- parsing git's own output

    [Fact]
    public void Parse_FindsTheStableTextAroundOneConflict()
    {
        ConflictDocument document = ConflictDocumentBuilder.Parse(SimpleMerged);

        Assert.Equal(3, document.Regions.Count);
        Assert.False(document.Regions[0].IsConflicted);
        Assert.True(document.Regions[1].IsConflicted);
        Assert.False(document.Regions[2].IsConflicted);

        Assert.Equal(["one\n"], document.Regions[0].Stable);
        Assert.Equal(["three\n"], document.Regions[2].Stable);
    }

    [Fact]
    public void Parse_KeepsAllThreeSidesOfTheConflict()
    {
        ConflictRegion region = ConflictDocumentBuilder.Parse(SimpleMerged).Conflicts.Single();

        Assert.Equal(["our version\n"], region.OurLines);
        Assert.Equal(["base version\n"], region.BaseLines);
        Assert.Equal(["their version\n"], region.TheirLines);
        Assert.True(region.NeedsDecision);
    }

    [Fact]
    public void Parse_HandlesSeveralConflictsWithTextBetweenThem()
    {
        string merged =
            "top\n"
            + "<<<<<<< ours\nfirst ours\n||||||| base\nfirst base\n=======\nfirst theirs\n>>>>>>> theirs\n"
            + "middle\n"
            + "<<<<<<< ours\nsecond ours\n||||||| base\nsecond base\n=======\nsecond theirs\n>>>>>>> theirs\n"
            + "bottom\n";

        ConflictDocument document = ConflictDocumentBuilder.Parse(merged);

        Assert.Equal(2, document.ConflictCount);
        Assert.Equal(5, document.Regions.Count);
        Assert.Equal(["middle\n"], document.Regions[2].Stable);
    }

    [Fact]
    public void Parse_HandlesAConflictAtTheVeryStartAndTheVeryEnd()
    {
        string merged =
            "<<<<<<< ours\nours\n||||||| base\nbase\n=======\ntheirs\n>>>>>>> theirs\n"
            + "middle\n"
            + "<<<<<<< ours\nours again\n||||||| base\nbase again\n=======\ntheirs again\n>>>>>>> theirs\n";

        ConflictDocument document = ConflictDocumentBuilder.Parse(merged);

        Assert.True(document.Regions[0].IsConflicted);
        Assert.True(document.Regions[^1].IsConflicted);
        Assert.Equal(2, document.ConflictCount);
    }

    [Fact]
    public void Parse_HandlesASideThatDeletedTheLines()
    {
        string merged = "<<<<<<< ours\n||||||| base\nbase\n=======\ntheirs\n>>>>>>> theirs\n";

        ConflictRegion region = ConflictDocumentBuilder.Parse(merged).Conflicts.Single();

        Assert.Empty(region.OurLines);
        Assert.Equal(["theirs\n"], region.TheirLines);
    }

    [Fact]
    public void Parse_HandlesAConflictWithNoBaseSection()
    {
        // A two-way conflict, which is what merge-file writes without --diff3.
        string merged = "<<<<<<< ours\nours\n=======\ntheirs\n>>>>>>> theirs\n";

        ConflictRegion region = ConflictDocumentBuilder.Parse(merged).Conflicts.Single();

        Assert.Equal(["ours\n"], region.OurLines);
        Assert.Empty(region.BaseLines);
        Assert.Equal(["theirs\n"], region.TheirLines);
    }

    [Fact]
    public void Parse_HandlesAFileWithNoConflictAtAll()
    {
        ConflictDocument document = ConflictDocumentBuilder.Parse("one\ntwo\n");

        Assert.Equal(0, document.ConflictCount);
        Assert.True(document.IsFullyResolved);
        Assert.Equal("one\ntwo\n", document.RenderPreview());
    }

    [Fact]
    public void Parse_RejectsNull()
        => Assert.Throws<ArgumentNullException>(() => ConflictDocumentBuilder.Parse(null!));

    // ---------------------------------------------------------------- rendering

    [Theory]
    [InlineData(ConflictResolution.Ours, "one\nour version\nthree\n")]
    [InlineData(ConflictResolution.Theirs, "one\ntheir version\nthree\n")]
    [InlineData(ConflictResolution.Base, "one\nbase version\nthree\n")]
    [InlineData(ConflictResolution.OursThenTheirs, "one\nour version\ntheir version\nthree\n")]
    [InlineData(ConflictResolution.TheirsThenOurs, "one\ntheir version\nour version\nthree\n")]
    public void RenderPreview_WritesWhatEachChoiceMeans(ConflictResolution resolution, string expected)
    {
        ConflictDocument document = ConflictDocumentBuilder.Parse(SimpleMerged);

        document.Conflicts.Single().Resolution = resolution;

        Assert.Equal(expected, document.RenderPreview());
        Assert.True(document.IsFullyResolved);
    }

    [Fact]
    public void RenderPreview_WritesWhateverTheReaderTyped()
    {
        ConflictDocument document = ConflictDocumentBuilder.Parse(SimpleMerged);
        ConflictRegion region = document.Conflicts.Single();

        region.Resolution = ConflictResolution.Custom;
        region.CustomText = "something else entirely";

        // The typed text gains the file's terminator, so the line after it is still a line.
        Assert.Equal("one\nsomething else entirely\nthree\n", document.RenderPreview());
    }

    [Fact]
    public void RenderPreview_MarksAnUnresolvedRegionInPlace()
    {
        string preview = ConflictDocumentBuilder.Parse(SimpleMerged).RenderPreview();

        // Dropping it silently would say the file is finished when it is not.
        Assert.Contains("<<<<<<<", preview, StringComparison.Ordinal);
        Assert.Contains("=======", preview, StringComparison.Ordinal);
        Assert.Contains(">>>>>>>", preview, StringComparison.Ordinal);
        Assert.Contains("our version", preview, StringComparison.Ordinal);
        Assert.Contains("their version", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void IsFullyResolved_FlipsOnlyWhenTheLastRegionIsDecided()
    {
        string merged =
            "<<<<<<< ours\na\n||||||| base\nb\n=======\nc\n>>>>>>> theirs\n"
            + "middle\n"
            + "<<<<<<< ours\nd\n||||||| base\ne\n=======\nf\n>>>>>>> theirs\n";

        ConflictDocument document = ConflictDocumentBuilder.Parse(merged);

        Assert.False(document.IsFullyResolved);
        Assert.Equal(0, document.ResolvedCount);

        document.Regions[0].Resolution = ConflictResolution.Ours;

        Assert.False(document.IsFullyResolved);
        Assert.Equal(1, document.ResolvedCount);

        document.Regions[^1].Resolution = ConflictResolution.Theirs;

        Assert.True(document.IsFullyResolved);
        Assert.Equal(2, document.ResolvedCount);
    }

    [Fact]
    public void ResolveAll_DecidesEveryRegionAtOnce()
    {
        string merged =
            "<<<<<<< ours\na\n||||||| base\nb\n=======\nc\n>>>>>>> theirs\n"
            + "<<<<<<< ours\nd\n||||||| base\ne\n=======\nf\n>>>>>>> theirs\n";

        ConflictDocument document = ConflictDocumentBuilder.Parse(merged);

        document.ResolveAll(ConflictResolution.Theirs);

        Assert.True(document.IsFullyResolved);
        Assert.Equal("c\nf\n", document.RenderPreview());
    }

    // ---------------------------------------------------------------- byte fidelity

    [Fact]
    public void RenderPreview_KeepsWindowsLineEndingsExactly()
    {
        string merged =
            "one\r\n"
            + "<<<<<<< ours\r\nour version\r\n||||||| base\r\nbase version\r\n=======\r\ntheir version\r\n>>>>>>> theirs\r\n"
            + "three\r\n";

        ConflictDocument document = ConflictDocumentBuilder.Parse(merged);

        Assert.Equal("\r\n", document.LineEnding);

        document.Conflicts.Single().Resolution = ConflictResolution.Ours;

        // Resolving a conflict must not rewrite the whole file's endings — the classic diff-noise bug.
        Assert.Equal("one\r\nour version\r\nthree\r\n", document.RenderPreview());
    }

    [Fact]
    public void RenderPreview_KeepsAMissingTrailingNewline()
    {
        string merged = "<<<<<<< ours\nours\n||||||| base\nbase\n=======\ntheirs\n>>>>>>> theirs\nlast line with none";

        ConflictDocument document = ConflictDocumentBuilder.Parse(merged);
        document.Conflicts.Single().Resolution = ConflictResolution.Ours;

        string preview = document.RenderPreview();

        Assert.Equal("ours\nlast line with none", preview);
        Assert.False(preview.EndsWith('\n'));
    }

    [Fact]
    public void SplitLines_RebuildsTheInputExactly()
    {
        foreach (string text in new[] { "a\nb\n", "a\r\nb\r\n", "a\nb", string.Empty, "\n", "only" })
        {
            Assert.Equal(text, string.Concat(ConflictDocument.SplitLines(text)));
        }
    }

    [Theory]
    [InlineData("a\nb\n", "\n")]
    [InlineData("a\r\nb\r\n", "\r\n")]
    [InlineData("no newline", "\n")]
    [InlineData("", "\n")]
    public void DetectLineEnding_ReadsWhatTheFileUses(string text, string expected)
        => Assert.Equal(expected, ConflictDocument.DetectLineEnding(text));

    // ---------------------------------------------------------------- the direct merge

    [Fact]
    public void Merge_FindsTheConflictWithoutAskingGit()
    {
        ConflictDocument document = ConflictDocumentBuilder.Merge(
            "one\nbase\nthree\n",
            "one\nours\nthree\n",
            "one\ntheirs\nthree\n");

        ConflictRegion region = Assert.Single(document.Conflicts);

        Assert.Equal(["ours\n"], region.OurLines);
        Assert.Equal(["base\n"], region.BaseLines);
        Assert.Equal(["theirs\n"], region.TheirLines);
    }

    [Fact]
    public void Merge_TakesAChangeOnlyOneSideMade()
    {
        ConflictDocument document = ConflictDocumentBuilder.Merge(
            "one\ntwo\nthree\n",
            "one\ntwo\nthree\n",
            "one\ntheir change\nthree\n");

        // Nothing to decide: only they touched it.
        Assert.Equal(0, document.ConflictCount);
        Assert.Equal("one\ntheir change\nthree\n", document.RenderPreview());
    }

    [Fact]
    public void Merge_TakesAChangeBothSidesMadeIdentically()
    {
        ConflictDocument document = ConflictDocumentBuilder.Merge(
            "one\ntwo\nthree\n",
            "one\nthe same change\nthree\n",
            "one\nthe same change\nthree\n");

        Assert.Equal(0, document.ConflictCount);
        Assert.Equal("one\nthe same change\nthree\n", document.RenderPreview());
    }

    [Fact]
    public void Merge_CombinesChangesToDifferentPartsOfTheFile()
    {
        ConflictDocument document = ConflictDocumentBuilder.Merge(
            "one\ntwo\nthree\nfour\nfive\n",
            "one CHANGED\ntwo\nthree\nfour\nfive\n",
            "one\ntwo\nthree\nfour\nfive CHANGED\n");

        Assert.Equal(0, document.ConflictCount);
        Assert.Equal("one CHANGED\ntwo\nthree\nfour\nfive CHANGED\n", document.RenderPreview());
    }

    [Fact]
    public void Merge_HandlesASideThatDeletedLines()
    {
        ConflictDocument document = ConflictDocumentBuilder.Merge(
            "one\ntwo\nthree\n",
            "one\nour version\nthree\n",
            "one\nthree\n");

        ConflictRegion region = Assert.Single(document.Conflicts);

        Assert.Equal(["our version\n"], region.OurLines);
        Assert.Empty(region.TheirLines);
    }

    [Fact]
    public void Merge_HandlesAFileWithNoBaseAtAll()
    {
        ConflictDocument document = ConflictDocumentBuilder.Merge(null, "ours\n", "theirs\n");

        ConflictRegion region = Assert.Single(document.Conflicts);

        Assert.Empty(region.BaseLines);
        Assert.Equal(["ours\n"], region.OurLines);
        Assert.Equal(["theirs\n"], region.TheirLines);
    }

    // ---------------------------------------------------------------- the strongest statement

    [Theory]
    [InlineData(ConflictResolution.Ours)]
    [InlineData(ConflictResolution.Theirs)]
    public void ChoosingOneSideEverywhereReproducesThatSideExactly(ConflictResolution resolution)
    {
        const string ours = "header\nour first\nshared\nour second\nfooter\n";
        const string theirs = "header\ntheir first\nshared\ntheir second\nfooter\n";

        ConflictDocument document = ConflictDocumentBuilder.Merge(
            "header\nbase first\nshared\nbase second\nfooter\n",
            ours,
            theirs);

        Assert.Equal(2, document.ConflictCount);

        document.ResolveAll(resolution);

        // If the document ever touched anything it was not asked to, this is where it shows.
        Assert.Equal(resolution == ConflictResolution.Ours ? ours : theirs, document.RenderPreview());
    }

    [Fact]
    public void ChoosingOneSideEverywhereReproducesItThroughGitsOwnOutputToo()
    {
        ConflictDocument document = ConflictDocumentBuilder.Parse(SimpleMerged);

        document.ResolveAll(ConflictResolution.Theirs);

        Assert.Equal("one\ntheir version\nthree\n", document.RenderPreview());
    }

    [Fact]
    public void Document_RejectsAnEmptyLineEnding()
        => Assert.Throws<ArgumentException>(() => new ConflictDocument([], string.Empty));
}
