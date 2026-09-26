using System;
using System.Collections.Generic;
using System.Globalization;

namespace Enigma.GitClient.Core.Diff;

/// <summary>
/// How much of a patch to parse.
/// </summary>
public sealed record DiffParseOptions
{
    /// <summary>
    /// The options the diff viewer uses.
    /// </summary>
    public static readonly DiffParseOptions Default = new();

    /// <summary>
    /// Gets the most lines a single file's patch may contribute before parsing stops for that file
    /// and it is reported as truncated. A generated file can run to millions of lines, and a viewer
    /// that tries to hold all of them is a viewer that freezes.
    /// </summary>
    public int MaxLinesPerFile { get; init; } = 5000;

    /// <summary>
    /// Gets a value indicating whether to compute word-level segments for paired changed lines.
    /// </summary>
    public bool ComputeWordDiff { get; init; } = true;
}

/// <summary>
/// Parses the unified patch git produces into the model the viewer renders.
/// </summary>
/// <remarks>
/// Parsing git's own output rather than diffing file contents ourselves is deliberate: git's diff is
/// what rename and copy detection, the whitespace options and the histogram algorithm all live in.
/// Re-implementing it would produce a viewer that disagrees with the repository it is showing.
/// </remarks>
public static class UnifiedDiffParser
{
    private const string FileHeaderPrefix = "diff --git ";
    private const string CombinedFileHeaderPrefix = "diff --cc ";
    private const string CombinedFileHeaderPrefixAlt = "diff --combined ";

    /// <summary>
    /// Parses a whole patch.
    /// </summary>
    /// <param name="patch">Everything git wrote to standard output.</param>
    /// <param name="options">How much to parse.</param>
    /// <returns>The files the patch touches.</returns>
    public static PatchSet Parse(string patch, DiffParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(patch);

        DiffParseOptions effective = options ?? DiffParseOptions.Default;

        if (patch.Length == 0)
        {
            return PatchSet.Empty;
        }

        string[] lines = patch.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<FilePatch> files = [];

        int index = 0;
        while (index < lines.Length)
        {
            string line = lines[index];

            if (IsFileHeader(line))
            {
                files.Add(ParseFile(lines, ref index, effective));
                continue;
            }

            index++;
        }

        return new PatchSet(files);
    }

    private static bool IsFileHeader(string line)
        => line.StartsWith(FileHeaderPrefix, StringComparison.Ordinal) ||
           line.StartsWith(CombinedFileHeaderPrefix, StringComparison.Ordinal) ||
           line.StartsWith(CombinedFileHeaderPrefixAlt, StringComparison.Ordinal);

    private static FilePatch ParseFile(string[] lines, ref int index, DiffParseOptions options)
    {
        string header = lines[index];
        bool isCombined = !header.StartsWith(FileHeaderPrefix, StringComparison.Ordinal);
        index++;

        (string? headerOld, string? headerNew) = isCombined
            ? (ExtractCombinedPath(header), ExtractCombinedPath(header))
            : SplitFileHeaderPaths(header.Substring(FileHeaderPrefix.Length));

        string? oldPath = headerOld;
        string? newPath = headerNew;
        string? oldMode = null;
        string? newMode = null;
        int? similarity = null;
        bool isBinary = false;
        bool sawNewFile = false;
        bool sawDeletedFile = false;
        bool sawRename = false;
        bool sawCopy = false;
        bool sawTypeChange = false;
        bool sawMinusHeader = false;
        bool sawPlusHeader = false;
        string? minusPath = null;
        string? plusPath = null;

        // --- extended headers, up to the first hunk or the next file -------------------------
        while (index < lines.Length && !lines[index].StartsWith("@@", StringComparison.Ordinal) && !IsFileHeader(lines[index]))
        {
            string line = lines[index];

            if (line.StartsWith("old mode ", StringComparison.Ordinal))
            {
                oldMode = line.Substring("old mode ".Length).Trim();
            }
            else if (line.StartsWith("new mode ", StringComparison.Ordinal))
            {
                newMode = line.Substring("new mode ".Length).Trim();
            }
            else if (line.StartsWith("new file mode ", StringComparison.Ordinal))
            {
                newMode = line.Substring("new file mode ".Length).Trim();
                sawNewFile = true;
            }
            else if (line.StartsWith("deleted file mode ", StringComparison.Ordinal))
            {
                oldMode = line.Substring("deleted file mode ".Length).Trim();
                sawDeletedFile = true;
            }
            else if (line.StartsWith("similarity index ", StringComparison.Ordinal))
            {
                similarity = ParsePercentage(line.Substring("similarity index ".Length));
            }
            else if (line.StartsWith("dissimilarity index ", StringComparison.Ordinal))
            {
                int? dissimilar = ParsePercentage(line.Substring("dissimilarity index ".Length));
                similarity = dissimilar.HasValue ? 100 - dissimilar.Value : null;
            }
            else if (line.StartsWith("rename from ", StringComparison.Ordinal))
            {
                oldPath = GitPathQuoting.Unquote(line.Substring("rename from ".Length).Trim());
                sawRename = true;
            }
            else if (line.StartsWith("rename to ", StringComparison.Ordinal))
            {
                newPath = GitPathQuoting.Unquote(line.Substring("rename to ".Length).Trim());
                sawRename = true;
            }
            else if (line.StartsWith("copy from ", StringComparison.Ordinal))
            {
                oldPath = GitPathQuoting.Unquote(line.Substring("copy from ".Length).Trim());
                sawCopy = true;
            }
            else if (line.StartsWith("copy to ", StringComparison.Ordinal))
            {
                newPath = GitPathQuoting.Unquote(line.Substring("copy to ".Length).Trim());
                sawCopy = true;
            }
            else if (line.StartsWith("index ", StringComparison.Ordinal))
            {
                string? mode = ParseIndexMode(line);
                if (mode is not null)
                {
                    oldMode ??= mode;
                    newMode ??= mode;
                }
            }
            else if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                sawMinusHeader = true;
                minusPath = GitPathQuoting.UnquoteAndStripPrefix(line.Substring(4));
            }
            else if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                sawPlusHeader = true;
                plusPath = GitPathQuoting.UnquoteAndStripPrefix(line.Substring(4));
            }
            else if (line.StartsWith("Binary files ", StringComparison.Ordinal) ||
                     line.StartsWith("GIT binary patch", StringComparison.Ordinal) ||
                     line.StartsWith("Files ", StringComparison.Ordinal))
            {
                isBinary = true;
            }
            else if (line.StartsWith("old file type is ", StringComparison.Ordinal) ||
                     line.StartsWith("new file type is ", StringComparison.Ordinal))
            {
                sawTypeChange = true;
            }

