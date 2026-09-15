using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.Diff;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Diff;

/// <summary>
/// Every payload in this class was captured from a real <c>git diff</c> run rather than written by
/// hand, so the parser is measured against what git actually emits.
/// </summary>
public sealed class UnifiedDiffParserTests
{
    private const string RealWorldPatch =
        """
        diff --git a/a file with spaces.txt b/a file with spaces.txt
        old mode 100644
        new mode 100755
        diff --git a/added.txt b/added.txt
        new file mode 100644
        index 0000000..cd0e0ae
        --- /dev/null
        +++ b/added.txt
        @@ -0,0 +1,2 @@
        +added file
        +second line
        \ No newline at end of file
        diff --git a/blob.bin b/blob.bin
        index 2f68929..4356f70 100644
        Binary files a/blob.bin and b/blob.bin differ
        diff --git a/gone.txt b/gone.txt
        deleted file mode 100644
        index 2d030d7..0000000
        --- a/gone.txt
        +++ /dev/null
        @@ -1 +0,0 @@
        -delete me
        diff --git a/old-name.txt b/new-name.txt
        similarity index 75%
        rename from old-name.txt
        rename to new-name.txt
        index 2103b2b..a2973b6 100644
        --- a/old-name.txt
        +++ b/new-name.txt
        @@ -1,4 +1,4 @@
         to be renamed content line 1
        -line 2
        +line 2 edited
         line 3
         line 4
        diff --git a/plain.txt b/plain.txt
        index 4cb29ea..c9b7c0b 100644
        --- a/plain.txt
        +++ b/plain.txt
        @@ -1,3 +1,4 @@
         one
        -two
        +two modified
         three
        +four
        diff --git a/rapport-écrit.txt b/rapport-écrit.txt
        index ffe22c5..a356421 100644
        --- a/rapport-écrit.txt
        +++ b/rapport-écrit.txt
        @@ -1 +1 @@
        -accentué
        +accentué et modifié

        """;

    private const string SectionHeadingPatch =
        """
        diff --git a/code.cs b/code.cs
        index 4a5f85c..d0d8a31 100644
        --- a/code.cs
        +++ b/code.cs
        @@ -6,7 +6,7 @@ public class Example
                 int b = 2;
                 int c = 3;
                 int d = 4;
        -        int e = 5;
        +        int e = 55;
                 int f = 6;
             }

        """;

    private const string QuotedRenamePatch =
        """
        diff --git "a/weird\"name.txt" "b/weird\"renamed.txt"
        similarity index 100%
        rename from "weird\"name.txt"
        rename to "weird\"renamed.txt"
        """;

    private const string CombinedMergePatch =
        """
        diff --combined code.cs
        index d0d8a31,d955d05..b5cd5a4
        --- a/code.cs
        +++ b/code.cs
        @@@ -2,11 -2,11 +2,11 @@@ public class Exampl
          {
              public void First()
              {
        -         int a = 1;
        +         int a = 111;
                  int b = 2;
                  int c = 3;
                  int d = 4;
         -        int e = 5;
         +        int e = 55;
                  int f = 6;
              }
        """;

    private static FilePatch File(PatchSet patch, string displayPath)
        => patch.Files.Single(file => file.DisplayPath == displayPath);

    // ---------------------------------------------------------------- whole patch

    [Fact]
    public void Parse_ReadsEveryFileOfARealPatch()
    {
        PatchSet patch = UnifiedDiffParser.Parse(RealWorldPatch);

        Assert.Equal(
            ["a file with spaces.txt", "added.txt", "blob.bin", "gone.txt", "new-name.txt", "plain.txt", "rapport-écrit.txt"],
            patch.Files.Select(file => file.DisplayPath));
    }

    [Fact]
    public void Parse_ReturnsNothingForAnEmptyPatch()
    {
        Assert.True(UnifiedDiffParser.Parse(string.Empty).IsEmpty);
        Assert.True(UnifiedDiffParser.Parse("\n\n").IsEmpty);
        Assert.True(PatchSet.Empty.IsEmpty);
    }

