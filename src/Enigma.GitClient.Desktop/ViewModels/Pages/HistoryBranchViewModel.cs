using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Desktop.Services;
using Enigma.Icons.Phosphor;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// The branch the history's next merge takes its work from.
/// </summary>
/// <param name="Name">The branch's short name — <c>feature</c>, <c>origin/feature</c>.</param>
/// <param name="IsRemote">Whether it lives on a remote.</param>
public sealed record MergeSource(string Name, bool IsRemote);

/// <summary>
/// The commands a branch on a history line offers, shared by every branch the page draws.
/// </summary>
/// <param name="Checkout">Checks the branch out — creating the tracking branch for a remote one.</param>
/// <param name="SetAsMergeSource">Makes the branch the one the next merge takes its work from.</param>
/// <param name="ClearMergeSource">Forgets the merge source.</param>
/// <param name="MergeInto">Merges the merge source into the branch.</param>
/// <param name="FastForwardInto">The same, refusing anything but a fast-forward.</param>
/// <param name="MergeIntoCurrent">Merges the branch into the one checked out.</param>
/// <param name="Delete">Deletes the branch, after asking.</param>
/// <param name="Pull">Brings a local branch up to date with its upstream, current or not.</param>
/// <param name="Push">Pushes a local branch, current or not.</param>
/// <param name="CurrentSource">Reads the merge source at the moment it is asked for.</param>
/// <param name="CurrentBranch">Reads the name of the branch HEAD is on, or <see langword="null"/> when detached.</param>
public sealed record HistoryBranchCommands(
    AsyncRelayCommand<HistoryBranchViewModel> Checkout,
    RelayCommand<HistoryBranchViewModel> SetAsMergeSource,
    RelayCommand ClearMergeSource,
    AsyncRelayCommand<HistoryBranchViewModel> MergeInto,
    AsyncRelayCommand<HistoryBranchViewModel> FastForwardInto,
    AsyncRelayCommand<HistoryBranchViewModel> MergeIntoCurrent,
    AsyncRelayCommand<HistoryBranchViewModel> Delete,
    AsyncRelayCommand<HistoryBranchViewModel> Pull,
    AsyncRelayCommand<HistoryBranchViewModel> Push,
    Func<MergeSource?> CurrentSource,
    Func<string?> CurrentBranch,
    AsyncRelayCommand<string>? Copy = null);

