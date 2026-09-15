using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Controls.Graph;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels.Panels;
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
    /// <summary>
    /// How long to wait after the last keystroke before searching. Long enough not to run a query
    /// per character, short enough to feel immediate.
    /// </summary>
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    private readonly ICommitLogReader _reader;
    private readonly IWorkingTreeProbe _workingTree;
    private readonly IDiffService _diffs;

    private DiffTarget? _diffTarget;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<HistoryPageViewModel> _logger;

    private GraphLayoutState? _layoutCarry;
    private CommitLogQuery _query = new();
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _searchDebounce;
    private bool _hasMore;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="reader">Reads the commits.</param>
    /// <param name="workingTree">Answers whether there is anything uncommitted.</param>
    /// <param name="diffs">Reads what the selected commit touched.</param>
    /// <param name="infoBar">Reports a failure the user can act on.</param>
    /// <param name="logger">Receives the detail behind a reported failure.</param>
    public HistoryPageViewModel(
        IRepositoryContext repositoryContext,
        ICommitLogReader reader,
        IWorkingTreeProbe workingTree,
        IDiffService diffs,
        ISystemInterop interop,
        DiffViewerViewModel diff,
        IInfoBarService infoBar,
        ILogger<HistoryPageViewModel> logger)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(workingTree);
        ArgumentNullException.ThrowIfNull(diffs);
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _reader = reader;
        _workingTree = workingTree;
        _diffs = diffs;

        Diff = diff;
        Files = new ChangedFilesPanelViewModel(interop);

        // The viewer follows the panel rather than the panel driving it: the panel's job ends at
        // "this file is selected", whoever is listening.
        Files.SelectionChanged += (_, _) => _ = ShowSelectedFileAsync();
        _infoBar = infoBar;
        _logger = logger;

        LoadMoreCommand = new AsyncRelayCommand(OnLoadMoreAsync, () => HasMore && IsNotBusy);
        RefreshCommand = new AsyncRelayCommand(ReloadAsync, () => IsRepositoryOpen && IsNotBusy);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => SearchText.Length > 0);

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
    /// Gets or sets what to search commit messages for.
    /// </summary>
    /// <remarks>
    /// The search re-queries git rather than filtering the rows already loaded: filtering in memory
    /// would search only the current page and quietly miss everything older, which is worse than not
    /// searching at all.
    /// </remarks>
    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ClearSearchCommand.NotifyCanExecuteChanged();
                _query = _query with { MessageFilter = value.Trim().Length == 0 ? null : value.Trim(), Skip = 0 };
                QueueDebouncedReload();
            }
        }
    } = string.Empty;

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
                _ = LoadChangedFilesAsync();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a row is selected.
    /// </summary>
    public bool HasSelection => SelectedRow is not null;

    /// <summary>
    /// Gets the panel listing what the selected commit touched.
    /// </summary>
    public ChangedFilesPanelViewModel Files { get; }

    /// <summary>
    /// Gets the diff viewer showing the file selected in <see cref="Files"/>.
    /// </summary>
    public DiffViewerViewModel Diff { get; }

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
        private set => SetProperty(ref field, value);
    } = CommitGraphCell.CalculateWidth(0, LaneWidth, LanePadding, MaximumLanes);

    /// <summary>Gets the distance between two graph lanes.</summary>
    public static double LaneWidth => 16;

    /// <summary>Gets the padding on each side of the graph column.</summary>
    public static double LanePadding => 8;

    /// <summary>Gets how many lanes the graph column may grow to.</summary>
    public static int MaximumLanes => 14;

    /// <summary>Gets the command that loads the next page.</summary>
    public AsyncRelayCommand LoadMoreCommand { get; }

    /// <summary>Gets the command that re-reads the history from scratch.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Gets the command that clears the search box.</summary>
    public RelayCommand ClearSearchCommand { get; }

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

        NotifyEmptyState();

        if (!IsRepositoryOpen)
        {
            return;
        }

        await LoadPageAsync(includeUncommittedRow: true).ConfigureAwait(true);
    }

    /// <inheritdoc />
    protected override void OnRepositoryChanged()
    {
        base.OnRepositoryChanged();

        RefreshCommand.NotifyCanExecuteChanged();
        _ = ReloadAsync();
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
            if (includeUncommittedRow && await IsWorkingTreeDirtyAsync(repository, cancellation.Token).ConfigureAwait(true))
            {
                Rows.Add(CommitRowViewModel.Uncommitted(0, 0));
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
            IsBusy = false;

            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
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
                now));
        }

        _query = _query with { Skip = _query.Skip + page.Commits.Count };
        HasMore = page.HasMore;

        GraphColumnWidth = CommitGraphCell.CalculateWidth(
            Math.Max(layout.MaxLane, LargestLoadedLane()),
            LaneWidth,
            LanePadding,
            MaximumLanes);
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

    private void QueueDebouncedReload()
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();

        CancellationTokenSource debounce = new();
        _searchDebounce = debounce;

        _ = DebounceAsync(debounce);
    }

    private async Task DebounceAsync(CancellationTokenSource debounce)
    {
        try
        {
            await Task.Delay(SearchDebounce, debounce.Token).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later keystroke.
        }
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
}
