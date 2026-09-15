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
    private readonly ILogger<MainWindowViewModel> _logger;
    private bool _initialised;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="shell">Owns the navigation rail and the pages on it.</param>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="gitEnvironment">Probes the host's git installation at startup.</param>
    /// <param name="dialogService">Shows the blocking dialog when git is unusable.</param>
    /// <param name="logger">Receives startup failures.</param>
    public MainWindowViewModel(
        IShellNavigation shell,
        IRepositoryContext repositoryContext,
        IGitEnvironment gitEnvironment,
        IContentDialogService dialogService,
        ILogger<MainWindowViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(repositoryContext);
        ArgumentNullException.ThrowIfNull(gitEnvironment);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(logger);

        Shell = shell;
        RepositoryContext = repositoryContext;
        _gitEnvironment = gitEnvironment;
        _dialogService = dialogService;
        _logger = logger;

        ToggleThemeCommand = new RelayCommand(OnToggleTheme);
        RefreshCommand = new AsyncRelayCommand(OnRefreshAsync, () => RepositoryContext.IsRepositoryOpen);

        RepositoryContext.PropertyChanged += OnRepositoryContextPropertyChanged;
    }

    /// <summary>
    /// Gets the shell navigation the rail and the content area bind to.
    /// </summary>
    public IShellNavigation Shell { get; }

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
                RefreshCommand.NotifyCanExecuteChanged();
                break;
            case nameof(IRepositoryContext.Head):
                OnPropertyChanged(nameof(HeadDisplayName));
                OnPropertyChanged(nameof(IsHeadDetached));
                OnPropertyChanged(nameof(HasOperationInProgress));
                OnPropertyChanged(nameof(OperationDescription));
                break;
            default:
                break;
        }
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
