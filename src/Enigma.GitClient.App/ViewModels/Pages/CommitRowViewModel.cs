using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.App.Formatting;
using Enigma.GitClient.Core.Graph;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// The commands a history row's context menu runs.
/// </summary>
/// <remarks>
/// A context menu opens in its own popup tree and cannot reach the page through a visual ancestor,
/// so the row carries the commands and the bindings stay plain.
/// </remarks>
/// <param name="CreateBranchHere">Creates a branch starting at the row's commit.</param>
/// <param name="CheckoutCommit">Checks out the row's commit itself, detaching HEAD.</param>
/// <param name="CreateTagHere">Creates a tag at the row's commit.</param>
/// <param name="Activate">
/// What a double-click does: show what the row changed — or, on the uncommitted-changes row, ask
/// the shell for the working directory. It no longer checks anything out: moving HEAD is the
/// menu's job, not something to arrive at by clicking twice.
/// </param>
/// <param name="ShowChanges">
/// Shows what the row changed. The same thing a double-click does, offered by name for a reader who
/// closed the dialog and wants the same commit back — no selection change would announce that.
/// </param>
/// <param name="OpenOnHost">Opens the row's commit on the host its remote points at.</param>
/// <param name="HostLabel">
/// What that menu item is called. A function rather than a string, because which host a repository
/// is on is read after the rows are built, and the menu asks for the label when it opens.
/// </param>
/// <param name="Branches">
/// What each branch on the row offers, from its own badge and from the row's menu. What used to be
/// here — check out, merge and delete "the" branch of a row — acted on whichever branch came first,
/// which on a line carrying several was not necessarily the one the reader meant.
/// </param>
public sealed record HistoryRowCommands(
    AsyncRelayCommand<CommitRowViewModel> CreateBranchHere,
    AsyncRelayCommand<CommitRowViewModel> CheckoutCommit,
    AsyncRelayCommand<CommitRowViewModel> CreateTagHere,
    RelayCommand<CommitRowViewModel> Activate,
    RelayCommand<CommitRowViewModel> ShowChanges,
    AsyncRelayCommand<CommitRowViewModel>? OpenOnHost = null,
    Func<string?>? HostLabel = null,
    HistoryBranchCommands? Branches = null);

/// <summary>
/// One badge on a history row.
/// </summary>
/// <param name="Kind">What the reference is, which decides the badge's colour and icon.</param>
/// <param name="Name">The reference's short name.</param>
/// <param name="IsCurrent">Whether this is the branch HEAD points at.</param>
/// <remarks>
/// Flattened out of <see cref="GitRef"/> on purpose: only <see cref="GitBranch"/> knows whether it
/// is checked out, and a template bound to the base type cannot see that — which is exactly how the
/// checked-out branch ends up drawn like any other.
/// </remarks>
public sealed record RefBadgeItem(GitRefKind Kind, string Name, bool IsCurrent)
{
    /// <summary>
    /// Projects a reference onto a badge.
    /// </summary>
    /// <param name="reference">The reference.</param>
    /// <returns>The badge.</returns>
    public static RefBadgeItem From(GitRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return new RefBadgeItem(reference.Kind, reference.ShortName, reference is GitBranch { IsCurrent: true });
    }
}

/// <summary>
/// One row of the history view: the graph segment to draw, the commit it belongs to, the refs
/// pointing at it, and everything already formatted for display.
/// </summary>
/// <remarks>
/// Formatting happens once, when the row is created, rather than in a converter on every redraw: a
/// virtualised list re-renders its rows constantly, and a relative timestamp computed per frame is
/// pure waste.
/// </remarks>
public sealed class CommitRowViewModel : ViewModelBase
{
    private static readonly IReadOnlyList<RefBadgeItem> NoRefs = [];
    private static readonly IReadOnlyList<HistoryBranchViewModel> NoBranches = [];

