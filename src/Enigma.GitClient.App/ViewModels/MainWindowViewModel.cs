using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Navigation;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.Core.Diagnostics;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.ViewModels;

/// <summary>
/// The shell: the navigation rail, the repository strip in the title bar, and the one-time startup
/// checks.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IGitEnvironment _gitEnvironment;
    private readonly IContentDialogService _dialogService;
    private readonly ISyncOperations _sync;
    private readonly IMergeOperations _merges;
    private readonly ILogger<MainWindowViewModel> _logger;
    private bool _initialised;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="shell">Owns the navigation rail and the pages on it.</param>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="gitEnvironment">Probes the host's git installation at startup.</param>
    /// <param name="dialogService">Shows the blocking dialog when git is unusable.</param>
    /// <param name="history">The graph page, whose uncommitted row navigates to the changes page.</param>
    /// <param name="conflicts">The conflicts page, whose progress the banner shows.</param>
    /// <param name="sync">Backs the toolbar's fetch, pull and push.</param>
    /// <param name="merges">Backs the banner's way out of a merge.</param>
    /// <param name="logger">Receives startup failures.</param>
    public MainWindowViewModel(
        IShellNavigation shell,
        IRepositoryContext repositoryContext,
        IGitEnvironment gitEnvironment,
        IContentDialogService dialogService,
        HistoryPageViewModel history,
        ConflictResolutionPageViewModel conflicts,
        ISyncOperations sync,
        IMergeOperations merges,
        ILogger<MainWindowViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(repositoryContext);
        ArgumentNullException.ThrowIfNull(gitEnvironment);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(conflicts);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(merges);
        ArgumentNullException.ThrowIfNull(logger);

        // The graph's uncommitted row belongs to the working directory page, and the shell is the
        // only thing that knows how to get there.
        history.WorkingDirectoryRequested += (_, _) => shell.GoTo(ShellPage.Changes);

        Shell = shell;
        Conflicts = conflicts;
        RepositoryContext = repositoryContext;
        _gitEnvironment = gitEnvironment;
        _dialogService = dialogService;
        _logger = logger;

        _sync = sync;
        _merges = merges;

        ToggleThemeCommand = new RelayCommand(OnToggleTheme);

        FetchCommand = new AsyncRelayCommand(RunSyncAsync(_sync.FetchAsync), () => CanSync);
        PullCommand = new AsyncRelayCommand(RunSyncAsync(_sync.PullAsync), () => CanSync);
        PushCommand = new AsyncRelayCommand(RunSyncAsync(() => _sync.PushAsync()), () => CanSync);

        AbortMergeCommand = new AsyncRelayCommand(OnAbortMergeAsync, () => IsMergeInProgress);
        ResolveConflictsCommand = new RelayCommand(() => Shell.GoTo(ShellPage.Conflicts), () => IsMergeInProgress);
        RefreshCommand = new AsyncRelayCommand(OnRefreshAsync, () => RepositoryContext.IsRepositoryOpen);

        RepositoryContext.PropertyChanged += OnRepositoryContextPropertyChanged;
    }

    /// <summary>
    /// Gets the shell navigation the rail and the content area bind to.
    /// </summary>
    public IShellNavigation Shell { get; }

    /// <summary>
    /// Gets the conflicts page, which the banner reads its progress and its commit button from.
    /// </summary>
    /// <remarks>
    /// The banner has to say how far a merge has got before the page has ever been opened, so it
    /// binds straight through to the page rather than keeping a second copy of the same counters.
    /// </remarks>
    public ConflictResolutionPageViewModel Conflicts { get; }

    /// <summary>
    /// Gets the navigation service the rail and the content area bind to.
    /// </summary>
    public INavigationService Navigation => Shell.Service;

    /// <summary>
    /// Gets the repository the application is looking at.
    /// </summary>
    public IRepositoryContext RepositoryContext { get; }

    /// <summary>
    /// Gets the window title.
    /// </summary>
    public string WindowTitle
        => RepositoryContext.Repository is { } repository
            ? $"{repository.Name} — {ProductInformation.Name}"
            : ProductInformation.Name;

    /// <summary>
    /// Gets the repository name shown in the strip, or a prompt when none is open.
    /// </summary>
    public string RepositoryName => RepositoryContext.Repository?.Name ?? "No repository open";

    /// <summary>
    /// Gets the full path of the open repository, shown as the strip's tooltip.
    /// </summary>
    public string? RepositoryPath => RepositoryContext.Repository?.WorkTreePath;

    /// <summary>
    /// Gets where HEAD is, as a short label.
    /// </summary>
    public string HeadDisplayName => RepositoryContext.Head?.DisplayName ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether HEAD is detached, which the strip marks in a warning colour.
    /// </summary>
    public bool IsHeadDetached => RepositoryContext.Head?.IsDetached ?? false;

    /// <summary>
    /// Gets a value indicating whether a multi-step operation is in progress.
    /// </summary>
    public bool HasOperationInProgress => RepositoryContext.Head?.HasOperationInProgress ?? false;

    /// <summary>
    /// Gets the banner text describing the operation in progress.
    /// </summary>
    public string OperationDescription
        => RepositoryContext.Head?.Operation switch
        {
            RepositoryOperation.Merge => "A merge is in progress. Resolve the conflicts, or abort it.",
            RepositoryOperation.CherryPick => "A cherry-pick is in progress.",
            RepositoryOperation.Revert => "A revert is in progress.",
            RepositoryOperation.Bisect => "A bisect session is running.",
            RepositoryOperation.Rebase =>
                "This repository was left in the middle of a rebase by another tool. "
                + "Enigma.GitClient never rebases; finish or abort it with git before continuing.",
            RepositoryOperation.ApplyMailbox => "An 'am' session is in progress.",
            _ => string.Empty,
        };

    /// <summary>
    /// Gets the command that switches between the Dark and Light theme variants.
    /// </summary>
    public RelayCommand ToggleThemeCommand { get; }

    /// <summary>
    /// Gets the command that re-reads the open repository's state.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Gets the command that fetches from every remote.</summary>
    public AsyncRelayCommand FetchCommand { get; }

    /// <summary>Gets the command that pulls into the current branch.</summary>
    public AsyncRelayCommand PullCommand { get; }

    /// <summary>Gets the command that pushes the current branch.</summary>
    public AsyncRelayCommand PushCommand { get; }

    /// <summary>Gets the command that abandons a merge in progress.</summary>
    public AsyncRelayCommand AbortMergeCommand { get; }

    /// <summary>Gets the command that opens the conflicts page.</summary>
    public RelayCommand ResolveConflictsCommand { get; }

    /// <summary>
    /// Gets a value indicating whether a merge is waiting to be finished or abandoned.
    /// </summary>
    public bool IsMergeInProgress
        => RepositoryContext.Head?.Operation == Core.Refs.RepositoryOperation.Merge;

    /// <summary>
    /// Gets a value indicating whether there is a repository to synchronise at all.
    /// </summary>
    public bool CanSync => RepositoryContext.IsRepositoryOpen;

    /// <summary>Gets how many commits the branch has that its upstream does not.</summary>
    public string Ahead
        => (RepositoryContext.Refs.CurrentBranch?.Tracking.Ahead ?? 0)
            .ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Gets how many commits the upstream has that the branch does not.</summary>
    public string Behind
        => (RepositoryContext.Refs.CurrentBranch?.Tracking.Behind ?? 0)
            .ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Gets a value indicating whether there is anything to push.</summary>
    public bool IsAhead => RepositoryContext.Refs.CurrentBranch?.Tracking.Ahead > 0;

    /// <summary>Gets a value indicating whether there is anything to pull.</summary>
    public bool IsBehind => RepositoryContext.Refs.CurrentBranch?.Tracking.Behind > 0;

    /// <summary>
    /// Gets a value indicating whether the current branch has an upstream to compare against.
    /// </summary>
    public bool HasUpstream => RepositoryContext.Refs.CurrentBranch?.UpstreamShortName is { Length: > 0 };

    /// <summary>
    /// Runs the one-time startup checks. Called once the window is on screen, because the git
    /// warning is shown through the content dialog host the window owns.
    /// </summary>
    /// <returns>A task that completes once the checks have run.</returns>
    public async Task InitialiseAsync()
    {
        if (_initialised)
        {
            return;
        }

        _initialised = true;

        // Selecting the first page builds it, which is why it happens here rather than while the
        // container is still wiring itself up.
        Shell.Start();

        GitAvailability availability = await _gitEnvironment.GetAvailabilityAsync().ConfigureAwait(true);

        if (availability.IsUsable)
        {
            _logger.LogInformation("Using git {Version} at {Path}", availability.Version, availability.ExecutablePath);
            return;
        }

        _logger.LogError("git is not usable: {Message}", availability.Message);

        await _dialogService.ShowAsync(dialog =>
        {
            dialog.Title = "git is required";
            dialog.Content = availability.Message
                + "\n\nEnigma.GitClient drives the real git executable, so your existing keys, "
                + "credential helpers and configuration keep working. Install git 2.20 or newer, "
                + "then restart the application.";
            dialog.IconData = PhosphorIconSet.Instance
                .GetGlyph(PhosphorIcon.Warning, PhosphorWeight.Regular)
                .ToGeometry();
            dialog.IconBrush = new SolidColorBrush(Color.Parse("#E8A33D"));
            dialog.CloseButtonText = "Close";
        }).ConfigureAwait(true);
    }

    private void OnRepositoryContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IRepositoryContext.Repository):
            case nameof(IRepositoryContext.IsRepositoryOpen):
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(RepositoryName));
                OnPropertyChanged(nameof(RepositoryPath));
                OnPropertyChanged(nameof(CanSync));
                RefreshCommand.NotifyCanExecuteChanged();
                FetchCommand.NotifyCanExecuteChanged();
                PullCommand.NotifyCanExecuteChanged();
                PushCommand.NotifyCanExecuteChanged();
                break;
            case nameof(IRepositoryContext.Refs):
                NotifyTracking();
                break;
            case nameof(IRepositoryContext.Head):
                OnPropertyChanged(nameof(HeadDisplayName));
                OnPropertyChanged(nameof(IsHeadDetached));
                OnPropertyChanged(nameof(HasOperationInProgress));
                OnPropertyChanged(nameof(OperationDescription));
                OnPropertyChanged(nameof(IsMergeInProgress));
                AbortMergeCommand.NotifyCanExecuteChanged();
                ResolveConflictsCommand.NotifyCanExecuteChanged();

                // The rail carries the conflicts page only while there is a merge to resolve.
                Shell.SetConflictsVisible(IsMergeInProgress);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Wraps a synchronise operation so the toolbar shows the result of it without each command
    /// repeating the same four lines.
    /// </summary>
    private Func<Task> RunSyncAsync(Func<Task<bool>> operation)
        => async () =>
        {
            IsBusy = true;

            try
            {
                await operation().ConfigureAwait(true);
            }
            finally
            {
                IsBusy = false;
                NotifyTracking();
            }
        };

    private async Task OnAbortMergeAsync()
    {
        IsBusy = true;

        try
        {
            await _merges.AbortAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyTracking()
    {
        OnPropertyChanged(nameof(Ahead));
        OnPropertyChanged(nameof(Behind));
        OnPropertyChanged(nameof(IsAhead));
        OnPropertyChanged(nameof(IsBehind));
        OnPropertyChanged(nameof(HasUpstream));
    }

    private static void OnToggleTheme()
    {
        Application? application = Application.Current;

        if (application is null)
        {
            return;
        }

        application.RequestedThemeVariant =
            application.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    private async Task OnRefreshAsync()
    {
        IsBusy = true;
        try
        {
            await RepositoryContext.RefreshAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
