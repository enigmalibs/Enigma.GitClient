using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.History;

namespace Enigma.GitClient.Core.Squash;

/// <summary>
/// What a squash between two loaded commits would replace, as the history's menu counts it.
/// </summary>
/// <param name="CommitCount">
/// The commits that descend from the base and lead to the tip, the tip included — the same count
/// <see cref="SquashPreview.CommitCount"/> reads from git.
/// </param>
public sealed record SquashRangeMeasure(int CommitCount);

/// <summary>
/// Measures a squash's range from commits already read, without asking git — what a context menu
/// can afford while it opens.
/// </summary>
/// <remarks>
/// It relies on the order the history reads in: newest first, and never a parent before one of its
/// children (<see cref="CommitLogOrdering.Date"/> and <see cref="CommitLogOrdering.Topological"/> both
/// promise it). Every commit that descends from a loaded base is then loaded too, above it, so the
/// first-parent line from a tip down to the base and the range's count are both exact.
/// </remarks>
public static class SquashRange
{
    /// <summary>
    /// Measures the range between a base and a tip.
    /// </summary>
    /// <param name="newestFirst">The loaded commits, in the history's order.</param>
    /// <param name="baseSha">The base's full sha.</param>
    /// <param name="tipSha">The tip's full sha.</param>
    /// <returns>
    /// The measure, or <see langword="null"/> when the base is the tip, either is not loaded, or the
    /// base is not on the tip's first-parent line.
    /// </returns>
    public static SquashRangeMeasure? Measure(IReadOnlyList<GitCommit> newestFirst, string baseSha, string tipSha)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);

        if (string.IsNullOrEmpty(baseSha) || string.IsNullOrEmpty(tipSha)
            || string.Equals(baseSha, tipSha, StringComparison.Ordinal))
        {
            return null;
        }

        Dictionary<string, int> positions = new(newestFirst.Count, StringComparer.Ordinal);

        for (int index = 0; index < newestFirst.Count; index++)
        {
            positions.TryAdd(newestFirst[index].Sha, index);
        }

        if (!positions.TryGetValue(baseSha, out int basePosition) || !positions.ContainsKey(tipSha))
        {
            return null;
        }

        if (!IsOnFirstParentLine(newestFirst, positions, baseSha, tipSha))
        {
            return null;
        }

        // Walking up from the base, a commit descends from it when one of its parents is the base or
        // already does: parents are always further down, so they are settled first.
        HashSet<string> descendants = new(StringComparer.Ordinal);

        for (int index = basePosition - 1; index >= 0; index--)
        {
            GitCommit commit = newestFirst[index];

            foreach (string parent in commit.ParentShas)
            {
                if (string.Equals(parent, baseSha, StringComparison.Ordinal) || descendants.Contains(parent))
                {
                    descendants.Add(commit.Sha);
                    break;
                }
            }
        }

        // The range is the tip's ancestors among them, the tip included.
        HashSet<string> range = new(StringComparer.Ordinal) { tipSha };
        Stack<string> pending = new();
        pending.Push(tipSha);

        while (pending.Count > 0)
        {
            GitCommit commit = newestFirst[positions[pending.Pop()]];

            foreach (string parent in commit.ParentShas)
            {
                if (descendants.Contains(parent) && range.Add(parent))
                {
                    pending.Push(parent);
                }
            }
        }

        return new SquashRangeMeasure(range.Count);
    }

    private static bool IsOnFirstParentLine(
        IReadOnlyList<GitCommit> newestFirst,
        Dictionary<string, int> positions,
        string baseSha,
        string tipSha)
    {
        string current = tipSha;

        // At most one step per loaded commit: history has no cycles, and the bound keeps a malformed
        // list from looping.
        for (int step = 0; step < newestFirst.Count; step++)
        {
            GitCommit commit = newestFirst[positions[current]];

            if (commit.ParentShas.Count == 0)
            {
                return false;
            }

            string firstParent = commit.ParentShas[0];

            if (string.Equals(firstParent, baseSha, StringComparison.Ordinal))
            {
                return true;
            }

            if (!positions.ContainsKey(firstParent))
            {
                return false;
            }

            current = firstParent;
        }

        return false;
    }
}
