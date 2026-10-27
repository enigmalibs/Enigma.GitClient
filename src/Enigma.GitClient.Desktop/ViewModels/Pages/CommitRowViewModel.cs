using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Core.Graph;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Reset;
using Enigma.GitClient.Core.Squash;
using Enigma.GitClient.Core.Stashes;
using Enigma.GitClient.Desktop.Formatting;
using Enigma.Icons.Phosphor;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

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
/// <param name="ShowChanges">
/// Shows what the row changed: selects it, and opens the diff on its first file. A click on the line
/// opens the details panel alone; this is the way straight to a diff, by name.
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
/// <param name="ResetSoft">
/// Moves the branch HEAD is on to the row's commit, keeping every change staged. It takes a
/// <see cref="HistoryResetRequest"/> rather than the row, so what moves is the branch the item named.
/// </param>
/// <param name="ResetHard">The same, discarding the uncommitted changes.</param>
/// <param name="CurrentBranch">
/// Reads the name of the branch HEAD is on, or <see langword="null"/> when it is detached or unborn —
/// which is what the two reset items are called after, and whether they are offered at all.
/// </param>
/// <param name="Stashes">The stash's commands, for the stash lines and the uncommitted line.</param>
/// <param name="Copy">Puts a hash on the clipboard.</param>
/// <param name="ShowDetails">
/// Shows the line's commit — its title, description, author, date and hash — in the details dialog,
/// the one the diff view's header opens.
/// </param>
/// <param name="DiscardUncommitted">
/// Throws every uncommitted change away, after asking in red; offered on the uncommitted line.
/// </param>
/// <param name="PushTag">
/// Pushes one tag, by its name, to the remote; offered from every tag badge on a line.
/// </param>
/// <param name="DeleteTag">Deletes one tag here, by its name, after asking; from every tag badge.</param>
/// <param name="DeleteRemoteTag">
/// Deletes one tag, by its name, from the remote its push goes to, after asking; from every tag badge.
/// </param>
/// <param name="Revert">
/// Records a commit on the branch HEAD is on that undoes the row's commit, after asking; offered beside
/// the reset items, under the same condition.
/// </param>
/// <param name="CreateWorktreeHere">
/// Creates a worktree — a working directory of its own, next to the repository — on a new branch
/// starting at the row's commit; offered beside "create branch here", under the same condition.
/// </param>
/// <param name="Squash">
/// The squash's two steps: mark a line as the base, then squash everything since it, up to another
/// line, into one commit on a new branch.
/// </param>
public sealed record HistoryRowCommands(
    AsyncRelayCommand<CommitRowViewModel> CreateBranchHere,
    AsyncRelayCommand<CommitRowViewModel> CheckoutCommit,
    AsyncRelayCommand<CommitRowViewModel> CreateTagHere,
    RelayCommand<CommitRowViewModel> ShowChanges,
    AsyncRelayCommand<CommitRowViewModel>? OpenOnHost = null,
    Func<string?>? HostLabel = null,
    HistoryBranchCommands? Branches = null,
    AsyncRelayCommand<HistoryResetRequest>? ResetSoft = null,
    AsyncRelayCommand<HistoryResetRequest>? ResetHard = null,
    Func<string?>? CurrentBranch = null,
    HistoryStashCommands? Stashes = null,
    AsyncRelayCommand<string>? Copy = null,
    AsyncRelayCommand<CommitRowViewModel>? ShowDetails = null,
    AsyncRelayCommand<CommitRowViewModel>? DiscardUncommitted = null,
    AsyncRelayCommand<string>? PushTag = null,
    AsyncRelayCommand<string>? DeleteTag = null,
    AsyncRelayCommand<string>? DeleteRemoteTag = null,
    AsyncRelayCommand<CommitRowViewModel>? Revert = null,
    AsyncRelayCommand<CommitRowViewModel>? CreateWorktreeHere = null,
    HistorySquashCommands? Squash = null);