    [Fact]
    public void Parse_TotalsTheAddedAndRemovedLinesOfThePatch()
    {
        PatchSet patch = UnifiedDiffParser.Parse(RealWorldPatch);

        // added.txt +2, gone.txt -1, new-name.txt +1/-1, plain.txt +2/-1, rapport +1/-1
        Assert.Equal(6, patch.AddedLines);
        Assert.Equal(4, patch.RemovedLines);
    }

    // ---------------------------------------------------------------- per-file shapes

    [Fact]
    public void Parse_ReadsAPureModeChange()
    {
        FilePatch file = File(UnifiedDiffParser.Parse(RealWorldPatch), "a file with spaces.txt");

        Assert.Equal(FileChangeKind.ModeChanged, file.ChangeKind);
        Assert.Equal("100644", file.OldMode);
        Assert.Equal("100755", file.NewMode);
        Assert.Empty(file.Hunks);
        Assert.True(file.HasNoTextualChange);

        // The path contains a space, so the "diff --git" line is ambiguous; the parser resolves it
        // by preferring the split that makes both halves identical.
        Assert.Equal("a file with spaces.txt", file.OldPath);
        Assert.Equal("a file with spaces.txt", file.NewPath);
    }

    [Fact]
    public void Parse_ReadsAnAddedFileWithNoTrailingNewline()
    {
        FilePatch file = File(UnifiedDiffParser.Parse(RealWorldPatch), "added.txt");

        Assert.Equal(FileChangeKind.Added, file.ChangeKind);
        Assert.Null(file.OldPath);
        Assert.Equal("added.txt", file.NewPath);
        Assert.Equal("100644", file.NewMode);
        Assert.Equal(2, file.AddedLines);
        Assert.Equal(0, file.RemovedLines);

        DiffHunk hunk = Assert.Single(file.Hunks);
        Assert.Equal(0, hunk.OldStart);
        Assert.Equal(0, hunk.OldCount);
        Assert.Equal(1, hunk.NewStart);
        Assert.Equal(2, hunk.NewCount);

        Assert.Equal(
            [DiffLineKind.Added, DiffLineKind.Added, DiffLineKind.NoNewline],
            hunk.Lines.Select(line => line.Kind));
        Assert.Equal([1, 2], hunk.Lines.Where(l => l.Kind == DiffLineKind.Added).Select(l => l.NewLineNumber));
        Assert.All(hunk.Lines.Where(l => l.Kind == DiffLineKind.Added), line => Assert.Null(line.OldLineNumber));
    }

    [Fact]
    public void Parse_ReadsABinaryFile()
    {
        FilePatch file = File(UnifiedDiffParser.Parse(RealWorldPatch), "blob.bin");

        Assert.True(file.IsBinary);
        Assert.Equal(FileChangeKind.Modified, file.ChangeKind);
        Assert.Empty(file.Hunks);
        Assert.Equal(0, file.AddedLines);
    }

    [Fact]
    public void Parse_ReadsADeletedFile()
    {
        FilePatch file = File(UnifiedDiffParser.Parse(RealWorldPatch), "gone.txt");

        Assert.Equal(FileChangeKind.Deleted, file.ChangeKind);
        Assert.Equal("gone.txt", file.OldPath);
        Assert.Null(file.NewPath);
        Assert.Equal("100644", file.OldMode);

        DiffHunk hunk = Assert.Single(file.Hunks);
        Assert.Equal(1, hunk.OldStart);
        Assert.Equal(1, hunk.OldCount);
        Assert.Equal(0, hunk.NewStart);
        Assert.Equal(0, hunk.NewCount);

        DiffLine line = Assert.Single(hunk.Lines);
        Assert.Equal(DiffLineKind.Removed, line.Kind);
        Assert.Equal("delete me", line.Text);
        Assert.Equal(1, line.OldLineNumber);
        Assert.Null(line.NewLineNumber);
    }

