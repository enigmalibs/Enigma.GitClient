using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Refs;

/// <summary>
/// Maps each commit to the references that point at it, so the graph can draw a row's badges
/// without searching the whole ref set per row.
/// </summary>
public sealed class RefDecorationIndex
{
    private static readonly IReadOnlyList<GitRef> NoRefs = [];

    private readonly Dictionary<string, List<GitRef>> _byCommit;

    private RefDecorationIndex(Dictionary<string, List<GitRef>> byCommit, HeadState? head)
    {
        _byCommit = byCommit;
        Head = head;
    }

    /// <summary>
    /// An index for a repository with no references.
    /// </summary>
    public static readonly RefDecorationIndex Empty = new([], null);

    /// <summary>
    /// Gets the HEAD state the index was built with, when one was supplied.
    /// </summary>
    public HeadState? Head { get; }

    /// <summary>
    /// Builds an index over a ref collection.
    /// </summary>
    /// <param name="refs">The references to index.</param>
    /// <param name="head">The HEAD state, used to sort the current branch first.</param>
    /// <returns>The index.</returns>
    public static RefDecorationIndex Build(RefCollection refs, HeadState? head = null)
    {
        ArgumentNullException.ThrowIfNull(refs);

        return Build(refs.All(), head);
    }

    /// <summary>
    /// Builds an index over an arbitrary sequence of references.
    /// </summary>
    /// <param name="refs">The references to index.</param>
    /// <param name="head">The HEAD state, used to sort the current branch first.</param>
    /// <returns>The index.</returns>
    public static RefDecorationIndex Build(IEnumerable<GitRef> refs, HeadState? head = null)
    {
        ArgumentNullException.ThrowIfNull(refs);

        Dictionary<string, List<GitRef>> byCommit = new(StringComparer.Ordinal);

        foreach (GitRef reference in refs)
        {
            if (reference.TargetSha.Length == 0)
            {
                continue;
            }

            if (!byCommit.TryGetValue(reference.TargetSha, out List<GitRef>? list))
            {
                list = [];
                byCommit[reference.TargetSha] = list;
            }

            list.Add(reference);
        }

        foreach (List<GitRef> list in byCommit.Values)
        {
            list.Sort(CompareForDisplay);
        }

        return new RefDecorationIndex(byCommit, head);
    }

    /// <summary>
    /// Gets the references pointing at a commit, in the order the badges should be drawn: the
    /// current branch, then other local branches, then remote-tracking branches, then tags, then
    /// anything else — each group ordered by name.
    /// </summary>
    /// <param name="sha">The commit's full SHA.</param>
    /// <returns>The references, empty when none point at the commit.</returns>
    public IReadOnlyList<GitRef> GetRefs(string sha)
    {
        ArgumentNullException.ThrowIfNull(sha);

        return _byCommit.TryGetValue(sha, out List<GitRef>? list) ? list : NoRefs;
    }

    /// <summary>
    /// Checks whether any reference points at a commit.
    /// </summary>
    /// <param name="sha">The commit's full SHA.</param>
    /// <returns><see langword="true"/> when at least one reference points at it.</returns>
    public bool HasRefs(string sha) => _byCommit.ContainsKey(sha);

    /// <summary>
    /// Checks whether HEAD points at a commit, including when HEAD is detached.
    /// </summary>
    /// <param name="sha">The commit's full SHA.</param>
    /// <returns><see langword="true"/> when HEAD resolves to that commit.</returns>
    public bool IsHead(string sha)
        => Head is not null && string.Equals(Head.Sha, sha, StringComparison.Ordinal);

    /// <summary>
    /// Gets the number of commits carrying at least one reference.
    /// </summary>
    public int DecoratedCommitCount => _byCommit.Count;

    private static int CompareForDisplay(GitRef left, GitRef right)
    {
        int result = GroupOrder(left).CompareTo(GroupOrder(right));
        return result != 0
            ? result
            : string.Compare(left.ShortName, right.ShortName, StringComparison.OrdinalIgnoreCase);
    }

    private static int GroupOrder(GitRef reference)
        => reference switch
        {
            GitBranch { IsCurrent: true } => 0,
            GitBranch { IsRemote: false } => 1,
            GitBranch => 2,
            GitTag => 3,
            { Kind: GitRefKind.Stash } => 4,
            _ => 5,
        };
}