/// <summary>
/// The commit a squash starts after, as the page holds it between the two steps.
/// </summary>
/// <param name="Sha">Its full sha.</param>
/// <param name="ShortSha">What the menu, the chip and the dialog call it.</param>
/// <param name="Subject">Its subject, which the dialog quotes.</param>
public sealed record SquashBase(string Sha, string ShortSha, string Subject);

/// <summary>
/// What the squash item of a history line's menu asks for: the line, which is the tip, and the base
/// the item was named after.
/// </summary>
/// <param name="Row">The tip: the squash commit takes its files.</param>
/// <param name="Base">
/// The base the menu showed. Carried rather than read again when the item is clicked, so what is
/// squashed is what the reader saw named.
/// </param>
public sealed record HistorySquashRequest(CommitRowViewModel Row, SquashBase Base);

/// <summary>
/// The squash's commands, as the history's lines offer them.
/// </summary>
/// <param name="SetBase">Makes a line the squash base.</param>
/// <param name="ClearBase">Forgets the squash base.</param>
/// <param name="Squash">Squashes everything since the base, up to a line, onto a new branch.</param>
/// <param name="CurrentBase">Reads the squash base at the moment it is asked for.</param>
/// <param name="Measure">
/// Measures the range from the base to a line among the loaded lines, or <see langword="null"/> when
/// the base is not on that line's first-parent line.
/// </param>
public sealed record HistorySquashCommands(
    RelayCommand<CommitRowViewModel> SetBase,
    RelayCommand ClearBase,
    AsyncRelayCommand<HistorySquashRequest> Squash,
    Func<SquashBase?> CurrentBase,
    Func<CommitRowViewModel, SquashRangeMeasure?> Measure);

/// <summary>
/// The stash's commands, as the history's lines offer them.
/// </summary>
/// <param name="StashAll">Puts every uncommitted change on the stash; offered on the uncommitted line.</param>
/// <param name="Apply">Brings a stash line's changes back and keeps it.</param>
/// <param name="Pop">Brings a stash line's changes back and removes it, unless they conflict.</param>
/// <param name="Drop">Deletes a stash line's entry, after asking.</param>
public sealed record HistoryStashCommands(
    AsyncRelayCommand<CommitRowViewModel> StashAll,
    AsyncRelayCommand<CommitRowViewModel> Apply,
    AsyncRelayCommand<CommitRowViewModel> Pop,
    AsyncRelayCommand<CommitRowViewModel> Drop);

/// <summary>
/// What a reset item of a history line's menu asks for: the line, and the branch the item was named
/// after.
/// </summary>
/// <param name="Row">The line whose commit the branch moves to.</param>
/// <param name="Branch">
/// The branch HEAD was on when the menu opened. Carried rather than read again when the item is
/// clicked: HEAD can move in between, and the branch that moves must be the one the reader saw named.
/// </param>
public sealed record HistoryResetRequest(CommitRowViewModel Row, string Branch);

/// <summary>
/// One badge on a history row.
/// </summary>
/// <param name="Kind">What the reference is, which decides the badge's colour and icon.</param>
/// <param name="Name">The reference's short name.</param>
/// <param name="IsCurrent">Whether this is the branch HEAD points at.</param>
/// <param name="Upstream">
/// For a local branch drawn together with its upstream — both on this commit — the upstream's short
/// name (<c>origin/main</c>); <see langword="null"/> for every other badge.
/// </param>
/// <param name="Worktree">
/// For a local branch checked out in another working tree, where it is
/// (<see cref="WorktreeText.CheckedOutTip"/>); <see langword="null"/> for every other badge.
/// </param>
/// <remarks>
/// Flattened out of <see cref="GitRef"/> on purpose: only <see cref="GitBranch"/> knows whether it
/// is checked out, and a template bound to the base type cannot see that — which is exactly how the
/// checked-out branch ends up drawn like any other.
/// </remarks>
public sealed record RefBadgeItem(GitRefKind Kind, string Name, bool IsCurrent, string? Upstream = null, string? Worktree = null)
{
    /// <summary>Gets a value indicating whether the badge stands for a local branch and its upstream.</summary>
    public bool HasUpstream => Upstream is { Length: > 0 };

