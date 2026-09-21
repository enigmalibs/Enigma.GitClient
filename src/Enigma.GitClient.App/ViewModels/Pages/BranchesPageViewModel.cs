using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.App.Formatting;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.Core.Merging;
using Enigma.GitClient.Core.Refs;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// Something the branches list holds: a branch, or the heading above a group of them.
/// </summary>
/// <remarks>
/// The list is flat so the page has one selection: nested lists would each keep their own, and two
/// rows would be highlighted at once. What the flattening costs is this — the list holds two kinds
/// of thing, and the view has to know which of them can be selected.
/// </remarks>
public interface IBranchListItem
{
    /// <summary>Gets a value indicating whether the item is one the reader can select.</summary>
    bool IsSelectable { get; }
}

/// <summary>
/// The heading above a group of branches, as a row of the flat list.
/// </summary>
/// <param name="Title">What the heading says: "Local", or a remote's name.</param>
/// <param name="IsRemote">Whether the group it introduces holds remote branches.</param>
/// <param name="Count">How many branches are under it, as the heading shows it.</param>
public sealed record BranchGroupHeaderViewModel(string Title, bool IsRemote, string Count) : IBranchListItem
{
    /// <inheritdoc />
    public bool IsSelectable => false;
}

/// <summary>
/// One branch, as the branches page shows it.
/// </summary>
/// <remarks>
/// The row carries the page's commands rather than raising events, so a context menu opening in its
/// own popup tree can still reach them with a plain binding against the row itself.
/// </remarks>
public sealed class BranchRowViewModel : ViewModelBase, IBranchListItem
{
    private readonly BranchesPageViewModel _owner;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="owner">The page the row belongs to.</param>
    /// <param name="branch">The branch it stands for.</param>
    /// <param name="publishedAs">
    /// The remote-tracking branch this one is on — <c>origin/main</c> — or <see langword="null"/> when
    /// the branch is on no remote at all.
    /// </param>
    /// <remarks>
    /// The page works out <paramref name="publishedAs"/>, because a row is in no position to: the
    /// question is about the other refs in the repository, and the answer is the same for every row, so
    /// it is resolved once per rebuild rather than scanned for per line.
    /// </remarks>
    public BranchRowViewModel(BranchesPageViewModel owner, GitBranch branch, string? publishedAs = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(branch);

        _owner = owner;
        Branch = branch;
        PublishedAs = publishedAs;
    }

    /// <summary>Gets the branch this row stands for.</summary>
    public GitBranch Branch { get; }

    /// <inheritdoc />
    public bool IsSelectable => true;

    /// <summary>Gets the name shown on the row.</summary>
    public string Name => Branch.IsRemote ? Branch.NameWithoutRemote : Branch.ShortName;

    /// <summary>Gets the branch's full short name, which commands are given.</summary>
    public string FullName => Branch.ShortName;

    /// <summary>Gets a value indicating whether this is the checked-out branch.</summary>
    public bool IsCurrent => Branch.IsCurrent;

    /// <summary>Gets a value indicating whether the branch lives on a remote.</summary>
    public bool IsRemote => Branch.IsRemote;

    /// <summary>Gets a value indicating whether this row stands for a local branch.</summary>
    public bool IsLocal => !Branch.IsRemote;

    /// <summary>Gets the upstream the branch tracks, empty when it tracks none.</summary>
    public string Upstream => Branch.UpstreamShortName ?? string.Empty;

    /// <summary>Gets a value indicating whether there is an upstream to show.</summary>
    public bool HasUpstream => Upstream.Length > 0;

    /// <summary>
    /// Gets the remote-tracking branch this local branch is on, or <see langword="null"/> when it is
    /// on none.
    /// </summary>
    public string? PublishedAs { get; }

