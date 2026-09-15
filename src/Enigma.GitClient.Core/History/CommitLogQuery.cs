using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.History;

/// <summary>
/// Which commits to read, and how many.
/// </summary>
public sealed record CommitLogQuery
{
    /// <summary>
    /// The number of commits a history page holds by default. Large enough to fill several screens
    /// and to keep the graph's lane state meaningful, small enough that the first paint is fast.
    /// </summary>
    public const int DefaultPageSize = 2000;

    /// <summary>
    /// Gets which refs the walk starts from.
    /// </summary>
    public CommitLogScope Scope { get; init; } = CommitLogScope.AllRefs;

    /// <summary>
    /// Gets the revision to walk when <see cref="Scope"/> is <see cref="CommitLogScope.Revision"/>.
    /// </summary>
    public string? Revision { get; init; }

    /// <summary>
    /// Gets how many commits to skip before the page starts.
    /// </summary>
    public int Skip { get; init; }

    /// <summary>
    /// Gets how many commits the page holds.
    /// </summary>
    public int Take { get; init; } = DefaultPageSize;

    /// <summary>
    /// Gets a value indicating whether to follow only the first parent of each merge, which hides
    /// the merged-in branches and renders a single straight line.
    /// </summary>
    public bool FirstParentOnly { get; init; }

    /// <summary>
    /// Gets the order commits are returned in.
    /// </summary>
    public CommitLogOrdering Ordering { get; init; } = CommitLogOrdering.Date;

    /// <summary>
    /// Gets a case-insensitive substring the author name or email must contain.
    /// </summary>
    public string? AuthorFilter { get; init; }

    /// <summary>
    /// Gets a case-insensitive substring the commit message must contain.
    /// </summary>
    public string? MessageFilter { get; init; }

    /// <summary>
    /// Gets the paths the commits must touch. Empty means every path.
    /// </summary>
    public IReadOnlyList<string> PathFilters { get; init; } = [];

    /// <summary>
    /// Gets the oldest commit date to include.
    /// </summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>
    /// Gets the newest commit date to include.
    /// </summary>
    public DateTimeOffset? Until { get; init; }

    /// <summary>
    /// Gets a value indicating whether the query carries any filter at all, which the UI uses to
    /// decide whether to show a "filtered" indicator.
    /// </summary>
    public bool IsFiltered
        => !string.IsNullOrWhiteSpace(AuthorFilter) ||
           !string.IsNullOrWhiteSpace(MessageFilter) ||
           PathFilters.Count > 0 ||
           Since.HasValue ||
           Until.HasValue ||
           FirstParentOnly;

    /// <summary>
    /// Returns the query for the page after this one.
    /// </summary>
    /// <returns>A copy whose <see cref="Skip"/> has advanced by <see cref="Take"/>.</returns>
    public CommitLogQuery NextPage() => this with { Skip = Skip + Take };
}

/// <summary>
/// Which refs a commit walk starts from.
/// </summary>
public enum CommitLogScope
{
    /// <summary>Every branch, tag and remote-tracking ref, plus HEAD.</summary>
    AllRefs,

    /// <summary>Only the history reachable from HEAD.</summary>
    Head,

    /// <summary>Only the history reachable from <see cref="CommitLogQuery.Revision"/>.</summary>
    Revision,
}

/// <summary>
/// The order commits are returned in.
/// </summary>
public enum CommitLogOrdering
{
    /// <summary>
    /// Chronological, but never showing a commit before one of its children. This is what reads
    /// best in a graph: the dates stay in order while the lanes stay untangled.
    /// </summary>
    Date,

    /// <summary>
    /// Strictly topological: a branch's commits stay contiguous even when that reorders the dates.
    /// </summary>
    Topological,

    /// <summary>git's own default order, with no ordering flag passed.</summary>
    Default,
}
