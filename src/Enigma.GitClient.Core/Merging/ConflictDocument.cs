using System;
using System.Collections.Generic;
using System.Text;

namespace Enigma.GitClient.Core.Merging;

/// <summary>
/// What a reader chose to keep for one conflicted region.
/// </summary>
public enum ConflictResolution
{
    /// <summary>Nothing has been chosen yet.</summary>
    Unresolved,

    /// <summary>Keep our version.</summary>
    Ours,

    /// <summary>Keep their version.</summary>
    Theirs,

    /// <summary>Keep both, ours first.</summary>
    OursThenTheirs,

    /// <summary>Keep both, theirs first.</summary>
    TheirsThenOurs,

    /// <summary>Keep what both sides started from.</summary>
    Base,

    /// <summary>Keep something the reader wrote instead.</summary>
    Custom,
}

/// <summary>
/// One stretch of a conflicted file: either text both sides agree on, or a disagreement to settle.
/// </summary>
public sealed class ConflictRegion
{
    /// <summary>
    /// Initialises a region both sides agree on.
    /// </summary>
    /// <param name="lines">The lines, each carrying its own terminator.</param>
    public ConflictRegion(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        Stable = lines;
        BaseLines = [];
        OurLines = [];
        TheirLines = [];
        IsConflicted = false;
        Resolution = ConflictResolution.Unresolved;
    }

    /// <summary>
    /// Initialises a region the two sides disagree about.
    /// </summary>
    /// <param name="baseLines">What both sides started from, empty when there was nothing.</param>
    /// <param name="ourLines">Our version.</param>
    /// <param name="theirLines">Their version.</param>
    public ConflictRegion(
        IReadOnlyList<string> baseLines,
        IReadOnlyList<string> ourLines,
        IReadOnlyList<string> theirLines)
    {
        ArgumentNullException.ThrowIfNull(baseLines);
        ArgumentNullException.ThrowIfNull(ourLines);
        ArgumentNullException.ThrowIfNull(theirLines);

        Stable = [];
        BaseLines = baseLines;
        OurLines = ourLines;
        TheirLines = theirLines;
        IsConflicted = true;
        Resolution = ConflictResolution.Unresolved;
    }

    /// <summary>Gets a value indicating whether this region needs a decision.</summary>
    public bool IsConflicted { get; }

    /// <summary>Gets the lines both sides agree on, empty for a conflicted region.</summary>
    public IReadOnlyList<string> Stable { get; }

    /// <summary>Gets what both sides started from.</summary>
    public IReadOnlyList<string> BaseLines { get; }

    /// <summary>Gets our version.</summary>
    public IReadOnlyList<string> OurLines { get; }

    /// <summary>Gets their version.</summary>
    public IReadOnlyList<string> TheirLines { get; }

    /// <summary>
    /// Gets or sets what the reader chose to keep.
    /// </summary>
    public ConflictResolution Resolution { get; set; }

    /// <summary>
    /// Gets or sets the text to keep when the choice is <see cref="ConflictResolution.Custom"/>.
    /// </summary>
    public string CustomText { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether this region still needs a decision.
    /// </summary>
    public bool NeedsDecision => IsConflicted && Resolution == ConflictResolution.Unresolved;

    /// <summary>
    /// Renders what this region contributes to the file.
    /// </summary>
    /// <param name="lineEnding">The terminator to give lines that have none of their own.</param>
    /// <returns>The text.</returns>
    /// <remarks>
    /// An unresolved region renders with git's own markers rather than as nothing: a preview that
    /// silently dropped it would say the file is finished when it is not.
    /// </remarks>
    public string Render(string lineEnding)
    {
        ArgumentNullException.ThrowIfNull(lineEnding);

        if (!IsConflicted)
        {
            return string.Concat(Stable);
        }

        return Resolution switch
        {
            ConflictResolution.Ours => string.Concat(OurLines),
            ConflictResolution.Theirs => string.Concat(TheirLines),
            ConflictResolution.Base => string.Concat(BaseLines),
            ConflictResolution.OursThenTheirs => string.Concat(OurLines) + string.Concat(TheirLines),
            ConflictResolution.TheirsThenOurs => string.Concat(TheirLines) + string.Concat(OurLines),
            ConflictResolution.Custom => Terminate(CustomText, lineEnding),
            _ => RenderWithMarkers(lineEnding),
        };
    }

    private string RenderWithMarkers(string lineEnding)
    {
        StringBuilder builder = new();

        builder.Append(ConflictMarkers.Ours).Append(lineEnding);
        builder.Append(string.Concat(OurLines));
        builder.Append(ConflictMarkers.Base).Append(lineEnding);
        builder.Append(string.Concat(BaseLines));
        builder.Append(ConflictMarkers.Separator).Append(lineEnding);
        builder.Append(string.Concat(TheirLines));
        builder.Append(ConflictMarkers.Theirs).Append(lineEnding);

        return builder.ToString();
    }

    private static string Terminate(string text, string lineEnding)
        => text.Length == 0 || text.EndsWith('\n') ? text : text + lineEnding;
}

/// <summary>
/// The marker lines git writes into a conflicted file.
/// </summary>
public static class ConflictMarkers
{
    /// <summary>The line that opens our side.</summary>
    public const string Ours = "<<<<<<<";

