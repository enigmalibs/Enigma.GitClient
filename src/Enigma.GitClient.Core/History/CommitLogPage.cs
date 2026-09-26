using System.Collections.Generic;

namespace Enigma.GitClient.Core.History;

/// <summary>
/// One page of commits from a history walk.
/// </summary>
/// <param name="Commits">The commits, in the order git returned them.</param>
/// <param name="Skip">How many commits were skipped before this page.</param>
/// <param name="HasMore">Whether at least one more commit follows this page.</param>
public sealed record CommitLogPage(IReadOnlyList<GitCommit> Commits, int Skip, bool HasMore)
{
    /// <summary>
    /// An empty page, used for a repository with no commits yet.
    /// </summary>
    public static readonly CommitLogPage Empty = new([], 0, false);

    /// <summary>
    /// Gets a value indicating whether the page holds no commits.
    /// </summary>
    public bool IsEmpty => Commits.Count == 0;
}
