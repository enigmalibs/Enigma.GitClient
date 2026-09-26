using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Enigma.GitClient.Core.Graph;
using Xunit;
using Xunit.Sdk;

namespace Enigma.GitClient.Core.UnitTests.Graph;

public sealed class CommitGraphLayoutTests
{
    /// <summary>
    /// Builds a layout input. Commits are named with short readable identifiers rather than real
    /// SHAs, because what these tests assert is topology, not hashing.
    /// </summary>
    private static GraphCommitInput Commit(string sha, params string[] parents)
        => new(sha, parents);

    private static GraphLayoutResult Layout(params GraphCommitInput[] commits)
        => CommitGraphLayout.Build(commits);

    private static GraphLayoutResult LayoutComplete(params GraphCommitInput[] commits)
        => CommitGraphLayout.BuildComplete(commits);

    private static GraphRow Row(GraphLayoutResult result, string sha)
        => result.Rows.Single(row => row.Sha == sha);

    private static IEnumerable<GraphEdge> Edges(GraphRow row, GraphEdgeKind kind)
        => row.Edges.Where(edge => edge.Kind == kind);

    /// <summary>
    /// A stable textual description of a row, used to compare a paged layout against a one-shot
    /// layout without depending on object identity.
    /// </summary>
    private static string Describe(GraphRow row)
    {
        string edges = string.Join(
            ",",
            row.Edges.Select(edge => string.Create(
                CultureInfo.InvariantCulture,
                $"{edge.Kind}:{edge.FromLane}->{edge.ToLane}#{edge.Colour}")));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{row.Sha}|lane={row.Lane}|colour={row.Colour}|merge={row.IsMerge}|root={row.IsRoot}|max={row.MaxLane}|{edges}");
    }

    // ---------------------------------------------------------------- linear history

    [Fact]
    public void Build_LaysOutALinearHistoryInOneLane()
    {
        GraphLayoutResult result = Layout(
            Commit("C", "B"),
            Commit("B", "A"),
            Commit("A"));

        Assert.Equal(0, result.MaxLane);
        Assert.All(result.Rows, row => Assert.Equal(0, row.Lane));
        Assert.All(result.Rows, row => Assert.Equal(result.Rows[0].Colour, row.Colour));

        // The tip has no incoming line; the root has no outgoing one.
        Assert.Empty(Edges(Row(result, "C"), GraphEdgeKind.MergeIn));
        Assert.Single(Edges(Row(result, "C"), GraphEdgeKind.BranchOut));
        Assert.Single(Edges(Row(result, "B"), GraphEdgeKind.MergeIn));
        Assert.Single(Edges(Row(result, "B"), GraphEdgeKind.BranchOut));
        Assert.Single(Edges(Row(result, "A"), GraphEdgeKind.MergeIn));
        Assert.Empty(Edges(Row(result, "A"), GraphEdgeKind.BranchOut));

        Assert.True(Row(result, "A").IsRoot);
        Assert.False(Row(result, "A").IsMerge);
    }

    [Fact]
    public void Build_ClosesTheLaneOfARootCommit()
    {
        GraphLayoutResult result = Layout(Commit("B", "A"), Commit("A"));

        Assert.Equal(0, result.State.OpenLaneCount);
    }

    [Fact]
    public void Build_LeavesTheLaneOpenWhenTheParentIsNotOnThisPage()
    {
        GraphLayoutResult result = Layout(Commit("B", "A"));

        Assert.Equal(1, result.State.OpenLaneCount);
        Assert.Equal("A", result.State.GetAwaitedSha(0));
    }

    // ---------------------------------------------------------------- branch and merge