    /// <summary>
    /// Gets a value indicating whether the badge stands for a local branch checked out in another
    /// working tree, which its pill marks with a tree.
    /// </summary>
    public bool IsWorktree => Worktree is { Length: > 0 };

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
/// pure waste. The age is the one exception, measured again by <see cref="RefreshRelativeDate"/> when
/// the history refreshes without redrawing — "just now" is only true for a minute.
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
    /// <param name="stash">The stash entry the commit records, when it is one.</param>
    /// <param name="worktreeOf">
    /// Says where a local branch is checked out in another working tree, by its short name, or
    /// <see langword="null"/> when no other working tree has it; without it no badge carries a tree.
    /// </param>
    public CommitRowViewModel(
        GitCommit commit,
        GraphRow row,
        IReadOnlyList<GitRef>? refs,
        bool isHead,
        DateTimeOffset now,
        HistoryRowCommands? commands = null,
        bool absoluteDates = false,
        StashEntry? stash = null,
        Func<string, string?>? worktreeOf = null)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(row);

        Commands = commands;
        Commit = commit;
        Row = row;
        Stash = stash;
        Refs = Project(refs, stash, worktreeOf);
        (Branches, Badges) = BuildBranches(Refs, commands);
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
    /// Gets the stash entry this line is, or <see langword="null"/> for any other commit.
    /// </summary>
    /// <remarks>
    /// A stash is one line, as GitKraken draws it: the commit git records the entry as, carrying a
    /// badge with the stash's icon and the entry's name.
    /// </remarks>
    public StashEntry? Stash { get; }

    /// <summary>Gets a value indicating whether this line is a stash entry.</summary>
    public bool IsStash => Stash is not null;

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
    /// Gets the branch the two reset items would move, or <see langword="null"/> when HEAD is not on
    /// a branch and they are not offered.
    /// </summary>
    public string? ResetBranch => Commands?.CurrentBranch?.Invoke() is { Length: > 0 } branch ? branch : null;

    /// <summary>
    /// What a reset item says.
    /// </summary>
    /// <param name="branch">The branch it moves.</param>
    /// <param name="mode">Whether it keeps the changes or discards them.</param>
    /// <returns>The item's header.</returns>
    public static string ResetHeader(string branch, ResetMode mode)
        => mode == ResetMode.Soft
            ? $"Reset \"{branch}\" to this commit - Soft (keep all changes)"
            : $"Reset \"{branch}\" to this commit - Hard (discard all changes)";

    /// <summary>
    /// What the revert item says. It names no branch, unlike the reset items: it moves none, and the
    /// question it asks names the branch its commit lands on.
    /// </summary>
    public const string RevertHeader = "Revert this commit…";

    /// <summary>What the item that makes a line the squash base says.</summary>
    public const string SetSquashBaseHeader = "Set as squash base";

    /// <summary>What the item that forgets the squash base says.</summary>
    public const string ClearSquashBaseHeader = "Clear the squash base";

    /// <summary>
    /// What the squash item says once the range is measured.
    /// </summary>
    /// <param name="commits">How many commits the squash replaces.</param>
    /// <param name="baseShortSha">The base's short sha.</param>
    /// <returns>The item's header.</returns>
    public static string SquashHeader(int commits, string baseShortSha)
        => commits == 1
            ? $"Squash 1 commit since {baseShortSha} into a new branch…"
            : $"Squash {commits.ToString(CultureInfo.CurrentCulture)} commits since {baseShortSha} into a new branch…";

