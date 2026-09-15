using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Graph;

/// <summary>
/// What a segment of the graph connects. Every segment is drawn inside exactly one row, which is
/// what lets the history view virtualise: a row can be rendered knowing nothing about its
/// neighbours.
/// </summary>
public enum GraphEdgeKind
{
    /// <summary>
    /// A lane that passes straight through the row without touching its commit: a vertical line
    /// from the row's top edge to its bottom edge.
    /// </summary>
    Straight,

    /// <summary>
    /// A line arriving from the row's top edge and ending at the commit node. When the lanes match
    /// it is the commit's own lane continuing upwards; when they differ it is another branch being
    /// merged in, and that lane ends here.
    /// </summary>
    MergeIn,

    /// <summary>
    /// A line leaving the commit node and reaching the row's bottom edge, on its way to a parent.
    /// When the lanes match it is the commit's own lane continuing downwards; when they differ the
    /// line forks out to the lane that parent occupies.
    /// </summary>
    BranchOut,
}

/// <summary>
/// One drawable segment inside a row.
/// </summary>
/// <param name="FromLane">
/// The lane the segment starts in — at the row's top edge for <see cref="GraphEdgeKind.MergeIn"/>
/// and <see cref="GraphEdgeKind.Straight"/>, at the commit node for
/// <see cref="GraphEdgeKind.BranchOut"/>.
/// </param>
/// <param name="ToLane">
/// The lane the segment ends in — at the commit node for <see cref="GraphEdgeKind.MergeIn"/>, at
/// the row's bottom edge otherwise.
/// </param>
/// <param name="Kind">What the segment connects.</param>
/// <param name="Colour">
/// The palette index of the line this segment belongs to. It is an index, never a colour value:
/// the theme owns what each index actually looks like in Dark and Light.
/// </param>
public readonly record struct GraphEdge(int FromLane, int ToLane, GraphEdgeKind Kind, int Colour)
{
    /// <summary>
    /// Gets a value indicating whether the segment stays in one lane, and so is drawn as a straight
    /// line rather than a curve.
    /// </summary>
    public bool IsVertical => FromLane == ToLane;
}

/// <summary>
/// One row of the commit graph: where the commit's node sits, and every segment drawn around it.
/// </summary>
public sealed class GraphRow
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="sha">The commit this row draws.</param>
    /// <param name="lane">The lane the commit's node sits in.</param>
    /// <param name="colour">The palette index of the commit's lane.</param>
    /// <param name="isMerge">Whether the commit has more than one parent.</param>
    /// <param name="isRoot">Whether the commit has no parents.</param>
    /// <param name="edges">Every segment drawn inside this row.</param>
    /// <param name="maxLane">The highest lane index this row occupies.</param>
    public GraphRow(
        string sha,
        int lane,
        int colour,
        bool isMerge,
        bool isRoot,
        IReadOnlyList<GraphEdge> edges,
        int maxLane)
    {
        ArgumentNullException.ThrowIfNull(sha);
        ArgumentNullException.ThrowIfNull(edges);

        Sha = sha;
        Lane = lane;
        Colour = colour;
        IsMerge = isMerge;
        IsRoot = isRoot;
        Edges = edges;
        MaxLane = maxLane;
    }

    /// <summary>
    /// Gets the SHA of the commit this row draws.
    /// </summary>
    public string Sha { get; }

    /// <summary>
    /// Gets the lane the commit's node sits in, counted from the left.
    /// </summary>
    public int Lane { get; }

    /// <summary>
    /// Gets the palette index of the commit's lane.
    /// </summary>
    public int Colour { get; }

    /// <summary>
    /// Gets a value indicating whether the commit merges two or more histories.
    /// </summary>
    public bool IsMerge { get; }

    /// <summary>
    /// Gets a value indicating whether the commit starts a history.
    /// </summary>
    public bool IsRoot { get; }

    /// <summary>
    /// Gets every segment drawn inside this row, ordered so straight lines come first and curves
    /// are painted over them.
    /// </summary>
    public IReadOnlyList<GraphEdge> Edges { get; }

    /// <summary>
    /// Gets the highest lane index this row occupies, which is what the graph column's width is
    /// measured from.
    /// </summary>
    public int MaxLane { get; }

    /// <inheritdoc />
    public override string ToString()
        => $"{(Sha.Length > 7 ? Sha.Substring(0, 7) : Sha)} lane {Lane} colour {Colour} edges {Edges.Count}";
}