    [Fact]
    public void Build_LaysOutABranchAndItsMerge()
    {
        //  D  merge of B and C
        //  |\
        //  | C   topic
        //  |/
        //  B
        //  A
        GraphLayoutResult result = Layout(
            Commit("D", "B", "C"),
            Commit("C", "B"),
            Commit("B", "A"),
            Commit("A"));

        GraphRow d = Row(result, "D");
        GraphRow c = Row(result, "C");
        GraphRow b = Row(result, "B");
        GraphRow a = Row(result, "A");

        Assert.Equal(1, result.MaxLane);

        // The merge sits in lane 0 and forks out to lane 1 for its second parent.
        Assert.Equal(0, d.Lane);
        Assert.True(d.IsMerge);
        Assert.Equal(
            [(0, 0), (0, 1)],
            Edges(d, GraphEdgeKind.BranchOut).Select(edge => (edge.FromLane, edge.ToLane)));

        // The topic commit occupies the forked lane, and the first parent's lane passes through.
        Assert.Equal(1, c.Lane);
        Assert.Equal((0, 0), Edges(c, GraphEdgeKind.Straight).Select(e => (e.FromLane, e.ToLane)).Single());
        Assert.Equal((1, 1), Edges(c, GraphEdgeKind.MergeIn).Select(e => (e.FromLane, e.ToLane)).Single());

        // The branch point takes the leftmost waiting lane and the topic lane merges into it.
        Assert.Equal(0, b.Lane);
        Assert.Equal(
            [(0, 0), (1, 0)],
            Edges(b, GraphEdgeKind.MergeIn).Select(edge => (edge.FromLane, edge.ToLane)));
        Assert.Empty(Edges(b, GraphEdgeKind.Straight));

        Assert.Equal(0, a.Lane);
        Assert.True(a.IsRoot);
        Assert.Equal(0, a.MaxLane);
    }

    [Fact]
    public void Build_GivesTheForkedLaneItsOwnColourAndKeepsIt()
    {
        GraphLayoutResult result = Layout(
            Commit("D", "B", "C"),
            Commit("C", "B"),
            Commit("B", "A"),
            Commit("A"));

        int mainColour = Row(result, "D").Colour;
        int topicColour = Row(result, "C").Colour;

        Assert.NotEqual(mainColour, topicColour);

        // The fork segment is drawn in the colour of the lane it heads for, not the merge's own.
        GraphEdge fork = Edges(Row(result, "D"), GraphEdgeKind.BranchOut).Single(edge => edge.ToLane == 1);
        Assert.Equal(topicColour, fork.Colour);

        // The topic lane merging back in keeps its own colour.
        GraphEdge mergedIn = Edges(Row(result, "B"), GraphEdgeKind.MergeIn).Single(edge => edge.FromLane == 1);
        Assert.Equal(topicColour, mergedIn.Colour);
    }

    // ---------------------------------------------------------------- octopus

    [Fact]
    public void Build_LaysOutAnOctopusMergeWithThreeParents()
    {
        GraphLayoutResult result = Layout(
            Commit("M", "A", "B", "C"),
            Commit("A", "R"),
            Commit("B", "R"),
            Commit("C", "R"),
            Commit("R"));

        GraphRow merge = Row(result, "M");

        Assert.True(merge.IsMerge);
        Assert.Equal(
            [(0, 0), (0, 1), (0, 2)],
            Edges(merge, GraphEdgeKind.BranchOut).Select(edge => (edge.FromLane, edge.ToLane)));
        Assert.Equal(2, merge.MaxLane);

        // All three arms rejoin at the shared root, which takes the leftmost of the three lanes.
        GraphRow root = Row(result, "R");
        Assert.Equal(0, root.Lane);
        Assert.Equal(
            [(0, 0), (1, 0), (2, 0)],
            Edges(root, GraphEdgeKind.MergeIn).Select(edge => (edge.FromLane, edge.ToLane)));
        Assert.Equal(0, result.State.OpenLaneCount);
    }

    [Fact]
    public void Build_GivesEveryOctopusArmADistinctColour()
    {
        GraphLayoutResult result = Layout(
            Commit("M", "A", "B", "C"),
            Commit("A", "R"),
            Commit("B", "R"),
            Commit("C", "R"),
            Commit("R"));

        int[] colours = [Row(result, "A").Colour, Row(result, "B").Colour, Row(result, "C").Colour];

        Assert.Equal(3, colours.Distinct().Count());
    }

    // ---------------------------------------------------------------- several roots

    [Fact]
    public void Build_LaysOutTwoIndependentRoots()
    {
        GraphLayoutResult result = Layout(
            Commit("B", "A"),
            Commit("A"),
            Commit("Y", "X"),
            Commit("X"));

        // The first history closes its lane, so the second one reuses it.
        Assert.Equal(0, Row(result, "B").Lane);
        Assert.Equal(0, Row(result, "A").Lane);
        Assert.Equal(0, Row(result, "Y").Lane);
        Assert.Equal(0, Row(result, "X").Lane);
        Assert.Equal(0, result.MaxLane);
        Assert.True(Row(result, "A").IsRoot);
        Assert.True(Row(result, "X").IsRoot);
    }