    /// <summary>
    /// What the squash item says, greyed out, on a line the base is not below on its first-parent line.
    /// </summary>
    /// <param name="baseShortSha">The base's short sha.</param>
    /// <returns>The item's header.</returns>
    public static string UnmeasuredSquashHeader(string baseShortSha)
        => $"Squash since {baseShortSha} into a new branch…";

    /// <summary>
    /// Gets a value indicating whether this line is the squash base, which a flag before its subject
    /// shows.
    /// </summary>
    public bool IsSquashBase
        => Commit is not null
            && Commands?.Squash?.CurrentBase() is { } squashBase
            && string.Equals(squashBase.Sha, Commit.Sha, StringComparison.Ordinal);

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
    /// Gets the line's menu, built as it is asked for: the commit's own actions, resetting the
    /// branch HEAD is on to the commit and reverting it, the squash's two steps, then — for every
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

            entries.Add(new HistoryMenuEntry("Show what it changed", commands.ShowChanges, this, PhosphorIcon.GitDiff));

            // Every line that is a commit — a stash is one too, with a message and an author — and
            // not the uncommitted line, which has neither.
            if (Commit is not null && commands.ShowDetails is { } details)
            {
                entries.Add(new HistoryMenuEntry("Show commit details", details, this, PhosphorIcon.Article));
            }

            // A stash is not a commit anyone builds on: GitKraken offers it its own three actions and
            // nothing else, and branching from or resetting to git's record of it is a trap.
            if (IsStash)
            {
                if (commands.Stashes is { } stash)
                {
                    entries.Add(HistoryMenuEntry.Separator);
                    entries.Add(new HistoryMenuEntry("Apply stash", stash.Apply, this, PhosphorIcon.TrayArrowUp));
                    entries.Add(new HistoryMenuEntry("Pop stash", stash.Pop, this, PhosphorIcon.ArrowCounterClockwise));
                    entries.Add(HistoryMenuEntry.Separator);
                    entries.Add(new HistoryMenuEntry("Delete stash…", stash.Drop, this, PhosphorIcon.Trash));
                }

                AddCopyEntries(entries, commands);

                return entries;
            }

            if (IsUncommitted && commands.Stashes is { } stashes)
            {
                entries.Add(new HistoryMenuEntry("Stash all changes…", stashes.StashAll, this, PhosphorIcon.Archive));
            }

            // Beside the stash, which is the way to put the same work aside and keep it.
            if (IsUncommitted && commands.DiscardUncommitted is { } discard)
            {
                entries.Add(new HistoryMenuEntry("Discard uncommitted files…", discard, this, PhosphorIcon.Trash));
            }

            entries.Add(HistoryMenuEntry.Separator);
            entries.Add(new HistoryMenuEntry("Create branch here…", commands.CreateBranchHere, this, PhosphorIcon.GitBranch));

            if (commands.CreateWorktreeHere is { } worktree)
            {
                entries.Add(new HistoryMenuEntry("Create worktree here…", worktree, this, PhosphorIcon.FolderPlus));
            }

            entries.Add(new HistoryMenuEntry("Create tag here…", commands.CreateTagHere, this, PhosphorIcon.Tag));
            entries.Add(HistoryMenuEntry.Separator);
            entries.Add(new HistoryMenuEntry("Check out this commit (detaches HEAD)", commands.CheckoutCommit, this, PhosphorIcon.SignIn));

            // Named after the branch they move, so on a detached HEAD there is nothing to name and
            // nothing to offer. The revert follows them: its commit lands on that same branch, and a
            // commit on a detached HEAD is lost at the next checkout.
            if (ResetBranch is { } current)
            {
                bool resets = commands.ResetSoft is not null && commands.ResetHard is not null;

                if (resets || commands.Revert is not null)
                {
                    entries.Add(HistoryMenuEntry.Separator);
                }

                if (resets)
                {
                    HistoryResetRequest request = new(this, current);

                    entries.Add(new HistoryMenuEntry(ResetHeader(current, ResetMode.Soft), commands.ResetSoft!, request, PhosphorIcon.ArrowUUpLeft));
                    entries.Add(new HistoryMenuEntry(ResetHeader(current, ResetMode.Hard), commands.ResetHard!, request, PhosphorIcon.ArrowUUpLeft));
                }

                if (commands.Revert is { } revert)
                {
                    entries.Add(new HistoryMenuEntry(RevertHeader, revert, this, PhosphorIcon.ArrowArcLeft));
                }
            }

