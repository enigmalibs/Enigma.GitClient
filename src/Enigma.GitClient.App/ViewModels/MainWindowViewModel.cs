using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Navigation;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.Core.Diagnostics;
using Enigma.GitClient.Core.Refs;

namespace Enigma.GitClient.App.ViewModels;

/// <summary>
/// The repository window: the navigation rail, the repository strip in the title bar, and the way
/// back to the start window.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IAppWindows _windows;
    private readonly ISyncOperations _sync;
    private readonly IMergeOperations _merges;
    private bool _initialised;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="shell">Owns the navigation rail and the pages on it.</param>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="windows">Takes the reader back to the start window when the repository is closed.</param>
    /// <param name="history">The graph page, whose uncommitted row navigates to the changes page.</param>
    /// <param name="conflicts">The conflicts page, whose progress the banner shows.</param>
    /// <param name="sync">Backs the toolbar's fetch, pull and push.</param>
    /// <param name="merges">Backs the banner's way out of a merge.</param>
    public MainWindowViewModel(
        IShellNavigation shell,
        IRepositoryContext repositoryContext,
        IAppWindows windows,
        HistoryPageViewModel history,
        ConflictResolutionPageViewModel conflicts,
        ISyncOperations sync,
        IMergeOperations merges)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(repositoryContext);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(conflicts);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(merges);

        // The graph's uncommitted row belongs to the working directory page, and the shell is the
        // only thing that knows how to get there.
        history.WorkingDirectoryRequested += (_, _) => shell.GoTo(ShellPage.Changes);

        Shell = shell;
        Conflicts = conflicts;
        RepositoryContext = repositoryContext;
        _windows = windows;

        _sync = sync;
        _merges = merges;

        ToggleThemeCommand = new RelayCommand(OnToggleTheme);

        FetchCommand = new AsyncRelayCommand(RunSyncAsync(_sync.FetchAsync), () => CanSync);
        PullCommand = new AsyncRelayCommand(RunSyncAsync(_sync.PullAsync), () => CanSync);
        PushCommand = new AsyncRelayCommand(RunSyncAsync(() => _sync.PushAsync()), () => CanSync);

        AbortMergeCommand = new AsyncRelayCommand(OnAbortMergeAsync, () => IsMergeInProgress);
        ResolveConflictsCommand = new RelayCommand(() => Shell.GoTo(ShellPage.Conflicts), () => IsMergeInProgress);
        RefreshCommand = new AsyncRelayCommand(OnRefreshAsync, () => RepositoryContext.IsRepositoryOpen);
        CloseRepositoryCommand = new RelayCommand(OnCloseRepository);

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

    /// <summary>
    /// Gets the command that closes the repository and goes back to the start window.
    /// </summary>
    public RelayCommand CloseRepositoryCommand { get; }

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
    /// Selects the first page, the first time the window opens. Called once the window is on
    /// screen rather than while the container is still wiring itself up, because selecting a page
    /// builds it.
    /// </summary>
    /// <returns>A task that completes once the page is selected.</returns>
    public Task InitialiseAsync()
    {
        if (!_initialised)
        {
            _initialised = true;
            Shell.Start();
        }

        return Task.CompletedTask;
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

    /// <summary>
    /// Closes the repository and hands the screen back to the start window, which is where another
    /// one is chosen.
    /// </summary>
    private void OnCloseRepository()
    {
        RepositoryContext.Close();
        _windows.ShowStart();
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
