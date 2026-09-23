using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Controls;
using Enigma.GitClient.App.Controls.Graph;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels.Panels;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Graph;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Status;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// Which part of the history the view is showing.
/// </summary>
/// <param name="Label">What the selector displays.</param>
/// <param name="Scope">The scope it maps to.</param>
public sealed record HistoryScopeOption(string Label, CommitLogScope Scope);

/// <summary>
/// The commit graph: pages commits in, lays them out, and keeps the selection in step with the rest
/// of the shell.
/// </summary>
public sealed class HistoryPageViewModel : PageViewModelBase
{
    private readonly ICommitLogReader _reader;
    private readonly IWorkingTreeProbe _workingTree;
    private readonly IDiffService _diffs;
    private readonly IBranchOperations _branchOperations;
    private readonly ITagOperations _tagOperations;
    private readonly ICheckoutOperations _checkoutOperations;
    private readonly IBranchDropOperations _dropOperations;
    private readonly ISyncOperations _syncOperations;
    private readonly IHostLinkService _links;
    private readonly ISettingsService _settings;
    private readonly IToolDialogService _tools;
    private bool _absoluteDates;

    private DiffTarget? _diffTarget;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<HistoryPageViewModel> _logger;

    private GraphLayoutState? _layoutCarry;
    private CommitLogQuery _query = new();
    private CancellationTokenSource? _loadCancellation;
    private bool _hasMore;
    private bool _refreshPending;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="reader">Reads the commits.</param>
    /// <param name="workingTree">Answers whether there is anything uncommitted.</param>
    /// <param name="diffs">Reads what the selected commit touched.</param>
    /// <param name="infoBar">Reports a failure the user can act on.</param>
    /// <param name="dropOperations">Merges one branch into another, checking the destination out first.</param>
    /// <param name="syncOperations">Pulls and pushes a branch from its badge.</param>
    /// <param name="tools">Opens the branches, tags and remotes over the history.</param>
    /// <param name="logger">Receives the detail behind a reported failure.</param>
    public HistoryPageViewModel(
        IRepositoryContext repositoryContext,
        ICommitLogReader reader,
        IWorkingTreeProbe workingTree,
        IDiffService diffs,
        ISystemInterop interop,
        IBranchOperations branchOperations,
        ITagOperations tagOperations,
        ICheckoutOperations checkoutOperations,
        IBranchDropOperations dropOperations,
        ISyncOperations syncOperations,
        IHostLinkService links,
        ISettingsService settings,
        DiffViewerViewModel diff,
        IInfoBarService infoBar,
        IToolDialogService tools,
        ILogger<HistoryPageViewModel> logger)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(workingTree);
        ArgumentNullException.ThrowIfNull(diffs);
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentNullException.ThrowIfNull(branchOperations);
        ArgumentNullException.ThrowIfNull(tagOperations);
        ArgumentNullException.ThrowIfNull(checkoutOperations);
        ArgumentNullException.ThrowIfNull(dropOperations);
        ArgumentNullException.ThrowIfNull(syncOperations);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(logger);

        _reader = reader;
        _workingTree = workingTree;
        _diffs = diffs;

        Diff = diff;
        _branchOperations = branchOperations;
        _tagOperations = tagOperations;
        _checkoutOperations = checkoutOperations;
        _dropOperations = dropOperations;
        _syncOperations = syncOperations;
        _links = links;
        _settings = settings;
        _tools = tools;

        ApplySettings(settings.Current);
        settings.Changed += (_, e) => ApplySettings(e.Settings);

        ClearMergeSourceCommand = new RelayCommand(() => MergeSource = null, () => MergeSource is not null);

        BranchCommands = new HistoryBranchCommands(
            new AsyncRelayCommand<HistoryBranchViewModel>(OnCheckoutBranchAsync, branch => branch?.CanCheckout == true),
            new RelayCommand<HistoryBranchViewModel>(OnSetMergeSource, branch => branch?.CanSetAsMergeSource == true),
            ClearMergeSourceCommand,
            new AsyncRelayCommand<HistoryBranchViewModel>(
                branch => OnMergeIntoAsync(branch, Core.Merging.FastForwardMode.WhenPossible),
                branch => branch?.CanMergeInto == true),
            new AsyncRelayCommand<HistoryBranchViewModel>(
                branch => OnMergeIntoAsync(branch, Core.Merging.FastForwardMode.Only),
                branch => branch?.CanMergeInto == true),
            new AsyncRelayCommand<HistoryBranchViewModel>(OnMergeIntoCurrentAsync, branch => branch?.CanMergeIntoCurrent == true),
            new AsyncRelayCommand<HistoryBranchViewModel>(OnDeleteBranchAsync, branch => branch?.CanDelete == true),
            new AsyncRelayCommand<HistoryBranchViewModel>(OnPullBranchAsync, branch => branch?.CanSynchronise == true),
            new AsyncRelayCommand<HistoryBranchViewModel>(OnPushBranchAsync, branch => branch?.CanSynchronise == true),
            () => MergeSource,
            () => RepositoryContext.Head is { IsDetached: false } head ? head.BranchName : null);