    /// <summary>
    /// Gets a value indicating whether this local branch is on a remote.
    /// </summary>
    /// <remarks>
    /// Being on a remote is not the same as tracking one. A branch pushed with a plain
    /// <c>git push origin main</c> has no upstream and git reports no tracking for it, and it is
    /// plainly on origin; what answers the question is whether a remote-tracking ref for it exists,
    /// which is what <see cref="PublishedAs"/> carries.
    /// </remarks>
    public bool IsPublished => IsLocal && PublishedAs is not null && !IsUpstreamGone;

    /// <summary>
    /// Gets a value indicating whether this local branch is on no remote — the branch that would go
    /// with the machine.
    /// </summary>
    public bool IsLocalOnly => IsLocal && PublishedAs is null && !IsUpstreamGone;

    /// <summary>Gets a value indicating whether the upstream the branch names has gone.</summary>
    public bool IsUpstreamGone => IsLocal && Branch.Tracking.IsUpstreamGone;

    /// <summary>Gets how many commits the branch is ahead of its upstream.</summary>
    public string Ahead => Branch.Tracking.Ahead.ToString(CultureInfo.CurrentCulture);

    /// <summary>Gets how many commits the branch is behind its upstream.</summary>
    public string Behind => Branch.Tracking.Behind.ToString(CultureInfo.CurrentCulture);

    /// <summary>Gets a value indicating whether the branch has commits its upstream does not.</summary>
    /// <remarks>
    /// Local rows only: ahead of what would a remote-tracking branch be? Those rows carried two empty
    /// counters before this was stated.
    /// </remarks>
    public bool IsAhead => IsLocal && Branch.Tracking.Ahead > 0;

    /// <summary>Gets a value indicating whether the upstream has commits the branch does not.</summary>
    public bool IsBehind => IsLocal && Branch.Tracking.Behind > 0;

    /// <summary>Gets what the ahead counter says when the pointer rests on it.</summary>
    public string AheadTip => Commits(Branch.Tracking.Ahead, "to push");

    /// <summary>Gets what the behind counter says when the pointer rests on it.</summary>
    public string BehindTip => Commits(Branch.Tracking.Behind, "to pull");

    /// <summary>
    /// Gets what the remote badge says when the pointer rests on it.
    /// </summary>
    public string RemoteStateTip
        => IsUpstreamGone
            ? $"The upstream \"{Upstream}\" no longer exists"
            : IsPublished
                ? $"On the remote as \"{PublishedAs}\""
                : "On no remote — this branch only exists here";

    /// <summary>
    /// Says how many commits, in words, without "1 commits".
    /// </summary>
    private static string Commits(int count, string what)
        => count == 1
            ? $"1 commit {what}"
            : $"{count.ToString(CultureInfo.CurrentCulture)} commits {what}";

    /// <summary>Gets the subject of the branch's tip commit.</summary>
    public string TipSubject => Branch.TipSubject;

    /// <summary>Gets who wrote the tip commit.</summary>
    public string TipAuthor => Branch.TipAuthor.Name;

    /// <summary>Gets how long ago the tip commit was written.</summary>
    public string TipDate => RelativeTime.Format(Branch.TipDate);

    /// <summary>Gets the tip commit's short hash.</summary>
    public string ShortSha => Branch.TargetSha.Length >= 7 ? Branch.TargetSha[..7] : Branch.TargetSha;

    /// <summary>Gets the command that checks the branch out.</summary>
    public AsyncRelayCommand<BranchRowViewModel> CheckoutCommand => _owner.CheckoutCommand;

    /// <summary>Gets the command that renames the branch.</summary>
    public AsyncRelayCommand<BranchRowViewModel> RenameCommand => _owner.RenameCommand;

    /// <summary>Gets the command that deletes the branch.</summary>
    public AsyncRelayCommand<BranchRowViewModel> DeleteCommand => _owner.DeleteCommand;

    /// <summary>Gets the command that points the branch at an upstream.</summary>
    public AsyncRelayCommand<BranchRowViewModel> SetUpstreamCommand => _owner.SetUpstreamCommand;

    /// <summary>Gets the command that merges the branch into the one checked out.</summary>
    public AsyncRelayCommand<BranchRowViewModel> MergeCommand => _owner.MergeCommand;

