using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.Diff;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Diff;

public sealed class WordDiffTests
{
    /// <summary>
    /// Renders a line's segments with the changed stretches wrapped in brackets, which makes a
    /// failing assertion readable at a glance.
    /// </summary>
    private static string Render(string text, IReadOnlyList<DiffSegment> segments)
    {
        if (segments.Count == 0)
        {
            return text;
        }

        System.Text.StringBuilder builder = new();

        foreach (DiffSegment segment in segments)
        {
            string part = text.Substring(segment.Start, segment.Length);
            builder.Append(segment.IsChanged ? $"[{part}]" : part);
        }

        return builder.ToString();
    }

    private static void AssertCoversTheLine(string text, IReadOnlyList<DiffSegment> segments)
    {
        if (segments.Count == 0)
        {
            return;
        }

        int position = 0;
        foreach (DiffSegment segment in segments)
        {
            Assert.Equal(position, segment.Start);
            Assert.True(segment.Length > 0, "a segment must cover at least one character");
            position = segment.End;
        }

        Assert.Equal(text.Length, position);
    }

    [Fact]
    public void Compute_ReturnsNothingForIdenticalLines()
    {
        (IReadOnlyList<DiffSegment> removed, IReadOnlyList<DiffSegment> added) =
            WordDiff.Compute("same line", "same line");

        Assert.Empty(removed);
        Assert.Empty(added);
    }

    [Fact]
    public void Compute_HighlightsASingleChangedWord()
    {
        (IReadOnlyList<DiffSegment> removed, IReadOnlyList<DiffSegment> added) =
            WordDiff.Compute("int total = count * price;", "int total = count * amount;");

        Assert.Equal("int total = count * [price];", Render("int total = count * price;", removed));
        Assert.Equal("int total = count * [amount];", Render("int total = count * amount;", added));
    }

    [Fact]
    public void Compute_HighlightsAnAppendedTail()
    {
        (IReadOnlyList<DiffSegment> removed, IReadOnlyList<DiffSegment> added) =
            WordDiff.Compute("two", "two modified");

        Assert.Equal("two", Render("two", removed));
        Assert.Equal("two[ modified]", Render("two modified", added));
    }

    [Fact]
    public void Compute_HighlightsAChangeInTheMiddleOfALine()
    {
        const string before = "public void Example(int first, int second)";
        const string after = "public void Example(int first, string second)";

        (IReadOnlyList<DiffSegment> removed, IReadOnlyList<DiffSegment> added) =
            WordDiff.Compute(before, after);

        Assert.Equal("public void Example(int first, [int] second)", Render(before, removed));
        Assert.Equal("public void Example(int first, [string] second)", Render(after, added));
    }

    [Fact]
    public void Compute_HighlightsSeveralSeparateChanges()
    {
        const string before = "alpha beta gamma delta epsilon";
        const string after = "alpha BETA gamma DELTA epsilon";

        (IReadOnlyList<DiffSegment> removed, IReadOnlyList<DiffSegment> added) =
            WordDiff.Compute(before, after);

        Assert.Equal("alpha [beta] gamma [delta] epsilon", Render(before, removed));
        Assert.Equal("alpha [BETA] gamma [DELTA] epsilon", Render(after, added));
    }

    [Fact]
    public void Compute_ReturnsNothingForTwoUnrelatedLines()
    {
        (IReadOnlyList<DiffSegment> removed, IReadOnlyList<DiffSegment> added) =
            WordDiff.Compute("the quick brown fox", "0123456789");

        // Nothing meaningful is shared, so the viewer highlights both lines whole by their kind
        // rather than pretending one was edited into the other.
        Assert.Empty(removed);
        Assert.Empty(added);
    }

    [Fact]
    public void Compute_ReturnsNothingWhenOneSideIsEmpty()
    {
        Assert.Empty(WordDiff.Compute(string.Empty, "added content").Added);
        Assert.Empty(WordDiff.Compute("removed content", string.Empty).Removed);
    }

    [Fact]
    public void Compute_HandlesIndentationOnlyChanges()
    {
        (IReadOnlyList<DiffSegment> removed, IReadOnlyList<DiffSegment> added) =
            WordDiff.Compute("    int value = 1;", "        int value = 1;");

        AssertCoversTheLine("    int value = 1;", removed);
        AssertCoversTheLine("        int value = 1;", added);
        Assert.Contains(added, segment => segment.IsChanged);
    }

    [Fact]
    public void Compute_FallsBackToTheWholeMiddleForAVeryLongLine()
    {
        string before = string.Join(" ", Enumerable.Range(0, 300).Select(index => $"token{index}"));
        string after = before.Replace("token150", "CHANGED", StringComparison.Ordinal);

        (IReadOnlyList<DiffSegment> removed, IReadOnlyList<DiffSegment> added) =
            WordDiff.Compute(before, after);

        // Too many tokens for a subsequence pass, so the shared prefix and suffix still bound the
        // highlight: exactly one changed stretch, not the whole line.
        Assert.Equal(1, removed.Count(segment => segment.IsChanged));
        Assert.Equal(1, added.Count(segment => segment.IsChanged));
        AssertCoversTheLine(before, removed);
        AssertCoversTheLine(after, added);
    }

