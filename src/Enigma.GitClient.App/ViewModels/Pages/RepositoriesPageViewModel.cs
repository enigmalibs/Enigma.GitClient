using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Controls;
using Enigma.GitClient.App.Navigation;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.Views.Dialogs;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// The landing page: the repositories the user has opened before, and the three ways to get a new
/// one — open, clone, create.
/// </summary>
public sealed class RepositoriesPageViewModel : PageViewModelBase
{
    private readonly IRepositoryService _repositories;
    private readonly IRecentRepositoryStore _recentStore;
    private readonly IFolderDialogService _folderDialogs;
    private readonly IContentDialogService _dialogs;
    private readonly IOverlayService _overlay;
    private readonly IInfoBarService _infoBar;
    private readonly IShellNavigation _shell;
    private readonly IServiceProvider _services;
    private readonly ILogger<RepositoriesPageViewModel> _logger;

    private CancellationTokenSource? _cloneCancellation;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="repositories">Opens, creates and clones repositories.</param>
    /// <param name="recentStore">Remembers what has been opened.</param>
    /// <param name="folderDialogs">Raises the folder picker.</param>
    /// <param name="dialogs">Shows the clone and create dialogs.</param>
    /// <param name="overlay">Shows clone progress.</param>
    /// <param name="infoBar">Reports outcomes.</param>
    /// <param name="shell">Navigates to the history once a repository is open.</param>
    /// <param name="services">Resolves the dialog views.</param>
    /// <param name="logger">Receives the detail behind a reported failure.</param>
    public RepositoriesPageViewModel(
        IRepositoryContext repositoryContext,
        IRepositoryService repositories,
        IRecentRepositoryStore recentStore,
        IFolderDialogService folderDialogs,
        IContentDialogService dialogs,
        IOverlayService overlay,
        IInfoBarService infoBar,
        IShellNavigation shell,
        IServiceProvider services,
        ILogger<RepositoriesPageViewModel> logger)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(recentStore);
        ArgumentNullException.ThrowIfNull(folderDialogs);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        _repositories = repositories;
        _recentStore = recentStore;
        _folderDialogs = folderDialogs;
        _dialogs = dialogs;
        _overlay = overlay;
        _infoBar = infoBar;
        _shell = shell;
        _services = services;
        _logger = logger;

