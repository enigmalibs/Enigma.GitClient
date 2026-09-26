using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.Diff;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Diff;

/// <summary>
/// Covers the projection from a parsed patch onto the rows the viewer draws, in both shapes. The
/// alignment is the part worth pinning: a side-by-side view whose columns drift apart by one row is
/// worse than no side-by-side view at all.
/// </summary>
public sealed class DiffRowBuilderTests
{
    /// <summary>
    /// An edit that replaces two lines with three, so one side is genuinely shorter than the other.
    /// </summary>
    private const string UnbalancedPatch =
        "diff --git a/src/app.txt b/src/app.txt\n" +
        "index 1111111..2222222 100644\n" +
        "--- a/src/app.txt\n" +
        "+++ b/src/app.txt\n" +
        "@@ -1,4 +1,5 @@\n" +
        " one\n" +
        "-two\n" +
        "-three\n" +
        "+two changed\n" +
        "+three changed\n" +
        "+four\n" +
        " five\n";

    private static FilePatch Parse(string patch)
        => Assert.Single(UnifiedDiffParser.Parse(patch).Files);

    private static FilePatch Unbalanced => Parse(UnbalancedPatch);

    // ---------------------------------------------------------------- unified

    [Fact]
    public void BuildUnified_LeadsEachHunkWithItsBand()
    {
        IReadOnlyList<DiffRow> rows = DiffRowBuilder.BuildUnified(Unbalanced);

        Assert.Equal(DiffRowKind.HunkHeader, rows[0].Kind);
        Assert.Equal("@@ -1,4 +1,5 @@", rows[0].Hunk!.Header);
        Assert.All(rows.Skip(1), row => Assert.Equal(DiffRowKind.Line, row.Kind));
    }

    [Fact]
    public void BuildUnified_KeepsGitsOwnOrder()
    {
        IReadOnlyList<DiffRow> rows = DiffRowBuilder.BuildUnified(Unbalanced);

        Assert.Equal(
            ["one", "two", "three", "two changed", "three changed", "four", "five"],
            rows.Skip(1).Select(row => row.Line!.Text));
    }

    [Fact]
    public void BuildUnified_CarriesBothLineNumbers()
    {
        DiffRow context = DiffRowBuilder.BuildUnified(Unbalanced).First(row => row.Line?.Text == "five");

        Assert.Equal(4, context.Line!.OldLineNumber);
        Assert.Equal(5, context.Line.NewLineNumber);
    }

    // ---------------------------------------------------------------- side by side

    [Fact]
    public void BuildSideBySide_PairsAnEditLineByLine()
    {
        IReadOnlyList<DiffPairRow> rows = DiffRowBuilder.BuildSideBySide(Unbalanced);

        Assert.Equal(DiffRowKind.HunkHeader, rows[0].Kind);

        Assert.Equal(
            [("one", "one"), ("two", "two changed"), ("three", "three changed"), (null, "four"), ("five", "five")],
            rows.Skip(1).Select(row => (row.Left?.Text, row.Right?.Text)));
    }

    [Fact]
    public void BuildSideBySide_PadsTheShorterSideWithAFiller()
    {
        DiffPairRow filler = DiffRowBuilder.BuildSideBySide(Unbalanced).Single(row => row.Left is null && row.Hunk is null);

        Assert.True(filler.IsLeftFiller);
        Assert.False(filler.IsRightFiller);
        Assert.Equal("four", filler.Right!.Text);
    }

    [Fact]
    public void BuildSideBySide_KeepsBothSidesOnTheSameRowAfterAnUnbalancedEdit()
    {
        IReadOnlyList<DiffPairRow> rows = DiffRowBuilder.BuildSideBySide(Unbalanced);

        // Without the filler, "five" would sit one row higher on the left than on the right and
        // every line after it would be off by one.
        DiffPairRow last = rows[^1];

        Assert.Equal("five", last.Left!.Text);
        Assert.Equal("five", last.Right!.Text);
        Assert.Equal(4, last.Left.OldLineNumber);
        Assert.Equal(5, last.Right.NewLineNumber);
    }

    [Fact]
    public void BuildSideBySide_ShowsAContextLineOnBothSides()
    {
        DiffPairRow first = DiffRowBuilder.BuildSideBySide(Unbalanced)[1];

        Assert.Same(first.Left, first.Right);
        Assert.False(first.IsLeftFiller);
        Assert.False(first.IsRightFiller);
    }

