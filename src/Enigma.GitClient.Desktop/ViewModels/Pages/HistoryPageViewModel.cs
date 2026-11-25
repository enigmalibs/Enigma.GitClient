using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Graph;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Reset;
using Enigma.GitClient.Core.Squash;
using Enigma.GitClient.Core.Stashes;
using Enigma.GitClient.Core.Status;
using Enigma.GitClient.Desktop.Controls;
using Enigma.GitClient.Desktop.Controls.Graph;
using Enigma.GitClient.Desktop.Formatting;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// The commit graph: pages commits in, lays them out, and keeps the selection in step with the rest
/// of the shell.
/// </summary>
/// <remarks>
/// The graph is always the whole repository — every branch, tag and remote-tracking ref, and HEAD —
/// which is the query's own default scope, with every parent of every merge followed: there is no
/// switch to narrow it to the current branch or to first parents.
/// </remarks>
public sealed class HistoryPageViewModel : PageViewModelBase, IHistoryRowHost, IDisposable
{
    private readonly ICommitLogReader _reader;
    private readonly IWorkingTreeProbe _workingTree;
    private readonly IDiffService _diffs;
    private readonly IRemoteTags _remoteTags;
    private readonly IHostLinkService _links;
    private readonly ISettingsService _settings;
    private readonly IToolDialogService _tools;
    private readonly IHiddenBranches _hidden;
    private readonly IStashService _stashes;
    private readonly ISystemInterop _interop;
    private bool _absoluteDates;

    // What the panel's files are compared against, and the row whose files they are — set together,
    // once the files have arrived. Until they do, the panel still lists the previous row's, none of
    // which may be shown against the new commit.
    private DiffTarget? _diffTarget;
    private CommitRowViewModel? _filesRow;

    // Set while an in-place reload replaces the rows: the list lets go of the selection while it is
    // emptied, and the reload puts it back on the same line straight after.
    private bool _keepingPlace;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<HistoryPageViewModel> _logger;

    private GraphLayoutState? _layoutCarry;
    private CommitLogQuery _query = new();
    private CancellationTokenSource? _loadCancellation;
    private bool _hasMore;
    private bool _refreshPending;

    // Set while the working tree is being read after a change on disk, and when another change asks
    // for a reading in the meantime: that one runs once, after.
    private bool _readingWorkingTree;
    private bool _workingTreeAgain;

    // Whether there is uncommitted work, as the refresh that decided to reload has just read it: the
    // reload's first page takes it instead of asking git a second time.
    private bool? _uncommittedJustRead;

    // Where HEAD and every reference were when the rows were last read: the badges and the lanes on
    // screen are drawn from that, so a context that has read anything else since has something new.
    private RepositoryStateStamp? _drawnStamp;

    // Whether rows were drawn, or the read in flight was walked, before the context had read the
    // repository's references at all — which an opening does, as the two reads race. See
    // CatchUpWithTheFirstState.
    private bool _drawnWithoutState;
    private bool _walkedWithoutState;

    // The stash as the last reload read it, and the commits git records its entries with, which the
    // history draws as one line per entry.
    private readonly StashFolding _stash = new();

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="reader">Reads the commits.</param>
    /// <param name="workingTree">Answers whether there is anything uncommitted.</param>
    /// <param name="diffs">Reads what the selected commit touched.</param>
    /// <param name="infoBar">Reports a failure the user can act on.</param>
    /// <param name="commitActions">What a line's own menu does with its commit.</param>
    /// <param name="stashActions">Stashes, applies, pops and drops, and throws the uncommitted work away.</param>
    /// <param name="branchActions">What a branch badge's menu does, and the drop of one badge on another.</param>
    /// <param name="tagActions">Creates a tag on a line; pushes and deletes one from its badge.</param>
    /// <param name="tools">Opens the branches, tags and remotes over the history.</param>
    /// <param name="hidden">The branches left out of the graph, and their badges with them.</param>
    /// <param name="remoteTags">Which tags the remote has, which the tag badges' clouds show.</param>
    /// <param name="workingTreePanel">What the details panel holds for the uncommitted line.</param>
    /// <param name="diffViewers">
    /// Builds the page's own diff viewer: one per consumer, owned and disposed by this page, so it is
    /// asked for through a factory rather than captured by this singleton's constructor.
    /// </param>
    /// <param name="logger">Receives the detail behind a reported failure.</param>
    public HistoryPageViewModel(
        IRepositoryContext repositoryContext,
        ICommitLogReader reader,
        IWorkingTreeProbe workingTree,
        IDiffService diffs,
        ISystemInterop interop,
        HistoryCommitActions commitActions,
        HistoryStashActions stashActions,
        HistoryBranchActions branchActions,
        HistoryTagActions tagActions,
        IHostLinkService links,
        ISettingsService settings,
        Func<DiffViewerViewModel> diffViewers,
        IInfoBarService infoBar,
        IToolDialogService tools,
        IHiddenBranches hidden,
        IStashService stashes,
        IRemoteTags remoteTags,
        WorkingTreePanelViewModel workingTreePanel,
        ILogger<HistoryPageViewModel> logger)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(workingTree);
        ArgumentNullException.ThrowIfNull(diffs);
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentNullException.ThrowIfNull(commitActions);
        ArgumentNullException.ThrowIfNull(stashActions);
        ArgumentNullException.ThrowIfNull(branchActions);
        ArgumentNullException.ThrowIfNull(tagActions);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(diffViewers);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(stashes);
        ArgumentNullException.ThrowIfNull(remoteTags);
        ArgumentNullException.ThrowIfNull(workingTreePanel);
        ArgumentNullException.ThrowIfNull(logger);

        _reader = reader;
        _workingTree = workingTree;
        _diffs = diffs;

        Diff = diffViewers();
        _links = links;
        _settings = settings;
        _tools = tools;
        _hidden = hidden;
        _hidden.Changed += (_, _) => OnHiddenBranchesChanged();
        _stashes = stashes;
        _interop = interop;

        // The remote answers after the lines are drawn — and again after every fetch: the tags'
        // clouds follow it in place, without redrawing the history.
        _remoteTags = remoteTags;
        _remoteTags.Changed += (_, _) => OnRemoteTagsChanged();

        WorkingTree = workingTreePanel;
        WorkingTree.SelectionChanged += (_, _) => OnWorkingTreeSelectionChanged();

        // As for a commit's files: letting go of a working-tree file puts its diff away.
        WorkingTree.SelectionReleased += (_, _) => IsDiffViewOpen = false;
        WorkingTree.Changed += (_, e) => OnWorkingTreeChanged(e);

        // Everything a line or a badge copies — a hash, a branch's name, a tag's — is plain text.
        CopyCommand = new AsyncRelayCommand<string>(
            text => text is { Length: > 0 } ? _interop.CopyTextAsync(text) : Task.CompletedTask,
            text => !string.IsNullOrEmpty(text));

        ApplySettings(settings.Current);
        settings.Changed += (_, e) => ApplySettings(e.Settings);

