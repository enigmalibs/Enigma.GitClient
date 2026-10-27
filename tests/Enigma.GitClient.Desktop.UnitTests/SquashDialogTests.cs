using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Squash;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The squash dialog's fields: the name it proposes, whether it switches, the message it starts from,
/// what it warns about, and what keeps it from being confirmed.
/// </summary>
public sealed class SquashDialogTests
{
    private const string BaseSha = "5c6eb022aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TipSha = "9f644b8dbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static SquashCandidate Candidate(bool headIsAtTip = true, params string[] tipBranches)
        => new(
            new SquashEnd(BaseSha, "5c6eb02", "docs(roadmap): plan FEATURE-E368"),
            new SquashEnd(TipSha, "9f644b8", "Merge branch 'phase10' into feature"),
            tipBranches,
            headIsAtTip);

    private static SquashPreview Preview(
        IReadOnlyList<SquashOutsideMerge>? outside = null,
        int outsideCommits = 0,
        bool changesNothing = false)
        => new(
            BaseSha,
            TipSha,
            20,
            10,
            ["add the overview (PHASE01)", "add the startup (PHASE02)"],
            outside ?? [],
            outsideCommits,
            changesNothing);

    private static SquashDialogViewModel Dialog(
        SquashCandidate? candidate = null,
        SquashPreview? preview = null,
        params string[] existing)
        => new(candidate ?? Candidate(true, "feature/architecture-docs"), preview ?? Preview(), existing);

    // ---------------------------------------------------------------- the name

    [Fact]
    public void TheName_IsTheTipsBranchSquashed()
        => Assert.Equal("feature/architecture-docs-squashed", Dialog().BranchName);

    [Fact]
    public void TheName_WithoutABranchAtTheTipNamesTheTip()
        => Assert.Equal("squash/9f644b8", Dialog(Candidate(false)).BranchName);

    [Fact]
    public void TheName_SkipsNamesAlreadyTaken()
    {
        SquashDialogViewModel dialog = Dialog(
            existing: ["feature/architecture-docs-squashed", "feature/architecture-docs-squashed-2"]);

        Assert.Equal("feature/architecture-docs-squashed-3", dialog.BranchName);
    }

    [Fact]
    public void TheName_FollowsTheFirstBranchListed()
        => Assert.Equal("current-squashed", SquashDialogViewModel.DefaultBranchName(["current", "other"], "9f644b8", []));

    // ---------------------------------------------------------------- the switch and the message

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheSwitch_IsTickedWhenHeadIsAtTheTip(bool headIsAtTip)
        => Assert.Equal(headIsAtTip, Dialog(Candidate(headIsAtTip, "feature")).SwitchToNewBranch);

    [Fact]
    public void TheMessage_StartsWithAnEmptyTitleAndTheSubjects()
    {
        SquashDialogViewModel dialog = Dialog();

        Assert.Equal("\n\n- add the overview (PHASE01)\n- add the startup (PHASE02)", dialog.Message);
        Assert.False(dialog.IsValid);
        Assert.Equal("Write a title on the first line of the message.", dialog.ValidationMessage);
    }

    [Fact]
    public void ATitle_MakesTheDialogConfirmable()
    {
        SquashDialogViewModel dialog = Dialog();
        int raised = 0;
        dialog.ValidationChanged += (_, _) => raised++;

        dialog.Message = "Architecture docs" + dialog.Message;

        Assert.True(dialog.IsValid);
        Assert.False(dialog.HasValidationMessage);
        Assert.Equal(1, raised);
    }

    // ---------------------------------------------------------------- validation

    [Fact]
    public void ATakenName_IsRefused()
    {
        SquashDialogViewModel dialog = Dialog(existing: ["taken"]);
        dialog.Message = "Title";

        dialog.BranchName = "taken";

        Assert.False(dialog.IsValid);
        Assert.Contains("already exists", dialog.ValidationMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad..name")]
    [InlineData("has space")]
    [InlineData("-dash")]
    public void AnInvalidName_IsRefused(string name)
    {
        SquashDialogViewModel dialog = Dialog();
        dialog.Message = "Title";

        dialog.BranchName = name;

        Assert.False(dialog.IsValid);
        Assert.True(dialog.HasValidationMessage);
    }

    // ---------------------------------------------------------------- what it says

    [Fact]
    public void TheRange_CountsTheCommitsAndTheMerges()
        => Assert.Equal("20 commits (10 merges)", Dialog().RangeText);

    [Fact]
    public void TheRange_CountsWhatCameInFromOutside()
        => Assert.Equal(
            "20 commits (10 merges), plus 3 commits merged in from outside the range",
            Dialog(preview: Preview([new SquashOutsideMerge(new string('d', 40), "Merge develop into feature")], 3)).RangeText);

    [Fact]
    public void ARangeWithNothingToWarnAbout_ShowsNoWarning()
    {
        SquashDialogViewModel dialog = Dialog();

        Assert.Empty(dialog.Warnings);
        Assert.False(dialog.HasWarnings);
    }

    [Fact]
    public void AMergeFromOutside_WarnsThatMergingThatBranchAgainMayConflict()
    {
        SquashDialogViewModel dialog = Dialog(
            preview: Preview([new SquashOutsideMerge("dddddddeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", "Merge develop into feature")], 3));

        string warning = Assert.Single(dialog.Warnings);
        Assert.StartsWith("ddddddd \"Merge develop into feature\"", warning, StringComparison.Ordinal);
        Assert.Contains("may conflict again", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ARangeThatChangesNothing_WarnsTheCommitIsEmpty()
        => Assert.Contains(Dialog(preview: Preview(changesNothing: true)).Warnings, warning => warning.Contains("empty", StringComparison.Ordinal));

    [Fact]
    public void ATipNoBranchEndsAt_WarnsThatNothingAboveItFollows()
    {
        SquashDialogViewModel dialog = Dialog(Candidate(false));

        Assert.Contains(dialog.Warnings, warning => warning.StartsWith("No local branch ends at 9f644b8", StringComparison.Ordinal));
        Assert.Equal("Nothing that exists changes: the original commits stay where they are.", dialog.UntouchedText);
    }

    [Fact]
    public void TheOriginal_IsSaidToKeepItsCommits()
        => Assert.Equal(
            "Nothing that exists changes: \"feature/architecture-docs\" keeps its 20 commits.",
            Dialog().UntouchedText);
}
