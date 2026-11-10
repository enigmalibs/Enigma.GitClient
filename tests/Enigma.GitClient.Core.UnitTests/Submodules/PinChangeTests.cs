using System;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Submodules;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Submodules;

/// <summary>
/// A submodule's change, as git writes it and as the application says it: the two
/// <c>Subproject commit</c> lines, which way the change goes and in what words, the commits listed and
/// the ones beyond, and what a discard says it does.
/// </summary>
public sealed class PinChangeTests
{
    private const string A = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
    private const string B = "f00ba12345678901234567890123456789abcdef";

    private static PinCommit Commit(string sha, string title)
        => new(sha, title, "Ada", new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.Zero));

    // ---------------------------------------------------------------- the patch

    [Theory]
    [InlineData("Subproject commit " + A, A, false)]
    [InlineData("Subproject commit " + A + "-dirty", A, true)]
    public void TryParse_ReadsTheCommitWithAndWithoutDirty(string text, string sha, bool dirty)
        => Assert.Equal(new SubprojectCommit(sha, dirty), SubprojectCommit.TryParse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Subproject commit ")]
    [InlineData("Subproject commit a1b2c3d")]
    [InlineData("Subproject commit zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("subproject commit " + A)]
    [InlineData("int x = 1;")]
    public void TryParse_RefusesWhatIsNotASubprojectLine(string? text)
        => Assert.Null(SubprojectCommit.TryParse(text));

    [Fact]
    public void ASubmodulesPatch_CarriesItsTwoCommits()
    {
        string patch =
            "diff --git a/libs/Common b/libs/Common\n" +
            "index a1b2c3d..f00ba12 160000\n" +
            "--- a/libs/Common\n" +
            "+++ b/libs/Common\n" +
            "@@ -1 +1 @@\n" +
            "-Subproject commit " + A + "\n" +
            "+Subproject commit " + B + "-dirty\n";

        FilePatch file = Assert.Single(UnifiedDiffParser.Parse(patch).Files);

        Assert.True(file.IsSubmodule);
        Assert.Equal(new SubprojectCommit(A, false), file.OldSubproject);
        Assert.Equal(new SubprojectCommit(B, true), file.NewSubproject);
    }

    [Fact]
    public void AFilesPatch_CarriesNoSubprojectCommit_EvenWithSuchALine()
    {
        string patch =
            "diff --git a/notes.txt b/notes.txt\n" +
            "index 1111111..2222222 100644\n" +
            "--- a/notes.txt\n" +
            "+++ b/notes.txt\n" +
            "@@ -1 +1 @@\n" +
            "-Subproject commit " + A + "\n" +
            "+Subproject commit " + B + "\n";

        FilePatch file = Assert.Single(UnifiedDiffParser.Parse(patch).Files);

        Assert.False(file.IsSubmodule);
        Assert.Null(file.OldSubproject);
        Assert.Null(file.NewSubproject);
    }

    // ---------------------------------------------------------------- the words, per direction

    [Fact]
    public void Forward_SaysTheNewCommits()
        => Assert.Equal(
            "libs/Common: a1b2c3d → f00ba12 · 3 new commits",
            new PinChange("libs/Common", A, B, PinDirection.Forward, [], 3, [], 0).Summary);

    [Fact]
    public void Back_SaysHowFarItGoesBack()
        => Assert.Equal(
            "libs/Common: a1b2c3d → f00ba12 · goes back 1 commit",
            new PinChange("libs/Common", A, B, PinDirection.Back, [], 0, [], 1).Summary);

    [Fact]
    public void Diverged_NamesBothCounts()
        => Assert.Equal(
            "libs/Common: a1b2c3d → f00ba12 · 2 new commits, 1 commit taken back",
            new PinChange("libs/Common", A, B, PinDirection.Diverged, [], 2, [], 1).Summary);

    [Fact]
    public void Unknown_ShowsTheTwoHashes()
    {
        PinChange change = new("libs/Common", A, B, PinDirection.Unknown, [], 0, [], 0, "The submodule is not downloaded, so its commits cannot be listed.");

        Assert.Equal("libs/Common: a1b2c3d → f00ba12", change.Summary);
        Assert.Contains("not downloaded", change.UnknownReason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAddedOrRemovedSubmodule_SaysSo()
    {
        Assert.Equal("libs/New: added at f00ba12", new PinChange("libs/New", string.Empty, B, PinDirection.Unknown, [], 0, [], 0).Summary);
        Assert.Equal("libs/Old: removed (was a1b2c3d)", new PinChange("libs/Old", A, string.Empty, PinDirection.Unknown, [], 0, [], 0).Summary);
    }

    [Fact]
    public void TheLines_MarkWhatComesInAndWhatGoes_AndSayHowManyMore()
    {
        PinChange change = new(
            "libs/Common",
            A,
            B,
            PinDirection.Diverged,
            [Commit("f00ba12", "Fix X")],
            13,
            [Commit("c0ffee1", "Add Y")],
            1);

        Assert.Equal(
            [
                "+ f00ba12  Fix X — Ada, 2026-11-01",
                "+ and 12 more",
                "− c0ffee1  Add Y — Ada, 2026-11-01",
            ],
            change.Lines);
    }

    [Theory]
    [InlineData(true, true, "The submodule has modified content and untracked files.")]
    [InlineData(true, false, "The submodule has modified content.")]
    [InlineData(false, true, "The submodule has untracked files.")]
    [InlineData(false, false, "")]
    public void DescribeDirt_SaysWhatTheSubmoduleHolds(bool modified, bool untracked, string expected)
        => Assert.Equal(expected, PinChange.DescribeDirt(modified, untracked));

    // ---------------------------------------------------------------- a discard

    [Fact]
    public void ADiscard_SaysWhereTheSubmoduleGoesBack_AndWhatItLeavesBehind()
    {
        Assert.Equal("libs/Common goes back to a1b2c3d.", new SubmoduleDiscard("libs/Common", A, B, false, 0).Describe());
        Assert.Equal(
            "libs/Common goes back to a1b2c3d; 2 commits no branch contains are left behind.",
            new SubmoduleDiscard("libs/Common", A, B, false, 2).Describe());

        Assert.True(new SubmoduleDiscard("libs/Common", A, B, false, 0).Moves);
        Assert.False(new SubmoduleDiscard("libs/Common", A, A, false, 0).Moves);
        Assert.False(new SubmoduleDiscard("libs/Common", A, null, false, 0).Moves);
    }

    // ---------------------------------------------------------------- git's output

    [Fact]
    public void ParseChangedGitlinkEntries_KeepsBothCommits_AndEmptiesAMissingSide()
    {
        string zeros = new('0', 40);
        string output =
            $":160000 160000 {A} {B} M\0libs/Common\0" +
            $":100644 100644 {A} {B} M\0README.md\0" +
            $":000000 160000 {zeros} {B} A\0libs/New\0";

        Assert.Equal(
            [("libs/Common", A, B), ("libs/New", string.Empty, B)],
            SubmoduleService.ParseChangedGitlinkEntries(output));
    }

    [Fact]
    public void ParseTreeGitlinkEntries_KeepsTheSubmodulesAndTheirCommits()
        => Assert.Equal(
            [("libs/Common", A)],
            SubmoduleService.ParseTreeGitlinkEntries($"100644 blob {B}\tREADME.md\0160000 commit {A}\tlibs/Common\0"));

    [Fact]
    public void ParsePinCommits_ReadsTheFourFields()
    {
        PinCommit commit = Assert.Single(SubmoduleService.ParsePinCommits("f00ba12\u001fFix X\u001fAda\u001f2026-11-01T09:30:00+00:00\n"));

        Assert.Equal(Commit("f00ba12", "Fix X"), commit);
    }
}