    /// <inheritdoc />
    public override string ToString() => FullName;
}

/// <summary>
/// One branch row dropped onto another.
/// </summary>
/// <param name="Source">The row that was dragged — the branch whose work is brought over.</param>
/// <param name="Target">The row it was dropped on — the branch that is written to.</param>
/// <remarks>
/// The rows rather than their names, because what the drop means depends on what each one is: a
/// remote source is merged from, a remote target is not written to at all, and the target being the
/// checked-out branch is what decides whether anything has to be checked out first.
/// </remarks>
public sealed record BranchDrop(BranchRowViewModel Source, BranchRowViewModel Target)
{
    /// <summary>
    /// The in-process format a row is dragged under.
    /// </summary>
    /// <remarks>
    /// In-process: the payload is the live row, because the drag never leaves the window and a
    /// branch name on its own would not say whether it is remote or checked out. An in-process
    /// format never reaches the platform's clipboard, so nothing of it escapes the application.
    /// </remarks>
    public static readonly DataFormat<BranchRowViewModel> DragFormat =
        DataFormat.CreateInProcessFormat<BranchRowViewModel>("enigma-gitclient/branch-row");

    /// <summary>
    /// Builds what a dragged row carries.
    /// </summary>
    /// <param name="row">The branch being dragged.</param>
    /// <returns>The transfer to start the drag session with.</returns>
    /// <remarks>
    /// <para>
    /// Two items, and the second one is not decoration. Avalonia does not publish an in-process
    /// format to the platform — on X11, <c>DataFormatHelper.ToAtoms</c> skips every in-process
    /// format, so a drag carrying only <see cref="DragFormat"/> takes ownership of the drag
    /// selection while advertising <em>no type at all</em>. A desktop that bridges that drag onward
    /// then has nothing it can offer anyone, nothing can accept it, and the pointer draws the
    /// refusal for the whole gesture — while Avalonia goes on delivering the drag in process, which
    /// is why the drop worked perfectly well the entire time it looked impossible.
    /// </para>
    /// <para>
    /// The branch's full name as text is the honest thing to advertise: it is what this drag is
    /// about, it costs one string, and it makes the gesture mean something outside the window too —
    /// drop a branch on a terminal or an editor and its name is typed. What the drop itself reads is
    /// still the in-process row, because a name alone would not say whether the branch is remote or
    /// checked out.
    /// </para>
    /// </remarks>
    public static DataTransfer TransferFor(BranchRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        DataTransfer transfer = new();
        transfer.Add(DataTransferItem.Create(DragFormat, row));
        transfer.Add(DataTransferItem.Create(DataFormat.Text, row.FullName));

        return transfer;
    }

    /// <summary>Gets what this pair asks the operations service to do.</summary>
    public BranchDropRequest Request => new(
        Source.FullName,
        Source.IsRemote,
        Target.FullName,
        Target.IsRemote,
        Target.IsCurrent);

    /// <summary>Gets the same pair the other way round.</summary>
    public BranchDrop Reversed() => new(Target, Source);

    /// <summary>Gets what the menu item that merges this pair is called.</summary>
    public string MergeHeader => $"Merge \"{Source.FullName}\" into \"{Target.FullName}\"";

    /// <summary>Gets what the fast-forward-only item is called.</summary>
    public string FastForwardHeader => $"Merge \"{Source.FullName}\" into \"{Target.FullName}\", fast-forward only";

    /// <summary>Gets what the item that merges the other way round is called.</summary>
    public string ReversedHeader => $"Merge \"{Target.FullName}\" into \"{Source.FullName}\"";
}

/// <summary>
/// A group of branches on the page: the local ones, or one remote's.
/// </summary>
/// <param name="Title">The heading shown above the group.</param>
/// <param name="IsRemote">Whether the group holds remote branches.</param>
/// <param name="Rows">The branches in the group.</param>
public sealed record BranchGroupViewModel(string Title, bool IsRemote, IReadOnlyList<BranchRowViewModel> Rows)
{
    /// <summary>Gets how many branches the group holds, for its heading.</summary>
    public string Count => Rows.Count.ToString(CultureInfo.CurrentCulture);
}

