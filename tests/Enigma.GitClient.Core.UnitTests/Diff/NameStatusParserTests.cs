using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Diff;

public sealed class NameStatusParserTests
{
    /// <summary>
    /// Builds a NUL-separated payload the way <c>-z</c> output looks.
    /// </summary>
    private static string Records(params string[] records) => string.Join('\0', records) + '\0';

    [Fact]
    public void ParseNameStatus_ReadsTheSimpleStatuses()
    {
        IReadOnlyList<ChangedFile> files = NameStatusParser.ParseNameStatus(Records(
            "A", "added.txt",
            "M", "modified.txt",
            "D", "deleted.txt",
            "T", "type-changed.txt"));

        Assert.Equal(4, files.Count);
        Assert.Equal(FileChangeKind.Added, files[0].ChangeKind);
        Assert.Equal("added.txt", files[0].Path);
        Assert.Equal(FileChangeKind.Modified, files[1].ChangeKind);
        Assert.Equal(FileChangeKind.Deleted, files[2].ChangeKind);
        Assert.Equal(FileChangeKind.TypeChanged, files[3].ChangeKind);
    }

    [Fact]
    public void ParseNameStatus_ReadsARenameWithBothPaths()
    {
        ChangedFile file = Assert.Single(NameStatusParser.ParseNameStatus(Records(
            "R096", "src/old-name.cs", "src/new-name.cs")));

        Assert.Equal(FileChangeKind.Renamed, file.ChangeKind);
        Assert.Equal("src/old-name.cs", file.OldPath);
        Assert.Equal("src/new-name.cs", file.Path);
        Assert.True(file.IsRenamed);
    }

    [Fact]
    public void ParseNameStatus_ReadsACopy()
    {
        ChangedFile file = Assert.Single(NameStatusParser.ParseNameStatus(Records(
            "C085", "src/original.cs", "src/copy.cs")));

        Assert.Equal(FileChangeKind.Copied, file.ChangeKind);
        Assert.Equal("src/original.cs", file.OldPath);
        Assert.Equal("src/copy.cs", file.Path);
    }

    [Fact]
    public void ParseNameStatus_ReadsARenameFollowedByAnotherFile()
    {
        // The record after a rename's two paths must be read as a status, not as a path: this is
        // exactly where a parser that assumes one path per record loses its place.
        IReadOnlyList<ChangedFile> files = NameStatusParser.ParseNameStatus(Records(
            "R100", "a.txt", "b.txt",
            "M", "c.txt"));

        Assert.Equal(2, files.Count);
        Assert.Equal("b.txt", files[0].Path);
        Assert.Equal("c.txt", files[1].Path);
        Assert.Equal(FileChangeKind.Modified, files[1].ChangeKind);
    }

    [Fact]
    public void ParseNameStatus_MarksAnUnmergedFileAsConflicted()
    {
        ChangedFile file = Assert.Single(NameStatusParser.ParseNameStatus(Records("U", "conflicted.txt")));

        Assert.Equal(FileChangeKind.Unmerged, file.ChangeKind);
        Assert.True(file.IsConflicted);
    }

    [Fact]
    public void ParseNameStatus_KeepsAPathWithSpacesAndNonAsciiCharacters()
    {
        IReadOnlyList<ChangedFile> files = NameStatusParser.ParseNameStatus(Records(
            "M", "a file with spaces.txt",
            "A", "rapport-écrit.txt"));

        Assert.Equal("a file with spaces.txt", files[0].Path);
        Assert.Equal("rapport-écrit.txt", files[1].Path);
    }

    [Fact]
    public void ParseNameStatus_StampsTheStagingStateOnEveryEntry()
    {
        IReadOnlyList<ChangedFile> files = NameStatusParser.ParseNameStatus(
            Records("M", "a.txt", "A", "b.txt"),
            FileStagingState.Staged);

        Assert.All(files, file => Assert.Equal(FileStagingState.Staged, file.Staging));
    }

    [Fact]
    public void ParseNameStatus_ReturnsNothingForEmptyOutput()
    {
        Assert.Empty(NameStatusParser.ParseNameStatus(string.Empty));
        Assert.Empty(NameStatusParser.ParseNameStatus("\0\0"));
    }

    [Fact]
    public void ParseNameStatus_IgnoresATruncatedFinalRecord()
        => Assert.Empty(NameStatusParser.ParseNameStatus("M"));

