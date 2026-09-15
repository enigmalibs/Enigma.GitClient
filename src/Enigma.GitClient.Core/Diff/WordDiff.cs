using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Diff;

/// <summary>
/// Works out which part of a changed line actually changed, so the viewer can highlight the edit
/// inside the line instead of painting the whole line one colour.
/// </summary>
/// <remarks>
/// <para>
/// The pass is deliberately bounded. It first trims the shared prefix and suffix, which alone
/// resolves the overwhelming majority of real edits at O(n) and no allocation. Only when a genuinely
/// different middle remains, and both middles are short enough, does it run a longest-common-
/// subsequence pass over word tokens. A line too long for that falls back to highlighting the whole
/// middle: over-highlighting is a cosmetic imperfection, whereas an unbounded quadratic pass over a
/// minified file is a frozen window.
/// </para>
/// <para>
/// Lines that are not similar enough to be recognisable as an edit of one another get no segments at
/// all, which the viewer renders as a plain whole-line add and remove — the honest answer when a
/// line was replaced rather than edited.
/// </para>
/// </remarks>
public static class WordDiff
{
    /// <summary>
    /// The most word tokens either side of an edit may have before the comparison falls back to
    /// highlighting the whole changed middle.
    /// </summary>
    public const int MaxTokensPerSide = 96;

    /// <summary>
    /// How much of the two lines must survive as common text before they are treated as an edit of
    /// one another rather than as an unrelated replacement.
    /// </summary>
    public const double SimilarityThreshold = 0.25;

    private static readonly IReadOnlyList<DiffSegment> NoSegments = [];

    /// <summary>
    /// Computes the segments of a removed line and the added line it was paired with.
    /// </summary>
    /// <param name="removed">The removed line's text.</param>
    /// <param name="added">The added line's text.</param>
    /// <returns>
    /// The segments for each side. Both are empty when the lines are identical, or when they are too
    /// dissimilar for an intra-line comparison to mean anything.
    /// </returns>
    public static (IReadOnlyList<DiffSegment> Removed, IReadOnlyList<DiffSegment> Added) Compute(
        string removed,
        string added)
    {
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(added);

        if (string.Equals(removed, added, StringComparison.Ordinal))
        {
            return (NoSegments, NoSegments);
        }

        if (removed.Length == 0 || added.Length == 0)
        {
            // One side is empty: the whole of the other side is the change, and there is nothing
            // to highlight inside an empty line.
            return (NoSegments, NoSegments);
        }

        int prefix = CommonPrefixLength(removed, added);
        int suffix = CommonSuffixLength(removed, added, prefix);

        int removedMiddle = removed.Length - prefix - suffix;
        int addedMiddle = added.Length - prefix - suffix;

        if (removedMiddle <= 0 && addedMiddle <= 0)
        {
            return (NoSegments, NoSegments);
        }

        double common = (prefix + suffix) * 2.0 / (removed.Length + added.Length);
        if (common < SimilarityThreshold)
        {
            // Not an edit of the same line — a replacement. Saying so with no segments reads better
            // than highlighting nearly everything.
            return (NoSegments, NoSegments);
        }

        List<Token> removedTokens = Tokenise(removed, prefix, removedMiddle);
        List<Token> addedTokens = Tokenise(added, prefix, addedMiddle);

        if (removedTokens.Count > MaxTokensPerSide || addedTokens.Count > MaxTokensPerSide)
        {
            return (
                WholeMiddle(removed, prefix, removedMiddle),
                WholeMiddle(added, prefix, addedMiddle));
        }

        bool[] removedChanged = new bool[removedTokens.Count];
        bool[] addedChanged = new bool[addedTokens.Count];
        MarkChangedTokens(removed, added, removedTokens, addedTokens, removedChanged, addedChanged);

        return (
            BuildSegments(removed, prefix, removedMiddle, removedTokens, removedChanged),
            BuildSegments(added, prefix, addedMiddle, addedTokens, addedChanged));
    }

    /// <summary>
    /// Pairs the removed and added lines of a hunk and fills in their segments.
    /// </summary>
    /// <param name="hunk">The hunk to annotate, in place.</param>
    /// <remarks>
    /// A run of removed lines followed immediately by a run of added lines is paired positionally —
    /// the first removed with the first added, and so on. That is what git's own
    /// <c>--word-diff</c> does, and it matches how an edit actually appears in a patch.
    /// </remarks>
    public static void Apply(DiffHunk hunk)
    {
        ArgumentNullException.ThrowIfNull(hunk);

        IReadOnlyList<DiffLine> lines = hunk.Lines;
        int index = 0;

        while (index < lines.Count)
        {
            if (lines[index].Kind != DiffLineKind.Removed)
            {
                index++;
                continue;
            }

            int removedStart = index;
            while (index < lines.Count && lines[index].Kind == DiffLineKind.Removed)
            {
                index++;
            }

            int removedCount = index - removedStart;

            int addedStart = index;
            while (index < lines.Count && lines[index].Kind == DiffLineKind.Added)
            {
                index++;
            }

            int addedCount = index - addedStart;
            int pairs = Math.Min(removedCount, addedCount);

            for (int offset = 0; offset < pairs; offset++)
            {
                DiffLine removedLine = lines[removedStart + offset];
                DiffLine addedLine = lines[addedStart + offset];

                (IReadOnlyList<DiffSegment> removedSegments, IReadOnlyList<DiffSegment> addedSegments) =
                    Compute(removedLine.Text, addedLine.Text);

                removedLine.Segments = removedSegments;
                addedLine.Segments = addedSegments;
            }
        }
    }

