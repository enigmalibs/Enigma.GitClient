using System;
using System.Linq;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Status;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Status;

/// <summary>
/// Covers the <c>--porcelain=v2 -z</c> parser against payloads shaped exactly as git writes them.
/// </summary>
/// <remarks>
/// The NUL separator is built from its character code rather than written as an escape, so the
/// source file holds no control bytes of its own — a file with a real NUL in it is one an editor,
/// a diff or a code review will mangle sooner or later.
/// </remarks>
public sealed class PorcelainV2ParserTests
{
    private const char Nul = (char)0;

    /// <summary>
    /// Joins records the way <c>-z</c> writes them: every record NUL-terminated.
    /// </summary>
    private static string Payload(params string[] records)
        => records.Length == 0 ? string.Empty : string.Join(Nul, records) + Nul;

    // ---------------------------------------------------------------- branch headers

    [Fact]
    public void Parse_ReadsTheBranchHeaders()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "# branch.oid 1c5c3fd6b1b0d2d5b0a5a0e0a2b1c3d4e5f60718",
            "# branch.head main",
            "# branch.upstream origin/main",
            "# branch.ab +3 -2"));

        Assert.Equal("1c5c3fd6b1b0d2d5b0a5a0e0a2b1c3d4e5f60718", status.Branch.Oid);
        Assert.Equal("main", status.Branch.Head);
        Assert.Equal("origin/main", status.Branch.Upstream);
        Assert.Equal(3, status.Branch.Ahead);
        Assert.Equal(2, status.Branch.Behind);
        Assert.True(status.Branch.HasUpstream);
        Assert.False(status.Branch.IsDetached);
        Assert.False(status.Branch.IsUnborn);
    }

    [Fact]
    public void Parse_KnowsADetachedHead()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "# branch.oid 1c5c3fd",
            "# branch.head (detached)"));

        Assert.True(status.Branch.IsDetached);
        Assert.False(status.Branch.HasUpstream);
    }

    [Fact]
    public void Parse_KnowsARepositoryWithNoCommits()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "# branch.oid (initial)",
            "# branch.head main"));

        Assert.True(status.Branch.IsUnborn);
    }

    // ---------------------------------------------------------------- ordinary changes

    [Fact]
    public void Parse_ReadsAStagedModification()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "1 M. N... 100644 100644 100644 aaaa bbbb src/app.cs"));

        ChangedFile file = Assert.Single(status.Staged);

        Assert.Equal("src/app.cs", file.Path);
        Assert.Equal(FileChangeKind.Modified, file.ChangeKind);
        Assert.Equal(FileStagingState.Staged, file.Staging);
        Assert.Equal(FileChangeKind.Modified, file.IndexStatus);
        Assert.Null(file.WorkTreeStatus);
        Assert.Empty(status.Unstaged);
    }

    [Fact]
    public void Parse_ReadsAnUnstagedModification()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "1 .M N... 100644 100644 100644 aaaa aaaa src/app.cs"));

        ChangedFile file = Assert.Single(status.Unstaged);

        Assert.Equal(FileStagingState.Unstaged, file.Staging);
        Assert.Null(file.IndexStatus);
        Assert.Equal(FileChangeKind.Modified, file.WorkTreeStatus);
        Assert.Empty(status.Staged);
    }

    [Fact]
    public void Parse_ReadsAFileThatIsBothStagedAndEditedAgain()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "1 MM N... 100644 100644 100644 aaaa bbbb src/app.cs"));

        // The same file is genuinely in both halves, and a client that keeps only one of them
        // eventually commits something the user never looked at.
        ChangedFile staged = Assert.Single(status.Staged);
        ChangedFile unstaged = Assert.Single(status.Unstaged);

        Assert.Equal("src/app.cs", staged.Path);
        Assert.Equal("src/app.cs", unstaged.Path);
        Assert.Equal(FileStagingState.PartiallyStaged, staged.Staging);
        Assert.Equal(FileStagingState.PartiallyStaged, unstaged.Staging);
    }

    [Theory]
    [InlineData("A.", FileChangeKind.Added)]
    [InlineData("D.", FileChangeKind.Deleted)]
    [InlineData("T.", FileChangeKind.TypeChanged)]
    [InlineData("M.", FileChangeKind.Modified)]
    public void Parse_MapsEveryStagedLetter(string xy, FileChangeKind expected)
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            $"1 {xy} N... 100644 100644 100644 aaaa bbbb src/app.cs"));

        Assert.Equal(expected, Assert.Single(status.Staged).ChangeKind);
    }

    [Fact]
    public void Parse_ReadsADeletionInTheWorkTree()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "1 .D N... 100644 100644 000000 aaaa aaaa gone.txt"));

        Assert.Equal(FileChangeKind.Deleted, Assert.Single(status.Unstaged).ChangeKind);
    }

    // ---------------------------------------------------------------- renames and copies

    [Fact]
    public void Parse_ReadsARenameWithItsOriginalPath()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "2 R. N... 100644 100644 100644 aaaa aaaa R100 src/new.cs",
            "src/old.cs"));

        ChangedFile file = Assert.Single(status.Staged);

        Assert.Equal("src/new.cs", file.Path);
        Assert.Equal("src/old.cs", file.OldPath);
        Assert.Equal(FileChangeKind.Renamed, file.ChangeKind);
        Assert.True(file.IsRenamed);
    }

    [Fact]
    public void Parse_ReadsACopy()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "2 C. N... 100644 100644 100644 aaaa aaaa C75 src/copy.cs",
            "src/original.cs"));

        ChangedFile file = Assert.Single(status.Staged);

        Assert.Equal(FileChangeKind.Copied, file.ChangeKind);
        Assert.Equal("src/original.cs", file.OldPath);
    }

    [Fact]
    public void Parse_DoesNotMistakeTheOriginalPathForAnotherRecord()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "2 R. N... 100644 100644 100644 aaaa aaaa R100 src/new.cs",
            "src/old.cs",
            "1 .M N... 100644 100644 100644 aaaa aaaa other.txt"));

        // The original path is a field, not a record: consuming it as one would drop the entry
        // after it.
        Assert.Single(status.Staged);
        Assert.Equal("other.txt", Assert.Single(status.Unstaged).Path);
    }

    [Fact]
    public void Parse_KeepsARenamesOldPathOffItsUnstagedHalf()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "2 RM N... 100644 100644 100644 aaaa bbbb R100 src/new.cs",
            "src/old.cs"));

        Assert.Equal("src/old.cs", Assert.Single(status.Staged).OldPath);

        // The unstaged half is an edit to the new path; naming the old one would read as an
        // unstaged rename, which it is not.
        Assert.Null(Assert.Single(status.Unstaged).OldPath);
    }

    // ---------------------------------------------------------------- conflicts

    [Theory]
    [InlineData("DD")]
    [InlineData("AU")]
    [InlineData("UD")]
    [InlineData("UA")]
    [InlineData("DU")]
    [InlineData("AA")]
    [InlineData("UU")]
    public void Parse_ReadsEveryConflictState(string state)
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            $"u {state} N... 100644 100644 100644 100644 aaaa bbbb cccc src/conflict.cs"));

        ChangedFile file = Assert.Single(status.Conflicted);

        Assert.Equal("src/conflict.cs", file.Path);
        Assert.True(file.IsConflicted);
        Assert.Equal(FileChangeKind.Unmerged, file.ChangeKind);
        Assert.Equal(state, file.ConflictState);
        Assert.True(status.HasConflicts);

        // A conflict is never staged: it is something to resolve, not something to commit.
        Assert.Empty(status.Staged);
    }

    // ---------------------------------------------------------------- untracked and ignored

    [Fact]
    public void Parse_ReadsUntrackedFiles()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "? src/brand-new.cs",
            "? notes.txt"));

        Assert.Equal(["src/brand-new.cs", "notes.txt"], status.Untracked.Select(file => file.Path));
        Assert.All(status.Untracked, file => Assert.True(file.IsUntracked));
        Assert.All(status.Untracked, file => Assert.Equal(FileChangeKind.Added, file.ChangeKind));
    }

    [Fact]
    public void Parse_ReadsIgnoredFilesSeparately()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "! bin/",
            "! obj/"));

        Assert.Equal(2, status.Ignored.Count);

        // Ignored files are on disk on purpose, so a tree full of build output is still clean.
        Assert.True(status.IsClean);
    }

    // ---------------------------------------------------------------- submodules

    [Fact]
    public void Parse_ReadsASubmoduleWithItsOwnDirtyState()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "1 .M SCMU 160000 160000 160000 aaaa aaaa vendor/lib"));

        ChangedFile file = Assert.Single(status.Unstaged);

        Assert.True(file.IsSubmodule);
        Assert.True(file.Submodule.IsSubmodule);
        Assert.True(file.Submodule.CommitChanged);
        Assert.True(file.Submodule.HasModifiedTracked);
        Assert.True(file.Submodule.HasUntracked);
        Assert.True(file.Submodule.IsDirty);
    }

    [Fact]
    public void Parse_KnowsAPlainFileIsNotASubmodule()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "1 .M N... 100644 100644 100644 aaaa aaaa src/app.cs"));

        Assert.False(Assert.Single(status.Unstaged).IsSubmodule);
    }

    [Theory]
    [InlineData("N...", false, false, false)]
    [InlineData("SC..", true, false, false)]
    [InlineData("S.M.", false, true, false)]
    [InlineData("S..U", false, false, true)]
    public void SubmoduleState_ReadsTheFourCharacterField(
        string field,
        bool commit,
        bool modified,
        bool untracked)
    {
        SubmoduleState state = SubmoduleState.Parse(field);

        Assert.Equal(field[0] == 'S', state.IsSubmodule);
        Assert.Equal(commit, state.CommitChanged);
        Assert.Equal(modified, state.HasModifiedTracked);
        Assert.Equal(untracked, state.HasUntracked);
    }

    // ---------------------------------------------------------------- awkward paths

    [Fact]
    public void Parse_KeepsAPathWithSpacesWhole()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "1 .M N... 100644 100644 100644 aaaa aaaa a file with spaces.txt"));

        // The path is the last field and may hold anything, which is why it is never split on.
        Assert.Equal("a file with spaces.txt", Assert.Single(status.Unstaged).Path);
    }

    [Fact]
    public void Parse_KeepsANonAsciiPathIntact()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "? rapport-écrit.txt",
            "1 .M N... 100644 100644 100644 aaaa aaaa données/été.txt"));

        Assert.Equal("rapport-écrit.txt", Assert.Single(status.Untracked).Path);
        Assert.Equal("données/été.txt", Assert.Single(status.Unstaged).Path);
    }

    [Fact]
    public void Parse_NormalisesBackslashesInPaths()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "? src\\windows\\style.txt"));

        Assert.Equal("src/windows/style.txt", Assert.Single(status.Untracked).Path);
    }

    // ---------------------------------------------------------------- the whole picture

    [Fact]
    public void Parse_ReadsAMixedStatusInOnePass()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "# branch.oid abcdef1",
            "# branch.head main",
            "# branch.upstream origin/main",
            "# branch.ab +1 -0",
            "1 M. N... 100644 100644 100644 aaaa bbbb staged.txt",
            "1 .M N... 100644 100644 100644 aaaa aaaa edited.txt",
            "1 MM N... 100644 100644 100644 aaaa bbbb both.txt",
            "2 R. N... 100644 100644 100644 aaaa aaaa R100 renamed.txt",
            "old-name.txt",
            "u UU N... 100644 100644 100644 100644 aaaa bbbb cccc conflict.txt",
            "? untracked.txt"));

        Assert.Equal(3, status.Staged.Count);
        Assert.Equal(2, status.Unstaged.Count);
        Assert.Single(status.Untracked);
        Assert.Single(status.Conflicted);
        Assert.Equal(7, status.Count);
        Assert.False(status.IsClean);

        // The "not staged" view is what the left half of the changes page shows.
        Assert.Equal(
            ["conflict.txt", "edited.txt", "both.txt", "untracked.txt"],
            status.NotStaged().Select(file => file.Path));
    }

    [Fact]
    public void Parse_HandlesACleanRepository()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "# branch.oid abcdef1",
            "# branch.head main"));

        Assert.True(status.IsClean);
        Assert.Equal(0, status.Count);
        Assert.False(status.HasConflicts);
    }

    [Fact]
    public void Parse_HandlesAnEmptyPayload()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(string.Empty);

        Assert.True(status.IsClean);
        Assert.Equal(WorkingTreeBranch.Unknown, status.Branch);
    }

    [Fact]
    public void Parse_SkipsARecordItCannotUnderstand()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "z something from a future git",
            "1 .M N... 100644 100644 100644 aaaa aaaa src/app.cs"));

        // A record kind this version does not model is skipped, not guessed at.
        Assert.Equal("src/app.cs", Assert.Single(status.Unstaged).Path);
    }

    [Fact]
    public void Parse_SkipsATruncatedRecord()
    {
        WorkingTreeStatus status = PorcelainV2Parser.Parse(Payload(
            "1 .M N... 100644",
            "? fine.txt"));

        Assert.Empty(status.Unstaged);
        Assert.Single(status.Untracked);
    }

    [Fact]
    public void Parse_RejectsNull()
        => Assert.Throws<ArgumentNullException>(() => PorcelainV2Parser.Parse(null!));

    // ---------------------------------------------------------------- the command itself

    [Fact]
    public void BuildArguments_AsksForEverythingThePanelNeeds()
    {
        System.Collections.Generic.List<string> arguments = StatusService.BuildArguments(includeIgnored: false);

        Assert.Equal("status", arguments[0]);
        Assert.Contains("--porcelain=v2", arguments);
        Assert.Contains("-z", arguments);
        Assert.Contains("--branch", arguments);

        // Files inside an untracked directory, not just the directory, so each can be staged alone.
        Assert.Contains("--untracked-files=all", arguments);

        // A submodule that quietly differs is a classic way to commit the wrong thing.
        Assert.Contains("--ignore-submodules=none", arguments);
        Assert.DoesNotContain("--ignored=matching", arguments);
    }

    [Fact]
    public void BuildArguments_AsksForIgnoredFilesOnlyWhenWanted()
        => Assert.Contains("--ignored=matching", StatusService.BuildArguments(includeIgnored: true));
}