    // ---------------------------------------------------------------- numstat

    [Fact]
    public void ParseNumstat_ReadsTheCounts()
    {
        IReadOnlyDictionary<string, (int? Added, int? Removed)> counts = NameStatusParser.ParseNumstat(
            Records("12\t3\tsrc/app.cs", "0\t7\tdeleted.txt"));

        Assert.Equal((12, 3), counts["src/app.cs"]);
        Assert.Equal((0, 7), counts["deleted.txt"]);
    }

    [Fact]
    public void ParseNumstat_ReadsARenamesThreeRecordForm()
    {
        IReadOnlyDictionary<string, (int? Added, int? Removed)> counts = NameStatusParser.ParseNumstat(
            Records("2\t2\t", "src/old.cs", "src/new.cs", "1\t0\tother.txt"));

        Assert.Equal((2, 2), counts["src/new.cs"]);
        Assert.Equal((1, 0), counts["other.txt"]);
    }

    [Fact]
    public void ParseNumstat_ReportsABinaryFileAsHavingNoCounts()
    {
        IReadOnlyDictionary<string, (int? Added, int? Removed)> counts =
            NameStatusParser.ParseNumstat(Records("-\t-\tblob.bin"));

        Assert.Null(counts["blob.bin"].Added);
        Assert.Null(counts["blob.bin"].Removed);
    }

    // ---------------------------------------------------------------- combining

    [Fact]
    public void Combine_FillsInTheCountsAndTheBinaryFlag()
    {
        IReadOnlyList<ChangedFile> files = NameStatusParser.ParseNameStatus(
            Records("M", "src/app.cs", "M", "blob.bin"));

        IReadOnlyList<ChangedFile> combined = NameStatusParser.Combine(
            files,
            NameStatusParser.ParseNumstat(Records("12\t3\tsrc/app.cs", "-\t-\tblob.bin")));

        ChangedFile app = combined.Single(file => file.Path == "src/app.cs");
        ChangedFile blob = combined.Single(file => file.Path == "blob.bin");

        Assert.Equal(12, app.AddedLines);
        Assert.Equal(3, app.RemovedLines);
        Assert.False(app.IsBinary);

        Assert.True(blob.IsBinary);
        Assert.Equal(0, blob.AddedLines);
    }

    [Fact]
    public void Combine_LeavesAFileWithNoCountsAlone()
    {
        IReadOnlyList<ChangedFile> combined = NameStatusParser.Combine(
            NameStatusParser.ParseNameStatus(Records("M", "src/app.cs")),
            new Dictionary<string, (int? Added, int? Removed)>());

        Assert.Equal(0, Assert.Single(combined).AddedLines);
    }

    [Theory]
    [InlineData('A', FileChangeKind.Added)]
    [InlineData('M', FileChangeKind.Modified)]
    [InlineData('D', FileChangeKind.Deleted)]
    [InlineData('R', FileChangeKind.Renamed)]
    [InlineData('C', FileChangeKind.Copied)]
    [InlineData('T', FileChangeKind.TypeChanged)]
    [InlineData('U', FileChangeKind.Unmerged)]
    [InlineData('X', FileChangeKind.Unknown)]
    public void ToChangeKind_MapsGitsStatusLetters(char code, FileChangeKind expected)
        => Assert.Equal(expected, NameStatusParser.ToChangeKind(code));
}

public sealed class DiffTargetTests
{
    [Fact]
    public void Commit_DefaultsToTheFirstParent()
    {
        DiffTarget target = DiffTarget.Commit("abc123");

        Assert.Equal(DiffTargetKind.Commit, target.Kind);
        Assert.Equal("abc123", target.From);
        Assert.Equal(0, target.ParentIndex);
    }

    [Fact]
    public void Commit_RejectsANegativeParentIndex()
        => Assert.Throws<ArgumentOutOfRangeException>(() => DiffTarget.Commit("abc123", -1));

    [Fact]
    public void Range_CarriesBothSides()
    {
        DiffTarget target = DiffTarget.Range("v1.0", "v2.0");

        Assert.Equal("v1.0", target.From);
        Assert.Equal("v2.0", target.To);
        Assert.Equal("v1.0..v2.0", target.ToString());
    }