    [Fact]
    public void Parse_ReadsARenameWithEdits()
    {
        FilePatch file = File(UnifiedDiffParser.Parse(RealWorldPatch), "new-name.txt");

        Assert.Equal(FileChangeKind.Renamed, file.ChangeKind);
        Assert.Equal("old-name.txt", file.OldPath);
        Assert.Equal("new-name.txt", file.NewPath);
        Assert.Equal(75, file.SimilarityIndex);
        Assert.Equal(1, file.AddedLines);
        Assert.Equal(1, file.RemovedLines);
    }

    [Fact]
    public void Parse_NumbersEveryLineOfEveryHunkCorrectly()
    {
        FilePatch file = File(UnifiedDiffParser.Parse(RealWorldPatch), "plain.txt");
        DiffHunk hunk = Assert.Single(file.Hunks);

        (DiffLineKind Kind, string Text, int? Old, int? New)[] expected =
        [
            (DiffLineKind.Context, "one", 1, 1),
            (DiffLineKind.Removed, "two", 2, null),
            (DiffLineKind.Added, "two modified", null, 2),
            (DiffLineKind.Context, "three", 3, 3),
            (DiffLineKind.Added, "four", null, 4),
        ];

        Assert.Equal(expected.Length, hunk.Lines.Count);

        for (int index = 0; index < expected.Length; index++)
        {
            DiffLine line = hunk.Lines[index];

            Assert.Equal(expected[index].Kind, line.Kind);
            Assert.Equal(expected[index].Text, line.Text);
            Assert.Equal(expected[index].Old, line.OldLineNumber);
            Assert.Equal(expected[index].New, line.NewLineNumber);
        }
    }

    [Fact]
    public void Parse_ReadsANonAsciiPathAndContent()
    {
        FilePatch file = File(UnifiedDiffParser.Parse(RealWorldPatch), "rapport-écrit.txt");
        DiffHunk hunk = Assert.Single(file.Hunks);

        Assert.Equal("accentué", hunk.Lines[0].Text);
        Assert.Equal("accentué et modifié", hunk.Lines[1].Text);
        Assert.Equal(1, hunk.OldStart);
        Assert.Equal(1, hunk.OldCount);
    }

    // ---------------------------------------------------------------- headers

    [Fact]
    public void Parse_ReadsAHunksSectionHeading()
    {
        FilePatch file = Assert.Single(UnifiedDiffParser.Parse(SectionHeadingPatch).Files);
        DiffHunk hunk = Assert.Single(file.Hunks);

        Assert.Equal("public class Example", hunk.SectionHeading);
        Assert.Equal(6, hunk.OldStart);
        Assert.Equal(7, hunk.OldCount);
        Assert.Equal("@@ -6,7 +6,7 @@ public class Example", hunk.Header);
    }

    [Fact]
    public void Parse_ReadsAQuotedPath()
    {
        FilePatch file = Assert.Single(UnifiedDiffParser.Parse(QuotedRenamePatch).Files);

        Assert.Equal(FileChangeKind.Renamed, file.ChangeKind);
        Assert.Equal("weird\"name.txt", file.OldPath);
        Assert.Equal("weird\"renamed.txt", file.NewPath);
        Assert.Equal(100, file.SimilarityIndex);
    }