        RowCommands = new HistoryRowCommands(
            new AsyncRelayCommand<CommitRowViewModel>(OnCreateBranchHereAsync, HasCommit),
            new AsyncRelayCommand<CommitRowViewModel>(OnCheckoutCommitAsync, HasCommit),
            new AsyncRelayCommand<CommitRowViewModel>(OnCreateTagHereAsync, HasCommit),
            new RelayCommand<CommitRowViewModel>(OnActivate, row => row is not null),
            new RelayCommand<CommitRowViewModel>(OnShowChanges, row => row is not null),
            new AsyncRelayCommand<CommitRowViewModel>(OnOpenOnHostAsync, HasCommit),
            () => _links.HostName,
            BranchCommands);

        Files = new ChangedFilesPanelViewModel(interop, settings)
        {
            // The file is opened at the commit that is selected, which is the only reference that
            // makes sense for a file the user is looking at in history.
            OpenOnHost = file => _links.OpenFileAsync(SelectedRow?.Sha ?? string.Empty, file.Path),
        };

        // The viewer follows the panel rather than the panel driving it: the panel's job ends at
        // "this file is selected", whoever is listening.
        Files.SelectionChanged += (_, _) => _ = ShowSelectedFileAsync();
        _infoBar = infoBar;
        _logger = logger;

