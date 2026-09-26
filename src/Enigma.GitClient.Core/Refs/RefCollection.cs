using System.Collections.Generic;

namespace Enigma.GitClient.Core.Refs;

/// <summary>
/// Every reference in a repository, already split by kind.
/// </summary>
/// <param name="LocalBranches">Branches under <c>refs/heads</c>.</param>
/// <param name="RemoteBranches">Remote-tracking branches under <c>refs/remotes</c>.</param>
/// <param name="Tags">Tags under <c>refs/tags</c>.</param>
/// <param name="Other">The stash, note refs and anything else.</param>
public sealed record RefCollection(
    IReadOnlyList<GitBranch> LocalBranches,
    IReadOnlyList<GitBranch> RemoteBranches,
    IReadOnlyList<GitTag> Tags,
    IReadOnlyList<GitRef> Other)
{
    /// <summary>
    /// A repository with no references at all, which is what a freshly initialised repository has.
    /// </summary>
    public static readonly RefCollection Empty = new([], [], [], []);

    /// <summary>
    /// Gets the branch HEAD points at, or <see langword="null"/> when HEAD is detached or unborn.
    /// </summary>
    public GitBranch? CurrentBranch
    {
        get
        {
            foreach (GitBranch branch in LocalBranches)
            {
                if (branch.IsCurrent)
                {
                    return branch;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the stash reference when the repository has stashed changes.
    /// </summary>
    public GitRef? Stash
    {
        get
        {
            foreach (GitRef reference in Other)
            {
                if (reference.Kind == GitRefKind.Stash)
                {
                    return reference;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Enumerates every reference, whatever its kind.
    /// </summary>
    /// <returns>All references.</returns>
    public IEnumerable<GitRef> All()
    {
        foreach (GitBranch branch in LocalBranches)
        {
            yield return branch;
        }

        foreach (GitBranch branch in RemoteBranches)
        {
            yield return branch;
        }

        foreach (GitTag tag in Tags)
        {
            yield return tag;
        }

        foreach (GitRef reference in Other)
        {
            yield return reference;
        }
    }
}