/// <summary>
/// ViewModel behind the branches page.
/// </summary>
/// <remarks>
/// Branches and nothing else: tags were the other half of this page, behind a switch, and are now
/// <see cref="TagsPageViewModel"/>. The page shows and filters; every write, with its dialog and its
/// confirmation, belongs to <see cref="IBranchOperations"/>, which the graph's own context menu
/// calls too. Two places offering the same operation have to ask the same questions.
/// </remarks>
public sealed class BranchesPageViewModel : PageViewModelBase
{
    private readonly IBranchOperations _operations;
    private readonly ICheckoutOperations _checkoutOperations;
    private readonly IMergeOperations _mergeOperations;
    private readonly IBranchDropOperations _dropOperations;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="operations">Performs the branch operations, dialogs and all.</param>
    /// <param name="checkoutOperations">Performs a checkout, including the questions it has to ask.</param>
    /// <param name="mergeOperations">Merges a branch into the current one.</param>
    /// <param name="dropOperations">Carries out one branch dropped onto another.</param>
    public BranchesPageViewModel(
        IRepositoryContext repositoryContext,
        IBranchOperations operations,
        ICheckoutOperations checkoutOperations,
        IMergeOperations mergeOperations,
        IBranchDropOperations dropOperations)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(checkoutOperations);
        ArgumentNullException.ThrowIfNull(mergeOperations);
        ArgumentNullException.ThrowIfNull(dropOperations);