    /// <summary>The line that opens the base, present only in a three-way conflict.</summary>
    public const string Base = "|||||||";

    /// <summary>The line between the base and their side.</summary>
    public const string Separator = "=======";

    /// <summary>The line that closes their side.</summary>
    public const string Theirs = ">>>>>>>";
}

/// <summary>
/// A conflicted file as something to resolve: a sequence of regions, and the exact bytes that
/// resolving them produces.
/// </summary>
/// <remarks>
/// The preview and the file that gets written come from the same method, so the preview is truthful
/// by construction rather than by care. Line terminators travel with their lines, so resolving a
/// conflict in a file with Windows endings does not quietly rewrite the whole file.
/// </remarks>
public sealed class ConflictDocument
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="regions">The regions, in file order.</param>
    /// <param name="lineEnding">The terminator this file uses.</param>
    public ConflictDocument(IReadOnlyList<ConflictRegion> regions, string lineEnding)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentException.ThrowIfNullOrEmpty(lineEnding);

        Regions = regions;
        LineEnding = lineEnding;
    }

    /// <summary>Gets the regions, in file order.</summary>
    public IReadOnlyList<ConflictRegion> Regions { get; }

    /// <summary>Gets the terminator this file uses.</summary>
    public string LineEnding { get; }

    /// <summary>
    /// Gets the regions that need a decision, in file order.
    /// </summary>
    public IEnumerable<ConflictRegion> Conflicts
    {
        get
        {
            foreach (ConflictRegion region in Regions)
            {
                if (region.IsConflicted)
                {
                    yield return region;
                }
            }
        }
    }

    /// <summary>Gets how many decisions the file needs in total.</summary>
    public int ConflictCount
    {
        get
        {
            int count = 0;

            foreach (ConflictRegion region in Regions)
            {
                if (region.IsConflicted)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Gets how many of them have been made.</summary>
    public int ResolvedCount
    {
        get
        {
            int count = 0;

            foreach (ConflictRegion region in Regions)
            {
                if (region.IsConflicted && !region.NeedsDecision)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Gets a value indicating whether every region has been decided.
    /// </summary>
    public bool IsFullyResolved
    {
        get
        {
            foreach (ConflictRegion region in Regions)
            {
                if (region.NeedsDecision)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Renders exactly what would be written to the file.
    /// </summary>
    /// <returns>The file's text.</returns>
    public string RenderPreview()
    {
        StringBuilder builder = new();

        foreach (ConflictRegion region in Regions)
        {
            builder.Append(region.Render(LineEnding));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Sets the same choice on every region that needs one.
    /// </summary>
    /// <param name="resolution">The choice to make.</param>
    public void ResolveAll(ConflictResolution resolution)
    {
        foreach (ConflictRegion region in Regions)
        {
            if (region.IsConflicted)
            {
                region.Resolution = resolution;
            }
        }
    }

    /// <summary>
    /// Splits text into lines that keep their own terminators, so concatenating them reproduces the
    /// input byte for byte.
    /// </summary>
    /// <param name="text">The text to split.</param>
    /// <returns>The lines, each ending with its terminator except possibly the last.</returns>
    public static IReadOnlyList<string> SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        List<string> lines = [];
        int start = 0;

        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            lines.Add(text[start..(index + 1)]);
            start = index + 1;
        }

        if (start < text.Length)
        {
            // A last line with no terminator: kept exactly, so the file does not gain one.
            lines.Add(text[start..]);
        }

        return lines;
    }

    /// <summary>
    /// Works out which terminator a file uses, so a rendered region matches the rest of it.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <returns><c>\r\n</c> when the first terminator is one, <c>\n</c> otherwise.</returns>
    public static string DetectLineEnding(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "\n";
        }

        int newline = text.IndexOf('\n', StringComparison.Ordinal);

        return newline > 0 && text[newline - 1] == '\r' ? "\r\n" : "\n";
    }
}
