using System;
using Enigma.GitClient.Core.Sync;

namespace Enigma.GitClient.Core.Configuration;

/// <summary>
/// Which theme the application uses.
/// </summary>
public enum ThemePreference
{
    /// <summary>Whatever the operating system is set to.</summary>
    System,

    /// <summary>Always dark.</summary>
    Dark,

    /// <summary>Always light.</summary>
    Light,
}

/// <summary>
/// How a commit's date is written.
/// </summary>
public enum DateDisplay
{
    /// <summary>"2 hours ago".</summary>
    Relative,

    /// <summary>The date and time themselves.</summary>
    Absolute,
}

/// <summary>
/// How the changed files are arranged.
/// </summary>
public enum FilesView
{
    /// <summary>A flat list of paths.</summary>
    List,

    /// <summary>A tree of directories.</summary>
    Tree,
}

/// <summary>
/// How a diff is drawn.
/// </summary>
public enum DiffView
{
    /// <summary>One column, additions and deletions interleaved.</summary>
    Unified,

    /// <summary>Two columns, the old file beside the new one.</summary>
    SideBySide,
}

/// <summary>
/// Everything the application remembers between runs.
/// </summary>
/// <remarks>
/// <para>
/// A record with init-only properties, so a change is a new value rather than a mutation nobody
/// noticed, and every property has a default — the defaults are what a fresh install runs on and
/// what a corrupt file falls back to.
/// </para>
/// <para>
/// <see cref="Version"/> is written into the file so a future build can migrate rather than guess.
/// A file from a newer build is read for the keys this build understands rather than discarded.
/// </para>
/// </remarks>
public sealed record AppSettings
{
    /// <summary>The schema version this build writes.</summary>
    /// <remarks>
    /// Version 2 raised the default row height from 26 to 36, and version 3 moved the diff to the
    /// side-by-side rendering; see <c>SettingsService.Migrate</c> for what each does to a file
    /// written by an earlier one.
    /// </remarks>
    public const int CurrentVersion = 3;

    /// <summary>
    /// The row height version 1 shipped as its default, which the version 2 migration replaces
    /// wherever it was never changed.
    /// </summary>
    public const double LegacyGraphRowHeight = 26;

    /// <summary>
    /// The diff rendering versions 1 and 2 shipped as their default, which the version 3 migration
    /// replaces wherever it was never changed.
    /// </summary>
    public const DiffView LegacyDiffView = DiffView.Unified;

    /// <summary>The settings a fresh install runs on.</summary>
    public static readonly AppSettings Defaults = new();

    /// <summary>Gets the schema version the file was written with.</summary>
    public int Version { get; init; } = CurrentVersion;

    // ---------------------------------------------------------------- appearance

    /// <summary>Gets which theme the application uses.</summary>
    public ThemePreference Theme { get; init; } = ThemePreference.System;

    // ---------------------------------------------------------------- history and graph

    /// <summary>Gets how many commits the history reads at a time.</summary>
    public int HistoryPageSize { get; init; } = History.CommitLogQuery.DefaultPageSize;

    /// <summary>Gets a value indicating whether the history follows only first parents.</summary>
    public bool FirstParentOnly { get; init; }

    /// <summary>Gets how a commit's date is written.</summary>
    public DateDisplay DateDisplay { get; init; } = DateDisplay.Relative;

    /// <summary>Gets how tall a history row is, in device-independent pixels.</summary>
    public double GraphRowHeight { get; init; } = 36;

    /// <summary>Gets the distance between two graph lanes.</summary>
    public double GraphLaneWidth { get; init; } = 16;

    // ---------------------------------------------------------------- changed files

    /// <summary>
    /// Gets how the changed files are arranged. A tree by default, which is what the panel has
    /// always opened as — a preference must not quietly change what the application does today.
    /// </summary>
    public FilesView FilesView { get; init; } = FilesView.Tree;

    /// <summary>Gets how many files a tree will expand on its own.</summary>
    public int FilesAutoExpandLimit { get; init; } = 500;

    // ---------------------------------------------------------------- diff

    /// <summary>
    /// Gets how a diff is drawn. Side by side, which is what a reader comparing two revisions
    /// actually wants to see; the unified rendering is one toolbar click away.
    /// </summary>
    public DiffView DiffView { get; init; } = DiffView.SideBySide;

    /// <summary>Gets how many unchanged lines are shown around a change.</summary>
    public int DiffContextLines { get; init; } = 3;

    /// <summary>Gets a value indicating whether spaces and tabs are drawn.</summary>
    public bool ShowWhitespace { get; init; }

    /// <summary>Gets a value indicating whether whitespace-only changes are ignored.</summary>
    public bool IgnoreWhitespace { get; init; }

    /// <summary>Gets how many columns a tab occupies.</summary>
    public int TabWidth { get; init; } = 4;

    /// <summary>Gets a value indicating whether long lines wrap.</summary>
    public bool WrapLines { get; init; }

    // ---------------------------------------------------------------- git

    /// <summary>
    /// Gets an explicit path to the git executable, empty to let the client find it.
    /// </summary>
    public string GitExecutablePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets what a pull is allowed to do — merge, or fast-forward only.
    /// </summary>
    /// <remarks>
    /// There is no third option and there never will be: this client never rebases, and its pull
    /// carries <c>--no-rebase</c> whatever the repository is configured to do.
    /// </remarks>
    public PullStrategy Pull { get; init; } = PullStrategy.Merge;

    /// <summary>
    /// Returns these settings with every value forced into a range the application can use.
    /// </summary>
    /// <returns>The clamped settings.</returns>
    /// <remarks>
    /// A hand-edited file is the ordinary case here, not an attack: someone types <c>0</c> for the
    /// page size and the history stops working. Clamping is friendlier than refusing to start, and
    /// it keeps every consumer from re-checking the same bounds.
    /// </remarks>
    public AppSettings Normalised()
        => this with
        {
            HistoryPageSize = Math.Clamp(HistoryPageSize, 50, 20_000),
            GraphRowHeight = Math.Clamp(GraphRowHeight, 18, 48),
            GraphLaneWidth = Math.Clamp(GraphLaneWidth, 8, 40),
            FilesAutoExpandLimit = Math.Clamp(FilesAutoExpandLimit, 0, 100_000),
            DiffContextLines = Math.Clamp(DiffContextLines, 0, 100_000),
            TabWidth = Math.Clamp(TabWidth, 1, 16),
            GitExecutablePath = GitExecutablePath?.Trim() ?? string.Empty,
            Theme = Enum.IsDefined(Theme) ? Theme : ThemePreference.System,
            DateDisplay = Enum.IsDefined(DateDisplay) ? DateDisplay : DateDisplay.Relative,
            FilesView = Enum.IsDefined(FilesView) ? FilesView : FilesView.List,
            DiffView = Enum.IsDefined(DiffView) ? DiffView : Defaults.DiffView,
            Pull = Enum.IsDefined(Pull) ? Pull : PullStrategy.Merge,
        };
}