    [Fact]
    public void BuildSideBySide_PutsAPureAdditionOnTheRightOnly()
    {
        FilePatch patch = Parse(
            "diff --git a/new.txt b/new.txt\n" +
            "new file mode 100644\n" +
            "--- /dev/null\n" +
            "+++ b/new.txt\n" +
            "@@ -0,0 +1,2 @@\n" +
            "+first\n" +
            "+second\n");

        IReadOnlyList<DiffPairRow> rows = DiffRowBuilder.BuildSideBySide(patch);

        Assert.All(rows.Skip(1), row => Assert.True(row.IsLeftFiller));
        Assert.Equal(["first", "second"], rows.Skip(1).Select(row => row.Right!.Text));
    }

    [Fact]
    public void BuildSideBySide_KeepsTheMissingNewlineMarkerWithItsOwnSide()
    {
        FilePatch patch = Parse(
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "\\ No newline at end of file\n" +
            "+new\n");

        IReadOnlyList<DiffPairRow> rows = DiffRowBuilder.BuildSideBySide(patch);

        // The marker belongs to the removed side; it must not push the added line down a row.
        Assert.Equal("old", rows[1].Left!.Text);
        Assert.Equal("new", rows[1].Right!.Text);
        Assert.Equal(DiffLineKind.NoNewline, rows[2].Left!.Kind);
        Assert.True(rows[2].IsRightFiller);
    }

    [Fact]
    public void BuildSideBySide_HandlesAPatchWithSeveralHunks()
    {
        FilePatch patch = Parse(
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1,2 +1,2 @@\n" +
            " one\n" +
            "-two\n" +
            "+TWO\n" +
            "@@ -10,2 +10,2 @@\n" +
            " ten\n" +
            "-eleven\n" +
            "+ELEVEN\n");

        IReadOnlyList<DiffPairRow> rows = DiffRowBuilder.BuildSideBySide(patch);

        Assert.Equal(2, rows.Count(row => row.Kind == DiffRowKind.HunkHeader));
        Assert.Equal("@@ -10,2 +10,2 @@", rows.Last(row => row.Kind == DiffRowKind.HunkHeader).Hunk!.Header);
    }

    [Fact]
    public void Build_ReturnsNothingForAPatchWithNoHunks()
    {
        FilePatch patch = new(null, "a.txt", FileChangeKind.Modified, [], isBinary: true);

        Assert.Empty(DiffRowBuilder.BuildUnified(patch));
        Assert.Empty(DiffRowBuilder.BuildSideBySide(patch));
    }

    // ---------------------------------------------------------------- copying

    [Fact]
    public void CopyText_LeavesTheMarkersBehind()
    {
        IReadOnlyList<DiffRow> rows = DiffRowBuilder.BuildUnified(Unbalanced);

        string copied = DiffRowBuilder.CopyText(rows.Where(row => row.Line is not null).Select(row => row.Line!));

        // Copying a diff is copying code, and code pasted with "+" on it does not compile.
        Assert.Equal("one\ntwo\nthree\ntwo changed\nthree changed\nfour\nfive\n", copied);
    }

    [Fact]
    public void CopyText_SkipsTheMissingNewlineMarker()
    {
        DiffLine[] lines =
        [
            new DiffLine(DiffLineKind.Added, "value", null, 1),
            new DiffLine(DiffLineKind.NoNewline, " No newline at end of file", null, null),
        ];

        Assert.Equal("value\n", DiffRowBuilder.CopyText(lines));
    }

    [Fact]
    public void PatchText_RebuildsWhatGitWrote()
    {
        string rebuilt = DiffRowBuilder.PatchText(Unbalanced);

        Assert.StartsWith("--- a/src/app.txt\n+++ b/src/app.txt\n@@ -1,4 +1,5 @@\n", rebuilt, StringComparison.Ordinal);
        Assert.Contains("-two\n", rebuilt, StringComparison.Ordinal);
        Assert.Contains("+four\n", rebuilt, StringComparison.Ordinal);
        Assert.Contains(" five\n", rebuilt, StringComparison.Ordinal);
    }

    [Fact]
    public void PatchText_NamesDevNullForAnAddition()
    {
        FilePatch patch = Parse(
            "diff --git a/new.txt b/new.txt\n" +
            "new file mode 100644\n" +
            "--- /dev/null\n" +
            "+++ b/new.txt\n" +
            "@@ -0,0 +1 @@\n" +
            "+first\n");

        Assert.StartsWith("--- /dev/null\n+++ b/new.txt\n", DiffRowBuilder.PatchText(patch), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => DiffRowBuilder.BuildUnified(null!));
        Assert.Throws<ArgumentNullException>(() => DiffRowBuilder.BuildSideBySide(null!));
        Assert.Throws<ArgumentNullException>(() => DiffRowBuilder.CopyText(null!));
        Assert.Throws<ArgumentNullException>(() => DiffRowBuilder.PatchText(null!));
    }
}
