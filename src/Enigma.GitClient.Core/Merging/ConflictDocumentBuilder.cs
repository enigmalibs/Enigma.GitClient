using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Merging;

/// <summary>
/// Turns the three versions of a conflicted file into something to resolve.
/// </summary>
/// <remarks>
/// <para>
/// The regions come from <c>git merge-file --diff3 -p</c> wherever possible, so they match what git
/// itself would have written into the file. Its marker grammar is stable and documented, and
/// parsing it is cheaper and more faithful than re-deriving the merge.
/// </para>
/// <para>
/// When that output is not available — git refused the file, or the caller has only the three
/// strings — <see cref="Merge"/> computes the same shape directly. It is the same algorithm in
/// spirit: find what each side changed against the base, take the changes only one side made, and
/// call the rest a conflict.
/// </para>
/// </remarks>
public static class ConflictDocumentBuilder
{
    /// <summary>
    /// Parses <c>git merge-file --diff3 -p</c> output.
    /// </summary>
    /// <param name="merged">Everything the command wrote.</param>
    /// <param name="lineEnding">The terminator the file uses.</param>
    /// <returns>The document.</returns>
    public static ConflictDocument Parse(string merged, string? lineEnding = null)
    {
        ArgumentNullException.ThrowIfNull(merged);

        string ending = lineEnding ?? ConflictDocument.DetectLineEnding(merged);

        List<ConflictRegion> regions = [];
        List<string> stable = [];
        List<string> ours = [];
        List<string> theirs = [];
        List<string> baseLines = [];

        Section section = Section.Stable;

        void CloseStable()
        {
            if (stable.Count > 0)
            {
                regions.Add(new ConflictRegion([.. stable]));
                stable.Clear();
            }
        }

        foreach (string line in ConflictDocument.SplitLines(merged))
        {
            string trimmed = line.TrimEnd('\n', '\r');

            if (section == Section.Stable && trimmed.StartsWith(ConflictMarkers.Ours, StringComparison.Ordinal))
            {
                CloseStable();
                section = Section.Ours;
                continue;
            }

            if (section != Section.Stable)
            {
                if (trimmed.StartsWith(ConflictMarkers.Base, StringComparison.Ordinal))
                {
                    section = Section.Base;
                    continue;
                }

                if (trimmed.StartsWith(ConflictMarkers.Separator, StringComparison.Ordinal))
                {
                    section = Section.Theirs;
                    continue;
                }

                if (trimmed.StartsWith(ConflictMarkers.Theirs, StringComparison.Ordinal))
                {
                    regions.Add(new ConflictRegion([.. baseLines], [.. ours], [.. theirs]));

                    ours.Clear();
                    theirs.Clear();
                    baseLines.Clear();
                    section = Section.Stable;
                    continue;
                }
            }

            switch (section)
            {
                case Section.Ours:
                    ours.Add(line);
                    break;

                case Section.Base:
                    baseLines.Add(line);
                    break;

                case Section.Theirs:
                    theirs.Add(line);
                    break;

                case Section.Stable:
                default:
                    stable.Add(line);
                    break;
            }
        }

        // A conflict git never closed: keep what was read rather than dropping it.
        if (section != Section.Stable)
        {
            regions.Add(new ConflictRegion([.. baseLines], [.. ours], [.. theirs]));
        }

        CloseStable();

        return new ConflictDocument(regions, ending);
    }

