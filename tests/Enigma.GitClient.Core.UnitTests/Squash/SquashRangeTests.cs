using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Squash;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Squash;

/// <summary>
/// Covers the menu's measure of a squash: the first-parent rule, and the count of what the squash
/// replaces, read from commits already loaded in the history's order.
/// </summary>
public sealed class SquashRangeTests
{
    private static readonly GitSignature Someone = new("Someone", "someone@example.invalid", DateTimeOffset.UnixEpoch);

    private static GitCommit Commit(string sha, params string[] parents)
        => new(sha, parents, Someone, Someone, sha, string.Empty);

    /// <summary>
    /// The shape that motivated the squash: a base, then three phases, each committed on its own
    /// branch and merged back with <c>--no-ff</c>. Newest first, as the history reads it.
    /// </summary>
    private static List<GitCommit> PhasedHistory() =>
    [
        Commit("M3", "M2", "P3"),
        Commit("P3", "M2"),
        Commit("M2", "M1", "P2"),
        Commit("P2", "M1"),
        Commit("M1", "B", "P1"),
        Commit("P1", "B"),
        Commit("B", "R"),
        Commit("R"),
    ];

    [Fact]
    public void Measure_CountsEveryCommitSinceTheBase()
    {
        SquashRangeMeasure? measure = SquashRange.Measure(PhasedHistory(), "B", "M3");

        Assert.NotNull(measure);
        Assert.Equal(6, measure.CommitCount);
    }

    [Fact]
    public void Measure_FromATipInTheMiddleCountsOnlyWhatLeadsToIt()
    {
        SquashRangeMeasure? measure = SquashRange.Measure(PhasedHistory(), "B", "M2");

        Assert.NotNull(measure);
        Assert.Equal(4, measure.CommitCount);
    }

    [Fact]
    public void Measure_AcceptsABaseFurtherDownTheMainLine()
    {
        SquashRangeMeasure? measure = SquashRange.Measure(PhasedHistory(), "R", "M3");

        Assert.NotNull(measure);
        Assert.Equal(7, measure.CommitCount);
    }

    [Fact]
    public void Measure_RefusesABaseOnAMergedSideBranch()
        => Assert.Null(SquashRange.Measure(PhasedHistory(), "P1", "M3"));

    [Fact]
    public void Measure_RefusesTheBaseAsTheTip()
        => Assert.Null(SquashRange.Measure(PhasedHistory(), "M3", "M3"));

    [Fact]
    public void Measure_RefusesATipBelowTheBase()
        => Assert.Null(SquashRange.Measure(PhasedHistory(), "M3", "B"));

    [Fact]
    public void Measure_RefusesABaseThatIsNotLoaded()
        => Assert.Null(SquashRange.Measure(PhasedHistory(), "X", "M3"));

    [Fact]
    public void Measure_RefusesATipThatIsNotLoaded()
        => Assert.Null(SquashRange.Measure(PhasedHistory(), "B", "X"));

    [Fact]
    public void Measure_RefusesABaseOnAnotherBranch()
    {
        // O forks from R beside B, so it is not below M3 at all.
        List<GitCommit> history = PhasedHistory();
        history.Insert(6, Commit("O", "R"));

        Assert.Null(SquashRange.Measure(history, "O", "M3"));
    }

    [Fact]
    public void Measure_LeavesOutWhatAMergeBroughtInFromOutside()
    {
        // D1 forks from R, below the base, and is merged in: its files come along, but it is not one
        // of the commits the squash replaces.
        List<GitCommit> history =
        [
            Commit("T", "MD"),
            Commit("MD", "P1", "D1"),
            Commit("P1", "B"),
            Commit("B", "R"),
            Commit("D1", "R"),
            Commit("R"),
        ];

        SquashRangeMeasure? measure = SquashRange.Measure(history, "B", "T");

        Assert.NotNull(measure);
        Assert.Equal(3, measure.CommitCount);
    }

    [Fact]
    public void Measure_CountsABranchForkedInsideTheRange()
    {
        // S forks from P1, inside the range, and comes back: it descends from the base, so it is one
        // of the replaced commits.
        List<GitCommit> history =
        [
            Commit("T", "P2", "S"),
            Commit("S", "P1"),
            Commit("P2", "P1"),
            Commit("P1", "B"),
            Commit("B"),
        ];

        SquashRangeMeasure? measure = SquashRange.Measure(history, "B", "T");

        Assert.NotNull(measure);
        Assert.Equal(4, measure.CommitCount);
    }

    [Fact]
    public void Measure_IgnoresALineThatIsNotAnAncestorOfTheTip()
    {
        // N descends from the base too, but on another branch: it is not below the tip.
        List<GitCommit> history = PhasedHistory();
        history.Insert(0, Commit("N", "B"));

        SquashRangeMeasure? measure = SquashRange.Measure(history, "B", "M3");

        Assert.NotNull(measure);
        Assert.Equal(6, measure.CommitCount);
    }
}