        OpenCommand = new AsyncRelayCommand(OnOpenAsync, () => IsNotBusy);
        CloneCommand = new AsyncRelayCommand(OnCloneAsync, () => IsNotBusy);
        CreateCommand = new AsyncRelayCommand(OnCreateAsync, () => IsNotBusy);
        OpenRecentCommand = new AsyncRelayCommand<RecentRepository>(OnOpenRecentAsync, _ => IsNotBusy);
        ForgetRecentCommand = new AsyncRelayCommand<RecentRepository>(OnForgetRecentAsync);
        TogglePinCommand = new AsyncRelayCommand<RecentRepository>(OnTogglePinAsync);
        CancelCloneCommand = new RelayCommand(OnCancelClone);
    }

    /// <summary>
    /// Gets the repositories the user has opened before.
    /// </summary>
    public ObservableCollection<RecentRepository> Recent { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the recent list is empty, which the empty state binds to.
    /// </summary>
    public bool HasRecent => Recent.Count > 0;

    /// <summary>Gets the command that opens an existing repository from a folder picker.</summary>
    public AsyncRelayCommand OpenCommand { get; }

    /// <summary>Gets the command that clones a repository.</summary>
    public AsyncRelayCommand CloneCommand { get; }

    /// <summary>Gets the command that creates a repository.</summary>
    public AsyncRelayCommand CreateCommand { get; }

    /// <summary>Gets the command that opens a repository from the recent list.</summary>
    public AsyncRelayCommand<RecentRepository> OpenRecentCommand { get; }

    /// <summary>Gets the command that forgets a repository.</summary>
    public AsyncRelayCommand<RecentRepository> ForgetRecentCommand { get; }

    /// <summary>Gets the command that pins or unpins a repository.</summary>
    public AsyncRelayCommand<RecentRepository> TogglePinCommand { get; }

    /// <summary>Gets the command that cancels a clone in progress.</summary>
    public RelayCommand CancelCloneCommand { get; }

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);
        await ReloadRecentAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Re-reads the recent list from the store.
    /// </summary>
    /// <returns>A task that completes once the list has been refreshed.</returns>
    public async Task ReloadRecentAsync()
    {
        IReadOnlyList<RecentRepository> entries = await _recentStore.GetAllAsync().ConfigureAwait(true);
        Replace(entries);
    }

    /// <summary>
    /// Opens a repository by path: discovers it, publishes it, remembers it and shows its history.
    /// </summary>
    /// <param name="path">A path inside the repository.</param>
    /// <returns><see langword="true"/> when a repository was opened.</returns>
    public async Task<bool> OpenPathAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        RepositoryDiscoveryResult discovery = await _repositories.OpenAsync(path).ConfigureAwait(true);

        if (!discovery.IsFound)
        {
            await ReportAsync("Cannot open that folder", discovery.Message, InfoBarSeverity.Warning)
                .ConfigureAwait(true);
            return false;
        }

        RepositoryHandle repository = discovery.Repository!;

        await RepositoryContext.OpenAsync(repository).ConfigureAwait(true);
        Replace(await _recentStore.TouchAsync(repository.WorkTreePath, repository.Name).ConfigureAwait(true));

        _shell.GoTo(ShellPage.History);
        return true;
    }

    /// <inheritdoc />
    protected override void OnBusyChanged()
    {
        OpenCommand.NotifyCanExecuteChanged();
        CloneCommand.NotifyCanExecuteChanged();
        CreateCommand.NotifyCanExecuteChanged();
        OpenRecentCommand.NotifyCanExecuteChanged();
    }

    private async Task OnOpenAsync()
    {
        IEnumerable<string> folders = await _folderDialogs
            .ShowOpenFolderDialogAsync(title: "Open a repository", allowMultiple: false)
            .ConfigureAwait(true);

        string? chosen = folders.FirstOrDefault();

        if (chosen is { Length: > 0 })
        {
            await RunBusyAsync(() => OpenPathAsync(chosen)).ConfigureAwait(true);
        }
    }

    private async Task OnOpenRecentAsync(RecentRepository? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (!entry.Exists)
        {
            await ReportAsync(
                    "That repository has moved",
                    $"'{entry.Path}' no longer exists. Forget it, or open it from its new location.",
                    InfoBarSeverity.Warning)
                .ConfigureAwait(true);
            return;
        }

        await RunBusyAsync(() => OpenPathAsync(entry.Path)).ConfigureAwait(true);
    }

    private async Task OnForgetRecentAsync(RecentRepository? entry)
    {
        if (entry is null)
        {
            return;
        }

        // Forgetting only removes the entry from this list; it never touches the repository.
        Replace(await _recentStore.RemoveAsync(entry.Path).ConfigureAwait(true));
    }

    private async Task OnTogglePinAsync(RecentRepository? entry)
    {
        if (entry is null)
        {
            return;
        }

        Replace(await _recentStore.SetPinnedAsync(entry.Path, !entry.IsPinned).ConfigureAwait(true));
    }

    private async Task OnCreateAsync()
    {
        InitRepositoryDialogViewModel model = new(_folderDialogs, DefaultParentDirectory());
        InitRepositoryDialogView view = _services.GetService(typeof(InitRepositoryDialogView)) as InitRepositoryDialogView
            ?? new InitRepositoryDialogView();
        view.DataContext = model;

        DialogResult result = await ShowFormAsync(
                "Create a repository",
                view,
                model.IsValid,
                handler => model.ValidationChanged += handler,
                handler => model.ValidationChanged -= handler,
                () => model.IsValid,
                "Create")
            .ConfigureAwait(true);

        if (result != DialogResult.Primary || !model.IsValid)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            try
            {
                RepositoryHandle repository = await _repositories
                    .InitAsync(model.TargetPath, model.InitialBranch.Trim())
                    .ConfigureAwait(true);

                await RepositoryContext.OpenAsync(repository).ConfigureAwait(true);
                Replace(await _recentStore.TouchAsync(repository.WorkTreePath, repository.Name).ConfigureAwait(true));

                await ReportAsync(
                        "Repository created",
                        $"{repository.Name} is ready on branch {model.InitialBranch.Trim()}.",
                        InfoBarSeverity.Success)
                    .ConfigureAwait(true);

                _shell.GoTo(ShellPage.History);
                return true;
            }
            catch (Exception exception) when (exception is GitCommandException or InvalidOperationException or System.IO.IOException)
            {
                _logger.LogError(exception, "Creating a repository at {Path} failed", model.TargetPath);
                await ReportAsync("The repository could not be created", exception.Message, InfoBarSeverity.Error)
                    .ConfigureAwait(true);
                return false;
            }
        }).ConfigureAwait(true);
    }

    private async Task OnCloneAsync()
    {
        CloneRepositoryDialogViewModel model = new(_folderDialogs, _repositories, DefaultParentDirectory());
        CloneRepositoryDialogView view = _services.GetService(typeof(CloneRepositoryDialogView)) as CloneRepositoryDialogView
            ?? new CloneRepositoryDialogView();
        view.DataContext = model;

        DialogResult result = await ShowFormAsync(
                "Clone a repository",
                view,
                model.IsValid,
                handler => model.ValidationChanged += handler,
                handler => model.ValidationChanged -= handler,
                () => model.IsValid,
                "Clone")
            .ConfigureAwait(true);

        if (result != DialogResult.Primary || !model.IsValid)
        {
            return;
        }

        await RunCloneAsync(model.ToRequest()).ConfigureAwait(true);
    }

    /// <summary>
    /// Runs a clone with the progress overlay and a working cancel.
    /// </summary>
    /// <param name="request">What to clone.</param>
    /// <returns><see langword="true"/> when the clone finished and the repository was opened.</returns>
    public async Task<bool> RunCloneAsync(CloneRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using CancellationTokenSource cancellation = new();
        _cloneCancellation = cancellation;

        ProgressOverlayCard card = new()
        {
            Title = $"Cloning {CloneRequest.DeriveDirectoryName(request.Url)}",
            Message = "Starting…",
            IsIndeterminate = true,
            CancelCommand = CancelCloneCommand,
        };

        Progress<CloneProgress> progress = new(card.Apply);

        await _overlay.ShowAsync(card).ConfigureAwait(true);
        IsBusy = true;

        try
        {
            RepositoryHandle repository = await _repositories
                .CloneAsync(request, progress, cancellation.Token)
                .ConfigureAwait(true);

            await RepositoryContext.OpenAsync(repository).ConfigureAwait(true);
            Replace(await _recentStore.TouchAsync(repository.WorkTreePath, repository.Name).ConfigureAwait(true));

            await ReportAsync("Clone finished", $"{repository.Name} is ready.", InfoBarSeverity.Success)
                .ConfigureAwait(true);

            _shell.GoTo(ShellPage.History);
            return true;
        }
        catch (OperationCanceledException)
        {
            await ReportAsync("Clone cancelled", "Nothing was left behind.", InfoBarSeverity.Info)
                .ConfigureAwait(true);
            return false;
        }
        catch (Exception exception) when (exception is GitCommandException or ArgumentException or InvalidOperationException)
        {
            _logger.LogError(exception, "Cloning {Url} failed", ArgumentRedactor.Redact(request.Url));
            await ReportAsync("The clone failed", exception.Message, InfoBarSeverity.Error).ConfigureAwait(true);
            return false;
        }
        finally
        {
            IsBusy = false;
            _cloneCancellation = null;

            // Always: an overlay left open makes the whole window unusable.
            await _overlay.HideAsync().ConfigureAwait(true);
        }
    }

    private void OnCancelClone() => _cloneCancellation?.Cancel();

    /// <summary>
    /// Shows a dialog whose primary button follows a ViewModel's validation.
    /// </summary>
    private async Task<DialogResult> ShowFormAsync(
        string title,
        Control content,
        bool initiallyValid,
        Action<EventHandler> subscribe,
        Action<EventHandler> unsubscribe,
        Func<bool> isValid,
        string primaryButtonText)
    {
        ContentDialog? shown = null;

        void OnValidationChanged(object? sender, EventArgs e)
        {
            if (shown is not null)
            {
                shown.IsPrimaryButtonEnabled = isValid();
            }
        }

        subscribe(OnValidationChanged);

        try
        {
            return await _dialogs.ShowAsync(dialog =>
            {
                shown = dialog;
                dialog.Title = title;
                dialog.Content = content;
                dialog.PrimaryButtonText = primaryButtonText;
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
                dialog.IsPrimaryButtonEnabled = initiallyValid;
            }).ConfigureAwait(true);
        }
        finally
        {
            unsubscribe(OnValidationChanged);
        }
    }

    private async Task RunBusyAsync(Func<Task<bool>> operation)
    {
        IsBusy = true;

        try
        {
            await operation().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ReportAsync(string title, string message, InfoBarSeverity severity)
        => _infoBar.ShowAsync(bar =>
        {
            bar.Title = title;
            bar.Message = message;
            bar.Severity = severity;
        });

    private void Replace(IReadOnlyList<RecentRepository> entries)
    {
        Recent.Clear();

        foreach (RecentRepository entry in entries)
        {
            Recent.Add(entry);
        }

        OnPropertyChanged(nameof(HasRecent));
    }

    /// <summary>
    /// Where a clone is created unless the user says otherwise.
    /// </summary>
    /// <returns>The absolute directory path.</returns>
    /// <remarks>
    /// Public because the integrations page clones from a repository the user picked on a host,
    /// with no dialog to choose a directory in.
    /// </remarks>
    public static string DefaultParentDirectory()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return documents.Length == 0 ? Environment.CurrentDirectory : documents;
    }
}
