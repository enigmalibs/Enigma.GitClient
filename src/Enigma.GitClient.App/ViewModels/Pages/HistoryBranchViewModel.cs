using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.Core.Refs;

namespace Enigma.GitClient.App.ViewModels.Pages;

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
    Func<MergeSource?> CurrentSource,
    Func<string?> CurrentBranch);

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
/// </remarks>
public sealed class HistoryBranchViewModel : ViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="badge">The badge the branch is drawn as.</param>
    /// <param name="commands">The page's branch commands.</param>
    public HistoryBranchViewModel(RefBadgeItem badge, HistoryBranchCommands commands)
    {
        ArgumentNullException.ThrowIfNull(badge);
        ArgumentNullException.ThrowIfNull(commands);

        Badge = badge;
        Commands = commands;
    }

    /// <summary>Gets the badge the branch is drawn as.</summary>
    public RefBadgeItem Badge { get; }

    /// <summary>Gets the page's branch commands.</summary>
    public HistoryBranchCommands Commands { get; }

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

    /// <summary>Gets what the delete item says.</summary>
    public string DeleteHeader => $"Delete \"{Name}\"…";

    /// <summary>Gets a value indicating whether the branch can be deleted: not while it is checked out.</summary>
    public bool CanDelete => !IsCurrent;

    /// <summary>
    /// Re-announces everything the merge source decides.
    /// </summary>
    public void NotifyMergeSourceChanged()
    {
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(IsMergeSource));
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
/// One item of a history line's menu, as data: the line's menu is built when it opens, because what
/// it offers depends on the branches on the line and on the page's merge source.
/// </summary>
/// <param name="Header">What the item says; <c>-</c> draws a separator.</param>
/// <param name="Command">What it runs.</param>
/// <param name="Parameter">What it runs it with.</param>
public sealed record HistoryMenuEntry(string Header, ICommand? Command = null, object? Parameter = null)
{
    /// <summary>A separator.</summary>
    public static readonly HistoryMenuEntry Separator = new("-");

    /// <summary>Gets a value indicating whether this is a separator rather than an item.</summary>
    public bool IsSeparator => Header == "-";
}
