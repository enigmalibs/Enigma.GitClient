using System;
using System.Globalization;

namespace Enigma.GitClient.Core.Repositories;

/// <summary>
/// Turns the lines <c>git clone --progress</c> writes to standard error into progress reports.
/// </summary>
/// <remarks>
/// git rewrites its progress line in place with a carriage return, so the runner emits one chunk per
/// <c>\r</c> as well as per <c>\n</c>. Anything this parser does not recognise is reported as a
/// message with no percentage rather than dropped: a clone that says something unexpected should
/// still say it.
/// </remarks>
public static class CloneProgressParser
{
    /// <summary>
    /// Parses one line of git's progress output.
    /// </summary>
    /// <param name="line">The raw line.</param>
    /// <returns>The progress it reports, or <see langword="null"/> for a line with no information.</returns>
    public static CloneProgress? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        string text = line.Trim();

        // "remote: Counting objects:  35% (7/20)" — the remote: prefix is git relaying the server.
        if (text.StartsWith("remote:", StringComparison.Ordinal))
        {
            text = text.Substring("remote:".Length).Trim();
        }

        if (text.Length == 0)
        {
            return null;
        }

        CloneStage? stage = MatchStage(text);

        if (stage is null)
        {
            return new CloneProgress(CloneStage.Starting, null, text);
        }

        return new CloneProgress(stage.Value, ExtractPercentage(text), text);
    }

    private static CloneStage? MatchStage(string text)
    {
        if (text.StartsWith("Counting objects", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Enumerating objects", StringComparison.OrdinalIgnoreCase))
        {
            return CloneStage.CountingObjects;
        }

        if (text.StartsWith("Compressing objects", StringComparison.OrdinalIgnoreCase))
        {
            return CloneStage.CompressingObjects;
        }

        if (text.StartsWith("Receiving objects", StringComparison.OrdinalIgnoreCase))
        {
            return CloneStage.ReceivingObjects;
        }

        if (text.StartsWith("Resolving deltas", StringComparison.OrdinalIgnoreCase))
        {
            return CloneStage.ResolvingDeltas;
        }

        if (text.StartsWith("Updating files", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Checking out files", StringComparison.OrdinalIgnoreCase))
        {
            return CloneStage.UpdatingFiles;
        }

        return null;
    }

    /// <summary>
    /// Reads the percentage out of a progress line, for example the <c>35</c> in
    /// <c>Counting objects:  35% (7/20)</c>.
    /// </summary>
    /// <param name="text">The line.</param>
    /// <returns>The percentage, or <see langword="null"/> when the line carries none.</returns>
    public static int? ExtractPercentage(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int percent = text.IndexOf('%');

        if (percent <= 0)
        {
            return null;
        }

        int start = percent - 1;
        while (start >= 0 && char.IsAsciiDigit(text[start]))
        {
            start--;
        }

        start++;

        if (start == percent)
        {
            return null;
        }

        return int.TryParse(
            text.AsSpan(start, percent - start),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int value)
            ? Math.Clamp(value, 0, 100)
            : null;
    }
}