        CloseDiffViewCommand = new RelayCommand(() => IsDiffViewOpen = false);
        LoadMoreCommand = new AsyncRelayCommand(OnLoadMoreAsync, () => HasMore && IsNotBusy);
        RefreshCommand = new AsyncRelayCommand(ReloadAsync, () => IsRepositoryOpen && IsNotBusy);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => SearchText.Length > 0);

        OpenBranchesCommand = new AsyncRelayCommand(() => OpenToolAsync(ToolDialog.Branches), () => IsRepositoryOpen);
        OpenTagsCommand = new AsyncRelayCommand(() => OpenToolAsync(ToolDialog.Tags), () => IsRepositoryOpen);
        OpenRemotesCommand = new AsyncRelayCommand(() => OpenToolAsync(ToolDialog.Remotes), () => IsRepositoryOpen);

        SelectedScope = ScopeOptions[0];
    }

    /// <summary>
    /// Gets the rows the list shows, newest first.
    /// </summary>
    public ObservableCollection<CommitRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Gets the scopes the selector offers.
    /// </summary>
    public IReadOnlyList<HistoryScopeOption> ScopeOptions { get; } =
    [
        new("All branches", CommitLogScope.AllRefs),
        new("Current branch", CommitLogScope.Head),
    ];

    /// <summary>
    /// Gets or sets which part of the history is shown.
    /// </summary>
    public HistoryScopeOption SelectedScope
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                _query = _query with { Scope = value.Scope, Skip = 0 };
                QueueReload();
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether merged-in branches are hidden.
    /// </summary>
    public bool FirstParentOnly
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                _query = _query with { FirstParentOnly = value, Skip = 0 };
                QueueReload();
            }
        }
    }

    /// <summary>
    /// Gets or sets what to look for in the commit messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The search marks the rows it finds and removes none: the graph is laid out a page at a time,
    /// so a list filtered down to the matches draws lanes between commits that are not adjacent in
    /// the history — a picture of a repository that does not exist. Nothing is hidden, so the lanes
    /// stay the ones git built.
    /// </para>
    /// <para>
    /// The price is that it searches what is loaded rather than the whole history, which is the
    /// honest reading of "highlight the lines that were found": a line that is not on screen cannot
    /// be highlighted. What is loaded grows with <c>Load more commits</c>, and the rows it brings in
    /// are marked as they arrive.
    /// </para>
    /// </remarks>
    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ClearSearchCommand.NotifyCanExecuteChanged();
                MarkMatches();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets how many of the loaded rows match the search.
    /// </summary>
    public int MatchCount { get; private set => SetProperty(ref field, value); }

    /// <summary>
    /// Gets a value indicating whether anything is being searched for.
    /// </summary>
    public bool HasSearch => SearchText.Trim().Length > 0;

    /// <summary>
    /// Gets what the toolbar says beside the search box, empty while nothing is searched for.
    /// </summary>
    /// <remarks>
    /// Without it, a search that found nothing and a search that found everything look the same:
    /// the list is the whole list either way.
    /// </remarks>
    public string MatchSummary
        => !HasSearch
            ? string.Empty
            : MatchCount switch
            {
                0 => "no match",
                1 => "1 match",
                _ => $"{MatchCount.ToString(CultureInfo.CurrentCulture)} matches",
            };

    /// <summary>
    /// Gets or sets the row the user has selected, which the rest of the shell follows.
    /// </summary>
    public CommitRowViewModel? SelectedRow
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RepositoryContext.SelectedCommit = value?.Commit;
                OnPropertyChanged(nameof(HasSelection));
                NotifySelectedCommitDetails();

                // Selecting a line selects it and nothing more: the diffs are asked for, by a
                // double-click or by the row's menu. Losing the selection — what a reload after a
                // checkout does — still puts them away, because a page describing a commit nobody
                // has selected is describing nothing.
                if (value is null)
                {
                    IsDiffViewOpen = false;
                }

                _ = LoadChangedFilesAsync();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a row is selected.
    /// </summary>
    public bool HasSelection => SelectedRow is not null;

    /// <summary>
    /// Gets or sets a value indicating whether what the selected commit changed has the page.
    /// </summary>
    /// <remarks>
    /// The page's own statement: the view shows the diffs over the whole of itself while this is
    /// set, and every way out — the back button, Escape — clears it. Closing deliberately leaves
    /// <see cref="SelectedRow"/> alone, because the selection is also what "create a branch here"
    /// starts from and what the row highlight shows. Nothing but an explicit request opens it: a
    /// double-click on a row, or that row's menu.
    /// </remarks>
    public bool IsDiffViewOpen
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && !value && _refreshPending)
            {
                // What the automatic refresh found while the diffs had the page, drawn now that the
                // graph is back.
                _refreshPending = false;
                _ = ReloadKeepingPlaceAsync();
            }
        }
    }

    /// <summary>
    /// Gets the panel listing what the selected commit touched.
    /// </summary>
    public ChangedFilesPanelViewModel Files { get; }

    /// <summary>
    /// Gets the diff viewer showing the file selected in <see cref="Files"/>.
    /// </summary>
    public DiffViewerViewModel Diff { get; }

    /// <summary>
    /// Gets the commands every row's context menu runs.
    /// </summary>
    public HistoryRowCommands RowCommands { get; }

    /// <summary>
    /// Gets the commands every branch on every row offers, from its badge and from the row's menu.
    /// </summary>
    public HistoryBranchCommands BranchCommands { get; }

    /// <summary>
    /// Gets or sets the branch the next merge takes its work from, or <see langword="null"/> when none
    /// has been chosen.
    /// </summary>
    /// <remarks>
    /// It stays until it is cleared, replaced, or its branch disappears: a merge does not use it up,
    /// so one source can be merged into several branches in a row. The toolbar shows it, which is what
    /// keeps a state that outlives a menu from being a state nobody can see.
    /// </remarks>
    public MergeSource? MergeSource
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasMergeSource));
                OnPropertyChanged(nameof(MergeSourceSummary));
                ClearMergeSourceCommand.NotifyCanExecuteChanged();
                NotifyBranchCommands();

                foreach (CommitRowViewModel row in Rows)
                {
                    row.NotifyMergeSourceChanged();
                }
            }
        }
    }

    /// <summary>Gets a value indicating whether a merge source is chosen.</summary>
    public bool HasMergeSource => MergeSource is not null;

    /// <summary>Gets what the toolbar says about the merge source.</summary>
    public string MergeSourceSummary => MergeSource is { } source ? $"Merge source: {source.Name}" : string.Empty;

    /// <summary>Gets the command that forgets the merge source.</summary>
    public RelayCommand ClearMergeSourceCommand { get; }

    /// <summary>
    /// Raised when the uncommitted-changes row is activated, so the shell can move to the page that
    /// actually does something with it.
    /// </summary>
    /// <remarks>
    /// An event rather than a navigation service: the shell's navigation builds the page ViewModels,
    /// so a page that asked it for a reference would be asking to be constructed by something it is
    /// constructing.
    /// </remarks>
    public event EventHandler? WorkingDirectoryRequested;

    /// <summary>Gets the selected commit's subject, or the pseudo-row's label.</summary>
    public string SelectedSubject => SelectedRow?.Subject ?? string.Empty;

    /// <summary>Gets the selected commit's full SHA.</summary>
    public string SelectedSha => SelectedRow?.Sha ?? string.Empty;

    /// <summary>Gets the selected commit's author.</summary>
    public string SelectedAuthor => SelectedRow?.Commit?.Author.ToString() ?? string.Empty;

    /// <summary>Gets when the selected commit was authored, in full.</summary>
    public string SelectedDate => SelectedRow?.AbsoluteDate ?? string.Empty;

    /// <summary>Gets the selected commit's message body, empty when it has none.</summary>
    public string SelectedBody => SelectedRow?.Commit?.Body ?? string.Empty;

    /// <summary>Gets a value indicating whether there is a body to show.</summary>
    public bool HasSelectedBody => SelectedBody.Length > 0;

    /// <summary>
    /// Gets a value indicating whether the details pane has a commit to describe, as opposed to the
    /// uncommitted-changes row or nothing at all.
    /// </summary>
    public bool HasSelectedCommit => SelectedRow?.Commit is not null;

    /// <summary>
    /// Gets or sets how many commits a page holds.
    /// </summary>
    /// <remarks>
    /// A page large enough to fill several screens keeps the first paint fast and bounds the
    /// graph's lane carry. It becomes a user setting with the preferences work item.
    /// </remarks>
    public int PageSize
    {
        get => _query.Take;
        set
        {
            if (_query.Take == value)
            {
                return;
            }

            _query = _query with { Take = Math.Max(1, value) };
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets a value indicating whether more commits can be loaded.
    /// </summary>
    public bool HasMore
    {
        get => _hasMore;
        private set
        {
            if (SetProperty(ref _hasMore, value))
            {
                LoadMoreCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the list has nothing to show.
    /// </summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// Gets the sentence shown when the list is empty, which depends on why it is empty.
    /// </summary>
    public string EmptyMessage
        => !IsRepositoryOpen
            ? "Open a repository to see its history, with the graph, the authors, the timestamps and the short hashes."
            : _query.IsFiltered
                ? "No commit matches the current filter."
                : "This repository has no commits yet. Make one from the Changes page.";

    /// <summary>
    /// Gets the width the graph column needs for the rows currently loaded.
    /// </summary>
    /// <remarks>
    /// One width for the whole page, not one per row: a per-row width would put every other column
    /// at a different horizontal position on every line.
    /// </remarks>
    public double GraphColumnWidth
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                Columns.GraphWidth = value;
            }
        }
    }

    /// <summary>
    /// Gets the width of every column the list draws, shared by the header and by every row.
    /// </summary>
    public HistoryColumnLayout Columns { get; } = new();

    /// <summary>
    /// Gets the distance between two graph lanes, which is a preference.
    /// </summary>
    public double LaneWidth
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                RecalculateGraphWidth();
            }
        }
    } = AppSettings.Defaults.GraphLaneWidth;

    /// <summary>
    /// Gets how tall a history row is, which is a preference.
    /// </summary>
    public double RowHeight { get; private set => SetProperty(ref field, value); }
        = AppSettings.Defaults.GraphRowHeight;

    /// <summary>
    /// Gets the width the badge column needs for the rows currently loaded.
    /// </summary>
    /// <remarks>
    /// One width for the whole page, for the same reason <see cref="GraphColumnWidth"/> is: the
    /// column is <c>Auto</c>, and an <c>Auto</c> column is measured per row, so a badge on one line
    /// used to push that line's subject, author, date and sha sideways while its neighbours stayed
    /// where they were. Zero when nothing is decorated, so an undecorated history spends no width
    /// on the column at all.
    /// </remarks>
    public double RefColumnWidth
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                // Offered, not imposed: once the reader has dragged that column's grip, the width
                // is theirs and the measurement stops overriding it.
                Columns.SeedRefsWidth(value);
            }
        }
    }

    /// <summary>Gets the padding on each side of the graph column.</summary>
    public static double LanePadding => 8;

    /// <summary>
    /// Gets how wide the badge column may grow to. One very long branch name is not a reason to
    /// take the subject's room away; past this the badge ellipsises instead.
    /// </summary>
    public static double MaximumRefColumnWidth => 280;

    /// <summary>Gets how many lanes the graph column may grow to.</summary>
    public static int MaximumLanes => 14;

    /// <summary>Gets the command that loads the next page.</summary>
    public AsyncRelayCommand LoadMoreCommand { get; }

    /// <summary>Gets the command that re-reads the history from scratch.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Gets the command that clears the search box.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>Gets the command that puts the diffs away and brings the graph back.</summary>
    public RelayCommand CloseDiffViewCommand { get; }

    /// <summary>Gets the command that opens the branches over the history.</summary>
    public AsyncRelayCommand OpenBranchesCommand { get; }

    /// <summary>Gets the command that opens the tags over the history.</summary>
    public AsyncRelayCommand OpenTagsCommand { get; }

    /// <summary>Gets the command that opens the remotes over the history.</summary>
    public AsyncRelayCommand OpenRemotesCommand { get; }

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);

        if (Rows.Count == 0 && IsRepositoryOpen)
        {
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Raised just before the rows are replaced by a refresh that keeps the reader's place, so the view
    /// can remember where its list was scrolled to.
    /// </summary>
    public event EventHandler? RowsReplacing;

    /// <summary>
    /// Raised once those rows are in, so the view can put its list back where it was.
    /// </summary>
    public event EventHandler? RowsReplaced;

    /// <summary>
    /// Brings the history up to date after an automatic refresh — and does nothing at all when there
    /// is nothing new to draw.
    /// </summary>
    /// <param name="referencesMoved">Whether HEAD or any reference moved in that refresh.</param>
    /// <returns>A task that completes once the history is current.</returns>
    /// <remarks>
    /// <para>
    /// Nothing new is the common case — an automatic refresh runs every few seconds — and redrawing
    /// then would throw the reader's place away for nothing. Besides the references, the one thing that
    /// changes what the graph draws is whether there is uncommitted work, which is the row at its top.
    /// </para>
    /// <para>
    /// When something did change, the same number of commits is read again, the selected commit is
    /// selected again, and the view keeps its scroll offset. While the diffs have the page, the redraw
    /// waits for them to close: replacing the rows would take away the commit they describe.
    /// </para>
    /// </remarks>
    public async Task RefreshInPlaceAsync(bool referencesMoved)
    {
        RepositoryHandle? repository = RepositoryContext.Repository;

        if (repository is null || IsBusy)
        {
            return;
        }

        bool dirty = await IsWorkingTreeDirtyAsync(repository, RepositoryContext.RepositoryLifetime).ConfigureAwait(true);
        bool showsUncommitted = Rows.Count > 0 && Rows[0].IsUncommitted;

        if (!referencesMoved && dirty == showsUncommitted)
        {
            return;
        }

        if (IsDiffViewOpen)
        {
            _refreshPending = true;
            return;
        }

        await ReloadKeepingPlaceAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Reads the history again as far as it had been read, and puts the selection back on the same
    /// commit.
    /// </summary>
    private async Task ReloadKeepingPlaceAsync()
    {
        string? selectedSha = SelectedRow?.Sha;
        bool uncommittedSelected = SelectedRow?.IsUncommitted ?? false;

        int loaded = 0;

        foreach (CommitRowViewModel row in Rows)
        {
            if (!row.IsUncommitted)
            {
                loaded++;
            }
        }

        RowsReplacing?.Invoke(this, EventArgs.Empty);

        // As many commits as were on screen, in one read: "load more" is the reader's, and a refresh
        // must not quietly undo it.
        int pageSize = _query.Take;
        _query = _query with { Take = Math.Max(pageSize, loaded) };

        try
        {
            await ReloadAsync().ConfigureAwait(true);
        }
        finally
        {
            _query = _query with { Take = pageSize };
        }

        foreach (CommitRowViewModel row in Rows)
        {
            if (uncommittedSelected ? row.IsUncommitted : selectedSha is { Length: > 0 } && row.Sha == selectedSha)
            {
                SelectedRow = row;
                break;
            }
        }

        RowsReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears the history and reads the first page again.
    /// </summary>
    /// <returns>A task that completes once the first page is loaded.</returns>
    public async Task ReloadAsync()
    {
        CancelInFlightLoad();

        Rows.Clear();
        _layoutCarry = null;
        _query = _query with { Skip = 0 };
        SelectedRow = null;
        HasMore = false;
        RefColumnWidth = 0;

        NotifyEmptyState();

        if (!IsRepositoryOpen)
        {
            return;
        }

        await LoadPageAsync(includeUncommittedRow: true).ConfigureAwait(true);
    }

    /// <inheritdoc />
    protected override void OnRepositoryStateRefreshed() => ForgetAMergeSourceThatIsGone();

    /// <inheritdoc />
    protected override void OnRepositoryChanged()
    {
        // A branch of the repository that was open is nothing to merge in this one.
        MergeSource = null;

        _ = _links.RefreshAsync();

        base.OnRepositoryChanged();

        RefreshCommand.NotifyCanExecuteChanged();
        OpenBranchesCommand.NotifyCanExecuteChanged();
        OpenTagsCommand.NotifyCanExecuteChanged();
        OpenRemotesCommand.NotifyCanExecuteChanged();
        _ = ReloadAsync();
    }

    /// <summary>
    /// Opens one of the secondary pages over the history, and re-reads the history afterwards when
    /// what it did moved a reference.
    /// </summary>
    /// <param name="dialog">Which page.</param>
    /// <returns>A task that completes once the dialog has closed.</returns>
    /// <remarks>
    /// Only when something moved: closing the tags dialog after reading it is not a reason to lose
    /// the selected line.
    /// </remarks>
    private async Task OpenToolAsync(ToolDialog dialog)
    {
        RepositoryStateStamp before = RepositoryStateStamp.Of(RepositoryContext);

        await _tools.ShowAsync(dialog).ConfigureAwait(true);

        if (IsRepositoryOpen && before != RepositoryStateStamp.Of(RepositoryContext))
        {
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    protected override void OnBusyChanged()
    {
        LoadMoreCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private Task OnLoadMoreAsync() => LoadPageAsync(includeUncommittedRow: false);

    private async Task LoadPageAsync(bool includeUncommittedRow)
    {
        RepositoryHandle? repository = RepositoryContext.Repository;

        if (repository is null)
        {
            return;
        }

        CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            RepositoryContext.RepositoryLifetime);

        CancelInFlightLoad();
        _loadCancellation = cancellation;

        IsBusy = true;

        try
        {
            bool dirty = includeUncommittedRow
                && await IsWorkingTreeDirtyAsync(repository, cancellation.Token).ConfigureAwait(true);

            // A newer load may have taken over while git answered. Cancelling cannot take back an answer
            // git had already given, and the newer load has cleared the rows and adds its own
            // uncommitted row: this one must add nothing to its list.
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            if (dirty)
            {
                Rows.Add(CommitRowViewModel.Uncommitted(0, 0, RowCommands));
            }

            CommitLogPage page = await _reader.GetPageAsync(repository, _query, cancellation.Token)
                .ConfigureAwait(true);

            // The repository may have been swapped while the read was in flight.
            if (cancellation.IsCancellationRequested || !ReferenceEquals(repository, RepositoryContext.Repository))
            {
                return;
            }

            AppendPage(page);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository changes or a search supersedes this load.
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Reading the history of {Repository} failed", repository.WorkTreePath);

            await _infoBar.ShowAsync(bar =>
            {
                bar.Title = "The history could not be read";
                bar.Message = exception.StandardError.Trim().Length == 0 ? exception.Message : exception.StandardError.Trim();
                bar.Severity = InfoBarSeverity.Error;
            }).ConfigureAwait(true);
        }
        finally
        {
            // The page is idle only when no other load has taken over from this one: a newer load is
            // still running, and it is what the automatic refresh and the commands wait for.
            if (_loadCancellation is null || ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
                IsBusy = false;
            }

            cancellation.Dispose();
            NotifyEmptyState();
        }
    }

    private async Task<bool> IsWorkingTreeDirtyAsync(RepositoryHandle repository, CancellationToken cancellationToken)
    {
        try
        {
            return await _workingTree.IsDirtyAsync(repository, includeUntracked: true, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (GitCommandException exception)
        {
            // Not worth interrupting the history for: the row is an affordance, not the history.
            _logger.LogWarning(exception, "The working-tree state of {Repository} could not be read", repository.WorkTreePath);
            return false;
        }
    }

    private void AppendPage(CommitLogPage page)
    {
        GraphLayoutResult layout = CommitGraphLayout.Build(
            GraphCommitInput.From(page.Commits),
            _layoutCarry,
            new GraphLayoutOptions
            {
                ColourCount = GraphPalette.Size,

                // A page that git says has nothing after it is the end of the history, so a lane
                // waiting for a parent that never arrives can be closed.
                HistoryIsComplete = !page.HasMore,
            });

        _layoutCarry = layout.State;

        RefDecorationIndex decorations = RepositoryContext.Decorations;
        string headSha = RepositoryContext.Head?.Sha ?? string.Empty;
        DateTimeOffset now = DateTimeOffset.Now;

        for (int index = 0; index < page.Commits.Count; index++)
        {
            GitCommit commit = page.Commits[index];

            Rows.Add(new CommitRowViewModel(
                commit,
                layout.Rows[index],
                decorations.GetRefs(commit.Sha),
                string.Equals(commit.Sha, headSha, StringComparison.Ordinal),
                now,
                RowCommands,
                _absoluteDates));
        }

        _query = _query with { Skip = _query.Skip + page.Commits.Count };
        HasMore = page.HasMore;

        RecalculateGraphWidth();
        RecalculateRefColumnWidth();

        // The rows that just arrived have never been looked at by the search.
        MarkMatches();
    }

    /// <summary>
    /// Re-measures the graph column, which changes with the lanes in view and with the lane width.
    /// </summary>
    private void RecalculateGraphWidth()
        => GraphColumnWidth = CommitGraphCell.CalculateWidth(
            LargestLoadedLane(),
            LaneWidth,
            LanePadding,
            MaximumLanes);

    /// <summary>
    /// Re-measures the badge column, which changes with the references on the rows in view.
    /// </summary>
    private void RecalculateRefColumnWidth()
    {
        double widest = 0;

        foreach (CommitRowViewModel row in Rows)
        {
            widest = Math.Max(widest, RefBadgeMetrics.Measure(row.Refs));
        }

        RefColumnWidth = Math.Min(widest, MaximumRefColumnWidth);
    }

    private int LargestLoadedLane()
    {
        int max = 0;

        foreach (CommitRowViewModel row in Rows)
        {
            max = Math.Max(max, row.Row.MaxLane);
        }

        return max;
    }

    private void QueueReload() => _ = ReloadAsync();

    /// <summary>
    /// Marks the loaded rows the search finds, and counts them.
    /// </summary>
    /// <remarks>
    /// Over the subject and the body, case-insensitively — what git's own <c>--grep</c> searched
    /// when the box filtered the query, so the same words still find the same commits.
    /// </remarks>
    private void MarkMatches()
    {
        string search = SearchText.Trim();
        int found = 0;

        foreach (CommitRowViewModel row in Rows)
        {
            bool matches = search.Length > 0 && row.Matches(search);
            row.IsSearchMatch = matches;

            if (matches)
            {
                found++;
            }
        }

        MatchCount = found;

        OnPropertyChanged(nameof(HasSearch));
        OnPropertyChanged(nameof(MatchSummary));
    }

    private void CancelInFlightLoad()
    {
        CancellationTokenSource? previous = _loadCancellation;
        _loadCancellation = null;

        if (previous is null)
        {
            return;
        }

        try
        {
            previous.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already finished and disposed itself.
        }
    }

    /// <summary>
    /// Reads what the selected row changed into the files panel.
    /// </summary>
    /// <returns>A task that completes once the panel has been filled.</returns>
    private async Task LoadChangedFilesAsync()
    {
        CommitRowViewModel? row = SelectedRow;
        RepositoryHandle? repository = RepositoryContext.Repository;

        if (row is null || repository is null)
        {
            _diffTarget = null;
            Files.Clear();
            Diff.Clear();
            return;
        }

        DiffTarget target = row.IsUncommitted
            ? DiffTarget.Uncommitted()
            : DiffTarget.Commit(row.Sha);

        _diffTarget = target;

        try
        {
            IReadOnlyList<Core.Files.ChangedFile> files = await _diffs
                .GetChangedFilesAsync(repository, target, RepositoryContext.RepositoryLifetime)
                .ConfigureAwait(true);

            // The selection may have moved on while git was running.
            if (!ReferenceEquals(row, SelectedRow))
            {
                return;
            }

            Files.WorkTreePath = repository.WorkTreePath;
            Files.SetFiles(files);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository changes.
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Reading the files of {Commit} failed", row.Sha);
            Files.Clear();
            Diff.Clear();
        }
    }

    /// <summary>
    /// Hands the file the panel has selected to the diff viewer.
    /// </summary>
    /// <returns>A task that completes once the viewer has read it.</returns>
    private async Task ShowSelectedFileAsync()
    {
        RepositoryHandle? repository = RepositoryContext.Repository;
        Core.Files.ChangedFile? file = Files.SelectedFile;

        if (repository is null || _diffTarget is null || file is null)
        {
            Diff.Clear();
            return;
        }

        await Diff.ShowAsync(repository, _diffTarget, file);
    }

    // ---------------------------------------------------------------- the row's own menu

    private async Task OnCreateBranchHereAsync(CommitRowViewModel? row)
    {
        if (row?.Commit is null)
        {
            return;
        }

        if (await _branchOperations.CreateAsync(row.Sha, row.ShortSha).ConfigureAwait(true))
        {
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    // ---------------------------------------------------------------- a branch's own menu

    private async Task OnCheckoutBranchAsync(HistoryBranchViewModel? branch)
    {
        if (branch is null)
        {
            return;
        }

        if (await _branchOperations.CheckoutAsync(branch.Name, branch.IsRemote).ConfigureAwait(true))
        {
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task OnDeleteBranchAsync(HistoryBranchViewModel? branch)
    {
        if (branch is null)
        {
            return;
        }

        if (await _branchOperations.DeleteAsync(branch.Name, branch.IsRemote).ConfigureAwait(true))
        {
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task OnPullBranchAsync(HistoryBranchViewModel? branch)
    {
        if (branch is { CanSynchronise: true } && await _syncOperations.PullBranchAsync(branch.Name).ConfigureAwait(true))
        {
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task OnPushBranchAsync(HistoryBranchViewModel? branch)
    {
        // A push moves the remote-tracking branch, which the graph draws too.
        if (branch is { CanSynchronise: true } && await _syncOperations.PushBranchAsync(branch.Name).ConfigureAwait(true))
        {
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    private void OnSetMergeSource(HistoryBranchViewModel? branch)
    {
        if (branch is not null)
        {
            MergeSource = new MergeSource(branch.Name, branch.IsRemote);
        }
    }

    /// <summary>
    /// Merges the merge source into a branch, through the same flow as dropping one branch on another:
    /// the branch is checked out first when it is not the current one.
    /// </summary>
    private async Task OnMergeIntoAsync(HistoryBranchViewModel? branch, Core.Merging.FastForwardMode fastForward)
    {
        if (branch?.MergeRequest is not { } request || !BranchDropOperations.CanDrop(request))
        {
            return;
        }

        if (await _dropOperations.DropAsync(request, fastForward).ConfigureAwait(true))
        {
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Merges a branch into the one checked out — the question the row's menu used to be able to ask of
    /// only one branch per line.
    /// </summary>
    private async Task OnMergeIntoCurrentAsync(HistoryBranchViewModel? branch)
    {
        if (branch is null || !branch.CanMergeIntoCurrent || BranchCommands.CurrentBranch() is not { } current)
        {
            return;
        }

        BranchDropRequest request = new(branch.Name, branch.IsRemote, current, false, true);

        if (await _dropOperations.DropAsync(request).ConfigureAwait(true))
        {
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Forgets the merge source once its branch is no longer in the repository — deleted, renamed, or
    /// pruned away by a fetch.
    /// </summary>
    private void ForgetAMergeSourceThatIsGone()
    {
        if (MergeSource is not { } source)
        {
            NotifyBranchCommands();
            return;
        }

        RefCollection refs = RepositoryContext.Refs;
        bool exists = false;

        foreach (GitBranch branch in source.IsRemote ? refs.RemoteBranches : refs.LocalBranches)
        {
            if (string.Equals(branch.ShortName, source.Name, StringComparison.Ordinal))
            {
                exists = true;
                break;
            }
        }

        if (!exists)
        {
            MergeSource = null;
        }

        NotifyBranchCommands();
    }

    /// <summary>
    /// Re-evaluates what every branch command allows: the merge source and HEAD both decide it.
    /// </summary>
    private void NotifyBranchCommands()
    {
        BranchCommands.Checkout.NotifyCanExecuteChanged();
        BranchCommands.SetAsMergeSource.NotifyCanExecuteChanged();
        BranchCommands.MergeInto.NotifyCanExecuteChanged();
        BranchCommands.FastForwardInto.NotifyCanExecuteChanged();
        BranchCommands.MergeIntoCurrent.NotifyCanExecuteChanged();
        BranchCommands.Delete.NotifyCanExecuteChanged();
        BranchCommands.Pull.NotifyCanExecuteChanged();
        BranchCommands.Push.NotifyCanExecuteChanged();
    }

    private static bool HasCommit(CommitRowViewModel? row) => row?.Commit is not null;

    /// <summary>
    /// Shows what a row changed, selecting it first when it is not the selected one.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <remarks>
    /// The one way into the dialog, shared by the row's menu and by a double-click: a selection
    /// change no longer opens anything, so both gestures ask for it here.
    /// </remarks>
    private void OnShowChanges(CommitRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (!ReferenceEquals(row, SelectedRow))
        {
            SelectedRow = row;
        }

        IsDiffViewOpen = true;
    }

    private async Task OnCheckoutCommitAsync(CommitRowViewModel? row)
    {
        if (row?.Commit is null)
        {
            return;
        }

        if (await _checkoutOperations.CheckoutAsync(row.Sha, row.ShortSha, detach: true).ConfigureAwait(true))
        {
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task OnCreateTagHereAsync(CommitRowViewModel? row)
    {
        if (row?.Commit is null)
        {
            return;
        }

        if (await _tagOperations.CreateAsync(row.Sha, row.ShortSha).ConfigureAwait(true))
        {
            await ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Opens a row's commit on whichever host the repository's remote points at.
    /// </summary>
    private async Task OnOpenOnHostAsync(CommitRowViewModel? row)
    {
        if (row?.Commit is { } commit)
        {
            await _links.OpenCommitAsync(commit.Sha).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// What a double-click on a row does: show what it changed.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <remarks>
    /// It used to check the row out, which is now the row menu's job alone — a gesture that moves
    /// HEAD is not one to arrive at by clicking twice. The uncommitted pseudo-row keeps its own
    /// meaning: there is nothing there to compare against a parent, and the page that acts on that
    /// work is the working directory.
    /// </remarks>
    private void OnActivate(CommitRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.IsUncommitted)
        {
            WorkingDirectoryRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        OnShowChanges(row);
    }

    private void NotifySelectedCommitDetails()
    {
        OnPropertyChanged(nameof(SelectedSubject));
        OnPropertyChanged(nameof(SelectedSha));
        OnPropertyChanged(nameof(SelectedAuthor));
        OnPropertyChanged(nameof(SelectedDate));
        OnPropertyChanged(nameof(SelectedBody));
        OnPropertyChanged(nameof(HasSelectedBody));
        OnPropertyChanged(nameof(HasSelectedCommit));
    }

    private void NotifyEmptyState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    /// <summary>
    /// Takes the stored history and graph preferences on, at startup and whenever they change.
    /// </summary>
    /// <param name="settings">The preferences.</param>
    private void ApplySettings(AppSettings settings)
    {
        LaneWidth = settings.GraphLaneWidth;
        RowHeight = settings.GraphRowHeight;

        bool absolute = settings.DateDisplay == DateDisplay.Absolute;
        bool rebuild = absolute != _absoluteDates || PageSize != settings.HistoryPageSize;

        _absoluteDates = absolute;
        PageSize = settings.HistoryPageSize;

        if (FirstParentOnly != settings.FirstParentOnly)
        {
            // Its own setter re-reads the history, so this branch must not do it twice.
            FirstParentOnly = settings.FirstParentOnly;
        }
        else if (rebuild && Rows.Count > 0)
        {
            _ = ReloadAsync();
        }
    }
}
