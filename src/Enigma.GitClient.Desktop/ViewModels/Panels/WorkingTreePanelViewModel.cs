using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Commits;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Staging;
using Enigma.GitClient.Core.Status;
using Enigma.GitClient.Core.Submodules;
using Enigma.GitClient.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.ViewModels.Panels;

/// <summary>
/// A file picked in the working-tree panel, and the half of the change it was picked in.
/// </summary>
/// <param name="Target">
/// What its diff compares: <see cref="DiffTarget.WorkingTree"/> for what is not staged,
/// <see cref="DiffTarget.Staged"/> for what is.
/// </param>
/// <param name="File">The file.</param>
public sealed record WorkingTreeChange(DiffTarget Target, ChangedFile File);

/// <summary>
/// What an operation of the working-tree panel did, for whoever draws the history.
/// </summary>
/// <param name="Committed">Whether it recorded a commit, which moves HEAD.</param>
public sealed class WorkingTreeChangedEventArgs(bool Committed) : EventArgs
{
    /// <summary>Gets a value indicating whether the operation recorded a commit.</summary>
    public bool Committed { get; } = Committed;
}

/// <summary>
/// The working tree, as the history's details panel shows it for the uncommitted line: what has
/// changed, what is staged, and the commit that turns the second into history.
/// </summary>
/// <remarks>
/// <para>
/// The two halves are the same <see cref="ChangedFilesPanelViewModel"/> a commit's files are listed
/// in, so the list/tree toggle, the filter and the row menu behave identically in both places.
/// </para>
/// <para>
/// The panel draws no diff of its own: it says which file is picked, and in which half
/// (<see cref="SelectedChange"/>), and the history shows that file's diff over the graph, as it does
/// a commit's. It reads the working tree only while the history shows it (<see cref="IsActive"/>):
/// a panel nobody is looking at has no reason to run <c>git status</c> at every refresh.
/// </para>
/// </remarks>
public sealed class WorkingTreePanelViewModel : ViewModelBase
{
    /// <summary>
    /// How long a subject line should be before it stops reading as a summary.
    /// </summary>
    public const int SubjectGuide = 50;

    /// <summary>
    /// How long any line of the body should be, so the message reads in a terminal.
    /// </summary>
    public const int BodyGuide = 72;

    private readonly IStatusService _status;
    private readonly IStagingService _staging;
    private readonly ICommitService _commits;
    private readonly IStashOperations _stashOperations;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<WorkingTreePanelViewModel> _logger;
    private readonly ISubmoduleService? _submodules;
    private readonly IInstanceLauncher? _launcher;

    private WorkingTreeStatus _current = WorkingTreeStatus.Empty;