    [Fact]
    public void Compute_SegmentsAlwaysCoverTheWholeLineWithoutOverlapping()
    {
        (string Before, string After)[] cases =
        [
            ("abc", "abd"),
            ("int x = 1;", "int x = 2;"),
            ("a b c d e", "a x c y e"),
            ("prefix middle suffix", "prefix MIDDLE suffix"),
            ("same start different end here", "same start different end there"),
        ];

        foreach ((string before, string after) in cases)
        {
            (IReadOnlyList<DiffSegment> removed, IReadOnlyList<DiffSegment> added) =
                WordDiff.Compute(before, after);

            AssertCoversTheLine(before, removed);
            AssertCoversTheLine(after, added);
        }
    }

    [Fact]
    public void Compute_NeverProducesTwoAdjacentSegmentsOfTheSameState()
    {
        (IReadOnlyList<DiffSegment> removed, _) =
            WordDiff.Compute("alpha beta gamma delta", "alpha BETA GAMMA delta");

        for (int index = 1; index < removed.Count; index++)
        {
            Assert.NotEqual(removed[index - 1].IsChanged, removed[index].IsChanged);
        }
    }

    [Fact]
    public void Compute_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => WordDiff.Compute(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => WordDiff.Compute("x", null!));
    }

    [Fact]
    public void DiffSegment_ReportsItsEnd()
        => Assert.Equal(7, new DiffSegment(2, 5, true).End);

    // ---------------------------------------------------------------- pairing inside a hunk

    private static DiffHunk Hunk(params (DiffLineKind Kind, string Text)[] lines)
        => new(1, lines.Length, 1, lines.Length, string.Empty,
            [.. lines.Select(line => new DiffLine(line.Kind, line.Text, 1, 1))]);

    [Fact]
    public void Apply_PairsARemovedLineWithTheAddedLineThatFollowsIt()
    {
        DiffHunk hunk = Hunk(
            (DiffLineKind.Context, "unchanged"),
            (DiffLineKind.Removed, "int value = 1;"),
            (DiffLineKind.Added, "int value = 2;"),
            (DiffLineKind.Context, "unchanged too"));

        WordDiff.Apply(hunk);

        Assert.False(hunk.Lines[0].HasSegments);
        Assert.True(hunk.Lines[1].HasSegments);
        Assert.True(hunk.Lines[2].HasSegments);
        Assert.False(hunk.Lines[3].HasSegments);
    }

    [Fact]
    public void Apply_PairsRunsPositionally()
    {
        DiffHunk hunk = Hunk(
            (DiffLineKind.Removed, "first old"),
            (DiffLineKind.Removed, "second old"),
            (DiffLineKind.Added, "first new"),
            (DiffLineKind.Added, "second new"));

        WordDiff.Apply(hunk);

        Assert.Equal("first [old]", Render("first old", hunk.Lines[0].Segments));
        Assert.Equal("second [old]", Render("second old", hunk.Lines[1].Segments));
        Assert.Equal("first [new]", Render("first new", hunk.Lines[2].Segments));
        Assert.Equal("second [new]", Render("second new", hunk.Lines[3].Segments));
    }

    [Fact]
    public void Apply_LeavesUnpairedLinesAlone()
    {
        DiffHunk hunk = Hunk(
            (DiffLineKind.Removed, "only removed line"),
            (DiffLineKind.Added, "only added line"),
            (DiffLineKind.Added, "an extra added line"));

        WordDiff.Apply(hunk);

        Assert.True(hunk.Lines[0].HasSegments);
        Assert.True(hunk.Lines[1].HasSegments);
        Assert.False(hunk.Lines[2].HasSegments);
    }

    [Fact]
    public void Apply_DoesNotPairAcrossAContextLine()
    {
        DiffHunk hunk = Hunk(
            (DiffLineKind.Removed, "removed value"),
            (DiffLineKind.Context, "between"),
            (DiffLineKind.Added, "added value"));

        WordDiff.Apply(hunk);

        Assert.False(hunk.Lines[0].HasSegments);
        Assert.False(hunk.Lines[2].HasSegments);
    }

    [Fact]
    public void Apply_HandlesAHunkWithNoChanges()
    {
        DiffHunk hunk = Hunk((DiffLineKind.Context, "a"), (DiffLineKind.Context, "b"));

        WordDiff.Apply(hunk);

        Assert.All(hunk.Lines, line => Assert.False(line.HasSegments));
    }

    [Fact]
    public void Apply_RejectsNull()
        => Assert.Throws<ArgumentNullException>(() => WordDiff.Apply(null!));
}