    [Fact]
    public void Build_LaysOutTwoInterleavedHistoriesInSeparateLanes()
    {
        GraphLayoutResult result = Layout(
            Commit("B", "A"),
            Commit("Y", "X"),
            Commit("A"),
            Commit("X"));

        Assert.Equal(0, Row(result, "B").Lane);
        Assert.Equal(1, Row(result, "Y").Lane);
        Assert.Equal(0, Row(result, "A").Lane);
        Assert.Equal(1, Row(result, "X").Lane);
        Assert.Equal(1, result.MaxLane);
    }

    // ---------------------------------------------------------------- criss-cross

    [Fact]
    public void Build_LaysOutCrissCrossMerges()
    {
        //  M1 = merge(A, B)   M2 = merge(B, A)
        //  A and B both descend from R.
        GraphLayoutResult result = Layout(
            Commit("M1", "A", "B"),
            Commit("M2", "B", "A"),
            Commit("A", "R"),
            Commit("B", "R"),
            Commit("R"));

        Assert.True(Row(result, "M1").IsMerge);
        Assert.True(Row(result, "M2").IsMerge);

        // Every lane closes at the shared root: a criss-cross must not leak lanes.
        Assert.Equal(0, result.State.OpenLaneCount);
        Assert.Equal(0, Row(result, "R").Lane);

        // No two commits share a lane at the same time, and every row's edges stay within its width.
        Assert.All(result.Rows, row => Assert.All(row.Edges, edge =>
        {
            Assert.InRange(edge.FromLane, 0, row.MaxLane);
            Assert.InRange(edge.ToLane, 0, row.MaxLane);
        }));
    }

    // ---------------------------------------------------------------- lane stability

    [Fact]
    public void Build_KeepsALongLivedBranchInOneLaneAndOneColour()
    {
        List<GraphCommitInput> commits = [];

        // A merge at the top, then fifty commits alternating between the two branches, then the
        // shared root. The topic lane must never move or change colour.
        commits.Add(Commit("M", "main-0", "topic-0"));

        for (int index = 0; index < 25; index++)
        {
            commits.Add(Commit(
                $"main-{index.ToString(CultureInfo.InvariantCulture)}",
                index == 24 ? "R" : $"main-{(index + 1).ToString(CultureInfo.InvariantCulture)}"));
            commits.Add(Commit(
                $"topic-{index.ToString(CultureInfo.InvariantCulture)}",
                index == 24 ? "R" : $"topic-{(index + 1).ToString(CultureInfo.InvariantCulture)}"));
        }

        commits.Add(Commit("R"));

        GraphLayoutResult result = CommitGraphLayout.Build(commits);

        List<GraphRow> topicRows = [.. result.Rows.Where(row => row.Sha.StartsWith("topic-", StringComparison.Ordinal))];

        Assert.Equal(25, topicRows.Count);
        Assert.Single(topicRows.Select(row => row.Lane).Distinct());
        Assert.Single(topicRows.Select(row => row.Colour).Distinct());
        Assert.Equal(1, topicRows[0].Lane);

        Assert.All(
            result.Rows.Where(row => row.Sha.StartsWith("main-", StringComparison.Ordinal)),
            row => Assert.Equal(0, row.Lane));
    }

    [Fact]
    public void Build_ReusesALaneFreedByAnEarlierBranch()
    {
        //  Two independent histories, each a branch merged back into its own trunk. The first one
        //  opens lane 1 and closes it at its root; the second must reuse lane 1 rather than pushing
        //  the graph wider.
        GraphLayoutResult result = Layout(
            Commit("M1", "B", "T1"),
            Commit("T1", "B"),
            Commit("B", "A"),
            Commit("A"),
            Commit("M2", "X", "T2"),
            Commit("T2", "X"),
            Commit("X"));

        Assert.Equal(1, Row(result, "T1").Lane);
        Assert.Equal(0, Row(result, "M2").Lane);
        Assert.Equal(1, Row(result, "T2").Lane);
        Assert.Equal(1, result.MaxLane);
        Assert.Equal(0, result.State.OpenLaneCount);
    }

    // ---------------------------------------------------------------- incomplete parents

    [Fact]
    public void BuildComplete_ClosesALaneWhoseParentIsNotInTheHistory()
    {
        // A path-filtered or shallow walk: B's parent is not in the result set.
        GraphLayoutResult result = LayoutComplete(Commit("C", "B"), Commit("B", "missing"));

        Assert.Equal(0, result.State.OpenLaneCount);
        Assert.Empty(Edges(Row(result, "B"), GraphEdgeKind.BranchOut));
        Assert.False(Row(result, "B").IsRoot);
    }