    // Set while a refresh replaces both halves' files: the selection they drop and take back is one
    // change of selection, reported once the refresh is done.
    private bool _applying;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="status">Reads what has changed.</param>
    /// <param name="staging">Moves changes into and out of the index.</param>
    /// <param name="commits">Records the commit.</param>
    /// <param name="stashOperations">Puts the work aside, as the history's toolbar does.</param>
    /// <param name="interop">Backs the file lists' own row menus.</param>
    /// <param name="settings">The file lists' shape.</param>
    /// <param name="dialogs">Raises the confirmations.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public WorkingTreePanelViewModel(
        IRepositoryContext repositoryContext,
        IStatusService status,
        IStagingService staging,
        ICommitService commits,
        IStashOperations stashOperations,
        ISystemInterop interop,
        ISettingsService settings,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<WorkingTreePanelViewModel> logger,
        ISubmoduleService? submodules = null,
        IInstanceLauncher? launcher = null)
    {
        ArgumentNullException.ThrowIfNull(repositoryContext);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(commits);
        ArgumentNullException.ThrowIfNull(stashOperations);
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        RepositoryContext = repositoryContext;
        _status = status;
        _staging = staging;
        _commits = commits;
        _stashOperations = stashOperations;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _logger = logger;

        _submodules = submodules;
        _launcher = launcher;

        Unstaged = new ChangedFilesPanelViewModel(interop, settings, launcher);
        Staged = new ChangedFilesPanelViewModel(interop, settings, launcher);

        // Only one side can be selected at a time: the diff shown has to be unambiguous about which
        // half of the change it is.
        Unstaged.SelectionChanged += (_, _) => OnSelected(Unstaged, Staged);
        Staged.SelectionChanged += (_, _) => OnSelected(Staged, Unstaged);

        // Either half letting go of its file on a click is the panel letting go of it.
        Unstaged.SelectionReleased += (_, _) => SelectionReleased?.Invoke(this, EventArgs.Empty);
        Staged.SelectionReleased += (_, _) => SelectionReleased?.Invoke(this, EventArgs.Empty);

        StageCommand = new AsyncRelayCommand<ChangedFileNodeViewModel>(OnStageAsync, node => node is not null);
        UnstageCommand = new AsyncRelayCommand<ChangedFileNodeViewModel>(OnUnstageAsync, node => node is not null);
        DiscardCommand = new AsyncRelayCommand<ChangedFileNodeViewModel>(OnDiscardAsync, node => node is not null);

        StageAllCommand = new AsyncRelayCommand(OnStageAllAsync, () => HasUnstaged);
        UnstageAllCommand = new AsyncRelayCommand(OnUnstageAllAsync, () => HasStaged);
        DiscardAllCommand = new AsyncRelayCommand(OnDiscardAllAsync, () => HasUnstaged);

        CommitCommand = new AsyncRelayCommand(OnCommitAsync, CanCommit);
        StashAllCommand = new AsyncRelayCommand(OnStashAllAsync, () => HasUnstaged || HasStaged);

        // The lists are the same control a commit's files are listed in; what differs is the verbs a
        // row offers and what an empty one means here.
        Unstaged.Actions = new ChangedFileRowActions("Stage", StageCommand, "Discard…", DiscardCommand);
        Unstaged.EmptyTitle = "Nothing to stage";
        Unstaged.EmptyMessage = "Everything you have changed is already staged.";

        Staged.Actions = new ChangedFileRowActions("Unstage", UnstageCommand, PrimaryIcon: "Minus");
        Staged.EmptyTitle = "Nothing staged";
        Staged.EmptyMessage = "Stage the changes you want in the next commit.";

        repositoryContext.RepositoryChanged += (_, _) => OnRepositoryChanged();
        repositoryContext.StateRefreshed += (_, _) => OnStateRefreshed();
    }

    /// <summary>Gets the repository the application is looking at.</summary>
    public IRepositoryContext RepositoryContext { get; }

    /// <summary>Gets a value indicating whether a repository is open.</summary>
    public bool IsRepositoryOpen => RepositoryContext.IsRepositoryOpen;

    /// <summary>
    /// Gets or sets a value indicating whether the history shows the panel, which it does while the
    /// uncommitted line is selected. Only then does a refresh of the repository read the working
    /// tree again.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Raised when the picked file changes, or is picked again by a refresh that re-read it.
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Raised when a click let go of the picked file, in either half, after the selection is cleared.
    /// </summary>
    /// <remarks>
    /// Never raised by a refresh that reads the files again, or by one half giving the selection up to
    /// the other: only by the reader putting the file down.
    /// </remarks>
    public event EventHandler? SelectionReleased;

    /// <summary>
    /// Raised after an operation changed the working tree, the index or HEAD, and the panel has read
    /// the working tree again — so the history can bring its own lines up to date.
    /// </summary>
    public event EventHandler<WorkingTreeChangedEventArgs>? Changed;

    /// <summary>Gets the file picked in either half, or <see langword="null"/> when none is.</summary>
    public WorkingTreeChange? SelectedChange { get; private set => SetProperty(ref field, value); }

    /// <summary>Gets the panel holding everything that is not staged.</summary>
    public ChangedFilesPanelViewModel Unstaged { get; }

    /// <summary>Gets the panel holding everything that is.</summary>
    public ChangedFilesPanelViewModel Staged { get; }

    /// <summary>
    /// Gets or sets the commit message.
    /// </summary>
    public string Message
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(Subject));
                OnPropertyChanged(nameof(SubjectLength));
                OnPropertyChanged(nameof(IsSubjectTooLong));
                OnPropertyChanged(nameof(HasLongBodyLine));
                CommitCommand.NotifyCanExecuteChanged();
            }
        }
    } = string.Empty;

    /// <summary>Gets the message's first line, which git records as the subject.</summary>
    public string Subject
    {
        get
        {
            int newline = Message.IndexOf('\n', StringComparison.Ordinal);
            return (newline < 0 ? Message : Message[..newline]).TrimEnd('\r');
        }
    }

    /// <summary>Gets how long the subject is, shown beside the guide.</summary>
    public string SubjectLength => Subject.Length.ToString(CultureInfo.CurrentCulture);

    /// <summary>
    /// Gets a value indicating whether the subject has passed the length a summary should be.
    /// </summary>
    public bool IsSubjectTooLong => Subject.Length > SubjectGuide;

    /// <summary>
    /// Gets a value indicating whether any body line has passed the width a message should wrap at.
    /// </summary>
    public bool HasLongBodyLine
    {
        get
        {
            string[] lines = Message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            for (int index = 1; index < lines.Length; index++)
            {
                if (lines[index].Length > BodyGuide)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets a value indicating whether anything is staged.</summary>
    public bool HasStaged => _current.Staged.Count > 0;

    /// <summary>Gets a value indicating whether anything is unstaged, untracked or conflicted.</summary>
    public bool HasUnstaged => _current.Unstaged.Count + _current.Untracked.Count + _current.Conflicted.Count > 0;

    /// <summary>Gets a value indicating whether the working tree has nothing in it at all.</summary>
    public bool IsClean => _current.IsClean;

    /// <summary>Gets a value indicating whether a merge left conflicts to resolve.</summary>
    public bool HasConflicts => _current.HasConflicts;

    /// <summary>Gets the command that stages one row.</summary>
    public AsyncRelayCommand<ChangedFileNodeViewModel> StageCommand { get; }

    /// <summary>Gets the command that unstages one row.</summary>
    public AsyncRelayCommand<ChangedFileNodeViewModel> UnstageCommand { get; }

    /// <summary>Gets the command that discards one row.</summary>
    public AsyncRelayCommand<ChangedFileNodeViewModel> DiscardCommand { get; }

    /// <summary>Gets the command that stages everything.</summary>
    public AsyncRelayCommand StageAllCommand { get; }

    /// <summary>Gets the command that unstages everything.</summary>
    public AsyncRelayCommand UnstageAllCommand { get; }

    /// <summary>Gets the command that discards everything.</summary>
    public AsyncRelayCommand DiscardAllCommand { get; }

    /// <summary>Gets the command that records the commit.</summary>
    public AsyncRelayCommand CommitCommand { get; }

    /// <summary>Gets the command that puts everything aside on the stash.</summary>
    public AsyncRelayCommand StashAllCommand { get; }

    /// <summary>
    /// Lets go of the picked file, in both halves.
    /// </summary>
    public void ClearSelection()
    {
        Unstaged.SelectedNode = null;
        Staged.SelectedNode = null;
    }

    /// <summary>
    /// Picks the first file: the first that is not staged, or the first staged one when everything is.
    /// </summary>
    /// <returns><see langword="true"/> when there was a file to pick.</returns>
    public bool SelectFirstFile() => Unstaged.SelectFirstFile() || Staged.SelectFirstFile();

    /// <summary>
    /// Gets the panel's latest reading of the working tree, finished or still under way.
    /// </summary>
    /// <remarks>
    /// The history asks it whether there is uncommitted work while the panel is on screen, rather than
    /// running a <c>git status</c> of its own beside the one the panel runs anyway.
    /// </remarks>
    public Task Reading { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Re-reads the status and rebuilds both halves.
    /// </summary>
    /// <returns>A task that completes once the panel is up to date.</returns>
    public Task RefreshAsync()
    {
        Task reading = ReadAsync();
        Reading = reading;

        return reading;
    }

    private async Task ReadAsync()
    {
        RepositoryHandle? repository = RepositoryContext.Repository;

        if (repository is null)
        {
            Apply(WorkingTreeStatus.Empty);
            return;
        }

        try
        {
            WorkingTreeStatus status = await _status
                .GetStatusAsync(repository, includeIgnored: false, RepositoryContext.RepositoryLifetime)
                .ConfigureAwait(true);

            Apply(status);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository changes under the read.
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Reading the working tree status failed");
            Apply(WorkingTreeStatus.Empty);
        }
    }

    private void OnRepositoryChanged()
    {
        OnPropertyChanged(nameof(IsRepositoryOpen));

        Message = string.Empty;

        // Another repository's files are nothing to show, whether or not the panel is on screen — and
        // the folders closed in it are not this one's.
        Unstaged.SetFiles([], keepSelection: false);
        Staged.SetFiles([], keepSelection: false);
        Apply(WorkingTreeStatus.Empty);

        if (IsActive)
        {
            RefreshAsync().Forget();
        }
    }

    private void OnStateRefreshed()
    {
        if (IsActive)
        {
            RefreshAsync().Forget();
        }
    }

    private void Apply(WorkingTreeStatus status)
    {
        _current = status;

        string? previousUnstaged = Unstaged.SelectedFile?.Path;
        string? previousStaged = Staged.SelectedFile?.Path;

        _applying = true;

        try
        {
            Unstaged.WorkTreePath = RepositoryContext.Repository?.WorkTreePath ?? string.Empty;
            Staged.WorkTreePath = Unstaged.WorkTreePath;

            Unstaged.SetFiles([.. status.NotStaged()]);
            Staged.SetFiles(status.Staged);

            // Keeping the selection across a refresh is what lets a user stage a file, look at the
            // next one, and not lose their place every time the panel re-reads. A file that moved to
            // the other half — the one just staged or unstaged — is followed there.
            _ = Unstaged.SelectPath(previousUnstaged)
                || Staged.SelectPath(previousStaged)
                || Staged.SelectPath(previousUnstaged)
                || Unstaged.SelectPath(previousStaged);
        }
        finally
        {
            _applying = false;
        }

        OnPropertyChanged(nameof(HasStaged));
        OnPropertyChanged(nameof(HasUnstaged));
        OnPropertyChanged(nameof(IsClean));
        OnPropertyChanged(nameof(HasConflicts));

        StageAllCommand.NotifyCanExecuteChanged();
        UnstageAllCommand.NotifyCanExecuteChanged();
        DiscardAllCommand.NotifyCanExecuteChanged();
        StashAllCommand.NotifyCanExecuteChanged();
        CommitCommand.NotifyCanExecuteChanged();

        UpdateSelectedChange();
    }

    private void OnSelected(ChangedFilesPanelViewModel picked, ChangedFilesPanelViewModel other)
    {
        if (_applying)
        {
            return;
        }

        if (picked.SelectedFile is not null)
        {
            other.SelectedNode = null;
        }
        else if (other.SelectedFile is not null)
        {
            // The other half letting go of its file on this one's behalf: this one has it.
            return;
        }

        UpdateSelectedChange();
    }

    private void UpdateSelectedChange()
    {
        SelectedChange = Unstaged.SelectedFile is { } unstaged
            ? new WorkingTreeChange(DiffTarget.WorkingTree(), unstaged)
            : Staged.SelectedFile is { } staged
                ? new WorkingTreeChange(DiffTarget.Staged(), staged)
                : null;

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanCommit()
    {
        return IsRepositoryOpen && Message.Trim().Length > 0 && !HasConflicts && HasStaged;
    }

    // ---------------------------------------------------------------- commands

    private Task OnStageAsync(ChangedFileNodeViewModel? node)
        => node is null
            ? Task.CompletedTask
            : RunAsync(
                (handle, token) => _staging.StageAsync(handle, PathsOf(node), token),
                "Could not stage");

    private Task OnUnstageAsync(ChangedFileNodeViewModel? node)
        => node is null
            ? Task.CompletedTask
            : RunAsync(
                (handle, token) => _staging.UnstageAsync(handle, PathsOf(node), token),
                "Could not unstage");

    private Task OnStageAllAsync()
        => RunAsync((handle, token) => _staging.StageAllAsync(handle, token), "Could not stage everything");

    private Task OnUnstageAllAsync()
        => RunAsync((handle, token) => _staging.UnstageAllAsync(handle, token), "Could not unstage everything");

    private async Task OnDiscardAsync(ChangedFileNodeViewModel? node)
    {
        if (node is null)
        {
            return;
        }

        IReadOnlyList<string> paths = PathsOf(node);

        if (await PlanSubmodulesAsync(paths).ConfigureAwait(true) is not { } plans)
        {
            return;
        }

        bool confirmed = await _dialogs.ConfirmDestructiveAsync(
            "Discard changes",
            $"Throw away the changes to {Count(paths.Count)}? This cannot be undone.\n\n"
            + string.Join('\n', paths)
            + DescribeSubmodules(plans),
            "Discard").ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await DiscardAsync(paths, plans, "Could not discard").ConfigureAwait(true);
    }

    private async Task OnDiscardAllAsync()
    {
        if (RepositoryContext.Repository is null)
        {
            return;
        }

        List<string> paths = [];

        foreach (ChangedFile file in _current.NotStaged())
        {
            paths.Add(file.Path);
        }

        if (paths.Count == 0)
        {
            return;
        }

        if (await PlanSubmodulesAsync(paths).ConfigureAwait(true) is not { } plans)
        {
            return;
        }

        // A plain question, confirmed in red: a hand can still click by accident, but the harmless
        // button is the default and the button that loses the work says so in its colour.
        bool confirmed = await _dialogs.ConfirmDestructiveAsync(
            "Discard everything",
            $"Throw away every change in {Count(paths.Count)}? This cannot be undone." + DescribeSubmodules(plans),
            "Discard everything").ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await DiscardAsync(paths, plans, "Could not discard the changes").ConfigureAwait(true);
    }

    // ---------------------------------------------------------------- submodules in a discard or a commit

    /// <summary>
    /// Works out what a discard does to the submodules among its paths, and refuses it — naming the
    /// submodule, with a way to open it — when one has uncommitted changes, which a discard cannot put
    /// back.
    /// </summary>
    /// <returns>The submodules among the paths, or <see langword="null"/> when the discard is refused.</returns>
    private async Task<IReadOnlyList<SubmoduleDiscard>?> PlanSubmodulesAsync(IReadOnlyList<string> paths)
    {
        if (_submodules is null || RepositoryContext.Repository is not { } repository || !RepositoryContext.HasSubmodules)
        {
            return [];
        }

        IReadOnlyList<SubmoduleDiscard> plans;

        try
        {
            plans = await _submodules.PlanDiscardAsync(repository, paths, RepositoryContext.RepositoryLifetime).ConfigureAwait(true);
        }
        catch (GitCommandException exception)
        {
            _logger.LogWarning(exception, "The submodules of a discard could not be read");
            return [];
        }

        if (plans.FirstOrDefault(plan => plan.HasUncommittedChanges) is not { } dirty)
        {
            return plans;
        }

        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "Discard changes";
            dialog.Content = $"{dirty.Path} has uncommitted changes of its own, which a discard here cannot put back. "
                + "Open it to commit or discard them first.";
            dialog.PrimaryButtonText = _launcher is null ? null : "Open submodule";
            dialog.CloseButtonText = "Cancel";
            dialog.DefaultButton = DefaultButton.Close;
        }).ConfigureAwait(true);

        if (answer == DialogResult.Primary)
        {
            _launcher?.Launch(System.IO.Path.Combine(repository.WorkTreePath, dirty.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        }

        return null;
    }

    /// <summary>
    /// Says, for a discard's question, where each submodule goes back to and what it leaves behind.
    /// </summary>
    /// <param name="plans">The submodules among the discarded paths.</param>
    /// <returns>The lines, after a blank one, or an empty string when no submodule moves.</returns>
    public static string DescribeSubmodules(IReadOnlyList<SubmoduleDiscard> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);

        List<string> lines = [.. plans.Where(plan => plan.Moves).Select(plan => plan.Describe())];

        return lines.Count == 0 ? string.Empty : "\n\n" + string.Join('\n', lines);
    }

    /// <summary>
    /// Discards the paths and puts the submodules among them back on HEAD's commit, in one write.
    /// </summary>
    private async Task DiscardAsync(IReadOnlyList<string> paths, IReadOnlyList<SubmoduleDiscard> plans, string failureTitle)
    {
        List<string> moving = [.. plans.Where(plan => plan.Moves).Select(plan => plan.Path)];
        SubmoduleUpdateResult? moved = null;

        bool done = await RunAsync(
            async (handle, token) =>
            {
                await _staging.DiscardAsync(handle, paths, token).ConfigureAwait(true);

                if (moving.Count > 0 && _submodules is not null)
                {
                    moved = await _submodules.PutBackAsync(handle, moving, null, token).ConfigureAwait(true);
                }
            },
            failureTitle).ConfigureAwait(true);

        if (done && moved is not null && SubmoduleOperations.DescribeProblems(moved) is { } problems)
        {
            Report(problems.Title, problems.Message, InfoBarSeverity.Warning);
        }
    }

    /// <summary>
    /// Asks before a commit that moves a submodule back — the stale folder that undoes a colleague's
    /// bump. A move forward, or one that cannot be read, asks nothing.
    /// </summary>
    /// <returns>Whether to go on with the commit.</returns>
    private async Task<bool> ConfirmPinsGoingBackAsync()
    {
        if (_submodules is null || RepositoryContext.Repository is not { } repository || !RepositoryContext.HasSubmodules)
        {
            return true;
        }

        List<PinChange> changes = [];

        try
        {
            foreach (StagedPin pin in await _submodules.ReadStagedPinsAsync(repository, RepositoryContext.RepositoryLifetime).ConfigureAwait(true))
            {
                if (pin.HeadSha.Length > 0 && pin.IndexSha.Length > 0)
                {
                    changes.Add(await _submodules
                        .DescribePinChangeAsync(repository, pin.Path, pin.HeadSha, pin.IndexSha, RepositoryContext.RepositoryLifetime)
                        .ConfigureAwait(true));
                }
            }
        }
        catch (GitCommandException exception)
        {
            // A guard that cannot read does not stand in the way: the commit is the user's.
            _logger.LogWarning(exception, "The staged submodules could not be read before a commit");
            return true;
        }

        if (DescribeGoingBack(changes) is not { } question)
        {
            return true;
        }

        return await _dialogs.ConfirmDestructiveAsync("A submodule goes back", question, "Commit anyway").ConfigureAwait(true);
    }

    /// <summary>
    /// Builds the question a commit asks when it moves submodules back.
    /// </summary>
    /// <param name="changes">The staged pin changes.</param>
    /// <returns>
    /// "This commit moves libs/Common back 3 commits (to a1b2c3d). Is that intended?", or
    /// <see langword="null"/> when none goes back.
    /// </returns>
    public static string? DescribeGoingBack(IReadOnlyList<PinChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        List<string> lines = [.. changes
            .Where(change => change.Direction == PinDirection.Back)
            .Select(change => $"This commit moves {change.Path} back {(change.RemovedCount == 1 ? "1 commit" : $"{change.RemovedCount.ToString(CultureInfo.CurrentCulture)} commits")} (to {PinChange.Short(change.NewSha)}).")];

        return lines.Count == 0 ? null : string.Join('\n', lines) + " Is that intended?";
    }

    private async Task OnCommitAsync()
    {
        if (!CanCommit())
        {
            return;
        }

        if (!await ConfirmPinsGoingBackAsync().ConfigureAwait(true))
        {
            return;
        }

        CommitRequest request = new() { Message = Message };

        string sha = string.Empty;

        bool done = await RunAsync(
            async (handle, token) => sha = await _commits.CommitAsync(handle, request, token).ConfigureAwait(true),
            "Could not commit",
            committing: true).ConfigureAwait(true);

        if (!done)
        {
            return;
        }

        Report(
            "Committed",
            $"{(sha.Length >= 7 ? sha[..7] : sha)} · {FirstLine(request.Message)}",
            InfoBarSeverity.Success);
    }

    // The stash's own questions and messages are the stash operations': this panel asks for them and
    // re-reads what it shows afterwards, exactly as the history's toolbar does.
    private async Task OnStashAllAsync()
    {
        if (!IsRepositoryOpen)
        {
            return;
        }

        IsBusy = true;

        try
        {
            if (await _stashOperations.StashAsync().ConfigureAwait(true))
            {
                await RefreshAsync().ConfigureAwait(true);
                Changed?.Invoke(this, new WorkingTreeChangedEventArgs(Committed: false));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------------------------------------------------------------- plumbing

    /// <summary>
    /// Collects the paths a row stands for: one file, or every file under a directory row.
    /// </summary>
    /// <param name="node">The row.</param>
    /// <returns>The paths.</returns>
    public static IReadOnlyList<string> PathsOf(ChangedFileNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!node.IsDirectory)
        {
            return [node.Path];
        }

        List<string> paths = [];

        foreach (ChangedFileNodeViewModel child in ChangedFilesPanelViewModel.Flatten(node.Children))
        {
            if (!child.IsDirectory)
            {
                paths.Add(child.Path);
            }
        }

        return paths;
    }

    private static string Count(int files)
        => files == 1 ? "1 file" : $"{files.ToString(CultureInfo.CurrentCulture)} files";

    private static string FirstLine(string text)
    {
        int newline = text.IndexOf('\n', StringComparison.Ordinal);

        return (newline < 0 ? text : text[..newline]).TrimEnd('\r');
    }

    private async Task<bool> RunAsync(
        Func<RepositoryHandle, CancellationToken, Task> operation,
        string failureTitle,
        bool committing = false)
    {
        if (!IsRepositoryOpen)
        {
            return false;
        }

        IsBusy = true;

        try
        {
            await RepositoryContext.RunExclusiveAsync(operation).ConfigureAwait(true);

            // Cleared before the panel reads the tree again: a commit that went through leaves
            // nothing of its message for the next one.
            if (committing)
            {
                Message = string.Empty;
            }

            await RefreshAsync().ConfigureAwait(true);
            Changed?.Invoke(this, new WorkingTreeChangedEventArgs(committing));

            return true;
        }
        catch (GitOperationRefusedException refusal)
        {
            Report(failureTitle, refusal.Message, InfoBarSeverity.Warning);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "{Title}", failureTitle);

            Report(failureTitle, GitFirstLine(exception.StandardError), InfoBarSeverity.Error);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the operation.
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }

    private static string GitFirstLine(string text)
    {
        string trimmed = text.Trim();

        return trimmed.Length == 0 ? "git reported no reason." : FirstLine(trimmed);
    }

    private void Report(string title, string message, InfoBarSeverity severity)
        => _infoBar.Notify(title, message, severity);
}