/// <summary>
/// One branch on a history line: the badge it is drawn as, and the menu that badge opens.
/// </summary>
/// <remarks>
/// <para>
/// A line can carry several branches, and each one needs its own menu — "set it as the merge source",
/// "merge the source into it" — which is why the branch, not the line, is what the menu is about.
/// </para>
/// <para>
/// What the menu says depends on the page's merge source, which changes while the line is on screen;
/// the page tells every line when it does (<see cref="NotifyMergeSourceChanged"/>), and the line tells
/// its branches.
/// </para>
/// <para>
/// A local branch and its upstream on the same commit are one badge, as GitKraken draws them: the
/// local one, carrying the remote as <see cref="Remote"/>. Its menu is the local branch's — checking
/// out, merging, pulling and pushing act on the local one, and on the same commit the remote would do
/// the same — plus deleting the remote, the one thing that differs.
/// </para>
/// </remarks>
public sealed class HistoryBranchViewModel : ViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="badge">The badge the branch is drawn as.</param>
    /// <param name="commands">The page's branch commands.</param>
    /// <param name="remote">
    /// The upstream drawn in the same badge, on the same commit; <see langword="null"/> for a badge of
    /// its own.
    /// </param>
    public HistoryBranchViewModel(RefBadgeItem badge, HistoryBranchCommands commands, HistoryBranchViewModel? remote = null)
    {
        ArgumentNullException.ThrowIfNull(badge);
        ArgumentNullException.ThrowIfNull(commands);

        Badge = badge;
        Commands = commands;
        Remote = remote;
    }

    /// <summary>Gets the badge the branch is drawn as.</summary>
    public RefBadgeItem Badge { get; }

    /// <summary>Gets the page's branch commands.</summary>
    public HistoryBranchCommands Commands { get; }

    /// <summary>
    /// Gets the upstream this badge draws too, on the same commit, or <see langword="null"/>.
    /// </summary>
    public HistoryBranchViewModel? Remote { get; }

    /// <summary>Gets a value indicating whether the badge stands for the branch and its upstream.</summary>
    public bool HasRemote => Remote is not null;

    /// <summary>Gets what the item that deletes the upstream drawn with the branch says.</summary>
    public string DeleteRemoteHeader => $"Delete \"{Remote?.Name}\"…";

    /// <summary>Gets the branch's short name.</summary>
    public string Name => Badge.Name;

    /// <summary>Gets a value indicating whether the branch lives on a remote.</summary>
    public bool IsRemote => Badge.Kind == GitRefKind.RemoteBranch;

    /// <summary>Gets a value indicating whether the branch is a local one.</summary>
    public bool IsLocal => !IsRemote;

    /// <summary>Gets a value indicating whether HEAD is on this branch.</summary>
    public bool IsCurrent => Badge.IsCurrent;

    /// <summary>Gets the merge source as the page holds it now.</summary>
    public MergeSource? Source => Commands.CurrentSource();

    /// <summary>Gets a value indicating whether this branch is the merge source.</summary>
    public bool IsMergeSource => Source is { } source && string.Equals(source.Name, Name, StringComparison.Ordinal);

    /// <summary>
    /// Gets a value indicating whether the badge is ringed as the merge source: this branch is, or the
    /// upstream drawn with it is — the line's own menu can still pick either.
    /// </summary>
    public bool IsDrawnAsMergeSource => IsMergeSource || Remote?.IsMergeSource == true;

    /// <summary>Gets a value indicating whether this branch can be made the merge source.</summary>
    public bool CanSetAsMergeSource => !IsMergeSource;

    /// <summary>Gets what the menu item that makes this branch the merge source says.</summary>
    public string SetAsMergeSourceHeader => $"Set \"{Name}\" as merge source";

    /// <summary>
    /// Gets what merging the source into this branch asks for, or <see langword="null"/> when there is
    /// no source.
    /// </summary>
    public BranchDropRequest? MergeRequest
        => Source is { } source ? new BranchDropRequest(source.Name, source.IsRemote, Name, IsRemote, IsCurrent) : null;

    /// <summary>
    /// Gets a value indicating whether the source can be merged into this branch — there is one, it is
    /// not this branch, and this branch is local.
    /// </summary>
    public bool CanMergeInto => MergeRequest is { } request && BranchDropOperations.CanDrop(request);

    /// <summary>Gets what the menu item that merges the source into this branch says.</summary>
    public string MergeIntoHeader => $"Merge \"{Source?.Name}\" into \"{Name}\"";

    /// <summary>Gets what the fast-forward-only item says.</summary>
    public string FastForwardIntoHeader => $"Merge \"{Source?.Name}\" into \"{Name}\", fast-forward only";

    /// <summary>
    /// Gets a value indicating whether this branch can be merged into the one checked out — there is
    /// one, and it is not this branch.
    /// </summary>
    public bool CanMergeIntoCurrent
        => Commands.CurrentBranch() is { Length: > 0 } current
            && !IsCurrent
            && !string.Equals(current, Name, StringComparison.Ordinal);

    /// <summary>Gets what the item that merges this branch into the current one says.</summary>
    public string MergeIntoCurrentHeader => $"Merge \"{Name}\" into \"{Commands.CurrentBranch()}\"";

    /// <summary>Gets a value indicating whether the branch can be checked out.</summary>
    public bool CanCheckout => !IsCurrent;

    /// <summary>Gets what the check-out item says.</summary>
    public string CheckoutHeader => $"Check out \"{Name}\"";

    /// <summary>Gets what the pull item says.</summary>
    public string PullHeader => $"Pull \"{Name}\"";

    /// <summary>Gets what the push item says.</summary>
    public string PushHeader => $"Push \"{Name}\"";

    /// <summary>
    /// Gets a value indicating whether the branch can be pulled or pushed: only a local one — there is
    /// nothing local to pull into, or push from, on a remote-tracking branch.
    /// </summary>
    public bool CanSynchronise => IsLocal;

    /// <summary>Gets what the delete item says.</summary>
    public string DeleteHeader => $"Delete \"{Name}\"…";

    /// <summary>Gets a value indicating whether the badge's menu can copy the branch's name.</summary>
    public bool CanCopyName => Commands.Copy is not null;

    /// <summary>Gets a value indicating whether the branch can be deleted: not while it is checked out.</summary>
    public bool CanDelete => !IsCurrent;

    /// <summary>
    /// Re-announces everything the merge source decides.
    /// </summary>
    public void NotifyMergeSourceChanged()
    {
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(IsMergeSource));
        OnPropertyChanged(nameof(IsDrawnAsMergeSource));
        OnPropertyChanged(nameof(CanSetAsMergeSource));
        OnPropertyChanged(nameof(MergeRequest));
        OnPropertyChanged(nameof(CanMergeInto));
        OnPropertyChanged(nameof(MergeIntoHeader));
        OnPropertyChanged(nameof(FastForwardIntoHeader));
    }

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// A tag's badge on a history line: the one reference on a line that brings a menu of its own besides
/// the branches — pushing the tag to the remote, deleting it here or there, and what it is called, to
/// copy.
/// </summary>
/// <param name="Badge">The badge as drawn.</param>
/// <param name="Copy">Copies a text to the clipboard.</param>
/// <param name="Push">Pushes a tag, by its name, to the remote; the push is not offered without it.</param>
/// <param name="Delete">Deletes a tag here, by its name, after asking; not offered without it.</param>
/// <param name="DeleteRemote">
/// Deletes a tag, by its name, from the remote its push goes to, after asking; not offered without it.
/// </param>
public sealed record HistoryTagViewModel(
    RefBadgeItem Badge,
    AsyncRelayCommand<string> Copy,
    AsyncRelayCommand<string>? Push = null,
    AsyncRelayCommand<string>? Delete = null,
    AsyncRelayCommand<string>? DeleteRemote = null)
{
    /// <summary>Gets the tag's name, as the badge shows it.</summary>
    public string Name => Badge.Name;

    /// <summary>Gets a value indicating whether the badge's menu can push the tag.</summary>
    public bool CanPush => Push is not null;

    /// <summary>Gets what the push item says: the branch badge's words, for a tag.</summary>
    public string PushHeader => $"Push \"{Name}\"";

    /// <summary>Gets a value indicating whether the badge's menu can delete the tag here.</summary>
    public bool CanDelete => Delete is not null;

    /// <summary>Gets what the local delete item says.</summary>
    public string DeleteHeader => $"Delete \"{Name}\" locally…";

    /// <summary>Gets a value indicating whether the badge's menu can delete the tag from the remote.</summary>
    public bool CanDeleteRemote => DeleteRemote is not null;

    /// <summary>
    /// Gets what the remote delete item says. The remote is named by the question it asks rather than
    /// here: which one it is follows the current branch, which can change while the line is on screen.
    /// </summary>
    public string DeleteRemoteHeader => $"Delete \"{Name}\" from the remote…";

    /// <summary>Gets a value indicating whether the menu has a delete item.</summary>
    public bool CanDeleteAny => CanDelete || CanDeleteRemote;

    /// <summary>Gets a value indicating whether the push and the deletes are both offered, with a separator between.</summary>
    public bool SeparatesPushFromDeletes => CanPush && CanDeleteAny;

    /// <summary>Gets a value indicating whether any action comes before the copy item, with a separator between.</summary>
    public bool HasActions => CanPush || CanDeleteAny;

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// One branch badge of the history dropped onto another: the dragged branch is merged into the one it
/// was dropped on.
/// </summary>
/// <param name="Source">The branch that was dragged, whose commits are brought over.</param>
/// <param name="Target">The branch it was dropped on, which is written to.</param>
public sealed record HistoryBranchDrop(HistoryBranchViewModel Source, HistoryBranchViewModel Target)
{
    /// <summary>Gets the merge the drop asks for.</summary>
    public BranchDropRequest Request => new(Source.Name, Source.IsRemote, Target.Name, Target.IsRemote, Target.IsCurrent);

    /// <summary>
    /// Gets a value indicating whether the drop can be merged: onto a local branch, and not onto the
    /// branch itself.
    /// </summary>
    public bool CanDrop => BranchDropOperations.CanDrop(Request);

    /// <summary>Gets the merge's menu item, both branches named in full.</summary>
    public string MergeHeader => $"Merge \"{Source.Name}\" into \"{Target.Name}\"";

    /// <summary>Gets the fast-forward's menu item, both branches named in full.</summary>
    public string FastForwardHeader => $"Merge \"{Source.Name}\" into \"{Target.Name}\", fast-forward only";
}

/// <summary>
/// One item of a history line's menu, as data: the line's menu is built when it opens, because what
/// it offers depends on the branches on the line and on the page's merge source.
/// </summary>
/// <param name="Header">What the item says; <c>-</c> draws a separator.</param>
/// <param name="Command">What it runs.</param>
/// <param name="Parameter">What it runs it with.</param>
/// <param name="Icon">The glyph drawn beside it, one per kind of action.</param>
public sealed record HistoryMenuEntry(string Header, ICommand? Command = null, object? Parameter = null, PhosphorIcon? Icon = null)
{
    /// <summary>Gets a value indicating whether the item draws a glyph.</summary>
    public bool HasIcon => Icon is not null;

    /// <summary>Gets the glyph to draw, for a binding that cannot take an absent one.</summary>
    public PhosphorIcon IconKind => Icon ?? PhosphorIcon.Circle;

    /// <summary>A separator.</summary>
    public static readonly HistoryMenuEntry Separator = new("-");

    /// <summary>Gets a value indicating whether this is a separator rather than an item.</summary>
    public bool IsSeparator => Header == "-";
}
