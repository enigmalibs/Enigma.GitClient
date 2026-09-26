using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.History;

namespace Enigma.GitClient.Core.Graph;

/// <summary>
/// The commit a layout pass needs to know about: its identity and its parents, and nothing else.
/// </summary>
/// <param name="Sha">The commit's full SHA.</param>
/// <param name="ParentShas">The parents' full SHAs, first parent first.</param>
public readonly record struct GraphCommitInput(string Sha, IReadOnlyList<string> ParentShas)
{
    /// <summary>
    /// Projects a commit onto the shape the layout needs.
    /// </summary>
    /// <param name="commit">The commit.</param>
    /// <returns>The layout input.</returns>
    public static GraphCommitInput From(GitCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        return new GraphCommitInput(commit.Sha, commit.ParentShas);
    }

    /// <summary>
    /// Projects a sequence of commits onto the shape the layout needs.
    /// </summary>
    /// <param name="commits">The commits.</param>
    /// <returns>The layout inputs, in the same order.</returns>
    public static IReadOnlyList<GraphCommitInput> From(IEnumerable<GitCommit> commits)
    {
        ArgumentNullException.ThrowIfNull(commits);

        List<GraphCommitInput> inputs = [];
        foreach (GitCommit commit in commits)
        {
            inputs.Add(From(commit));
        }

        return inputs;
    }
}

/// <summary>
/// How a layout pass behaves at the edges of the history it was given.
/// </summary>
/// <remarks>
/// Deliberately a record <em>class</em> rather than a record struct: a struct's implicit
/// parameterless constructor ignores primary-constructor defaults, so <c>new()</c> and
/// <c>default</c> would silently mean "zero colours" instead of the intended ten.
/// </remarks>
public sealed record GraphLayoutOptions
{
    /// <summary>
    /// The options a paged history view uses.
    /// </summary>
    public static readonly GraphLayoutOptions Default = new();

    /// <summary>
    /// Gets how many palette entries the theme offers. Lanes cycle through them, avoiding a colour
    /// another open lane is already using.
    /// </summary>
    public int ColourCount { get; init; } = 10;

    /// <summary>
    /// Gets a value indicating whether the commits handed to this pass are the whole history. When
    /// they are, a parent that is not among them cannot arrive later, so its lane is closed instead
    /// of being left to draw a line off the bottom of the graph forever. Leave it
    /// <see langword="false"/> while paging.
    /// </summary>
    public bool HistoryIsComplete { get; init; }
}

/// <summary>
/// The outcome of a layout pass.
/// </summary>
/// <param name="Rows">One row per commit, in the order the commits were given.</param>
/// <param name="State">
/// The lane table at the end of the pass, to be carried into the next page.
/// </param>
/// <param name="MaxLane">The highest lane index any row of this pass occupies.</param>
public sealed record GraphLayoutResult(IReadOnlyList<GraphRow> Rows, GraphLayoutState State, int MaxLane)
{
    /// <summary>
    /// An empty result, for a history with no commits.
    /// </summary>
    public static GraphLayoutResult Empty => new([], new GraphLayoutState(), 0);
}

/// <summary>
/// Assigns every commit a lane and works out the segments drawn around it.
/// </summary>
/// <remarks>
/// <para>
/// The rules are the ones a reader of a graph expects, and they are what keeps a long-lived branch
/// on a stable column:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A commit takes the leftmost lane that was waiting for it. Any other lane waiting for the same
/// commit is a branch being merged in, and it ends at this row.
/// </description></item>
/// <item><description>
/// A commit nothing is waiting for is a branch tip, and opens the leftmost free lane.
/// </description></item>
/// <item><description>
/// The first parent continues in the commit's own lane, so following a branch downwards never
/// changes column. Every further parent takes a lane of its own — reusing one already waiting for
/// that parent when there is one, otherwise the leftmost free lane.
/// </description></item>
/// <item><description>
/// A lane keeps its colour for its whole lifetime, and a newly opened lane avoids the colours the
/// open lanes are already using.
/// </description></item>
/// </list>
/// <para>
/// The pass is O(rows × open lanes) and allocates one row object per commit, which is what lets a
/// hundred-thousand-commit history lay out in well under a second.
/// </para>
/// </remarks>
public static class CommitGraphLayout
{
    /// <summary>
    /// Lays out a page of history.
    /// </summary>
    /// <param name="commits">The commits, in the order git returned them (newest first).</param>
    /// <param name="carry">
    /// The lane table left by the previous page, or <see langword="null"/> to start a history.
    /// </param>
    /// <param name="options">How the pass behaves at the edges of the given history.</param>
    /// <returns>The rows, the lane table to carry forward, and the widest lane used.</returns>
    public static GraphLayoutResult Build(
        IReadOnlyList<GraphCommitInput> commits,
        GraphLayoutState? carry = null,
        GraphLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(commits);

        GraphLayoutOptions effective = options ?? GraphLayoutOptions.Default;
        int colourCount = Math.Max(1, effective.ColourCount);

        GraphLayoutState state = carry?.Clone() ?? new GraphLayoutState();

        HashSet<string>? known = null;
        if (effective.HistoryIsComplete)
        {
            known = new HashSet<string>(commits.Count, StringComparer.Ordinal);
            foreach (GraphCommitInput commit in commits)
            {
                known.Add(commit.Sha);
            }
        }

        List<GraphRow> rows = new(commits.Count);
        List<int> reserved = [];
        int maxLaneOverall = 0;

        foreach (GraphCommitInput commit in commits)
        {
            rows.Add(BuildRow(commit, state, known, colourCount, reserved, ref maxLaneOverall));
        }

        state.TrimTrailingFreeLanes();

        return new GraphLayoutResult(rows, state, maxLaneOverall);
    }