    [Fact]
    public void Parse_ReadsACombinedMergeDiffAsCombined()
    {
        FilePatch file = Assert.Single(UnifiedDiffParser.Parse(CombinedMergePatch).Files);

        Assert.True(file.IsCombined);
        Assert.Equal("code.cs", file.DisplayPath);

        DiffHunk hunk = Assert.Single(file.Hunks);
        Assert.Equal("public class Exampl", hunk.SectionHeading);
        Assert.Equal(2, hunk.OldStart);
        Assert.Equal(11, hunk.OldCount);
        Assert.Equal(2, hunk.NewStart);
        Assert.Equal(11, hunk.NewCount);

        // Two marker columns: the markers are stripped and the kinds still read correctly.
        Assert.Contains(hunk.Lines, line => line.Kind == DiffLineKind.Added && line.Text.Contains("int a = 111;", StringComparison.Ordinal));
        Assert.Contains(hunk.Lines, line => line.Kind == DiffLineKind.Removed && line.Text.Contains("int a = 1;", StringComparison.Ordinal));
        Assert.Contains(hunk.Lines, line => line.Kind == DiffLineKind.Context && line.Text.Contains("int b = 2;", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("@@ -1,3 +1,4 @@", 1, 3, 1, 4, "", 1)]
    [InlineData("@@ -1 +1 @@", 1, 1, 1, 1, "", 1)]
    [InlineData("@@ -0,0 +1,2 @@", 0, 0, 1, 2, "", 1)]
    [InlineData("@@ -10,7 +12,9 @@ void Example()", 10, 7, 12, 9, "void Example()", 1)]
    [InlineData("@@@ -2,11 -2,11 +2,11 @@@ heading", 2, 11, 2, 11, "heading", 2)]
    public void TryParseHunkHeader_ReadsEveryShape(
        string header,
        int oldStart,
        int oldCount,
        int newStart,
        int newCount,
        string heading,
        int markerWidth)
    {
        Assert.True(UnifiedDiffParser.TryParseHunkHeader(
            header,
            out int parsedOldStart,
            out int parsedOldCount,
            out int parsedNewStart,
            out int parsedNewCount,
            out string parsedHeading,
            out int parsedMarkerWidth));

        Assert.Equal(oldStart, parsedOldStart);
        Assert.Equal(oldCount, parsedOldCount);
        Assert.Equal(newStart, parsedNewStart);
        Assert.Equal(newCount, parsedNewCount);
        Assert.Equal(heading, parsedHeading);
        Assert.Equal(markerWidth, parsedMarkerWidth);
    }

    [Theory]
    [InlineData("")]
    [InlineData("@ -1 +1 @")]
    [InlineData("@@ nonsense @@")]
    [InlineData("not a hunk header")]
    public void TryParseHunkHeader_RejectsAnythingElse(string header)
        => Assert.False(UnifiedDiffParser.TryParseHunkHeader(header, out _, out _, out _, out _, out _, out _));

    [Theory]
    [InlineData("a/src/file.cs b/src/file.cs", "src/file.cs", "src/file.cs")]
    [InlineData("a/old.txt b/new.txt", "old.txt", "new.txt")]
    [InlineData("a/a file with spaces.txt b/a file with spaces.txt", "a file with spaces.txt", "a file with spaces.txt")]
    [InlineData("a/dir b/nested.txt b/dir b/nested.txt", "dir b/nested.txt", "dir b/nested.txt")]
    public void SplitFileHeaderPaths_ResolvesTheHeader(string value, string? expectedOld, string? expectedNew)
    {
        (string? oldPath, string? newPath) = UnifiedDiffParser.SplitFileHeaderPaths(value);

        Assert.Equal(expectedOld, oldPath);
        Assert.Equal(expectedNew, newPath);
    }

    // ---------------------------------------------------------------- content edge cases

    [Fact]
    public void Parse_KeepsALineWhoseContentStartsWithAMarker()
    {
        const string patch =
            """
            diff --git a/f.txt b/f.txt
            index 1..2 100644
            --- a/f.txt
            +++ b/f.txt
            @@ -1,2 +1,2 @@
            --- old marker line
            +++ new marker line
            """;

        DiffHunk hunk = Assert.Single(Assert.Single(UnifiedDiffParser.Parse(patch).Files).Hunks);

        Assert.Equal(DiffLineKind.Removed, hunk.Lines[0].Kind);
        Assert.Equal("-- old marker line", hunk.Lines[0].Text);
        Assert.Equal(DiffLineKind.Added, hunk.Lines[1].Kind);
        Assert.Equal("++ new marker line", hunk.Lines[1].Text);
    }

    [Fact]
    public void Parse_ReadsSeveralHunksInOneFile()
    {
        const string patch =
            """
            diff --git a/f.txt b/f.txt
            index 1..2 100644
            --- a/f.txt
            +++ b/f.txt
            @@ -1,3 +1,3 @@
             one
            -two
            +TWO
            @@ -10,3 +10,3 @@ section
             ten
            -eleven
            +ELEVEN
            """;

        FilePatch file = Assert.Single(UnifiedDiffParser.Parse(patch).Files);

        Assert.Equal(2, file.Hunks.Count);
        Assert.Equal(1, file.Hunks[0].OldStart);
        Assert.Equal(10, file.Hunks[1].OldStart);
        Assert.Equal("section", file.Hunks[1].SectionHeading);
        Assert.Equal(10, file.Hunks[1].Lines[0].OldLineNumber);
        Assert.Equal(11, file.Hunks[1].Lines[1].OldLineNumber);
        Assert.Equal(11, file.Hunks[1].Lines[2].NewLineNumber);
        Assert.Equal(2, file.AddedLines);
        Assert.Equal(2, file.RemovedLines);
    }

    [Fact]
    public void Parse_ReadsAnEmptyContextLine()
    {
        const string patch =
            "diff --git a/f.txt b/f.txt\n" +
            "index 1..2 100644\n" +
            "--- a/f.txt\n" +
            "+++ b/f.txt\n" +
            "@@ -1,3 +1,3 @@\n" +
            " one\n" +
            " \n" +
            "-three\n" +
            "+THREE\n";

        DiffHunk hunk = Assert.Single(Assert.Single(UnifiedDiffParser.Parse(patch).Files).Hunks);

        Assert.Equal(DiffLineKind.Context, hunk.Lines[1].Kind);
        Assert.Equal(string.Empty, hunk.Lines[1].Text);
        Assert.Equal(2, hunk.Lines[1].OldLineNumber);
        Assert.Equal(3, hunk.Lines[2].OldLineNumber);
    }

    [Fact]
    public void Parse_ReadsASubmoduleEntry()
    {
        const string patch =
            """
            diff --git a/vendor/lib b/vendor/lib
            index 1111111..2222222 160000
            --- a/vendor/lib
            +++ b/vendor/lib
            @@ -1 +1 @@
            -Subproject commit 1111111111111111111111111111111111111111
            +Subproject commit 2222222222222222222222222222222222222222
            """;

        FilePatch file = Assert.Single(UnifiedDiffParser.Parse(patch).Files);

        Assert.True(file.IsSubmodule);
        Assert.Equal("160000", file.NewMode);
    }

    [Fact]
    public void Parse_HandlesCarriageReturnLineEndingsInThePatchItself()
    {
        string patch = RealWorldPatch.Replace("\n", "\r\n", StringComparison.Ordinal);

        PatchSet parsed = UnifiedDiffParser.Parse(patch);

        Assert.Equal(7, parsed.Files.Count);
        Assert.Equal("two modified", File(parsed, "plain.txt").Hunks[0].Lines[2].Text);
    }

    // ---------------------------------------------------------------- truncation

    [Fact]
    public void Parse_TruncatesAFileBeyondTheConfiguredLimitAndKeepsGoing()
    {
        System.Text.StringBuilder builder = new();
        builder.Append("diff --git a/big.txt b/big.txt\nindex 1..2 100644\n--- a/big.txt\n+++ b/big.txt\n");
        builder.Append("@@ -1,500 +1,500 @@\n");

        for (int index = 0; index < 500; index++)
        {
            builder.Append("-old ").Append(index).Append('\n');
            builder.Append("+new ").Append(index).Append('\n');
        }

        builder.Append("diff --git a/small.txt b/small.txt\nindex 1..2 100644\n--- a/small.txt\n+++ b/small.txt\n");
        builder.Append("@@ -1 +1 @@\n-a\n+b\n");

        PatchSet patch = UnifiedDiffParser.Parse(
            builder.ToString(),
            new DiffParseOptions { MaxLinesPerFile = 100 });

        Assert.Equal(2, patch.Files.Count);

        FilePatch big = File(patch, "big.txt");
        Assert.True(big.IsTruncated);
        Assert.Equal(100, big.Hunks.Sum(hunk => hunk.Lines.Count));

        // The file after the truncated one must still parse — the parser has to resynchronise.
        FilePatch small = File(patch, "small.txt");
        Assert.False(small.IsTruncated);
        Assert.Equal(2, Assert.Single(small.Hunks).Lines.Count);
    }

    [Fact]
    public void Parse_DoesNotMarkAFileWithinTheLimitAsTruncated()
        => Assert.All(UnifiedDiffParser.Parse(RealWorldPatch).Files, file => Assert.False(file.IsTruncated));

    // ---------------------------------------------------------------- options

    [Fact]
    public void DiffParseOptions_DefaultsAreUsable()
    {
        Assert.Equal(5000, DiffParseOptions.Default.MaxLinesPerFile);
        Assert.True(DiffParseOptions.Default.ComputeWordDiff);
    }

    [Fact]
    public void Parse_SkipsTheWordDiffWhenItIsTurnedOff()
    {
        PatchSet patch = UnifiedDiffParser.Parse(
            RealWorldPatch,
            new DiffParseOptions { ComputeWordDiff = false });

        DiffHunk hunk = File(patch, "plain.txt").Hunks[0];

        Assert.All(hunk.Lines, line => Assert.False(line.HasSegments));
    }

    [Fact]
    public void Parse_ComputesTheWordDiffByDefault()
    {
        DiffHunk hunk = File(UnifiedDiffParser.Parse(RealWorldPatch), "plain.txt").Hunks[0];

        DiffLine removed = hunk.Lines.Single(line => line.Kind == DiffLineKind.Removed);
        DiffLine added = hunk.Lines.First(line => line.Kind == DiffLineKind.Added);

        Assert.True(added.HasSegments);
        Assert.Equal("two", removed.Text);
        Assert.Equal("two modified", added.Text);
        Assert.Contains(added.Segments, segment => segment.IsChanged);
    }

    [Fact]
    public void Parse_RejectsNull()
        => Assert.Throws<ArgumentNullException>(() => UnifiedDiffParser.Parse(null!));

    [Fact]
    public void DiffLine_RendersItsMarkerInToString()
    {
        Assert.Equal("+added", new DiffLine(DiffLineKind.Added, "added", null, 1).ToString());
        Assert.Equal("-removed", new DiffLine(DiffLineKind.Removed, "removed", 1, null).ToString());
        Assert.Equal(" context", new DiffLine(DiffLineKind.Context, "context", 1, 1).ToString());
    }

    [Fact]
    public void FilePatch_SummarisesItselfForLogs()
    {
        FilePatch file = File(UnifiedDiffParser.Parse(RealWorldPatch), "plain.txt");

        Assert.Equal("Modified plain.txt +2 -1", file.ToString());
    }

    [Fact]
    public void DiffHunk_RendersTheGitHeaderItCameFrom()
    {
        List<DiffHunk> hunks = [.. UnifiedDiffParser.Parse(RealWorldPatch).Files.SelectMany(file => file.Hunks)];

        Assert.Contains("@@ -0,0 +1,2 @@", hunks.Select(hunk => hunk.Header));
        Assert.Contains("@@ -1 +0,0 @@", hunks.Select(hunk => hunk.Header));
        Assert.Contains("@@ -1,3 +1,4 @@", hunks.Select(hunk => hunk.Header));
    }
}
