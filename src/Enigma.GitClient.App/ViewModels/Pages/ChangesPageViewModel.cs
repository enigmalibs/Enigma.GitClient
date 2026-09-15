using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.ViewModels.Panels;
using Enigma.GitClient.App.Views.Dialogs;
using Enigma.GitClient.Core.Commits;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Staging;
using Enigma.GitClient.Core.Status;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// ViewModel behind the working directory page: what has changed, what is staged, and the commit
/// that turns the second into history.
/// </summary>
/// <remarks>
/// The two halves are the same <see cref="ChangedFilesPanelViewModel"/> the history page uses, so
/// the list/tree toggle, the filter and the row menu all behave identically in both places. The
/// diff pane is the same viewer too: a change is a change, whether it is in a commit or on its way
/// to one.
/// </remarks>
public sealed class ChangesPageViewModel : PageViewModelBase
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
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<ChangesPageViewModel> _logger;

    private WorkingTreeStatus _current = WorkingTreeStatus.Empty;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="status">Reads what has changed.</param>
    /// <param name="staging">Moves changes into and out of the index.</param>
    /// <param name="commits">Records the commit.</param>
    /// <param name="interop">Backs the file panels' own row menus.</param>
    /// <param name="diff">Shows the selected file's diff.</param>
    /// <param name="dialogs">Raises the confirmations.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public ChangesPageViewModel(
        IRepositoryContext repositoryContext,
        IStatusService status,
        IStagingService staging,
        ICommitService commits,
        ISystemInterop interop,
        DiffViewerViewModel diff,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<ChangesPageViewModel> logger)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(commits);
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _status = status;
        _staging = staging;
        _commits = commits;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _logger = logger;

        Diff = diff;
        Unstaged = new ChangedFilesPanelViewModel(interop);
        Staged = new ChangedFilesPanelViewModel(interop);

        // Only one side can be selected at a time: the diff shown has to be unambiguous about which
        // half of the change it is.
        Unstaged.SelectionChanged += (_, _) => OnSelected(Unstaged, Staged, DiffTarget.WorkingTree());
        Staged.SelectionChanged += (_, _) => OnSelected(Staged, Unstaged, DiffTarget.Staged());

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsRepositoryOpen);

        StageCommand = new AsyncRelayCommand<ChangedFileNodeViewModel>(OnStageAsync, node => node is not null);
        UnstageCommand = new AsyncRelayCommand<ChangedFileNodeViewModel>(OnUnstageAsync, node => node is not null);
        DiscardCommand = new AsyncRelayCommand<ChangedFileNodeViewModel>(OnDiscardAsync, node => node is not null);

        StageAllCommand = new AsyncRelayCommand(OnStageAllAsync, () => HasUnstaged);
        UnstageAllCommand = new AsyncRelayCommand(OnUnstageAllAsync, () => HasStaged);
        DiscardAllCommand = new AsyncRelayCommand(OnDiscardAllAsync, () => HasUnstaged);

        CommitCommand = new AsyncRelayCommand(OnCommitAsync, CanCommit);

        // The panels are the same control the history uses; what differs is the verbs a row offers.
        Unstaged.Actions = new ChangedFileRowActions("Stage", StageCommand, "Discard…", DiscardCommand);
        Staged.Actions = new ChangedFileRowActions("Unstage", UnstageCommand, PrimaryIcon: "Minus");
    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Working directory";

    /// <summary>Gets the panel holding everything that is not staged.</summary>
    public ChangedFilesPanelViewModel Unstaged { get; }

    /// <summary>Gets the panel holding everything that is.</summary>
    public ChangedFilesPanelViewModel Staged { get; }

    /// <summary>Gets the diff viewer showing whichever file is selected.</summary>
    public DiffViewerViewModel Diff { get; }

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

    /// <summary>
    /// Gets or sets a value indicating whether the commit replaces the current tip.
    /// </summary>
    public bool Amend
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(CommitButtonText));
                CommitCommand.NotifyCanExecuteChanged();
                _ = OnAmendChangedAsync();
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether a sign-off trailer is appended.
    /// </summary>
    public bool SignOff { get; set => SetProperty(ref field, value); }

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

    /// <summary>Gets what the commit button says, which changes for an amend.</summary>
    public string CommitButtonText => Amend ? "Amend commit" : "Commit";

    /// <summary>Gets a value indicating whether anything is staged.</summary>
    public bool HasStaged => _current.Staged.Count > 0;

    /// <summary>Gets a value indicating whether anything is unstaged, untracked or conflicted.</summary>
    public bool HasUnstaged => _current.Unstaged.Count + _current.Untracked.Count + _current.Conflicted.Count > 0;

    /// <summary>Gets a value indicating whether the working tree has nothing in it at all.</summary>
    public bool IsClean => _current.IsClean;

    /// <summary>Gets a value indicating whether a merge left conflicts to resolve.</summary>
    public bool HasConflicts => _current.HasConflicts;

    /// <summary>Gets the branch HEAD is on, for the header.</summary>
    public string BranchName
        => _current.Branch.IsDetached
            ? "detached HEAD"
            : _current.Branch.Head.Length > 0 ? _current.Branch.Head : "no branch";

    /// <summary>Gets the upstream and how far apart they are, empty when there is no upstream.</summary>
    public string TrackingSummary
    {
        get
        {
            if (!_current.Branch.HasUpstream)
            {
                return string.Empty;
            }

            string ahead = _current.Branch.Ahead.ToString(CultureInfo.CurrentCulture);
            string behind = _current.Branch.Behind.ToString(CultureInfo.CurrentCulture);

            return _current.Branch is { Ahead: 0, Behind: 0 }
                ? $"up to date with {_current.Branch.Upstream}"
                : $"{_current.Branch.Upstream} · {ahead} ahead, {behind} behind";
        }
    }

    /// <summary>Gets a value indicating whether there is a tracking summary to show.</summary>
    public bool HasTrackingSummary => TrackingSummary.Length > 0;

    /// <summary>
    /// Gets the sentence shown while the page has nothing to display.
    /// </summary>
    public string EmptyMessage
        => !IsRepositoryOpen
            ? "Open a repository to see what has changed in its working directory."
            : "Nothing has changed since the last commit.";

    /// <summary>Gets the command that re-reads the status.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

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

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Re-reads the status and rebuilds both panels.
    /// </summary>
    /// <returns>A task that completes once the page is up to date.</returns>
    public async Task RefreshAsync()
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

    /// <inheritdoc />
    protected override void OnRepositoryChanged()
    {
        base.OnRepositoryChanged();

        RefreshCommand.NotifyCanExecuteChanged();
        Message = string.Empty;
        Amend = false;

        _ = RefreshAsync();
    }

    /// <inheritdoc />
    protected override void OnRepositoryStateRefreshed() => _ = RefreshAsync();

    private void Apply(WorkingTreeStatus status)
    {
        _current = status;

        string? previousUnstaged = Unstaged.SelectedFile?.Path;
        string? previousStaged = Staged.SelectedFile?.Path;

        Unstaged.WorkTreePath = RepositoryContext.Repository?.WorkTreePath ?? string.Empty;
        Staged.WorkTreePath = Unstaged.WorkTreePath;

        Unstaged.SetFiles([.. status.NotStaged()]);
        Staged.SetFiles(status.Staged);

        // Keeping the selection across a refresh is what lets a user stage a file, look at the next
        // one, and not lose their place every time the page re-reads.
        _ = Unstaged.SelectPath(previousUnstaged) || Staged.SelectPath(previousStaged);

        OnPropertyChanged(nameof(HasStaged));
        OnPropertyChanged(nameof(HasUnstaged));
        OnPropertyChanged(nameof(IsClean));
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(BranchName));
        OnPropertyChanged(nameof(TrackingSummary));
        OnPropertyChanged(nameof(HasTrackingSummary));
        OnPropertyChanged(nameof(EmptyMessage));

        StageAllCommand.NotifyCanExecuteChanged();
        UnstageAllCommand.NotifyCanExecuteChanged();
        DiscardAllCommand.NotifyCanExecuteChanged();
        CommitCommand.NotifyCanExecuteChanged();

        if (Unstaged.SelectedFile is null && Staged.SelectedFile is null)
        {
            Diff.Clear();
        }
    }

    private void OnSelected(
        ChangedFilesPanelViewModel picked,
        ChangedFilesPanelViewModel other,
        DiffTarget target)
    {
        if (picked.SelectedFile is not { } file)
        {
            return;
        }

        other.SelectedNode = null;

        RepositoryHandle? repository = RepositoryContext.Repository;

        if (repository is not null)
        {
            _ = Diff.ShowAsync(repository, target, file);
        }
    }

    private bool CanCommit()
    {
        if (!IsRepositoryOpen || Message.Trim().Length == 0 || HasConflicts)
        {
            return false;
        }

        // An amend has something to record even with nothing staged: the message itself.
        return HasStaged || Amend;
    }

    private async Task OnAmendChangedAsync()
    {
        RepositoryHandle? repository = RepositoryContext.Repository;

        if (!Amend || repository is null || Message.Trim().Length > 0)
        {
            return;
        }

        try
        {
            Message = await _commits
                .GetLastCommitMessageAsync(repository, RepositoryContext.RepositoryLifetime)
                .ConfigureAwait(true);
        }
        catch (GitCommandException exception)
        {
            _logger.LogWarning(exception, "Reading the last commit message failed");
        }
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

        bool confirmed = await ConfirmAsync(
            "Discard changes",
            $"Throw away the changes to {Count(paths.Count)}? This cannot be undone.\n\n"
            + string.Join('\n', paths)).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunAsync(
            (handle, token) => _staging.DiscardAsync(handle, paths, token),
            "Could not discard").ConfigureAwait(true);
    }

    private async Task OnDiscardAllAsync()
    {
        RepositoryHandle? repository = RepositoryContext.Repository;

        if (repository is null)
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

        string name = System.IO.Path.GetFileName(repository.WorkTreePath.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));

        // The one genuinely unrecoverable bulk action in the client, so it asks for the repository's
        // name to be typed rather than for a click. A click is something a hand does by accident.
        ConfirmTextDialogViewModel model = new(
            $"This throws away every change in {Count(paths.Count)} and cannot be undone.",
            name,
            "Type the repository's name to confirm");

        ConfirmTextDialogView view = new() { DataContext = model };

        ContentDialog? shown = null;

        void OnChanged(object? sender, EventArgs e)
        {
            if (shown is not null)
            {
                shown.IsPrimaryButtonEnabled = model.IsConfirmed;
            }
        }

        model.ConfirmationChanged += OnChanged;

        DialogResult result;

        try
        {
            result = await _dialogs.ShowAsync(dialog =>
            {
                shown = dialog;
                dialog.Title = "Discard everything";
                dialog.Content = view;
                dialog.PrimaryButtonText = "Discard everything";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Close;
                dialog.IsPrimaryButtonEnabled = false;
            }).ConfigureAwait(true);
        }
        finally
        {
            model.ConfirmationChanged -= OnChanged;
        }

        if (result != DialogResult.Primary || !model.IsConfirmed)
        {
            return;
        }

        await RunAsync(
            (handle, token) => _staging.DiscardAsync(handle, paths, token),
            "Could not discard the changes").ConfigureAwait(true);
    }

    private async Task OnCommitAsync()
    {
        if (!CanCommit())
        {
            return;
        }

        CommitRequest request = new()
        {
            Message = Message,
            Amend = Amend,
            SignOff = SignOff,
        };

        string sha = string.Empty;

        bool done = await RunAsync(
            async (handle, token) => sha = await _commits.CommitAsync(handle, request, token).ConfigureAwait(true),
            "Could not commit").ConfigureAwait(true);

        if (!done)
        {
            return;
        }

        Message = string.Empty;
        Amend = false;

        await ReportAsync(
            Amend ? "Commit amended" : "Committed",
            $"{(sha.Length >= 7 ? sha[..7] : sha)} · {FirstLine(request.Message)}",
            InfoBarSeverity.Success).ConfigureAwait(true);
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

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        DialogResult result = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = title;
            dialog.Content = message;
            dialog.PrimaryButtonText = "Discard";
            dialog.CloseButtonText = "Cancel";

            // The harmless button is the default, as everywhere something can be lost.
            dialog.DefaultButton = DefaultButton.Close;
        }).ConfigureAwait(true);

        return result == DialogResult.Primary;
    }

    private async Task<bool> RunAsync(
        Func<RepositoryHandle, CancellationToken, Task> operation,
        string failureTitle)
    {
        if (!IsRepositoryOpen)
        {
            return false;
        }

        IsBusy = true;

        try
        {
            await RepositoryContext.RunExclusiveAsync(operation).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);

            return true;
        }
        catch (GitOperationRefusedException refusal)
        {
            await ReportAsync(failureTitle, refusal.Message, InfoBarSeverity.Warning).ConfigureAwait(true);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "{Title}", failureTitle);

            await ReportAsync(failureTitle, GitFirstLine(exception.StandardError), InfoBarSeverity.Error)
                .ConfigureAwait(true);
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

    private Task ReportAsync(string title, string message, InfoBarSeverity severity)
        => _infoBar.ShowAsync(bar =>
        {
            bar.Title = title;
            bar.Message = message;
            bar.Severity = severity;
        });
}