    private static int CommonPrefixLength(string left, string right)
    {
        int limit = Math.Min(left.Length, right.Length);
        int index = 0;

        while (index < limit && left[index] == right[index])
        {
            index++;
        }

        return index;
    }

    private static int CommonSuffixLength(string left, string right, int prefix)
    {
        int limit = Math.Min(left.Length, right.Length) - prefix;
        int index = 0;

        while (index < limit && left[left.Length - 1 - index] == right[right.Length - 1 - index])
        {
            index++;
        }

        return index;
    }

    private static IReadOnlyList<DiffSegment> WholeMiddle(string text, int prefix, int middleLength)
    {
        if (middleLength <= 0)
        {
            return NoSegments;
        }

        List<DiffSegment> segments = [];

        if (prefix > 0)
        {
            segments.Add(new DiffSegment(0, prefix, false));
        }

        segments.Add(new DiffSegment(prefix, middleLength, true));

        int suffixStart = prefix + middleLength;
        if (suffixStart < text.Length)
        {
            segments.Add(new DiffSegment(suffixStart, text.Length - suffixStart, false));
        }

        return segments;
    }

    /// <summary>
    /// A token is a run of word characters, a run of whitespace, or a single other character. Using
    /// whole words rather than characters is what makes the highlight readable: a renamed
    /// identifier lights up as one unit instead of a scatter of letters.
    /// </summary>
    private readonly record struct Token(int Start, int Length);

    private static List<Token> Tokenise(string text, int start, int length)
    {
        List<Token> tokens = [];
        int index = start;
        int end = start + length;

        while (index < end)
        {
            int tokenStart = index;
            char current = text[index];

            if (char.IsLetterOrDigit(current) || current == '_')
            {
                while (index < end && (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
                {
                    index++;
                }
            }
            else if (char.IsWhiteSpace(current))
            {
                while (index < end && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }
            }
            else
            {
                index++;
            }

            tokens.Add(new Token(tokenStart, index - tokenStart));
        }

        return tokens;
    }

    private static void MarkChangedTokens(
        string removed,
        string added,
        List<Token> removedTokens,
        List<Token> addedTokens,
        bool[] removedChanged,
        bool[] addedChanged)
    {
        int rows = removedTokens.Count;
        int columns = addedTokens.Count;

        // Longest common subsequence over tokens. Bounded by MaxTokensPerSide, so the table is at
        // most 97 x 97 entries.
        int[,] lengths = new int[rows + 1, columns + 1];

        for (int row = rows - 1; row >= 0; row--)
        {
            for (int column = columns - 1; column >= 0; column--)
            {
                lengths[row, column] = TokensEqual(removed, added, removedTokens[row], addedTokens[column])
                    ? lengths[row + 1, column + 1] + 1
                    : Math.Max(lengths[row + 1, column], lengths[row, column + 1]);
            }
        }

        for (int index = 0; index < rows; index++)
        {
            removedChanged[index] = true;
        }

        for (int index = 0; index < columns; index++)
        {
            addedChanged[index] = true;
        }

        int r = 0;
        int c = 0;

        while (r < rows && c < columns)
        {
            if (TokensEqual(removed, added, removedTokens[r], addedTokens[c]))
            {
                removedChanged[r] = false;
                addedChanged[c] = false;
                r++;
                c++;
            }
            else if (lengths[r + 1, c] >= lengths[r, c + 1])
            {
                r++;
            }
            else
            {
                c++;
            }
        }
    }

    private static bool TokensEqual(string left, string right, Token leftToken, Token rightToken)
        => leftToken.Length == rightToken.Length &&
           left.AsSpan(leftToken.Start, leftToken.Length)
               .SequenceEqual(right.AsSpan(rightToken.Start, rightToken.Length));

    private static IReadOnlyList<DiffSegment> BuildSegments(
        string text,
        int prefix,
        int middleLength,
        List<Token> tokens,
        bool[] changed)
    {
        List<DiffSegment> segments = [];

        if (prefix > 0)
        {
            segments.Add(new DiffSegment(0, prefix, false));
        }

        for (int index = 0; index < tokens.Count;)
        {
            bool state = changed[index];
            int start = tokens[index].Start;
            int end = tokens[index].Start + tokens[index].Length;

            index++;
            while (index < tokens.Count && changed[index] == state)
            {
                end = tokens[index].Start + tokens[index].Length;
                index++;
            }

            segments.Add(new DiffSegment(start, end - start, state));
        }

        int suffixStart = prefix + middleLength;
        if (suffixStart < text.Length)
        {
            segments.Add(new DiffSegment(suffixStart, text.Length - suffixStart, false));
        }

        return MergeAdjacent(segments);
    }

    private static IReadOnlyList<DiffSegment> MergeAdjacent(List<DiffSegment> segments)
    {
        if (segments.Count <= 1)
        {
            return segments;
        }

        List<DiffSegment> merged = [segments[0]];

        for (int index = 1; index < segments.Count; index++)
        {
            DiffSegment current = segments[index];
            DiffSegment previous = merged[^1];

            if (previous.IsChanged == current.IsChanged && previous.End == current.Start)
            {
                merged[^1] = new DiffSegment(previous.Start, previous.Length + current.Length, previous.IsChanged);
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }
}