    /// <summary>
    /// Lays out a whole history in one pass, closing every lane at the end.
    /// </summary>
    /// <param name="commits">The commits, newest first.</param>
    /// <param name="colourCount">How many palette entries the theme offers.</param>
    /// <returns>The rows, the final lane table, and the widest lane used.</returns>
    public static GraphLayoutResult BuildComplete(IReadOnlyList<GraphCommitInput> commits, int colourCount = 10)
        => Build(
            commits,
            carry: null,
            new GraphLayoutOptions { ColourCount = colourCount, HistoryIsComplete = true });

    private static GraphRow BuildRow(
        GraphCommitInput commit,
        GraphLayoutState state,
        HashSet<string>? known,
        int colourCount,
        List<int> reserved,
        ref int maxLaneOverall)
    {
        reserved.Clear();

        List<string?> lanes = state.Lanes;
        for (int index = 0; index < lanes.Count; index++)
        {
            if (string.Equals(lanes[index], commit.Sha, StringComparison.Ordinal))
            {
                reserved.Add(index);
            }
        }

        int lane;
        int colour;

        if (reserved.Count == 0)
        {
            // Nothing below has claimed this commit, so it is the tip of a branch: open a lane.
            lane = state.FindFreeLane();
            colour = state.OpenLane(lane, commit.Sha, colourCount);
        }
        else
        {
            lane = reserved[0];
            colour = state.GetColour(lane);
        }

        List<GraphEdge> straight = [];
        List<GraphEdge> mergeIn = [];
        List<GraphEdge> branchOut = [];

        // Every lane that is not part of this row simply passes through it.
        for (int index = 0; index < lanes.Count; index++)
        {
            if (lanes[index] is null || index == lane || reserved.Contains(index))
            {
                continue;
            }

            straight.Add(new GraphEdge(index, index, GraphEdgeKind.Straight, state.GetColour(index)));
        }

        // Lines arriving from above: the commit's own lane, plus any branch merging into it.
        foreach (int index in reserved)
        {
            mergeIn.Add(new GraphEdge(index, lane, GraphEdgeKind.MergeIn, state.GetColour(index)));

            if (index != lane)
            {
                state.Release(index);
            }
        }

        // Lines leaving towards the parents.
        bool laneCarriedOn = false;
        foreach (string parent in commit.ParentShas)
        {
            if (known is not null && !known.Contains(parent))
            {
                // The history is complete and this parent is not in it — a filtered or shallow
                // walk. The line ends here rather than heading for a commit that never arrives.
                continue;
            }

            if (!laneCarriedOn)
            {
                // The first parent that is actually in the history continues in this commit's own
                // lane, so following a branch downwards never changes column.
                laneCarriedOn = true;
                state.Reserve(lane, parent);
                branchOut.Add(new GraphEdge(lane, lane, GraphEdgeKind.BranchOut, colour));
                continue;
            }

            int parentLane = FindLaneAwaiting(lanes, parent);
            int parentColour;

            if (parentLane >= 0)
            {
                parentColour = state.GetColour(parentLane);
            }
            else
            {
                parentLane = state.FindFreeLane();
                parentColour = state.OpenLane(parentLane, parent, colourCount);
            }

            branchOut.Add(new GraphEdge(lane, parentLane, GraphEdgeKind.BranchOut, parentColour));
        }

        if (!laneCarriedOn)
        {
            // Nothing below continues this line — a root commit, or one whose parents all fell
            // outside a complete but filtered history. Either way the lane closes here and becomes
            // available to the next branch that needs one.
            state.Release(lane);
        }

        List<GraphEdge> edges = new(straight.Count + mergeIn.Count + branchOut.Count);
        edges.AddRange(straight);
        edges.AddRange(mergeIn);
        edges.AddRange(branchOut);

        int maxLane = lane;
        foreach (GraphEdge edge in edges)
        {
            maxLane = Math.Max(maxLane, Math.Max(edge.FromLane, edge.ToLane));
        }

        maxLaneOverall = Math.Max(maxLaneOverall, maxLane);

        return new GraphRow(
            commit.Sha,
            lane,
            colour,
            commit.ParentShas.Count > 1,
            commit.ParentShas.Count == 0,
            edges,
            maxLane);
    }

    private static int FindLaneAwaiting(List<string?> lanes, string sha)
    {
        for (int index = 0; index < lanes.Count; index++)
        {
            if (string.Equals(lanes[index], sha, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