    [Fact]
    public void BuildComplete_KeepsTheKnownParentOfAPartiallyVisibleMerge()
    {
        GraphLayoutResult result = LayoutComplete(
            Commit("M", "missing", "B"),
            Commit("B", "A"),
            Commit("A"));

        // The only parent that is present continues in the merge's own lane, so the graph does not
        // fork towards a commit that will never be drawn.
        Assert.Equal(
            [(0, 0)],
            Edges(Row(result, "M"), GraphEdgeKind.BranchOut).Select(edge => (edge.FromLane, edge.ToLane)));
        Assert.Equal(0, Row(result, "B").Lane);
        Assert.Equal(0, result.MaxLane);
    }

    [Fact]
    public void Build_WhileStillPagingKeepsTheLaneOpenForAParentItHasNotSeenYet()
    {
        GraphLayoutResult result = Layout(Commit("C", "B"), Commit("B", "later"));

        Assert.Equal(1, result.State.OpenLaneCount);
        Assert.Single(Edges(Row(result, "B"), GraphEdgeKind.BranchOut));
    }

    // ---------------------------------------------------------------- paging

    [Fact]
    public void Build_PagedProducesExactlyTheSameRowsAsOneShot()
    {
        List<GraphCommitInput> commits =
        [
            Commit("H", "G", "F"),
            Commit("G", "E"),
            Commit("F", "D"),
            Commit("E", "C"),
            Commit("D", "C"),
            Commit("C", "B", "Z"),
            Commit("Z", "B"),
            Commit("B", "A"),
            Commit("A"),
        ];

        GraphLayoutResult whole = CommitGraphLayout.Build(commits);

        GraphLayoutResult firstPage = CommitGraphLayout.Build(commits.Take(4).ToList());
        GraphLayoutResult secondPage = CommitGraphLayout.Build(commits.Skip(4).ToList(), firstPage.State);

        List<string> pagedRows =
        [
            .. firstPage.Rows.Select(Describe),
            .. secondPage.Rows.Select(Describe),
        ];

        Assert.Equal(whole.Rows.Select(Describe), pagedRows);
    }