        ClearMergeSourceCommand = new RelayCommand(() => MergeSource = null, () => MergeSource is not null);

        BranchCommands = branchActions.CreateCommands(
            this,
            ClearMergeSourceCommand,
            () => RepositoryContext.Head is { IsDetached: false } head ? head.BranchName : null,
            CopyCommand,
            RepositoryContext.WorktreeOf);

        ClearSquashBaseCommand = new RelayCommand(() => SquashBase = null, () => SquashBase is not null);

        HistoryCommitCommands commits = commitActions.CreateCommands(this, ClearSquashBaseCommand);
        HistoryStashActionCommands stash = stashActions.CreateCommands(this, () => HasUncommittedChanges);
        HistoryTagCommands tags = tagActions.CreateCommands(this);

        SquashCommands = commits.Squash;

        RowCommands = new HistoryRowCommands(
            commits.CreateBranchHere,
            commits.Checkout,
            tags.CreateHere,
            new RelayCommand<CommitRowViewModel>(OnShowChanges, row => row is not null),
            new AsyncRelayCommand<CommitRowViewModel>(OnOpenOnHostAsync, HasCommit),
            () => _links.HostName,
            BranchCommands,
            commits.ResetSoft,
            commits.ResetHard,
            () => RepositoryContext.Head is { IsDetached: false, IsUnborn: false } head ? head.BranchName : null,
            stash.Lines,
            CopyCommand,
            ShowDetails: commits.ShowDetails,
            DiscardUncommitted: stash.DiscardUncommitted,
            PushTag: tags.Push,
            DeleteTag: tags.Delete,
            DeleteRemoteTag: tags.DeleteRemote,
            Revert: commits.Revert,
            CreateWorktreeHere: commits.CreateWorktreeHere,
            Squash: SquashCommands);

        StashCommand = stash.Stash;

        // "Merge" records a merge commit even when a fast-forward would do; "fast-forward only" beside
        // it is how a branch is moved without one — the two items of the drop menu.
        MergeDropCommand = branchActions.CreateDropCommand(this, Core.Merging.FastForwardMode.Never);
        FastForwardDropCommand = branchActions.CreateDropCommand(this, Core.Merging.FastForwardMode.Only);

        Files = new ChangedFilesPanelViewModel(interop, settings)
        {
            // The file is opened at the commit that is selected, which is the only reference that
            // makes sense for a file the user is looking at in history.
            OpenOnHost = file => _links.OpenFileAsync(SelectedRow?.Sha ?? string.Empty, file.Path),
        };

        // The viewer follows the panel rather than the panel driving it: the panel's job ends at
        // "this file is selected", whoever is listening.
        Files.SelectionChanged += (_, _) => OnFileSelectionChanged();

        // A click that lets go of the file puts its diff away, as closing the diff lets go of the
        // file: the graph comes back, and the line and its panel stay.
        Files.SelectionReleased += (_, _) => IsDiffViewOpen = false;
        _infoBar = infoBar;
        _logger = logger;