        _operations = operations;
        _checkoutOperations = checkoutOperations;
        _mergeOperations = mergeOperations;
        _dropOperations = dropOperations;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsRepositoryOpen);
        CreateBranchCommand = new AsyncRelayCommand(OnCreateAsync, () => IsRepositoryOpen);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => SearchText.Length > 0);

        CheckoutCommand = new AsyncRelayCommand<BranchRowViewModel>(OnCheckoutAsync, CanCheckout);
        RenameCommand = new AsyncRelayCommand<BranchRowViewModel>(OnRenameAsync, IsLocal);
        DeleteCommand = new AsyncRelayCommand<BranchRowViewModel>(OnDeleteAsync, CanDelete);
        SetUpstreamCommand = new AsyncRelayCommand<BranchRowViewModel>(OnSetUpstreamAsync, IsLocal);
        MergeCommand = new AsyncRelayCommand<BranchRowViewModel>(OnMergeAsync, CanMerge);

        MergeDropCommand = new AsyncRelayCommand<BranchDrop>(drop => OnDropAsync(drop, FastForwardMode.WhenPossible), CanDrop);
        FastForwardDropCommand = new AsyncRelayCommand<BranchDrop>(drop => OnDropAsync(drop, FastForwardMode.Only), CanDrop);
        MergeReversedDropCommand = new AsyncRelayCommand<BranchDrop>(
            drop => OnDropAsync(drop?.Reversed(), FastForwardMode.WhenPossible),
            drop => CanDrop(drop?.Reversed()));

    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Branches";

    /// <summary>Gets what the filter box says it filters.</summary>
    public string SearchPlaceholder => "Filter branches";

    /// <summary>Gets the branches, grouped as local and one group per remote.</summary>
    public ObservableCollection<BranchGroupViewModel> Groups { get; } = [];

    /// <summary>
    /// Gets the same branches the groups hold, flattened into the one list the view shows: each
    /// group's heading followed by its rows.
    /// </summary>
    /// <remarks>
    /// The grouping is the model and this is its presentation, rebuilt from it every time. One list
    /// rather than a list per group, because a page has one selection: nested lists would each keep
    /// their own and two rows would be highlighted at once.
    /// </remarks>
    public ObservableCollection<IBranchListItem> Items { get; } = [];

    /// <summary>
    /// Gets or sets the item the reader has selected, which is a branch or nothing — a heading is
    /// not selectable.
    /// </summary>
    public IBranchListItem? SelectedItem
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(SelectedBranch));
            }
        }
    }

    /// <summary>Gets the selected branch, or <see langword="null"/> when none is selected.</summary>
    public BranchRowViewModel? SelectedBranch => SelectedItem as BranchRowViewModel;

    /// <summary>
    /// Gets or sets a substring the shown branch names must contain.
    /// </summary>
    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ClearSearchCommand.NotifyCanExecuteChanged();
                Rebuild();
            }
        }
    } = string.Empty;

    /// <summary>Gets a value indicating whether the page has nothing to show.</summary>
    public bool IsEmpty => Groups.Count == 0;

    /// <summary>
    /// Gets the sentence shown while the page has nothing to display.
    /// </summary>
    public string EmptyMessage
        => !IsRepositoryOpen
            ? "Open a repository to manage its branches."
            : SearchText.Trim().Length > 0
                ? "No branch matches this search."
                : "This repository has no branches yet. The first commit creates one.";

    /// <summary>Gets the command that re-reads the references.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Gets the command that opens the create-branch dialog.</summary>
    public AsyncRelayCommand CreateBranchCommand { get; }

    /// <summary>Gets the command that clears the search box.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>Gets the command that checks a branch out.</summary>
    public AsyncRelayCommand<BranchRowViewModel> CheckoutCommand { get; }

    /// <summary>Gets the command that renames a branch.</summary>
    public AsyncRelayCommand<BranchRowViewModel> RenameCommand { get; }

    /// <summary>Gets the command that deletes a branch.</summary>
    public AsyncRelayCommand<BranchRowViewModel> DeleteCommand { get; }

    /// <summary>Gets the command that points a branch at an upstream.</summary>
    public AsyncRelayCommand<BranchRowViewModel> SetUpstreamCommand { get; }

    /// <summary>Gets the command that merges a branch into the one checked out.</summary>
    public AsyncRelayCommand<BranchRowViewModel> MergeCommand { get; }

    /// <summary>Gets the command that merges the dropped branch into the one it landed on.</summary>
    public AsyncRelayCommand<BranchDrop> MergeDropCommand { get; }

    /// <summary>Gets the command that does the same, refusing anything but a fast-forward.</summary>
    public AsyncRelayCommand<BranchDrop> FastForwardDropCommand { get; }

    /// <summary>
    /// Gets the command that merges the other way round — the branch that was landed on into the one
    /// that was dragged.
    /// </summary>
    /// <remarks>
    /// Offered because dragging the pair the wrong way round is the mistake this gesture invites,
    /// and the menu is already naming both ends. It refuses when the reversed pair is one the
    /// service would not carry out, which is what a remote source means.
    /// </remarks>
    public AsyncRelayCommand<BranchDrop> MergeReversedDropCommand { get; }

    /// <summary>
    /// Answers whether a drop is one the page would carry out, which is what the drag asks on every
    /// pointer move.
    /// </summary>
    /// <param name="drop">The pair.</param>
    /// <returns><see langword="true"/> when the drop would mean something.</returns>
    public static bool CanDrop(BranchDrop? drop)
        => drop is not null && BranchDropOperations.CanDrop(drop.Request);

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);
        Rebuild();
    }

    /// <summary>
    /// Re-reads the repository's references and rebuilds the page.
    /// </summary>
    /// <returns>A task that completes once the page is up to date.</returns>
    public async Task RefreshAsync()
    {
        if (IsRepositoryOpen)
        {
            await RepositoryContext.RefreshAsync(RepositoryContext.RepositoryLifetime).ConfigureAwait(true);
        }

        Rebuild();
    }

    /// <inheritdoc />
    protected override void OnRepositoryChanged()
    {
        base.OnRepositoryChanged();

        RefreshCommand.NotifyCanExecuteChanged();
        CreateBranchCommand.NotifyCanExecuteChanged();
        Rebuild();
    }

    /// <inheritdoc />
    protected override void OnRepositoryStateRefreshed() => Rebuild();

    /// <summary>
    /// Rebuilds the groups from whatever the repository context last read.
    /// </summary>
    private void Rebuild()
    {
        // Captured before the list is emptied: clearing a list tells its ListBox the selection is
        // gone, and the ListBox tells this page so. What survives a rebuild is the name.
        string? selectedBranch = SelectedBranch?.FullName;

        Groups.Clear();
        Items.Clear();

        RefCollection refs = RepositoryContext.Refs;

        // From the whole collection, not from the rows below: the filter box hides branches, it does
        // not take them off the remote.
        Dictionary<string, string> published = PublishedBranches(refs);

        List<BranchRowViewModel> local = [];

        foreach (GitBranch branch in refs.LocalBranches)
        {
            if (Matches(branch))
            {
                local.Add(new BranchRowViewModel(this, branch, published.GetValueOrDefault(branch.ShortName)));
            }
        }

        if (local.Count > 0)
        {
            Groups.Add(new BranchGroupViewModel("Local", false, local));
        }

        // One group per remote, in the order the remotes' branches were read, so a repository with
        // several remotes does not shuffle between refreshes.
        Dictionary<string, List<BranchRowViewModel>> byRemote = new(StringComparer.Ordinal);
        List<string> order = [];

        foreach (GitBranch branch in refs.RemoteBranches)
        {
            if (!Matches(branch))
            {
                continue;
            }

            string remote = branch.RemoteName ?? "remote";

            if (!byRemote.TryGetValue(remote, out List<BranchRowViewModel>? rows))
            {
                rows = [];
                byRemote[remote] = rows;
                order.Add(remote);
            }

            rows.Add(new BranchRowViewModel(this, branch));
        }

        foreach (string remote in order)
        {
            Groups.Add(new BranchGroupViewModel(remote, true, byRemote[remote]));
        }

        foreach (BranchGroupViewModel group in Groups)
        {
            Items.Add(new BranchGroupHeaderViewModel(group.Title, group.IsRemote, group.Count));

            foreach (BranchRowViewModel row in group.Rows)
            {
                Items.Add(row);
            }
        }

        RestoreSelection(selectedBranch);

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    /// <summary>
    /// Works out which remote-tracking branch each local branch is on.
    /// </summary>
    /// <param name="refs">Every reference the repository has.</param>
    /// <returns>The remote-tracking branch's short name, by local branch name.</returns>
    /// <remarks>
    /// <para>
    /// Two ways of being on a remote, and the second is the one an upstream cannot answer. A branch
    /// that tracks an upstream names it, and that is the answer. A branch pushed with a plain
    /// <c>git push origin main</c> tracks nothing at all — git reports no upstream and no ahead/behind
    /// for it — and it is on origin all the same, which the remote-tracking ref of the same name says.
    /// Reading only the upstream would call that branch local-only every time.
    /// </para>
    /// <para>
    /// A configured upstream still has to exist among the refs: a branch whose upstream git reports as
    /// <c>[gone]</c> is not on a remote, and neither is one pointing at a remote nobody fetches any
    /// more. Where several remotes carry the same branch name, the first read wins — the collection is
    /// ordered, so the answer does not shuffle between refreshes.
    /// </para>
    /// </remarks>
    private static Dictionary<string, string> PublishedBranches(RefCollection refs)
    {
        Dictionary<string, string> byBranchName = new(StringComparer.Ordinal);
        HashSet<string> remoteRefs = new(StringComparer.Ordinal);

        foreach (GitBranch remote in refs.RemoteBranches)
        {
            remoteRefs.Add(remote.ShortName);
            byBranchName.TryAdd(remote.NameWithoutRemote, remote.ShortName);
        }

        Dictionary<string, string> published = new(StringComparer.Ordinal);

        foreach (GitBranch branch in refs.LocalBranches)
        {
            if (branch.Tracking.IsUpstreamGone)
            {
                continue;
            }

            if (branch.UpstreamShortName is { Length: > 0 } upstream && remoteRefs.Contains(upstream))
            {
                published[branch.ShortName] = upstream;
            }
            else if (byBranchName.TryGetValue(branch.ShortName, out string? sameName))
            {
                published[branch.ShortName] = sameName;
            }
        }

        return published;
    }

    /// <summary>
    /// Puts the selection back on the row that stands for what was selected before the rebuild.
    /// </summary>
    /// <param name="branch">The full name of the branch that was selected, if any.</param>
    /// <remarks>
    /// By name, because every row is a new object: the page rebuilds on a refresh, on an operation
    /// and on every keystroke in the filter box, and a selection that did not survive that would be
    /// a selection nobody could keep. A row that is gone — deleted, renamed, filtered out — takes
    /// the selection with it.
    /// </remarks>
    private void RestoreSelection(string? branch)
        => SelectedItem = branch is null
            ? null
            : Items.OfType<BranchRowViewModel>().FirstOrDefault(row => row.FullName == branch);

    private bool Matches(GitBranch branch) => Matches(branch.ShortName);

    private bool Matches(string name)
    {
        string search = SearchText.Trim();

        return search.Length == 0 || name.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLocal(BranchRowViewModel? row) => row is { IsRemote: false };

    private static bool CanCheckout(BranchRowViewModel? row) => row is not null && !row.IsCurrent;

    private static bool CanDelete(BranchRowViewModel? row) => row is { IsCurrent: false };

    /// <summary>
    /// Merging a branch into itself is the one case that means nothing.
    /// </summary>
    private static bool CanMerge(BranchRowViewModel? row) => row is { IsCurrent: false };

    // ---------------------------------------------------------------- commands

    private async Task OnCheckoutAsync(BranchRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        // A remote branch is checked out by creating the tracking branch for it, which is the
        // branch service's job; a local one goes through the checkout flow, so the questions about
        // uncommitted work get asked.
        await Run(() => row.IsRemote
            ? _operations.CheckoutAsync(row.FullName, true)
            : _checkoutOperations.CheckoutAsync(row.FullName)).ConfigureAwait(true);
    }

    private async Task OnMergeAsync(BranchRowViewModel? row)
    {
        if (row is not null)
        {
            await Run(async () =>
            {
                Core.Merging.MergeOutcome outcome = await _mergeOperations.MergeAsync(row.FullName)
                    .ConfigureAwait(true);

                return outcome.ChangedAnything;
            }).ConfigureAwait(true);
        }
    }

    private async Task OnDropAsync(BranchDrop? drop, FastForwardMode fastForward)
    {
        if (drop is not null)
        {
            await Run(() => _dropOperations.DropAsync(drop.Request, fastForward)).ConfigureAwait(true);
        }
    }

    private Task OnCreateAsync() => Run(() => _operations.CreateAsync());

    private async Task OnRenameAsync(BranchRowViewModel? row)
    {
        if (row is not null)
        {
            await Run(() => _operations.RenameAsync(row.FullName)).ConfigureAwait(true);
        }
    }

    private async Task OnDeleteAsync(BranchRowViewModel? row)
    {
        if (row is not null)
        {
            await Run(() => _operations.DeleteAsync(row.FullName, row.IsRemote)).ConfigureAwait(true);
        }
    }

    private async Task OnSetUpstreamAsync(BranchRowViewModel? row)
    {
        if (row is not null)
        {
            await Run(() => _operations.SetUpstreamAsync(row.FullName, row.HasUpstream ? row.Upstream : null))
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Runs one operation with the page marked busy, and rebuilds when it changed something.
    /// </summary>
    private async Task Run(Func<Task<bool>> operation)
    {
        IsBusy = true;

        try
        {
            if (await operation().ConfigureAwait(true))
            {
                Rebuild();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