    /// <summary>
    /// Merges the three versions directly, without asking git.
    /// </summary>
    /// <param name="baseText">What both sides started from.</param>
    /// <param name="ourText">Our version.</param>
    /// <param name="theirText">Their version.</param>
    /// <returns>The document.</returns>
    public static ConflictDocument Merge(string? baseText, string? ourText, string? theirText)
    {
        IReadOnlyList<string> baseLines = ConflictDocument.SplitLines(baseText);
        IReadOnlyList<string> ourLines = ConflictDocument.SplitLines(ourText);
        IReadOnlyList<string> theirLines = ConflictDocument.SplitLines(theirText);

        string ending = ConflictDocument.DetectLineEnding(ourText ?? theirText ?? baseText);

        bool[] ourMatched = MatchAgainstBase(baseLines, ourLines, out int[] ourToBase);
        bool[] theirMatched = MatchAgainstBase(baseLines, theirLines, out int[] theirToBase);

        List<ConflictRegion> regions = [];
        List<string> stable = [];

        int baseIndex = 0;
        int ourIndex = 0;
        int theirIndex = 0;

        void CloseStable()
        {
            if (stable.Count > 0)
            {
                regions.Add(new ConflictRegion([.. stable]));
                stable.Clear();
            }
        }

        while (baseIndex < baseLines.Count || ourIndex < ourLines.Count || theirIndex < theirLines.Count)
        {
            bool ourAligned = ourIndex < ourLines.Count && ourMatched[ourIndex] && ourToBase[ourIndex] == baseIndex;
            bool theirAligned = theirIndex < theirLines.Count && theirMatched[theirIndex] && theirToBase[theirIndex] == baseIndex;

            if (ourAligned && theirAligned)
            {
                // All three agree on this line: nothing to decide.
                stable.Add(ourLines[ourIndex]);
                baseIndex++;
                ourIndex++;
                theirIndex++;
                continue;
            }

            // Collect the run each side changed, up to the next line all three share.
            Position nextBase = NextCommonBase(baseIndex, ourIndex, theirIndex, ourMatched, ourToBase, theirMatched, theirToBase, baseLines.Count, ourLines.Count, theirLines.Count);

            List<string> baseRun = Take(baseLines, baseIndex, nextBase.BaseIndex);
            List<string> ourRun = Take(ourLines, ourIndex, nextBase.OurIndex);
            List<string> theirRun = Take(theirLines, theirIndex, nextBase.TheirIndex);

            if (Same(ourRun, theirRun))
            {
                // Both sides made the same change, which is agreement rather than a conflict.
                stable.AddRange(ourRun);
            }
            else if (Same(baseRun, ourRun))
            {
                // Only they changed it.
                stable.AddRange(theirRun);
            }
            else if (Same(baseRun, theirRun))
            {
                // Only we changed it.
                stable.AddRange(ourRun);
            }
            else
            {
                CloseStable();
                regions.Add(new ConflictRegion(baseRun, ourRun, theirRun));
            }

            baseIndex = nextBase.BaseIndex;
            ourIndex = nextBase.OurIndex;
            theirIndex = nextBase.TheirIndex;
        }

        CloseStable();

        return new ConflictDocument(regions, ending);
    }

    private enum Section
    {
        Stable,
        Ours,
        Base,
        Theirs,
    }

    private readonly record struct Position(int BaseIndex, int OurIndex, int TheirIndex);

    /// <summary>
    /// Finds the next base line both sides still have in common, so the changed runs on either side
    /// can be taken whole.
    /// </summary>
    private static Position NextCommonBase(
        int baseIndex,
        int ourIndex,
        int theirIndex,
        bool[] ourMatched,
        int[] ourToBase,
        bool[] theirMatched,
        int[] theirToBase,
        int baseCount,
        int ourCount,
        int theirCount)
    {
        for (int candidate = baseIndex + 1; candidate <= baseCount; candidate++)
        {
            int our = FindMatch(ourMatched, ourToBase, ourCount, ourIndex, candidate);
            int their = FindMatch(theirMatched, theirToBase, theirCount, theirIndex, candidate);

            if (our >= 0 && their >= 0)
            {
                return new Position(candidate, our, their);
            }
        }

        return new Position(baseCount, ourCount, theirCount);
    }

    private static int FindMatch(bool[] matched, int[] toBase, int count, int from, int baseIndex)
    {
        for (int index = from; index < count; index++)
        {
            if (matched[index] && toBase[index] == baseIndex)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Runs a longest-common-subsequence match of one side against the base, so every line either
    /// corresponds to a base line or is new.
    /// </summary>
    private static bool[] MatchAgainstBase(
        IReadOnlyList<string> baseLines,
        IReadOnlyList<string> sideLines,
        out int[] toBase)
    {
        int rows = baseLines.Count;
        int columns = sideLines.Count;

        int[,] lengths = new int[rows + 1, columns + 1];

        for (int row = rows - 1; row >= 0; row--)
        {
            for (int column = columns - 1; column >= 0; column--)
            {
                lengths[row, column] = string.Equals(baseLines[row], sideLines[column], StringComparison.Ordinal)
                    ? lengths[row + 1, column + 1] + 1
                    : Math.Max(lengths[row + 1, column], lengths[row, column + 1]);
            }
        }

        bool[] matched = new bool[columns];
        toBase = new int[columns];

        int baseIndex = 0;
        int sideIndex = 0;

        while (baseIndex < rows && sideIndex < columns)
        {
            if (string.Equals(baseLines[baseIndex], sideLines[sideIndex], StringComparison.Ordinal))
            {
                matched[sideIndex] = true;
                toBase[sideIndex] = baseIndex;
                baseIndex++;
                sideIndex++;
            }
            else if (lengths[baseIndex + 1, sideIndex] >= lengths[baseIndex, sideIndex + 1])
            {
                baseIndex++;
            }
            else
            {
                sideIndex++;
            }
        }

        return matched;
    }

    private static List<string> Take(IReadOnlyList<string> lines, int from, int to)
    {
        List<string> taken = [];

        for (int index = from; index < to && index < lines.Count; index++)
        {
            taken.Add(lines[index]);
        }

        return taken;
    }

    private static bool Same(List<string> left, List<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