    [Fact]
    public void WorkingTreeVariantsAreDistinct()
    {
        Assert.Equal(DiffTargetKind.WorkingTree, DiffTarget.WorkingTree().Kind);
        Assert.Equal(DiffTargetKind.Staged, DiffTarget.Staged().Kind);
        Assert.Equal(DiffTargetKind.Uncommitted, DiffTarget.Uncommitted().Kind);
    }

    // ---------------------------------------------------------------- argument building

    [Fact]
    public void BuildArguments_UsesShowForACommitAgainstItsFirstParent()
    {
        List<string> arguments = DiffService.BuildArguments(
            DiffTarget.Commit("abc123"),
            ["--name-status"],
            DiffOptions.Default,
            paths: null);

        Assert.Equal("show", arguments[0]);

        // "show" is what makes a root commit work: it diffs against the empty tree instead of
        // failing on a parent that does not exist.
        Assert.Contains("--first-parent", arguments);
        Assert.Contains("-m", arguments);
        Assert.Contains("abc123", arguments);
        Assert.Contains("-z", arguments);
    }

    [Fact]
    public void BuildArguments_UsesDiffForALaterParent()
    {
        List<string> arguments = DiffService.BuildArguments(
            DiffTarget.Commit("abc123", parentIndex: 1),
            ["--name-status"],
            DiffOptions.Default,
            paths: null);

        Assert.Equal("diff", arguments[0]);
        Assert.Contains("abc123^2", arguments);
        Assert.Contains("abc123", arguments);
    }

    [Fact]
    public void BuildArguments_DoesNotPassMinusZForAPatch()
    {
        List<string> arguments = DiffService.BuildArguments(
            DiffTarget.Commit("abc123"),
            ["--patch"],
            DiffOptions.Default,
            paths: ["src/app.cs"]);

        Assert.DoesNotContain("-z", arguments);
        Assert.Contains("--unified=3", arguments);
        Assert.Equal("src/app.cs", arguments[^1]);
        Assert.Equal("--", arguments[^2]);
    }

    [Fact]
    public void BuildArguments_PassesBothSidesOfARenameAsPathspecs()
    {
        // git pairs a deletion with an addition to spot a rename, and it does that after the
        // pathspec has filtered the diff — so both halves have to survive the filter.
        List<string> arguments = DiffService.BuildArguments(
            DiffTarget.Commit("abc123"),
            ["--patch"],
            DiffOptions.Default,
            paths: ["src/new.cs", "src/old.cs"]);

        Assert.Equal(["--", "src/new.cs", "src/old.cs"], arguments[^3..]);
    }

    [Fact]
    public void BuildArguments_CarriesTheWhitespaceOptions()
    {
        List<string> arguments = DiffService.BuildArguments(
            DiffTarget.WorkingTree(),
            ["--patch"],
            new DiffOptions { IgnoreAllWhitespace = true, IgnoreBlankLines = true, ContextLines = 8 },
            paths: null);

        Assert.Contains("--ignore-all-space", arguments);
        Assert.Contains("--ignore-blank-lines", arguments);
        Assert.Contains("--unified=8", arguments);
    }

    [Fact]
    public void BuildArguments_CanTurnRenameDetectionOff()
    {
        List<string> arguments = DiffService.BuildArguments(
            DiffTarget.WorkingTree(),
            ["--name-status"],
            new DiffOptions { DetectRenames = false },
            paths: null);

        Assert.DoesNotContain("--find-renames", arguments);
    }

    [Theory]
    [InlineData(DiffTargetKind.Staged, "--cached")]
    [InlineData(DiffTargetKind.Uncommitted, "HEAD")]
    public void BuildArguments_SelectsTheWorkingTreeComparison(DiffTargetKind kind, string expected)
    {
        DiffTarget target = kind switch
        {
            DiffTargetKind.Staged => DiffTarget.Staged(),
            DiffTargetKind.Uncommitted => DiffTarget.Uncommitted(),
            _ => DiffTarget.WorkingTree(),
        };

        Assert.Contains(expected, DiffService.BuildArguments(target, ["--name-status"], DiffOptions.Default, null));
    }

    [Fact]
    public void BuildArguments_ForTheUnstagedWorkingTreeNamesNoRevision()
    {
        List<string> arguments = DiffService.BuildArguments(
            DiffTarget.WorkingTree(),
            ["--name-status"],
            DiffOptions.Default,
            paths: null);

        Assert.DoesNotContain("HEAD", arguments);
        Assert.DoesNotContain("--cached", arguments);
        Assert.Equal("--", arguments[^1]);
    }
}
