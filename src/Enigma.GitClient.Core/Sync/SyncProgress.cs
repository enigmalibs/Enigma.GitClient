using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Enigma.GitClient.Core.Sync;

/// <summary>
/// Which part of a transfer git is reporting on.
/// </summary>
public enum SyncStage
{
    /// <summary>Nothing has been reported yet.</summary>
    Starting,

    /// <summary>The remote is working out what it has to send.</summary>
    Enumerating,

    /// <summary>Objects are being counted.</summary>
    Counting,

    /// <summary>Objects are being compressed before transfer.</summary>
    Compressing,

    /// <summary>Objects are arriving.</summary>
    Receiving,

    /// <summary>Objects are being written out.</summary>
    Writing,

    /// <summary>Deltas are being resolved against what is already here.</summary>
    Resolving,

    /// <summary>Files are being written into the work tree.</summary>
    CheckingOut,

    /// <summary>Something git said that this version does not model.</summary>
    Other,
}

/// <summary>
/// One progress report from a network operation.
/// </summary>
/// <param name="Stage">Which part of the transfer it is about.</param>
/// <param name="Percent">How far through that part git is, or <see langword="null"/> when it did not say.</param>
/// <param name="Current">How many items are done, or <see langword="null"/>.</param>
/// <param name="Total">How many items there are, or <see langword="null"/>.</param>
/// <param name="Text">The line git wrote, kept verbatim for anything this does not model.</param>
public sealed record SyncProgress(
    SyncStage Stage,
    int? Percent,
    int? Current,
    int? Total,
    string Text)
{
    /// <summary>
    /// What a caller shows before git has said anything.
    /// </summary>
    public static readonly SyncProgress Starting = new(SyncStage.Starting, null, null, null, string.Empty);

    /// <summary>
    /// Gets the sentence to put in front of a user.
    /// </summary>
    public string Description
        => Stage switch
        {
            SyncStage.Enumerating => "Working out what to transfer",
            SyncStage.Counting => "Counting objects",
            SyncStage.Compressing => "Compressing objects",
            SyncStage.Receiving => "Receiving objects",
            SyncStage.Writing => "Writing objects",
            SyncStage.Resolving => "Resolving deltas",
            SyncStage.CheckingOut => "Updating files",
            SyncStage.Starting => "Contacting the remote",
            _ => Text.Length > 0 ? Text : "Working",
        };

    /// <summary>
    /// Gets a value indicating whether there is a percentage worth showing on a bar.
    /// </summary>
    public bool IsDeterminate => Percent is >= 0 and <= 100;
}

/// <summary>
/// Turns git's <c>--progress</c> chatter into something a progress bar can use.
/// </summary>
/// <remarks>
/// <para>
/// git writes progress to standard error and rewrites the same line with carriage returns, so the
/// caller splits on both <c>\r</c> and <c>\n</c> and hands each chunk here. A line looks like
/// <c>Receiving objects:  73% (1234/1690), 4.02 MiB | 2.01 MiB/s</c>, and the parts worth keeping
/// are the stage, the percentage and the two counts.
/// </para>
/// <para>
/// Anything unrecognised is passed through as <see cref="SyncStage.Other"/> with its text intact
/// rather than dropped: git says useful things that are not progress, and swallowing them would
/// leave a user watching a bar that never explains itself.
/// </para>
/// </remarks>
public static class SyncProgressParser
{
    private static readonly Regex Line = new(
        @"^(?<stage>[A-Za-z][A-Za-z ]+):\s+(?:(?<percent>\d+)%\s*)?(?:\((?<current>\d+)/(?<total>\d+)\))?",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses one chunk of git's standard error.
    /// </summary>
    /// <param name="text">The chunk, without its line terminator.</param>
    /// <returns>The progress, or <see langword="null"/> when the chunk says nothing at all.</returns>
    public static SyncProgress? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim();
        Match match = Line.Match(trimmed);

        if (!match.Success)
        {
            return new SyncProgress(SyncStage.Other, null, null, null, trimmed);
        }

        SyncStage stage = ToStage(match.Groups["stage"].Value);

        return new SyncProgress(
            stage,
            ParseNumber(match.Groups["percent"]),
            ParseNumber(match.Groups["current"]),
            ParseNumber(match.Groups["total"]),
            trimmed);
    }

    /// <summary>
    /// Maps git's own stage wording onto the modelled stages.
    /// </summary>
    /// <param name="stage">The words before the colon.</param>
    /// <returns>The stage.</returns>
    public static SyncStage ToStage(string stage)
    {
        ArgumentNullException.ThrowIfNull(stage);

        return stage.Trim() switch
        {
            "Enumerating objects" => SyncStage.Enumerating,
            "Counting objects" => SyncStage.Counting,
            "Compressing objects" => SyncStage.Compressing,
            "Receiving objects" => SyncStage.Receiving,
            "Writing objects" => SyncStage.Writing,
            "Resolving deltas" => SyncStage.Resolving,
            "Updating files" or "Checking out files" => SyncStage.CheckingOut,
            _ => SyncStage.Other,
        };
    }

    private static int? ParseNumber(Group group)
        => group.Success && int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;
}