            if (commands.Squash is { } squash)
            {
                AddSquashEntries(entries, squash);
            }

            if (Branches.Count > 0 && commands.Branches is { } branchCommands)
            {
                entries.Add(HistoryMenuEntry.Separator);

                foreach (HistoryBranchViewModel branch in Branches)
                {
                    if (branch.CanSetAsMergeSource)
                    {
                        entries.Add(new HistoryMenuEntry(branch.SetAsMergeSourceHeader, branchCommands.SetAsMergeSource, branch, PhosphorIcon.Target));
                    }
                }

                foreach (HistoryBranchViewModel branch in Branches)
                {
                    if (branch.CanMergeInto)
                    {
                        entries.Add(new HistoryMenuEntry(branch.MergeIntoHeader, branchCommands.MergeInto, branch, PhosphorIcon.GitMerge));
                    }
                }
            }

            if (CanOpenOnHost && commands.OpenOnHost is { } openOnHost)
            {
                entries.Add(HistoryMenuEntry.Separator);
                entries.Add(new HistoryMenuEntry(HostLabel, openOnHost, this, PhosphorIcon.ArrowSquareOut));
            }

            AddCopyEntries(entries, commands);

            return entries;
        }
    }

    /// <summary>
    /// The squash's two steps. With no base, every line offers to become it; the base's own line
    /// offers to forget it; any other line offers to replace it, to squash everything since it up to
    /// this line — counted from the loaded lines, and greyed out when the base is not on this line's
    /// first-parent line — and to forget it. It moves no branch, so unlike the reset group it does not
    /// depend on where HEAD is.
    /// </summary>
    private void AddSquashEntries(List<HistoryMenuEntry> entries, HistorySquashCommands squash)
    {
        entries.Add(HistoryMenuEntry.Separator);

        if (squash.CurrentBase() is not { } squashBase)
        {
            entries.Add(new HistoryMenuEntry(SetSquashBaseHeader, squash.SetBase, this, PhosphorIcon.FlagBanner));
            return;
        }

        if (IsSquashBase)
        {
            entries.Add(new HistoryMenuEntry(ClearSquashBaseHeader, squash.ClearBase, null, PhosphorIcon.X));
            return;
        }

        SquashRangeMeasure? measure = Commit is null ? null : squash.Measure(this);
        string header = measure is null
            ? UnmeasuredSquashHeader(squashBase.ShortSha)
            : SquashHeader(measure.CommitCount, squashBase.ShortSha);

        entries.Add(new HistoryMenuEntry(SetSquashBaseHeader, squash.SetBase, this, PhosphorIcon.FlagBanner));
        entries.Add(new HistoryMenuEntry(header, squash.Squash, new HistorySquashRequest(this, squashBase), PhosphorIcon.ArrowsInLineVertical));
        entries.Add(new HistoryMenuEntry(ClearSquashBaseHeader, squash.ClearBase, null, PhosphorIcon.X));
    }

    /// <summary>
    /// The line's hash, short or whole, to the clipboard — for a commit, not for the uncommitted line,
    /// which has none.
    /// </summary>
    private void AddCopyEntries(List<HistoryMenuEntry> entries, HistoryRowCommands commands)
    {
        if (Commit is null || commands.Copy is not { } copy)
        {
            return;
        }

        entries.Add(HistoryMenuEntry.Separator);
        entries.Add(new HistoryMenuEntry("Copy short commit hash", copy, ShortSha, PhosphorIcon.Copy));
        entries.Add(new HistoryMenuEntry("Copy full commit hash", copy, Sha, PhosphorIcon.Copy));
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
    /// Re-announces everything the page's squash base decides: the line's flag and its menu.
    /// </summary>
    public void NotifySquashBaseChanged()
    {
        OnPropertyChanged(nameof(IsSquashBase));
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
    public string RelativeDate
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                // One of the two shows the age, whichever way the dates are displayed.
                OnPropertyChanged(nameof(DateText));
                OnPropertyChanged(nameof(DateTooltip));
            }
        }
    } = string.Empty;

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
    /// Measures the commit's age again, from a later moment than the row was built at.
    /// </summary>
    /// <param name="now">The moment to measure from.</param>
    /// <remarks>
    /// Notifies only when the phrase changed, which for a commit older than a day is almost never. The
    /// uncommitted-changes row has no age and is left as it is.
    /// </remarks>
    public void RefreshRelativeDate(DateTimeOffset now)
    {
        if (Commit is { } commit)
        {
            RelativeDate = RelativeTime.Format(commit.Author.When, now);
        }
    }

    /// <summary>
    /// Gets the commit's full SHA, empty for the uncommitted-changes row.
    /// </summary>
    public string Sha => Commit?.Sha ?? string.Empty;

    /// <summary>
    /// Whether this row's commit is what is being searched for: by its message, its SHA or its author.
    /// </summary>
    /// <param name="search">What to look for, already trimmed.</param>
    /// <returns>
    /// <see langword="true"/> when the subject or the body contains it, the SHA starts with it, or the
    /// author's name or email contains it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The message — subject and body — case-insensitively: that is what git's own <c>--grep</c>
    /// matched when the search box filtered the query, so the same words still find the same commits.
    /// The author's name and email the same way.
    /// </para>
    /// <para>
    /// A SHA only by its start, which is how git abbreviates one and how a reader pastes one. Anywhere
    /// in it, a hex word such as <c>add</c> or <c>face</c> would mark one commit in a hundred for no
    /// reason.
    /// </para>
    /// <para>
    /// The uncommitted-changes row has no commit, and matches nothing.
    /// </para>
    /// </remarks>
    public bool Matches(string search)
    {
        ArgumentNullException.ThrowIfNull(search);

        if (IsUncommitted || Commit is not { } commit || search.Length == 0)
        {
            return false;
        }

        return Subject.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || commit.Body.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || commit.Sha.StartsWith(search, StringComparison.OrdinalIgnoreCase)
            || commit.Author.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || commit.Author.Email.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Creates the pseudo-row shown above the history when the working directory is dirty.
    /// </summary>
    /// <param name="row">
    /// Where it is drawn: the layout's row for <see cref="GraphCommitInput.WorkingTree"/>, in the lane
    /// its dashed line to HEAD's commit runs down.
    /// </param>
    /// <param name="commands">
    /// The commands the row's own menu runs. The pseudo-row has no commit, so most of them refuse —
    /// but activating it is what takes the reader to the page that can act on the work.
    /// </param>
    /// <returns>The row.</returns>
    public static CommitRowViewModel Uncommitted(GraphRow row, HistoryRowCommands? commands = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new(row, commands);
    }

    private static (IReadOnlyList<HistoryBranchViewModel> Branches, IReadOnlyList<object> Badges) BuildBranches(
        IReadOnlyList<RefBadgeItem> refs,
        HistoryRowCommands? rowCommands)
    {
        if (refs.Count == 0)
        {
            return (NoBranches, []);
        }

        HistoryBranchCommands? commands = rowCommands?.Branches;
        AsyncRelayCommand<string>? copy = rowCommands?.Copy;

        List<HistoryBranchViewModel> branches = [];
        List<object> badges = new(refs.Count);

        foreach (RefBadgeItem badge in refs)
        {
            if (commands is not null && badge.Kind is GitRefKind.LocalBranch or GitRefKind.RemoteBranch)
            {
                // A local branch drawn with its upstream: one badge, whose menu reaches the remote too.
                // The line's own menu still names both, so both are among its branches.
                HistoryBranchViewModel? remote = badge.Upstream is { Length: > 0 } upstream
                    ? new HistoryBranchViewModel(new RefBadgeItem(GitRefKind.RemoteBranch, upstream, false), commands)
                    : null;

                HistoryBranchViewModel branch = new(badge, commands, remote);
                branches.Add(branch);
                badges.Add(branch);

                if (remote is not null)
                {
                    branches.Add(remote);
                }
            }
            else if (copy is not null && badge.Kind == GitRefKind.Tag)
            {
                badges.Add(new HistoryTagViewModel(badge, copy, rowCommands?.PushTag, rowCommands?.DeleteTag, rowCommands?.DeleteRemoteTag));
            }
            else
            {
                badges.Add(badge);
            }
        }

        return (branches.Count == 0 ? NoBranches : branches, badges);
    }

    private static IReadOnlyList<RefBadgeItem> Project(
        IReadOnlyList<GitRef>? refs,
        StashEntry? stash,
        Func<string, string?>? worktreeOf)
    {
        if ((refs is null || refs.Count == 0) && stash is null)
        {
            return NoRefs;
        }

        refs ??= [];

        List<RefBadgeItem> items = new(refs.Count + 1);

        if (stash is not null)
        {
            items.Add(new RefBadgeItem(GitRefKind.Stash, stash.Reference, false));
        }

        Dictionary<string, string> joined = JoinUpstreams(refs);

        foreach (GitRef reference in refs)
        {
            if (reference is GitBranch { IsRemote: true } remote && joined.ContainsValue(remote.ShortName))
            {
                // Drawn by the local branch that tracks it.
                continue;
            }

            if (reference is not GitBranch { IsRemote: false } local)
            {
                items.Add(RefBadgeItem.From(reference));
                continue;
            }

            // The branch this window has checked out keeps its check, never the tree.
            string? worktree = local.IsCurrent ? null : worktreeOf?.Invoke(local.ShortName);

            items.Add(new RefBadgeItem(
                GitRefKind.LocalBranch,
                local.ShortName,
                local.IsCurrent,
                joined.GetValueOrDefault(local.ShortName),
                worktree));
        }

        return items;
    }

    /// <summary>
    /// Pairs every local branch on the line with its upstream when the upstream is on the line too:
    /// the two are on the same commit, and GitKraken draws them as one badge.
    /// </summary>
    /// <param name="refs">The line's references, which all point at its commit.</param>
    /// <returns>Each paired local branch's short name, with its upstream's.</returns>
    /// <remarks>
    /// The configured upstream, not a name that merely matches: that is the pairing git itself records,
    /// so an <c>upstream/main</c> beside <c>origin/main</c> keeps its own badge. A remote branch two
    /// local branches track is drawn with the first of them only, so it never appears twice.
    /// </remarks>
    private static Dictionary<string, string> JoinUpstreams(IReadOnlyList<GitRef> refs)
    {
        Dictionary<string, string> joined = new(StringComparer.Ordinal);
        HashSet<string> remotes = new(StringComparer.Ordinal);

        foreach (GitRef reference in refs)
        {
            if (reference is GitBranch { IsRemote: true } remote)
            {
                remotes.Add(remote.ShortName);
            }
        }

        if (remotes.Count == 0)
        {
            return joined;
        }

        foreach (GitRef reference in refs)
        {
            if (reference is GitBranch { IsRemote: false, UpstreamShortName: { Length: > 0 } upstream } local
                && remotes.Remove(upstream))
            {
                joined[local.ShortName] = upstream;
            }
        }

        return joined;
    }

    /// <inheritdoc />
    public override string ToString() => IsUncommitted ? "(uncommitted)" : $"{ShortSha} {Subject}";
}