    [Fact]
    public void Build_PagedAtEveryPossibleBoundaryMatchesTheOneShotLayout()
    {
        List<GraphCommitInput> commits =
        [
            Commit("F", "E", "D"),
            Commit("E", "C"),
            Commit("D", "C"),
            Commit("C", "B"),
            Commit("B", "A"),
            Commit("A"),
        ];

        List<string> expected = [.. CommitGraphLayout.Build(commits).Rows.Select(Describe)];

        for (int split = 0; split <= commits.Count; split++)
        {
            GraphLayoutResult first = CommitGraphLayout.Build(commits.Take(split).ToList());
            GraphLayoutResult second = CommitGraphLayout.Build(commits.Skip(split).ToList(), first.State);

            List<string> actual = [.. first.Rows.Select(Describe), .. second.Rows.Select(Describe)];

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Build_DoesNotMutateTheCarriedState()
    {
        GraphLayoutResult first = CommitGraphLayout.Build([Commit("B", "A")]);
        GraphLayoutState carried = first.State;
        int openBefore = carried.OpenLaneCount;
        string? awaitedBefore = carried.GetAwaitedSha(0);

        _ = CommitGraphLayout.Build([Commit("A")], carried);

        Assert.Equal(openBefore, carried.OpenLaneCount);
        Assert.Equal(awaitedBefore, carried.GetAwaitedSha(0));
    }

    // ---------------------------------------------------------------- degenerate input

    [Fact]
    public void Build_HandlesAnEmptyHistory()
    {
        GraphLayoutResult result = CommitGraphLayout.Build([]);

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.MaxLane);
        Assert.Equal(0, result.State.OpenLaneCount);
    }

    [Fact]
    public void Build_RejectsANullCommitList()
        => Assert.Throws<ArgumentNullException>(() => CommitGraphLayout.Build(null!));

    [Fact]
    public void Build_HandlesASingleRootCommit()
    {
        GraphLayoutResult result = Layout(Commit("A"));

        GraphRow row = Assert.Single(result.Rows);
        Assert.True(row.IsRoot);
        Assert.Empty(row.Edges);
        Assert.Equal(0, row.MaxLane);
    }

    [Fact]
    public void Build_FallsBackToRepeatingColoursWhenThePaletteRunsOut()
    {
        // Twelve simultaneous branch tips against a three-colour palette.
        List<GraphCommitInput> commits = [];
        for (int index = 0; index < 12; index++)
        {
            commits.Add(Commit($"tip-{index.ToString(CultureInfo.InvariantCulture)}", "shared"));
        }

        GraphLayoutResult result = CommitGraphLayout.Build(commits, null, new GraphLayoutOptions { ColourCount = 3 });

        Assert.All(result.Rows, row => Assert.InRange(row.Colour, 0, 2));
        Assert.Equal(11, result.MaxLane);
    }

    [Fact]
    public void Build_ToleratesAColourCountOfZero()
    {
        GraphLayoutResult result = CommitGraphLayout.Build([Commit("A")], null, new GraphLayoutOptions { ColourCount = 0 });

        Assert.Equal(0, Assert.Single(result.Rows).Colour);
    }

    // ---------------------------------------------------------------- performance

    [Fact]
    public void Build_LaysOutAHundredThousandCommitsQuickly()
    {
        const int count = 100_000;
        List<GraphCommitInput> commits = new(count);

        // A long trunk with a merged side branch every ten commits, which keeps several lanes open
        // at all times rather than measuring a degenerate single-lane case.
        for (int index = 0; index < count; index++)
        {
            string sha = index.ToString("D8", CultureInfo.InvariantCulture);
            string parent = (index + 1).ToString("D8", CultureInfo.InvariantCulture);

            commits.Add(index % 10 == 0 && index + 3 < count
                ? Commit(sha, parent, (index + 3).ToString("D8", CultureInfo.InvariantCulture))
                : Commit(sha, parent));
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        GraphLayoutResult result = CommitGraphLayout.Build(commits);
        stopwatch.Stop();

        Assert.Equal(count, result.Rows.Count);

        // Generous, so the assertion stays meaningful on a loaded machine while still failing loudly
        // if the pass ever becomes quadratic.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"laying out {count} commits took {stopwatch.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------------- invariants

    [Fact]
    public void Build_EveryRowsEdgesStayWithinItsReportedWidth()
    {
        GraphLayoutResult result = Layout(
            Commit("H", "G", "F"),
            Commit("G", "E"),
            Commit("F", "D"),
            Commit("E", "C"),
            Commit("D", "C"),
            Commit("C", "B"),
            Commit("B", "A"),
            Commit("A"));

        foreach (GraphRow row in result.Rows)
        {
            Assert.InRange(row.Lane, 0, row.MaxLane);
            Assert.InRange(row.MaxLane, 0, result.MaxLane);

            foreach (GraphEdge edge in row.Edges)
            {
                Assert.InRange(edge.FromLane, 0, row.MaxLane);
                Assert.InRange(edge.ToLane, 0, row.MaxLane);
            }
        }
    }

    [Fact]
    public void Build_OrdersEdgesStraightThenMergeInThenBranchOut()
    {
        GraphRow row = Row(
            Layout(
                Commit("X", "W"),
                Commit("M", "B", "C"),
                Commit("C", "B"),
                Commit("B", "A"),
                Commit("A"),
                Commit("W")),
            "B");

        List<GraphEdgeKind> kinds = [.. row.Edges.Select(edge => edge.Kind)];

        // A drawing order that paints the through-lines first and the curves over them.
        Assert.Equal(kinds.OrderBy(kind => (int)kind), kinds);
        Assert.Contains(GraphEdgeKind.Straight, kinds);
        Assert.Contains(GraphEdgeKind.MergeIn, kinds);
        Assert.Contains(GraphEdgeKind.BranchOut, kinds);
    }

    [Fact]
    public void GraphEdge_ReportsWhetherItIsVertical()
    {
        Assert.True(new GraphEdge(2, 2, GraphEdgeKind.Straight, 0).IsVertical);
        Assert.False(new GraphEdge(0, 2, GraphEdgeKind.BranchOut, 0).IsVertical);
    }

    [Fact]
    public void GraphCommitInput_ProjectsACommit()
    {
        Core.History.GitCommit commit = new(
            "1111111111111111111111111111111111111111",
            ["2222222222222222222222222222222222222222"],
            Core.History.GitSignature.Empty,
            Core.History.GitSignature.Empty,
            "S",
            string.Empty);

        GraphCommitInput input = GraphCommitInput.From(commit);

        Assert.Equal(commit.Sha, input.Sha);
        Assert.Equal(commit.ParentShas, input.ParentShas);
        Assert.Single(GraphCommitInput.From([commit]));
    }

    [Fact]
    public void Layout_ProducesOneRowPerCommitInTheGivenOrder()
    {
        GraphLayoutResult result = Layout(Commit("C", "B"), Commit("B", "A"), Commit("A"));

        Assert.Equal(["C", "B", "A"], result.Rows.Select(row => row.Sha));
    }

    [Fact]
    public void GraphRow_ToStringSummarisesTheRow()
    {
        string text = Row(Layout(Commit("abcdef1234567890", "B")), "abcdef1234567890").ToString();

        Assert.Contains("abcdef1", text, StringComparison.Ordinal);
        Assert.Contains("lane 0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_KeepsTheStateUsableForAThirdPage()
    {
        List<GraphCommitInput> page1 = [Commit("F", "E", "D")];
        List<GraphCommitInput> page2 = [Commit("E", "C"), Commit("D", "C")];
        List<GraphCommitInput> page3 = [Commit("C", "B"), Commit("B", "A"), Commit("A")];

        GraphLayoutResult first = CommitGraphLayout.Build(page1);
        GraphLayoutResult second = CommitGraphLayout.Build(page2, first.State);
        GraphLayoutResult third = CommitGraphLayout.Build(page3, second.State);

        List<GraphCommitInput> all = [.. page1, .. page2, .. page3];
        List<string> expected = [.. CommitGraphLayout.Build(all).Rows.Select(Describe)];
        List<string> actual =
        [
            .. first.Rows.Select(Describe),
            .. second.Rows.Select(Describe),
            .. third.Rows.Select(Describe),
        ];

        Assert.Equal(expected, actual);
        Assert.Equal(0, third.State.OpenLaneCount);
    }

    [Fact]
    public void Build_DoesNotThrowOnACommitListContainingADuplicateSha()
    {
        // Defensive: a caller appending an overlapping page must not corrupt the pass.
        GraphLayoutResult result = Layout(Commit("B", "A"), Commit("B", "A"), Commit("A"));

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(0, result.State.OpenLaneCount);
    }

    [Fact]
    public void Describe_IsStableForTheSameInput()
    {
        GraphLayoutResult first = Layout(Commit("B", "A"), Commit("A"));
        GraphLayoutResult second = Layout(Commit("B", "A"), Commit("A"));

        Assert.Equal(first.Rows.Select(Describe), second.Rows.Select(Describe));
    }

    [Fact]
    public void GraphLayoutResult_EmptyIsUsableAsAStartingPoint()
    {
        GraphLayoutResult empty = GraphLayoutResult.Empty;

        Assert.Empty(empty.Rows);

        GraphLayoutResult next = CommitGraphLayout.Build([Commit("A")], empty.State);
        Assert.Single(next.Rows);
    }

    [Fact]
    public void GraphLayoutOptions_DefaultsToTenColoursAndPagedBehaviour()
    {
        Assert.Equal(10, GraphLayoutOptions.Default.ColourCount);
        Assert.False(GraphLayoutOptions.Default.HistoryIsComplete);
    }

    [Fact]
    public void Build_AssignsDistinctColoursToAdjacentOpenLanes()
    {
        // Five branch tips all pointing at one commit: no two open lanes may share a colour while
        // the palette still has room.
        GraphLayoutResult result = Layout(
            Commit("t0", "shared"),
            Commit("t1", "shared"),
            Commit("t2", "shared"),
            Commit("t3", "shared"),
            Commit("t4", "shared"));

        List<int> colours = [.. result.Rows.Select(row => row.Colour)];

        Assert.Equal(colours.Count, colours.Distinct().Count());
    }

    [Fact]
    public void Build_ThrowsNothingWhenTheSameParentIsClaimedByManyChildren()
    {
        GraphLayoutResult result = Layout(
            Commit("t0", "shared"),
            Commit("t1", "shared"),
            Commit("t2", "shared"),
            Commit("shared"));

        GraphRow shared = Row(result, "shared");

        Assert.Equal(0, shared.Lane);
        Assert.Equal(3, Edges(shared, GraphEdgeKind.MergeIn).Count());
        Assert.Equal(0, result.State.OpenLaneCount);
    }

    [Fact]
    public void FailingAssertionsReportTheRowTheyCameFrom()
    {
        // Guards the test helper itself: Describe must include the SHA, or a failure in the paging
        // tests above is unreadable.
        string described = Describe(Row(Layout(Commit("A")), "A"));

        Assert.StartsWith("A|", described, StringComparison.Ordinal);
        Assert.IsType<XunitException>(Record.Exception(() => Assert.Equal("x", described)), exactMatch: false);
    }
}