        CloseDiffViewCommand = new RelayCommand(() => IsDiffViewOpen = false);
        CloseDetailsPanelCommand = new RelayCommand(() => SelectedRow = null);
        ShowCommitDetailsCommand = new AsyncRelayCommand(() => commits.ShowDetails.ExecuteAsync(DetailsRow), () => DetailsRow?.Commit is not null);
        LoadMoreCommand = new AsyncRelayCommand(OnLoadMoreAsync, () => HasMore && IsNotBusy);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => SearchText.Length > 0);
        NextMatchCommand = new RelayCommand(() => GoToMatch(forward: true), () => MatchCount > 0);
        PreviousMatchCommand = new RelayCommand(() => GoToMatch(forward: false), () => MatchCount > 0);

        OpenBranchesCommand = new AsyncRelayCommand(() => OpenToolAsync(ToolDialog.Branches), () => IsRepositoryOpen);
        OpenWorktreesCommand = new AsyncRelayCommand(() => OpenToolAsync(ToolDialog.Worktrees), () => IsRepositoryOpen);
        OpenSubmodulesCommand = new AsyncRelayCommand(() => OpenToolAsync(ToolDialog.Submodules), () => IsRepositoryOpen);
        OpenTagsCommand = new AsyncRelayCommand(() => OpenToolAsync(ToolDialog.Tags), () => IsRepositoryOpen);
        OpenRemotesCommand = new AsyncRelayCommand(() => OpenToolAsync(ToolDialog.Remotes), () => IsRepositoryOpen);
        ShowHiddenBranchesCommand = new RelayCommand(_hidden.ShowAll, () => HiddenBranchCount > 0);
    }

    /// <summary>
    /// Gets the rows the list shows, newest first.
    /// </summary>
    public RangeObservableCollection<CommitRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Gets or sets what to look for in the commits: their messages, their SHAs and their authors
    /// (<see cref="CommitRowViewModel.Matches"/>).
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
    /// Gets or sets a value indicating whether the search bar is shown, under the command bar. It is
    /// not when the page opens: the search is reached by the toggle at the end of the commands, or by
    /// Ctrl+F.
    /// </summary>
    /// <remarks>
    /// Hiding it clears the search: highlighted lines with no search in sight, and no box to clear it
    /// from, would be a state the reader cannot see the cause of.
    /// </remarks>
    public bool IsSearchBarOpen
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && !value)
            {
                SearchText = string.Empty;
            }
        }
    }

    /// <summary>
    /// Gets how many of the loaded rows match the search.
    /// </summary>
    public int MatchCount { get; private set => SetProperty(ref field, value); }

    /// <summary>
    /// Gets a value indicating whether anything is being searched for.
    /// </summary>
    public bool HasSearch => SearchText.Trim().Length > 0;

    /// <summary>
    /// Gets what the search bar says beside its box, empty while nothing is searched for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without it, a search that found nothing and a search that found everything look the same:
    /// the list is the whole list either way.
    /// </para>
    /// <para>
    /// <c>match 3/12</c> is the selected line's place among the lines found; <c>match –/12</c> says
    /// the selected line is not one of them — nothing is selected yet, or the reader clicked
    /// elsewhere — and the arrows beside it are how to get to one (<see cref="NextMatchCommand"/>).
    /// </para>
    /// </remarks>
    public string MatchSummary => HistorySearch.Summarise(HasSearch, MatchCount, MatchPosition);

    /// <summary>
    /// Gets the selected line's place among the lines the search found, counted from 1 down the list;
    /// 0 when the selected line is not one of them.
    /// </summary>
    public int MatchPosition => HistorySearch.PositionOf(Rows, SelectedRow);

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
                NotifyMatchPosition();

                // Selecting a line opens the details panel and nothing more: the diffs are asked
                // for, by a file of the panel or by the row's menu. Losing the selection — what a
                // reload after a checkout does — still puts them away, because a page describing a
                // commit nobody has selected is describing nothing.
                if (value is null)
                {
                    IsDiffViewOpen = false;
                }

                // An in-place reload lets go of the line only to select it again once the rows are
                // back: the panel waits for it, rather than closing and emptying under the reader
                // at every automatic refresh.
                if (_keepingPlace && value is null)
                {
                    return;
                }

                DetailsRow = value;
                LoadChangedFilesAsync().Forget();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a row is selected.
    /// </summary>
    public bool HasSelection => SelectedRow is not null;

    /// <summary>
    /// Gets the line the details panel describes: the selected one, and still that one while an
    /// in-place reload is putting the selection back on it.
    /// </summary>
    public CommitRowViewModel? DetailsRow
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsDetailsPanelOpen));
                OnPropertyChanged(nameof(HasDetailsCommit));
                OnPropertyChanged(nameof(IsWorkingTreeShown));
                ShowCommitDetailsCommand.NotifyCanExecuteChanged();

                // The working tree reads itself only while it is the panel's: a panel nobody sees
                // has no reason to run git status at every refresh.
                WorkingTree.IsActive = IsWorkingTreeShown;

                if (value is { IsUncommitted: true } row)
                {
                    ShowWorkingTreeAsync(row).Forget();
                }
                else
                {
                    // A file left picked would be picked again by the next reading, and open its
                    // diff the moment the uncommitted line is selected.
                    WorkingTree.ClearSelection();
                }
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the details panel is on screen, which it is for as long as a
    /// line is selected: the lines are its toggles.
    /// </summary>
    public bool IsDetailsPanelOpen => DetailsRow is not null;

    /// <summary>
    /// Gets a value indicating whether the panel describes a commit there are details of, as opposed
    /// to the uncommitted line.
    /// </summary>
    public bool HasDetailsCommit => DetailsRow?.Commit is not null;

    /// <summary>
    /// Gets a value indicating whether the panel holds the working tree — what the uncommitted line
    /// stands for — rather than a commit's files.
    /// </summary>
    public bool IsWorkingTreeShown => DetailsRow?.IsUncommitted == true;

    /// <summary>
    /// Gets what the details panel holds for the uncommitted line: what is not staged, what is, and
    /// the commit form.
    /// </summary>
    public WorkingTreePanelViewModel WorkingTree { get; }

    /// <summary>
    /// Gets or sets how wide the details panel is.
    /// </summary>
    /// <remarks>
    /// The reader's, from the grip on the panel's edge, and kept for as long as the window is open;
    /// the list beside it is what gives way, and in the list the message column, whose width is
    /// whatever the other columns leave it.
    /// </remarks>
    public double DetailsPanelWidth
    {
        get;
        set => SetProperty(ref field, Math.Clamp(value, MinimumDetailsPanelWidth, MaximumDetailsPanelWidth));
    } = DefaultDetailsPanelWidth;

    /// <summary>Gets how wide the details panel opens the first time.</summary>
    public static double DefaultDetailsPanelWidth => 380;

    /// <summary>
    /// Gets how narrow the details panel may be made: the working tree's headers, with their
    /// buttons, still fit.
    /// </summary>
    public static double MinimumDetailsPanelWidth => 280;

    /// <summary>Gets how wide the details panel may be made.</summary>
    public static double MaximumDetailsPanelWidth => 720;

    /// <summary>
    /// Moves the details panel's left edge by what the pointer moved on its grip.
    /// </summary>
    /// <param name="delta">How far the pointer moved, positive to the right.</param>
    /// <remarks>
    /// The panel is docked on the right, so the edge moving right makes it narrower.
    /// </remarks>
    public void ResizeDetailsPanel(double delta) => DetailsPanelWidth -= delta;

    /// <summary>
    /// What a plain click on a line does: the selected line lets go of the selection, which closes
    /// the details panel, and any other line takes it.
    /// </summary>
    /// <param name="row">The line clicked.</param>
    public void ToggleSelection(CommitRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        SelectedRow = ReferenceEquals(row, SelectedRow) ? null : row;
    }

    /// <summary>Gets the command that closes the details panel, letting go of the selected line.</summary>
    public RelayCommand CloseDetailsPanelCommand { get; }

    /// <summary>
    /// Gets or sets a value indicating whether a file's diff is drawn over the graph.
    /// </summary>
    /// <remarks>
    /// The page's own statement: the view shows the diff over the graph while this is set — the
    /// details panel stays beside it, and is how the reader goes from one file to the next — and
    /// every way out — the back button, Escape — clears it. Closing deliberately leaves
    /// <see cref="SelectedRow"/> alone, because the selection is also what "create a branch here"
    /// starts from, what the row highlight shows and what keeps the panel open. It opens when a file
    /// of the panel is picked, or from the row's menu.
    /// <para>
    /// From the menu it opens on the commit's first file — never on the file that was selected
    /// before, in another commit or the last time this one was open. When the files are still being
    /// read, the first one is selected as they arrive. Closing lets go of the file, so that picking
    /// the same one again opens it again.
    /// </para>
    /// </remarks>
    public bool IsDiffViewOpen
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            if (value)
            {
                if (!HasSelectedFile)
                {
                    SelectFirstFile();
                }
            }
            else
            {
                Files.SelectedNode = null;
                WorkingTree.ClearSelection();
            }

            if (!value && _refreshPending)
            {
                // What the automatic refresh found while the diffs had the page, drawn now that the
                // graph is back.
                _refreshPending = false;
                ReloadKeepingPlaceAsync().Forget();
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
    /// Lets go of the diff viewer and the changed-files list the page built, which follow the settings
    /// until they are disposed.
    /// </summary>
    public void Dispose()
    {
        Diff.Dispose();
        Files.Dispose();
    }

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
    /// Gets the squash's commands, which every line's menu offers.
    /// </summary>
    public HistorySquashCommands SquashCommands { get; }

    /// <summary>
    /// Gets or sets the commit the next squash starts after, or <see langword="null"/> when none has
    /// been chosen.
    /// </summary>
    /// <remarks>
    /// Like the merge source, a choice that outlives the menu it was made in: the toolbar shows it and
    /// its line carries a flag. It serves one squash — a squash that created something forgets it —
    /// and is forgotten too when another repository opens or a reload no longer lists its commit. A
    /// refresh, a fetch or a branch switch keeps it: a commit does not stop existing when HEAD moves.
    /// </remarks>
    public SquashBase? SquashBase
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasSquashBase));
                OnPropertyChanged(nameof(SquashBaseSummary));
                ClearSquashBaseCommand.NotifyCanExecuteChanged();
                SquashCommands.Squash.NotifyCanExecuteChanged();

                foreach (CommitRowViewModel row in Rows)
                {
                    row.NotifySquashBaseChanged();
                }
            }
        }
    }

    /// <summary>Gets a value indicating whether a squash base is chosen.</summary>
    public bool HasSquashBase => SquashBase is not null;

    /// <summary>Gets what the toolbar says about the squash base.</summary>
    public string SquashBaseSummary => SquashBase is { } squashBase ? $"Squash base: {squashBase.ShortSha}" : string.Empty;

    /// <summary>Gets the command that forgets the squash base.</summary>
    public RelayCommand ClearSquashBaseCommand { get; }

    /// <summary>Gets the selected commit's subject, or the pseudo-row's label.</summary>
    public string SelectedSubject => SelectedRow?.Subject ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether the selection is a commit there are details of, as opposed to
    /// the uncommitted-changes row or nothing at all.
    /// </summary>
    public bool HasSelectedCommit => SelectedRow?.Commit is not null;

    /// <summary>
    /// Gets the command that shows the selected commit's details — its title, description, author,
    /// date and hash — from the details panel's header and from the diff view's.
    /// </summary>
    /// <remarks>
    /// The headers carry the subject alone, on one line: everything else about the commit is here,
    /// one click away, as text to select and copy.
    /// </remarks>
    public AsyncRelayCommand ShowCommitDetailsCommand { get; }

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
    /// Gets a value indicating whether the history shows uncommitted changes — the line at its top —
    /// which is what there is to stash.
    /// </summary>
    public bool HasUncommittedChanges => Rows.Count > 0 && Rows[0].IsUncommitted;

    /// <inheritdoc />
    IReadOnlyList<CommitRowViewModel> IHistoryRowHost.Rows => Rows;

    /// <summary>
    /// Gets the command that copies a hash or a name to the clipboard, which the line's and the
    /// badges' menus run with the text as their parameter.
    /// </summary>
    public AsyncRelayCommand<string> CopyCommand { get; }

    /// <summary>
    /// Gets the command that puts every uncommitted change on the stash, from the toolbar.
    /// </summary>
    public AsyncRelayCommand StashCommand { get; }

    /// <summary>
    /// Gets the command a branch badge dropped onto another runs to merge the dragged branch into the
    /// one it was dropped on, always recording a merge commit.
    /// </summary>
    public AsyncRelayCommand<HistoryBranchDrop> MergeDropCommand { get; }

    /// <summary>
    /// Gets the command a branch badge dropped onto another runs to move the branch it was dropped on
    /// forward to the dragged one, refusing when the two have diverged.
    /// </summary>
    public AsyncRelayCommand<HistoryBranchDrop> FastForwardDropCommand { get; }

    /// <summary>
    /// Gets a value indicating whether the history is still reading something it will draw: its
    /// commits, or — just after a repository was opened — the references its badges come from.
    /// </summary>
    /// <remarks>
    /// A context with no <see cref="IRepositoryContext.Head"/> has not read the repository yet: every
    /// read of it, an unborn repository's included, sets one.
    /// </remarks>
    public bool IsLoading => IsBusy || (IsRepositoryOpen && RepositoryContext.Head is null);

    /// <summary>
    /// Gets the sentence shown when the list is empty, which depends on why it is empty.
    /// </summary>
    public string EmptyMessage
        => !IsRepositoryOpen
            ? "Open a repository to see its history, with the graph, the authors, the timestamps and the short hashes."
            : _query.IsFiltered
                ? "No commit matches the current filter."
                : "This repository has no commits yet. Change a file, then commit it from the uncommitted line.";

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
                // Offered, not imposed, as the badge column's is: once the reader has dragged the
                // graph's grip, the width is theirs.
                Columns.SeedGraphWidth(value);
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

    /// <summary>Gets the command that clears the search box.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>
    /// Gets the command that selects the next line the search found, down the list from the selected
    /// one — the first one when nothing is selected — and back to the top past the last.
    /// </summary>
    /// <remarks>
    /// Selecting is going there: the list brings the line into view and the details panel follows,
    /// as a click on it would. Only the loaded lines are searched, so it wraps around within them;
    /// loading more is the reader's call.
    /// </remarks>
    public RelayCommand NextMatchCommand { get; }

    /// <summary>
    /// Gets the command that selects the previous line the search found, up the list from the selected
    /// one — the last one when nothing is selected — and round to the bottom past the first.
    /// </summary>
    public RelayCommand PreviousMatchCommand { get; }

    /// <summary>Gets the command that puts the diffs away and brings the graph back.</summary>
    public RelayCommand CloseDiffViewCommand { get; }

    /// <summary>Gets the command that opens the branches over the history.</summary>
    public AsyncRelayCommand OpenBranchesCommand { get; }

    /// <summary>Gets the command that opens the worktrees over the history.</summary>
    public AsyncRelayCommand OpenWorktreesCommand { get; }

    /// <summary>Gets the command that opens the submodules over the history.</summary>
    public AsyncRelayCommand OpenSubmodulesCommand { get; }

    /// <summary>
    /// Gets a value indicating whether the repository declares submodules, which is when the toolbar
    /// offers the Submodules dialog.
    /// </summary>
    public bool HasSubmodules => RepositoryContext.HasSubmodules;

    /// <summary>Gets the command that opens the tags over the history.</summary>
    public AsyncRelayCommand OpenTagsCommand { get; }

    /// <summary>Gets the command that opens the remotes over the history.</summary>
    public AsyncRelayCommand OpenRemotesCommand { get; }

    /// <summary>
    /// Gets how many branches the graph leaves out: those hidden from the branches dialog that still
    /// exist, less the checked-out one, which is drawn whatever it is told.
    /// </summary>
    public int HiddenBranchCount => ExcludedRefs().Count;

    /// <summary>Gets a value indicating whether the graph leaves any branch out.</summary>
    public bool HasHiddenBranches => HiddenBranchCount > 0;

    /// <summary>
    /// Gets what the toolbar says while branches are hidden: the graph is not the whole repository,
    /// and a reader who forgot hiding them has to be told so.
    /// </summary>
    public string HiddenBranchesSummary
        => HiddenBranchCount switch
        {
            0 => string.Empty,
            1 => "1 branch hidden",
            int count => $"{count.ToString(CultureInfo.CurrentCulture)} branches hidden",
        };

    /// <summary>Gets the command that shows every hidden branch again.</summary>
    public RelayCommand ShowHiddenBranchesCommand { get; }

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
    /// Brings the history up to date after an automatic refresh, or after a fetch, pull or push — and
    /// does nothing at all when there is nothing new to draw.
    /// </summary>
    /// <param name="referencesMoved">
    /// Whether to redraw even if the references are those the rows were drawn from: HEAD or a
    /// reference moved in that refresh, or the reader asked for it.
    /// </param>
    /// <param name="workingTreeRead">
    /// Whether the working-tree panel has just read the tree, or is reading it, for the same refresh —
    /// after a re-read of the references, which it follows on its own, or after its own operation.
    /// While it is on screen its reading then answers whether there is uncommitted work, and no
    /// second <c>git status</c> runs beside it.
    /// </param>
    /// <returns>A task that completes once the history is current.</returns>
    /// <remarks>
    /// <para>
    /// Nothing new is the common case — an automatic refresh runs every few seconds — and redrawing
    /// then would throw the reader's place away for nothing. What changes what the graph draws is where
    /// HEAD and the references are, compared with where they were when the rows were read, and whether
    /// there is uncommitted work, which is the row at its top.
    /// </para>
    /// <para>
    /// Compared with the rows, not with the refresh's own start: a push re-reads the references as it
    /// ends, so by the next refresh the remote branch has already moved in the context, and only the
    /// rows still show it where it was.
    /// </para>
    /// <para>
    /// When something did change, the same number of commits is read again, the selected commit is
    /// selected again, and the view keeps its scroll offset. While the diffs have the page, the redraw
    /// waits for them to close: replacing the rows would take away the commit they describe.
    /// </para>
    /// <para>
    /// Either way the rows' ages are measured again first (<see cref="RefreshRelativeDates"/>): a
    /// refresh that redraws nothing must still turn "just now" into "2 minutes ago".
    /// </para>
    /// </remarks>
    public async Task RefreshInPlaceAsync(bool referencesMoved, bool workingTreeRead = false)
    {
        RepositoryHandle? repository = RepositoryContext.Repository;

        if (repository is null || IsBusy)
        {
            return;
        }

        RefreshRelativeDates(DateTimeOffset.Now);

        bool moved = referencesMoved || _drawnStamp != RepositoryStateStamp.Of(RepositoryContext);

        // Dropping an older stash entry moves no reference: only the stash's own list says so.
        if (!moved)
        {
            IReadOnlyList<StashEntry> stashes = await ReadStashesAsync(repository, RepositoryContext.RepositoryLifetime)
                .ConfigureAwait(true);

            moved = _stash.HasMoved(stashes);
        }

        bool dirty = await HasUncommittedWorkAsync(repository, readPanel: !workingTreeRead, RepositoryContext.RepositoryLifetime)
            .ConfigureAwait(true);
        bool showsUncommitted = Rows.Count > 0 && Rows[0].IsUncommitted;

        if (!moved && dirty == showsUncommitted)
        {
            return;
        }

        if (IsDiffViewOpen)
        {
            _refreshPending = true;
            return;
        }

        _uncommittedJustRead = dirty;
        await ReloadKeepingPlaceAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Measures every line's age again, without redrawing anything: the rows, the selection and the
    /// scroll stay exactly as they are.
    /// </summary>
    /// <param name="now">The moment to measure from.</param>
    /// <remarks>
    /// The ages are measured when the rows are drawn, and most refreshes draw nothing; this is what
    /// keeps "how long ago" true between redraws. In absolute mode it is the tooltip that moves.
    /// </remarks>
    public void RefreshRelativeDates(DateTimeOffset now)
    {
        foreach (CommitRowViewModel row in Rows)
        {
            row.RefreshRelativeDate(now);
        }
    }

    /// <summary>
    /// Brings the working tree up to date after a change on disk that moved no reference — a file
    /// saved, added or deleted outside the application — and draws the uncommitted line again when it
    /// has to appear or go.
    /// </summary>
    /// <returns>A task that completes once the working tree has been read.</returns>
    /// <remarks>
    /// <para>
    /// One <c>git status</c>: the panel's own while it is on screen, which lists the files and answers
    /// the question both; the probe otherwise. Nothing else is read — no references, no stash — since
    /// the change moved none.
    /// </para>
    /// <para>
    /// One reading at a time: a change during one makes exactly one more after it, so a build that
    /// writes for a minute never has two <c>git status</c> running at once.
    /// </para>
    /// </remarks>
    public async Task RefreshWorkingTreeAsync()
    {
        if (_readingWorkingTree)
        {
            _workingTreeAgain = true;
            return;
        }

        _readingWorkingTree = true;

        try
        {
            do
            {
                _workingTreeAgain = false;
                await ReadWorkingTreeAsync().ConfigureAwait(true);
            }
            while (_workingTreeAgain);
        }
        finally
        {
            _readingWorkingTree = false;
        }
    }

    private async Task ReadWorkingTreeAsync()
    {
        if (RepositoryContext.Repository is not { } repository)
        {
            return;
        }

        bool dirty = await HasUncommittedWorkAsync(repository, readPanel: true, RepositoryContext.RepositoryLifetime)
            .ConfigureAwait(true);

        // A load under way reads the working tree with its first page; and a line still to come, or
        // already gone, is the next reload's.
        if (IsBusy || !ReferenceEquals(repository, RepositoryContext.Repository))
        {
            return;
        }

        bool showsUncommitted = Rows.Count > 0 && Rows[0].IsUncommitted;

        if (dirty == showsUncommitted)
        {
            return;
        }

        if (IsDiffViewOpen)
        {
            _refreshPending = true;
            return;
        }

        _uncommittedJustRead = dirty;
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
        _keepingPlace = true;

        try
        {
            await ReloadAsync().ConfigureAwait(true);

            foreach (CommitRowViewModel row in Rows)
            {
                if (uncommittedSelected ? row.IsUncommitted : selectedSha is { Length: > 0 } && row.Sha == selectedSha)
                {
                    SelectedRow = row;
                    break;
                }
            }
        }
        finally
        {
            _query = _query with { Take = pageSize };
            _keepingPlace = false;
        }

        // The line the panel described is gone — its commit is no longer drawn, or the work it
        // stood for was committed or thrown away — and the panel goes with it.
        if (SelectedRow is null && DetailsRow is not null)
        {
            DetailsRow = null;
            LoadChangedFilesAsync().Forget();
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
        _drawnWithoutState = false;
        _stash.ForgetHelpers();

        NotifyEmptyState();

        // Replaced by the first page's own once it arrives; kept when the read fails, so a history that
        // cannot be read is not read again, and reported again, at every automatic refresh.
        _drawnStamp = RepositoryStateStamp.Of(RepositoryContext);

        if (!IsRepositoryOpen)
        {
            return;
        }

        await LoadPageAsync(includeUncommittedRow: true).ConfigureAwait(true);

        ForgetASquashBaseThatIsGone();
    }

    /// <inheritdoc />
    protected override void OnRepositoryStateRefreshed()
    {
        ForgetAMergeSourceThatIsGone();

        // A hidden branch deleted, or checked out, changes what the chip counts.
        NotifyHiddenBranches();

        CatchUpWithTheFirstState();
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(HasSubmodules));
    }

    /// <summary>
    /// Redraws what was read before the context knew the repository's references, now that it does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Opening a repository starts the history's read and the context's read of the references
    /// together. When the log answers first — a repository with many branches, whose tracking
    /// counts make the reference read slow — the rows are drawn with no badge at all, and nothing
    /// used to draw them again before the next automatic refresh.
    /// </para>
    /// <para>
    /// Only the first state of an opening is waited for: every later re-read follows an operation
    /// or a refresh, which redraws the history itself. A read still in flight is left alone — its
    /// rows take their badges from the context when they land — unless it was walked without knowing
    /// which branches are hidden, and some are.
    /// </para>
    /// </remarks>
    private void CatchUpWithTheFirstState()
    {
        bool drawnWithout = _drawnWithoutState;
        bool walkedWithout = _walkedWithoutState;

        _drawnWithoutState = false;
        _walkedWithoutState = false;

        if (!IsRepositoryOpen)
        {
            return;
        }

        if (IsBusy)
        {
            if (drawnWithout || (walkedWithout && ExcludedRefs().Count > 0))
            {
                ReloadAsync().Forget();
            }

            return;
        }

        if (!drawnWithout)
        {
            return;
        }

        if (IsDiffViewOpen)
        {
            _refreshPending = true;
            return;
        }

        ReloadKeepingPlaceAsync().Forget();
    }

    /// <inheritdoc />
    protected override void OnRepositoryChanged()
    {
        // A branch of the repository that was open is nothing to merge in this one, and its commits
        // are nothing to squash.
        MergeSource = null;
        SquashBase = null;

        _links.RefreshAsync().Forget();

        base.OnRepositoryChanged();

        OpenBranchesCommand.NotifyCanExecuteChanged();
        OpenWorktreesCommand.NotifyCanExecuteChanged();
        OpenSubmodulesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSubmodules));
        OpenTagsCommand.NotifyCanExecuteChanged();
        OpenRemotesCommand.NotifyCanExecuteChanged();
        NotifyHiddenBranches();
        OnPropertyChanged(nameof(IsLoading));
        ReloadAsync().Forget();
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

        string? reveal = await _tools.ShowAsync(dialog).ConfigureAwait(true);

        // The tags dialog pushes and deletes tags on the remote, and the remotes dialog changes which
        // remote there is: what the badges' clouds say is asked again.
        if (IsRepositoryOpen && dialog is ToolDialog.Tags or ToolDialog.Remotes)
        {
            _remoteTags.RefreshAsync().Forget();
        }

        if (IsRepositoryOpen && before != RepositoryStateStamp.Of(RepositoryContext))
        {
            await ReloadAsync().ConfigureAwait(true);
        }

        if (reveal is not null)
        {
            await RevealAsync(reveal).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Selects a commit's line, reading further pages of the history while it is not loaded yet — the
    /// list then brings the selected line into view.
    /// </summary>
    /// <param name="sha">The commit.</param>
    /// <returns><see langword="true"/> when the line was found and selected.</returns>
    /// <remarks>
    /// A commit the graph does not draw — only a hidden branch reaches it — is not found however far
    /// the history is read, and the reader is told so rather than left looking at the old selection.
    /// </remarks>
    public async Task<bool> RevealAsync(string sha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);

        if (!IsRepositoryOpen)
        {
            return false;
        }

        // A read still under way is waited for: its rows are the ones to look through.
        while (IsBusy)
        {
            await Task.Delay(10).ConfigureAwait(true);
        }

        while (true)
        {
            foreach (CommitRowViewModel row in Rows)
            {
                if (string.Equals(row.Sha, sha, StringComparison.Ordinal))
                {
                    IsDiffViewOpen = false;
                    SelectedRow = row;
                    return true;
                }
            }

            if (!HasMore)
            {
                break;
            }

            int loaded = Rows.Count;
            await LoadPageAsync(includeUncommittedRow: false).ConfigureAwait(true);

            // A read that failed, or was superseded, brought nothing: stop rather than ask again.
            if (Rows.Count == loaded)
            {
                break;
            }
        }

        _infoBar.Notify(
            "Not in the history",
            HiddenBranchCount > 0
                ? "The graph does not draw that commit: only branches hidden from the history reach it."
                : "The graph does not draw that commit.",
            InfoBarSeverity.Info);

        return false;
    }

    /// <inheritdoc />
    protected override void OnBusyChanged()
    {
        LoadMoreCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsLoading));
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
            // Taken whatever this page is: an answer is only ever good for the load it was read for.
            bool? justRead = _uncommittedJustRead;
            _uncommittedJustRead = null;

            bool dirty = includeUncommittedRow
                && (justRead ?? await IsWorkingTreeDirtyAsync(repository, cancellation.Token).ConfigureAwait(true));

            // The stash is read with the first page and kept for the pages after it, so every page of
            // one reading walks from the same entries.
            IReadOnlyList<StashEntry>? stashes = _query.Skip == 0
                ? await ReadStashesAsync(repository, cancellation.Token).ConfigureAwait(true)
                : null;

            // A newer load may have taken over while git answered. Cancelling cannot take back an answer
            // git had already given, and the newer load has cleared the rows and adds its own
            // uncommitted row: this one must add nothing to its list.
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            if (stashes is not null)
            {
                _stash.Use(stashes);
            }

            // The hidden branches are read as each page is asked for, not kept in the query: a page
            // is always read with the set the reader sees now, and a change reloads from the top.
            _walkedWithoutState = RepositoryContext.Head is null;

            // Every stash entry is walked from its own commit: --all reaches only the newest, through
            // refs/stash, and the older ones live in that reference's log.
            CommitLogQuery query = _query with
            {
                ExcludedRefs = [.. ExcludedRefs()],
                IncludedRevisions = [.. _stash.Entries.Select(entry => entry.Sha)],
            };

            CommitLogPage page = await _reader
                .GetPageAsync(repository, query, cancellation.Token)
                .ConfigureAwait(true);

            // The repository may have been swapped while the read was in flight.
            if (cancellation.IsCancellationRequested || !ReferenceEquals(repository, RepositoryContext.Repository))
            {
                return;
            }

            // The uncommitted line is laid out with the page, above it: its dashed line runs down the
            // lane HEAD's commit is drawn in, however far below that commit is.
            AppendPage(page, withWorkingTree: dirty);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository changes or a search supersedes this load.
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Reading the history of {Repository} failed", repository.WorkTreePath);

            _infoBar.Notify(
                "The history could not be read",
                exception.StandardError.Trim().Length == 0 ? exception.Message : exception.StandardError.Trim(),
                InfoBarSeverity.Error);
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

    /// <summary>
    /// Says whether there is uncommitted work, with one read of the working tree: the panel's while it
    /// is on screen — it reads the tree anyway, to list it — and the probe otherwise.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="readPanel">
    /// Whether to have the panel read the tree again; otherwise its latest reading is taken, finished
    /// or under way, which the caller knows to be this refresh's.
    /// </param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns><see langword="true"/> when something is uncommitted.</returns>
    private async Task<bool> HasUncommittedWorkAsync(RepositoryHandle repository, bool readPanel, CancellationToken cancellationToken)
    {
        if (!WorkingTree.IsActive)
        {
            return await IsWorkingTreeDirtyAsync(repository, cancellationToken).ConfigureAwait(true);
        }

        await (readPanel ? WorkingTree.RefreshAsync() : WorkingTree.Reading).ConfigureAwait(true);

        return !WorkingTree.IsClean;
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

    private void AppendPage(CommitLogPage page, bool withWorkingTree)
    {
        RefDecorationIndex decorations = RepositoryContext.Decorations;
        IReadOnlyList<GitCommit> commits = _stash.Fold(page.Commits, decorations);

        List<GraphCommitInput> inputs = new(commits.Count + 1);

        if (withWorkingTree)
        {
            inputs.Add(GraphCommitInput.WorkingTree(RepositoryContext.Head?.Sha));
        }

        inputs.AddRange(GraphCommitInput.From(commits));

        GraphLayoutResult layout = CommitGraphLayout.Build(
            inputs,
            _layoutCarry,
            new GraphLayoutOptions
            {
                ColourCount = GraphPalette.Size,

                // A page that git says has nothing after it is the end of the history, so a lane
                // waiting for a parent that never arrives can be closed.
                HistoryIsComplete = !page.HasMore,
            });

        _layoutCarry = layout.State;

        IReadOnlySet<string> excluded = ExcludedRefs();
        string headSha = RepositoryContext.Head?.Sha ?? string.Empty;
        DateTimeOffset now = DateTimeOffset.Now;

        // The first page's badges are taken from the context as it is now, which is therefore what the
        // rows show — even when the read began before the context had read the repository at all.
        if (_query.Skip == 0)
        {
            _drawnStamp = RepositoryStateStamp.Of(RepositoryContext);
        }

        // No state at all is not "no reference": it is a context that has not read them yet.
        _drawnWithoutState |= RepositoryContext.Head is null;

        int first = 0;
        List<CommitRowViewModel> rows = new(commits.Count + 1);

        if (withWorkingTree)
        {
            rows.Add(CommitRowViewModel.Uncommitted(layout.Rows[0], RowCommands));
            first = 1;
        }

        for (int index = 0; index < commits.Count; index++)
        {
            GitCommit commit = commits[index];

            rows.Add(new CommitRowViewModel(
                commit,
                layout.Rows[first + index],
                StashFolding.WithoutStashRef(WithoutHidden(decorations.GetRefs(commit.Sha), excluded)),
                string.Equals(commit.Sha, headSha, StringComparison.Ordinal),
                now,
                RowCommands,
                _absoluteDates,
                _stash.EntryAt(commit.Sha),
                WorktreeTipOf,
                _remoteTags.RemoteOf));
        }

        // The page in one go: one notification for the list, however many rows it brings.
        Rows.AddRange(rows);

        _query = _query with { Skip = _query.Skip + page.Commits.Count };
        HasMore = page.HasMore;

        RecalculateGraphWidth();
        RecalculateRefColumnWidth();

        // The rows that just arrived have never been looked at by the search.
        MarkMatches();
    }

    /// <summary>
    /// Reads the stash, which is never worth interrupting the history for: a stash that cannot be
    /// read is drawn as no stash.
    /// </summary>
    private async Task<IReadOnlyList<StashEntry>> ReadStashesAsync(RepositoryHandle repository, CancellationToken cancellationToken)
    {
        try
        {
            return await _stashes.ListAsync(repository, cancellationToken).ConfigureAwait(true);
        }
        catch (GitCommandException exception)
        {
            _logger.LogWarning(exception, "The stash of {Repository} could not be read", repository.WorkTreePath);
            return [];
        }
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

    /// <summary>
    /// Says where a local branch is checked out in another working tree, for its pill's tree — or
    /// <see langword="null"/> when no other working tree has it.
    /// </summary>
    private string? WorktreeTipOf(string branch)
        => RepositoryContext.WorktreeOf(branch) is { } worktree ? WorktreeText.CheckedOutTip(worktree) : null;

    /// <summary>
    /// The refs the graph leaves out: every hidden one that still exists, less the branch HEAD is on —
    /// git walks HEAD whatever it is told, so leaving that branch "out" would only lose its badge.
    /// </summary>
    private IReadOnlySet<string> ExcludedRefs()
    {
        IReadOnlySet<string> hidden = _hidden.Hidden;

        if (hidden.Count == 0)
        {
            return hidden;
        }

        string? current = RepositoryContext.Head is { IsDetached: false, BranchName: { Length: > 0 } name }
            ? GitBranch.LocalPrefix + name
            : null;

        HashSet<string> excluded = new(StringComparer.Ordinal);
        RefCollection refs = RepositoryContext.Refs;

        foreach (GitBranch branch in (IEnumerable<GitBranch>)[.. refs.LocalBranches, .. refs.RemoteBranches])
        {
            if (hidden.Contains(branch.FullName) && !string.Equals(branch.FullName, current, StringComparison.Ordinal))
            {
                excluded.Add(branch.FullName);
            }
        }

        return excluded;
    }

    /// <summary>
    /// A row's badges without those of the branches the graph leaves out — otherwise hiding a branch
    /// that is already merged would change nothing anyone could see.
    /// </summary>
    private static IReadOnlyList<GitRef> WithoutHidden(IReadOnlyList<GitRef> refs, IReadOnlySet<string> excluded)
    {
        if (excluded.Count == 0 || refs.Count == 0)
        {
            return refs;
        }

        List<GitRef> shown = new(refs.Count);

        foreach (GitRef reference in refs)
        {
            if (!(reference is GitBranch && excluded.Contains(reference.FullName)))
            {
                shown.Add(reference);
            }
        }

        return shown.Count == refs.Count ? refs : shown;
    }

    /// <summary>
    /// Redraws the graph with the branches the reader has just hidden or shown — keeping the place,
    /// and waiting, as the automatic refresh does, while the diffs have the page.
    /// </summary>
    private void OnHiddenBranchesChanged()
    {
        NotifyHiddenBranches();

        if (!IsRepositoryOpen)
        {
            return;
        }

        if (IsDiffViewOpen)
        {
            _refreshPending = true;
            return;
        }

        ReloadKeepingPlaceAsync().Forget();
    }

    private void NotifyHiddenBranches()
    {
        OnPropertyChanged(nameof(HiddenBranchCount));
        OnPropertyChanged(nameof(HasHiddenBranches));
        OnPropertyChanged(nameof(HiddenBranchesSummary));
        ShowHiddenBranchesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Marks the loaded rows the search finds, and counts them.
    /// </summary>
    /// <remarks>
    /// What a row matches on — the message, the start of the SHA, the author — is the row's own
    /// <see cref="CommitRowViewModel.Matches"/>.
    /// </remarks>
    private void MarkMatches()
    {
        MatchCount = HistorySearch.Mark(Rows, SearchText);

        OnPropertyChanged(nameof(HasSearch));
        NotifyMatchPosition();

        NextMatchCommand.NotifyCanExecuteChanged();
        PreviousMatchCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Selects the next or the previous line the search found, wrapping around the loaded lines.
    /// </summary>
    /// <param name="forward">Down the list when <see langword="true"/>, up it otherwise.</param>
    private void GoToMatch(bool forward)
    {
        if (MatchCount > 0 && HistorySearch.Next(Rows, SelectedRow, forward) is { } next)
        {
            SelectedRow = next;
        }
    }

    private void NotifyMatchPosition()
    {
        OnPropertyChanged(nameof(MatchPosition));
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
    /// <remarks>
    /// The uncommitted line has no files of its own here: the panel shows the working tree for it,
    /// which reads itself.
    /// </remarks>
    private async Task LoadChangedFilesAsync()
    {
        CommitRowViewModel? row = SelectedRow;
        RepositoryHandle? repository = RepositoryContext.Repository;

        if (row is null || repository is null || row.IsUncommitted)
        {
            _diffTarget = null;
            _filesRow = row;
            Files.Clear();

            if (row is null)
            {
                Diff.Clear();
            }

            return;
        }

        // A stash line's commit holds only its tracked changes; the untracked files it took are
        // elsewhere, and the stash target reads them too (BUG-1F3A).
        DiffTarget target = row.IsStash ? DiffTarget.Stash(row.Sha) : DiffTarget.Commit(row.Sha);

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

            // Another commit's list: nothing is carried over from the one before, not even a path
            // both happen to have.
            Files.WorkTreePath = repository.WorkTreePath;
            Files.SetFiles(files, keepSelection: false);
            _diffTarget = target;
            _filesRow = row;

            if (IsDiffViewOpen)
            {
                SelectFirstFile();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository changes.
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Reading the files of {Commit} failed", row.Sha);
            _filesRow = null;
            Files.Clear();
            Diff.Clear();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the panel has a file picked, in whichever list it shows.
    /// </summary>
    private bool HasSelectedFile
        => IsWorkingTreeShown ? WorkingTree.SelectedChange is not null : Files.SelectedFile is not null;

    /// <summary>
    /// Selects the first file of the selected row, once the panel holds that row's files — which is
    /// what the diff view opens on.
    /// </summary>
    private void SelectFirstFile()
    {
        if (IsWorkingTreeShown)
        {
            WorkingTree.SelectFirstFile();
            return;
        }

        if (_filesRow is not null && ReferenceEquals(_filesRow, SelectedRow))
        {
            Files.SelectFirstFile();
        }
    }

    /// <summary>
    /// Follows the file the panel has selected: the diff viewer shows it, over the graph.
    /// </summary>
    /// <remarks>
    /// A file of the list the panel still holds for the previous line, until the selected line's
    /// files arrive, is no file of the selected line: it is neither shown nor opened. Neither is
    /// anything the list does while the panel shows the working tree instead.
    /// </remarks>
    private void OnFileSelectionChanged()
    {
        if (IsWorkingTreeShown)
        {
            return;
        }

        bool current = ReferenceEquals(_filesRow, SelectedRow);

        if (Files.SelectedFile is not null && !current)
        {
            return;
        }

        ShowSelectedFileAsync().Forget();

        if (Files.SelectedFile is not null)
        {
            IsDiffViewOpen = true;
        }
    }

    /// <summary>
    /// Reads the working tree into the panel for the uncommitted line, and opens the diff on its first
    /// file when "Show what it changed" asked for it before the files were there.
    /// </summary>
    /// <param name="row">The uncommitted line.</param>
    /// <returns>A task that completes once the panel is up to date.</returns>
    private async Task ShowWorkingTreeAsync(CommitRowViewModel row)
    {
        await WorkingTree.RefreshAsync().ConfigureAwait(true);

        if (IsDiffViewOpen && ReferenceEquals(DetailsRow, row) && WorkingTree.SelectedChange is null)
        {
            WorkingTree.SelectFirstFile();
        }
    }

    /// <summary>
    /// Follows the file picked in the working tree, as <see cref="OnFileSelectionChanged"/> follows a
    /// commit's: its diff — of what is not staged, or of what is — over the graph.
    /// </summary>
    /// <remarks>
    /// The panel picks its file again every time a refresh re-reads the working tree. The file already
    /// on screen is only refreshed, which redraws it when it changed and leaves the reader's place
    /// alone when it did not (BUG-6590); a file staged or unstaged meanwhile is another half's, and is
    /// shown afresh.
    /// </remarks>
    private void OnWorkingTreeSelectionChanged()
    {
        if (!IsWorkingTreeShown)
        {
            return;
        }

        RepositoryHandle? repository = RepositoryContext.Repository;

        if (repository is null || WorkingTree.SelectedChange is not { } change)
        {
            Diff.Clear();
            return;
        }

        _ = Diff.IsShowing(repository, change.Target, change.File.Path)
            ? Diff.RefreshAsync(change.File)
            : Diff.ShowAsync(repository, change.Target, change.File);

        IsDiffViewOpen = true;
    }

    /// <summary>
    /// Brings the history up to date after the working tree panel changed something: a commit is a
    /// new line, and a tree left clean has no uncommitted line any more.
    /// </summary>
    /// <param name="e">What the operation did.</param>
    /// <remarks>
    /// The diff of a file that the operation took out of both lists — committed, discarded, stashed —
    /// is put away first: there is nothing left for it to describe, and the graph it covered is
    /// where the result is.
    /// </remarks>
    private void OnWorkingTreeChanged(WorkingTreeChangedEventArgs e)
    {
        if (IsDiffViewOpen && IsWorkingTreeShown && WorkingTree.SelectedChange is null)
        {
            IsDiffViewOpen = false;
        }

        // The panel has just read the tree, which is what it raised this after.
        RefreshInPlaceAsync(referencesMoved: e.Committed, workingTreeRead: true).Forget();
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

    /// <summary>
    /// Draws every loaded tag badge with what the remote now says, and measures the Refs column again:
    /// a cloud makes a badge wider.
    /// </summary>
    private void OnRemoteTagsChanged()
    {
        bool changed = false;

        foreach (CommitRowViewModel row in Rows)
        {
            changed |= row.ShowRemoteTags(_remoteTags.RemoteOf);
        }

        if (changed)
        {
            RecalculateRefColumnWidth();
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
        BranchCommands.DeleteWithRemote?.NotifyCanExecuteChanged();
        BranchCommands.Pull.NotifyCanExecuteChanged();
        BranchCommands.Push.NotifyCanExecuteChanged();
    }

    private static bool HasCommit(CommitRowViewModel? row) => row?.Commit is not null;

    /// <summary>
    /// Shows what a row changed, selecting it first when it is not the selected one, and opening the
    /// diff on its first file.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <remarks>
    /// The row menu's way into the diffs. Selecting a line only opens the details panel, whose files
    /// are the other way in.
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

    /// <summary>
    /// Forgets the squash base once a finished reload no longer lists its commit — its branch deleted
    /// or hidden, or the history read again from the top past where it was.
    /// </summary>
    private void ForgetASquashBaseThatIsGone()
    {
        // A load still running, or one that read nothing, is no evidence that the commit is gone.
        if (SquashBase is not { } squashBase || IsBusy || !IsRepositoryOpen || Rows.Count == 0)
        {
            return;
        }

        foreach (CommitRowViewModel row in Rows)
        {
            if (string.Equals(row.Sha, squashBase.Sha, StringComparison.Ordinal))
            {
                return;
            }
        }

        SquashBase = null;
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

    private void NotifySelectedCommitDetails()
    {
        OnPropertyChanged(nameof(SelectedSubject));
        OnPropertyChanged(nameof(HasSelectedCommit));
    }

    private void NotifyEmptyState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(HasUncommittedChanges));
        StashCommand.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------- the stash

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

        if (rebuild && Rows.Count > 0)
        {
            ReloadAsync().Forget();
        }
    }
}