    /// <summary>
    /// Initialises a row for a commit.
    /// </summary>
    /// <param name="commit">The commit.</param>
    /// <param name="row">Its graph row.</param>
    /// <param name="refs">The references pointing at it.</param>
    /// <param name="isHead">Whether HEAD resolves to it.</param>
    /// <param name="now">The moment relative timestamps are measured from.</param>
    /// <param name="commands">The commands the row's own context menu runs.</param>
    /// <param name="absoluteDates">
    /// Whether the row shows the date itself rather than how long ago it was. Both are always
    /// built: the one that is not shown is the tooltip.
    /// </param>
    public CommitRowViewModel(
        GitCommit commit,
        GraphRow row,
        IReadOnlyList<GitRef>? refs,
        bool isHead,
        DateTimeOffset now,
        HistoryRowCommands? commands = null,
        bool absoluteDates = false)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(row);

        Commands = commands;
        Commit = commit;
        Row = row;
        Refs = Project(refs);
        (Branches, Badges) = BuildBranches(Refs, commands?.Branches);
        IsHead = isHead;

        Subject = commit.Subject;
        AuthorName = commit.Author.Name;
        AuthorInitials = commit.Author.Initials;
        AuthorTooltip = commit.Author.ToString();
        ShortSha = commit.ShortSha;
        RelativeDate = RelativeTime.Format(commit.Author.When, now);
        AbsoluteDate = RelativeTime.FormatAbsolute(commit.Author.When);
        ShowsAbsoluteDate = absoluteDates;
    }

    private CommitRowViewModel(GraphRow row, HistoryRowCommands? commands)
    {
        Commands = commands;
        Row = row;
        Refs = NoRefs;
        Branches = NoBranches;
        Badges = [];
        IsUncommitted = true;
        Subject = "Uncommitted changes";
        AuthorName = string.Empty;
        AuthorInitials = string.Empty;
        AuthorTooltip = string.Empty;
        ShortSha = string.Empty;
        RelativeDate = string.Empty;
        AbsoluteDate = string.Empty;
    }

    /// <summary>
    /// Gets the commit, or <see langword="null"/> for the uncommitted-changes row.
    /// </summary>
    public GitCommit? Commit { get; }

    /// <summary>
    /// Gets the graph segment this row draws.
    /// </summary>
    public GraphRow Row { get; }

    /// <summary>
    /// Gets the badges for the references pointing at this commit, in badge order.
    /// </summary>
    public IReadOnlyList<RefBadgeItem> Refs { get; }

    /// <summary>
    /// Gets the commands the row's context menu runs, or <see langword="null"/> when the row was
    /// built without them.
    /// </summary>
    public HistoryRowCommands? Commands { get; }

    /// <summary>
    /// Gets what the "open on the host" menu item is called, naming the host when one is known.
    /// </summary>
    public string HostLabel => Commands?.HostLabel?.Invoke() is { Length: > 0 } host
        ? $"Open this commit on {host}"
        : "Open this commit on the host";

    /// <summary>
    /// Gets a value indicating whether this row can be opened on a host at all.
    /// </summary>
    public bool CanOpenOnHost => Commit is not null && Commands?.HostLabel?.Invoke() is { Length: > 0 };

    /// <summary>
    /// Gets the branches pointing at this commit, each with what it offers, in badge order.
    /// </summary>
    public IReadOnlyList<HistoryBranchViewModel> Branches { get; }

    /// <summary>
    /// Gets what the badge strip draws: a branch as a <see cref="HistoryBranchViewModel"/>, which
    /// brings its own menu, and every other reference as a plain <see cref="RefBadgeItem"/>.
    /// </summary>
    /// <remarks>
    /// Two item types rather than one with an optional menu: a tag badge carrying an empty menu would
    /// still swallow the right-click that should open the line's.
    /// </remarks>
    public IReadOnlyList<object> Badges { get; }

    /// <summary>Gets a value indicating whether a branch points at this commit.</summary>
    public bool HasBranch
    {
        get
        {
            foreach (RefBadgeItem badge in Refs)
            {
                if (badge.Kind is GitRefKind.LocalBranch or GitRefKind.RemoteBranch)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Gets the line's menu, built as it is asked for: the commit's own actions, then — for every
    /// branch on the line — "set it as the merge source" and, when there is a source, "merge it into
    /// this one".
    /// </summary>
    /// <remarks>
    /// Data rather than markup because how many items there are depends on how many branches the line
    /// carries. Every item is listed even when its command would refuse, so the menu keeps its shape;
    /// the command's own can-execute greys it out.
    /// </remarks>
    public IReadOnlyList<HistoryMenuEntry> MenuEntries
    {
        get
        {
            List<HistoryMenuEntry> entries = [];

            if (Commands is not { } commands)
            {
                return entries;
            }

            entries.Add(new HistoryMenuEntry("Show what it changed", commands.ShowChanges, this));
            entries.Add(HistoryMenuEntry.Separator);
            entries.Add(new HistoryMenuEntry("Create branch here…", commands.CreateBranchHere, this));
            entries.Add(new HistoryMenuEntry("Create tag here…", commands.CreateTagHere, this));
            entries.Add(HistoryMenuEntry.Separator);
            entries.Add(new HistoryMenuEntry("Check out this commit (detaches HEAD)", commands.CheckoutCommit, this));

            if (Branches.Count > 0 && commands.Branches is { } branchCommands)
            {
                entries.Add(HistoryMenuEntry.Separator);

                foreach (HistoryBranchViewModel branch in Branches)
                {
                    if (branch.CanSetAsMergeSource)
                    {
                        entries.Add(new HistoryMenuEntry(branch.SetAsMergeSourceHeader, branchCommands.SetAsMergeSource, branch));
                    }
                }

                foreach (HistoryBranchViewModel branch in Branches)
                {
                    if (branch.CanMergeInto)
                    {
                        entries.Add(new HistoryMenuEntry(branch.MergeIntoHeader, branchCommands.MergeInto, branch));
                    }
                }
            }

            if (CanOpenOnHost && commands.OpenOnHost is { } openOnHost)
            {
                entries.Add(HistoryMenuEntry.Separator);
                entries.Add(new HistoryMenuEntry(HostLabel, openOnHost, this));
            }

            return entries;
        }
    }

    /// <summary>
    /// Re-announces everything the page's merge source decides: the line's menu, and each branch's.
    /// </summary>
    public void NotifyMergeSourceChanged()
    {
        foreach (HistoryBranchViewModel branch in Branches)
        {
            branch.NotifyMergeSourceChanged();
        }

        OnPropertyChanged(nameof(MenuEntries));
    }

    /// <summary>
    /// Asks the line's menu to be built again, which is what the view does as it opens: the host's
    /// name, for one, is only known after the rows were built.
    /// </summary>
    public void RefreshMenu() => OnPropertyChanged(nameof(MenuEntries));

    /// <summary>
    /// Gets a value indicating whether there is anything to show in the badge strip.
    /// </summary>
    public bool HasRefs => Refs.Count > 0;

    /// <summary>
    /// Gets a value indicating whether HEAD resolves to this commit.
    /// </summary>
    public bool IsHead { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the row is one the search found.
    /// </summary>
    /// <remarks>
    /// Settable and observable, unlike everything else on the row: the rest is formatted once when
    /// the row is built, but a search must be able to mark a row the list has already realised
    /// without rebuilding it.
    /// </remarks>
    public bool IsSearchMatch
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Gets a value indicating whether this is the pseudo-row standing for the working directory.
    /// </summary>
    public bool IsUncommitted { get; }

    /// <summary>Gets the commit's subject, or the pseudo-row's label.</summary>
    public string Subject { get; }

    /// <summary>Gets the author's name.</summary>
    public string AuthorName { get; }

    /// <summary>Gets the author's initials, for the monogram avatar.</summary>
    public string AuthorInitials { get; }

    /// <summary>Gets the author's name and email, for the avatar's tooltip.</summary>
    public string AuthorTooltip { get; }

    /// <summary>Gets the abbreviated SHA.</summary>
    public string ShortSha { get; }

    /// <summary>Gets the age of the commit, as a short phrase.</summary>
    public string RelativeDate { get; }

    /// <summary>Gets the exact timestamp, for the tooltip behind the relative one.</summary>
    public string AbsoluteDate { get; }

    /// <summary>
    /// Gets a value indicating whether the row shows the date itself rather than how long ago it
    /// was.
    /// </summary>
    public bool ShowsAbsoluteDate { get; }

    /// <summary>Gets the date the row shows.</summary>
    public string DateText => ShowsAbsoluteDate ? AbsoluteDate : RelativeDate;

    /// <summary>Gets the date the row shows in its tooltip, which is the other one.</summary>
    public string DateTooltip => ShowsAbsoluteDate ? RelativeDate : AbsoluteDate;

    /// <summary>
    /// Gets the commit's full SHA, empty for the uncommitted-changes row.
    /// </summary>
    public string Sha => Commit?.Sha ?? string.Empty;

    /// <summary>
    /// Whether this row's commit message contains what is being searched for.
    /// </summary>
    /// <param name="search">What to look for, already trimmed.</param>
    /// <returns><see langword="true"/> when the subject or the body contains it.</returns>
    /// <remarks>
    /// Subject and body, case-insensitively: that is what git's own <c>--grep</c> matched when the
    /// search box filtered the query, so the same words still find the same commits. The
    /// uncommitted-changes row has no message and matches nothing.
    /// </remarks>
    public bool Matches(string search)
    {
        ArgumentNullException.ThrowIfNull(search);

        if (IsUncommitted || search.Length == 0)
        {
            return false;
        }

        return Subject.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || (Commit?.Body.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    /// <summary>
    /// Creates the pseudo-row shown above the history when the working directory is dirty.
    /// </summary>
    /// <param name="lane">The lane it is drawn in, normally the one HEAD occupies.</param>
    /// <param name="colour">The palette index it is drawn in.</param>
    /// <param name="commands">
    /// The commands the row's own menu runs. The pseudo-row has no commit, so most of them refuse —
    /// but activating it is what takes the reader to the page that can act on the work.
    /// </param>
    /// <returns>The row.</returns>
    public static CommitRowViewModel Uncommitted(int lane, int colour, HistoryRowCommands? commands = null)
        => new(
            new GraphRow(
                string.Empty,
                lane,
                colour,
                isMerge: false,
                isRoot: false,
                [new GraphEdge(lane, lane, GraphEdgeKind.BranchOut, colour)],
                lane),
            commands);

    private static (IReadOnlyList<HistoryBranchViewModel> Branches, IReadOnlyList<object> Badges) BuildBranches(
        IReadOnlyList<RefBadgeItem> refs,
        HistoryBranchCommands? commands)
    {
        if (refs.Count == 0)
        {
            return (NoBranches, []);
        }

        List<HistoryBranchViewModel> branches = [];
        List<object> badges = new(refs.Count);

        foreach (RefBadgeItem badge in refs)
        {
            if (commands is not null && badge.Kind is GitRefKind.LocalBranch or GitRefKind.RemoteBranch)
            {
                HistoryBranchViewModel branch = new(badge, commands);
                branches.Add(branch);
                badges.Add(branch);
            }
            else
            {
                badges.Add(badge);
            }
        }

        return (branches.Count == 0 ? NoBranches : branches, badges);
    }

    private static IReadOnlyList<RefBadgeItem> Project(IReadOnlyList<GitRef>? refs)
    {
        if (refs is null || refs.Count == 0)
        {
            return NoRefs;
        }

        List<RefBadgeItem> items = new(refs.Count);

        foreach (GitRef reference in refs)
        {
            items.Add(RefBadgeItem.From(reference));
        }

        return items;
    }

    /// <inheritdoc />
    public override string ToString() => IsUncommitted ? "(uncommitted)" : $"{ShortSha} {Subject}";
}