            index++;
        }

        // The ---/+++ headers are the only unambiguous source of paths: a "diff --git" line with a
        // space in the path cannot be split reliably. Use them whenever they are present.
        if (sawMinusHeader && !sawRename && !sawCopy)
        {
            oldPath = minusPath;
        }

        if (sawPlusHeader && !sawRename && !sawCopy)
        {
            newPath = plusPath;
        }

        if (sawDeletedFile)
        {
            newPath = null;
        }

        if (sawNewFile)
        {
            oldPath = null;
        }

        // --- hunks ---------------------------------------------------------------------------
        List<DiffHunk> hunks = [];
        int lineBudget = options.MaxLinesPerFile;
        bool truncated = false;

        while (index < lines.Length && lines[index].StartsWith("@@", StringComparison.Ordinal))
        {
            if (lineBudget <= 0)
            {
                truncated = true;
                break;
            }

            DiffHunk? hunk = ParseHunk(lines, ref index, isCombined, ref lineBudget, out bool hunkTruncated);

            if (hunk is not null)
            {
                hunks.Add(hunk);
            }

            if (hunkTruncated)
            {
                truncated = true;
                break;
            }
        }

        if (truncated)
        {
            // Skip whatever is left of this file so the next one starts cleanly.
            while (index < lines.Length && !IsFileHeader(lines[index]))
            {
                index++;
            }
        }

        FileChangeKind kind = DetermineKind(
            sawNewFile,
            sawDeletedFile,
            sawRename,
            sawCopy,
            sawTypeChange,
            oldMode,
            newMode,
            hunks.Count > 0,
            isBinary);

        if (options.ComputeWordDiff && !isCombined)
        {
            foreach (DiffHunk hunk in hunks)
            {
                WordDiff.Apply(hunk);
            }
        }

        return new FilePatch(
            oldPath,
            newPath,
            kind,
            hunks,
            oldMode,
            newMode,
            similarity,
            isBinary,
            isCombined,
            truncated);
    }

    private static DiffHunk? ParseHunk(
        string[] lines,
        ref int index,
        bool isCombined,
        ref int lineBudget,
        out bool truncated)
    {
        truncated = false;

        if (!TryParseHunkHeader(lines[index], out int oldStart, out int oldCount, out int newStart, out int newCount, out string heading, out int markerWidth))
        {
            index++;
            return null;
        }

        index++;

        List<DiffLine> hunkLines = [];
        int oldLine = oldStart;
        int newLine = newStart;

        while (index < lines.Length)
        {
            string line = lines[index];

            if (line.StartsWith("@@", StringComparison.Ordinal) || IsFileHeader(line))
            {
                break;
            }

            if (line.Length == 0)
            {
                // A completely empty line at the end of the payload is the split artefact of the
                // final newline, not a context line.
                if (index == lines.Length - 1)
                {
                    index++;
                    break;
                }

                // Some tools emit a bare empty line for an empty context line.
                hunkLines.Add(new DiffLine(DiffLineKind.Context, string.Empty, oldLine, newLine));
                oldLine++;
                newLine++;
                index++;
                lineBudget--;
                continue;
            }

            if (line[0] == '\\')
            {
                hunkLines.Add(new DiffLine(DiffLineKind.NoNewline, line.Substring(1).TrimStart(), null, null));
                index++;
                lineBudget--;
                continue;
            }

            if (isCombined)
            {
                // A combined diff carries one marker column per parent. The client shows it as
                // text rather than pretending it is an ordinary two-sided patch.
                string markers = line.Length >= markerWidth ? line.Substring(0, markerWidth) : line;
                string text = line.Length >= markerWidth ? line.Substring(markerWidth) : string.Empty;

                DiffLineKind combinedKind = markers.Contains('+', StringComparison.Ordinal)
                    ? DiffLineKind.Added
                    : markers.Contains('-', StringComparison.Ordinal)
                        ? DiffLineKind.Removed
                        : DiffLineKind.Context;

                hunkLines.Add(new DiffLine(combinedKind, text, null, null));
                index++;
                lineBudget--;
                continue;
            }

            switch (line[0])
            {
                case '+':
                    hunkLines.Add(new DiffLine(DiffLineKind.Added, line.Substring(1), null, newLine));
                    newLine++;
                    break;
                case '-':
                    hunkLines.Add(new DiffLine(DiffLineKind.Removed, line.Substring(1), oldLine, null));
                    oldLine++;
                    break;
                case ' ':
                    hunkLines.Add(new DiffLine(DiffLineKind.Context, line.Substring(1), oldLine, newLine));
                    oldLine++;
                    newLine++;
                    break;
                default:
                    // Anything else ends the hunk: git does not emit it inside one.
                    return new DiffHunk(oldStart, oldCount, newStart, newCount, heading, hunkLines);
            }

            index++;
            lineBudget--;

            if (lineBudget <= 0)
            {
                truncated = true;
                break;
            }
        }

        return new DiffHunk(oldStart, oldCount, newStart, newCount, heading, hunkLines);
    }

    /// <summary>
    /// Parses a hunk header such as <c>@@ -1,7 +1,9 @@ void Example()</c>, and the combined form
    /// <c>@@@ -1,7 -1,7 +1,9 @@@</c>.
    /// </summary>
    /// <param name="line">The header line.</param>
    /// <param name="oldStart">The first old-side line number.</param>
    /// <param name="oldCount">How many old-side lines the hunk covers.</param>
    /// <param name="newStart">The first new-side line number.</param>
    /// <param name="newCount">How many new-side lines the hunk covers.</param>
    /// <param name="heading">The section heading that follows the markers.</param>
    /// <param name="markerWidth">How many marker columns each content line carries.</param>
    /// <returns><see langword="true"/> when the header could be parsed.</returns>
    public static bool TryParseHunkHeader(
        string line,
        out int oldStart,
        out int oldCount,
        out int newStart,
        out int newCount,
        out string heading,
        out int markerWidth)
    {
        ArgumentNullException.ThrowIfNull(line);

        oldStart = 0;
        oldCount = 0;
        newStart = 0;
        newCount = 0;
        heading = string.Empty;
        markerWidth = 1;

        int at = 0;
        while (at < line.Length && line[at] == '@')
        {
            at++;
        }

        if (at < 2)
        {
            return false;
        }

        markerWidth = at - 1;

        int closing = line.IndexOf(new string('@', at), at, StringComparison.Ordinal);
        if (closing < 0)
        {
            return false;
        }

        string ranges = line.Substring(at, closing - at).Trim();
        heading = closing + at < line.Length ? line.Substring(closing + at).Trim() : string.Empty;

        List<(int Start, int Count)> minus = [];
        (int Start, int Count)? plus = null;

        foreach (string token in ranges.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < 2)
            {
                continue;
            }

            if (!TryParseRange(token.Substring(1), out int start, out int count))
            {
                continue;
            }

            if (token[0] == '-')
            {
                minus.Add((start, count));
            }
            else if (token[0] == '+')
            {
                plus = (start, count);
            }
        }

        if (minus.Count == 0 || plus is null)
        {
            return false;
        }

        // A combined diff lists one old range per parent; the first is the one the client reports.
        oldStart = minus[0].Start;
        oldCount = minus[0].Count;
        newStart = plus.Value.Start;
        newCount = plus.Value.Count;

        return true;
    }

    private static bool TryParseRange(string token, out int start, out int count)
    {
        start = 0;
        count = 1;

        int comma = token.IndexOf(',');

        if (comma < 0)
        {
            // "@@ -1 +1 @@": an omitted count means exactly one line.
            return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out start);
        }

        return int.TryParse(token.AsSpan(0, comma), NumberStyles.None, CultureInfo.InvariantCulture, out start) &&
               int.TryParse(token.AsSpan(comma + 1), NumberStyles.None, CultureInfo.InvariantCulture, out count);
    }

    /// <summary>
    /// Splits the paths out of a <c>diff --git</c> header.
    /// </summary>
    /// <param name="value">Everything after <c>diff --git </c>.</param>
    /// <returns>The old and new paths, as far as the header allows them to be told apart.</returns>
    /// <remarks>
    /// The header is genuinely ambiguous for a path containing a space, which is why the
    /// <c>---</c>/<c>+++</c> lines override whatever this returns whenever they are present. The
    /// heuristic below is only the fallback for a header with no such lines — a pure rename or a
    /// pure mode change.
    /// </remarks>
    public static (string? OldPath, string? NewPath) SplitFileHeaderPaths(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string text = value.Trim();

        if (text.StartsWith('"'))
        {
            int closing = FindClosingQuote(text, 0);
            if (closing > 0 && closing + 2 <= text.Length)
            {
                string first = text.Substring(0, closing + 1);
                string rest = text.Substring(closing + 1).Trim();
                return (GitPathQuoting.UnquoteAndStripPrefix(first), GitPathQuoting.UnquoteAndStripPrefix(rest));
            }
        }

        // Prefer the split that makes both halves equal, which is the overwhelmingly common case
        // and the only one that can be resolved without guessing.
        for (int position = text.IndexOf(" b/", StringComparison.Ordinal);
             position >= 0;
             position = text.IndexOf(" b/", position + 1, StringComparison.Ordinal))
        {
            string left = text.Substring(0, position);
            string right = text.Substring(position + 1);

            string? leftPath = GitPathQuoting.UnquoteAndStripPrefix(left);
            string? rightPath = GitPathQuoting.UnquoteAndStripPrefix(right);

            if (string.Equals(leftPath, rightPath, StringComparison.Ordinal))
            {
                return (leftPath, rightPath);
            }
        }

        int lastSeparator = text.LastIndexOf(" b/", StringComparison.Ordinal);
        if (lastSeparator > 0)
        {
            return (
                GitPathQuoting.UnquoteAndStripPrefix(text.Substring(0, lastSeparator)),
                GitPathQuoting.UnquoteAndStripPrefix(text.Substring(lastSeparator + 1)));
        }

        return (null, null);
    }

    private static string? ExtractCombinedPath(string header)
    {
        int space = header.IndexOf(' ', StringComparison.Ordinal);
        int second = space < 0 ? -1 : header.IndexOf(' ', space + 1);

        return second < 0 || second + 1 >= header.Length
            ? null
            : GitPathQuoting.Unquote(header.Substring(second + 1).Trim());
    }

    private static int FindClosingQuote(string text, int openingIndex)
    {
        for (int index = openingIndex + 1; index < text.Length; index++)
        {
            if (text[index] == '\\')
            {
                index++;
                continue;
            }

            if (text[index] == '"')
            {
                return index;
            }
        }

        return -1;
    }

    private static int? ParsePercentage(string value)
    {
        string trimmed = value.Trim().TrimEnd('%');
        return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;
    }

    private static string? ParseIndexMode(string line)
    {
        // "index 0000000..1234567 100644"
        int lastSpace = line.LastIndexOf(' ');
        if (lastSpace < 0 || lastSpace == line.Length - 1)
        {
            return null;
        }

        string candidate = line.Substring(lastSpace + 1).Trim();
        return candidate.Length is 6 && int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? candidate
            : null;
    }

    private static FileChangeKind DetermineKind(
        bool sawNewFile,
        bool sawDeletedFile,
        bool sawRename,
        bool sawCopy,
        bool sawTypeChange,
        string? oldMode,
        string? newMode,
        bool hasHunks,
        bool isBinary)
    {
        if (sawTypeChange)
        {
            return FileChangeKind.TypeChanged;
        }

        if (sawNewFile)
        {
            return FileChangeKind.Added;
        }

        if (sawDeletedFile)
        {
            return FileChangeKind.Deleted;
        }

        if (sawCopy)
        {
            return FileChangeKind.Copied;
        }

        if (sawRename)
        {
            return FileChangeKind.Renamed;
        }

        bool modeChanged = oldMode is not null && newMode is not null &&
                           !string.Equals(oldMode, newMode, StringComparison.Ordinal);

        if (modeChanged && !hasHunks && !isBinary)
        {
            return FileChangeKind.ModeChanged;
        }

        return FileChangeKind.Modified;
    }
}
