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
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.Views.Dialogs;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// The landing page: the selected profile's list of repositories, in the user's order, and the three
/// ways to get a new one — open, clone, create.
/// </summary>
public sealed class RepositoriesPageViewModel : PageViewModelBase
{
    private readonly IRepositoryService _repositories;
    private readonly IRepositoryListStore _lists;
    private readonly IProfileSelection _selection;
    private readonly ISettingsService _settings;
    private readonly IFolderDialogService _folderDialogs;
    private readonly IContentDialogService _dialogs;
    private readonly IOverlayService _overlay;
    private readonly IInfoBarService _infoBar;
    private readonly IRepositoryOpener _opener;
    private readonly IAppWindows _windows;
    private readonly IInstanceLauncher _launcher;
    private readonly IAboutDialogService _about;
    private readonly IThemeSwitcher _theme;
    private readonly IServiceProvider _services;
    private readonly ILogger<RepositoriesPageViewModel> _logger;

    private CancellationTokenSource? _cloneCancellation;
    private bool _showingProfiles;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="repositories">Opens, creates and clones repositories.</param>
    /// <param name="lists">Keeps each profile's list of repositories.</param>
    /// <param name="selection">Says whose list is shown.</param>
    /// <param name="settings">Remembers where the last clone was made.</param>
    /// <param name="folderDialogs">Raises the folder picker.</param>
    /// <param name="dialogs">Shows the clone and create dialogs.</param>
    /// <param name="overlay">Shows clone progress.</param>
    /// <param name="infoBar">Reports outcomes.</param>
    /// <param name="opener">Opens a repository by path, the same way the command line does.</param>
    /// <param name="windows">Swaps this window for the repository window once a repository is open.</param>
    /// <param name="launcher">Starts another instance on a repository, for working on two at once.</param>
    /// <param name="about">Shows the About dialog from the header, as the repository window's toolbar does.</param>
    /// <param name="theme">The theme switch beside it, the repository window's own.</param>
    /// <param name="services">Resolves the dialog views.</param>
    /// <param name="logger">Receives the detail behind a reported failure.</param>
    public RepositoriesPageViewModel(
        IRepositoryContext repositoryContext,
        IRepositoryService repositories,
        IRepositoryListStore lists,
        IProfileSelection selection,
        ISettingsService settings,
        IFolderDialogService folderDialogs,
        IContentDialogService dialogs,
        IOverlayService overlay,
        IInfoBarService infoBar,
        IRepositoryOpener opener,
        IAppWindows windows,
        IInstanceLauncher launcher,
        IAboutDialogService about,
        IThemeSwitcher theme,
        IServiceProvider services,
        ILogger<RepositoriesPageViewModel> logger)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(folderDialogs);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(opener);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(about);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        _repositories = repositories;
        _lists = lists;
        _selection = selection;
        _settings = settings;
        _folderDialogs = folderDialogs;
        _dialogs = dialogs;
        _overlay = overlay;
        _infoBar = infoBar;
        _opener = opener;
        _windows = windows;
        _launcher = launcher;
        _about = about;
        _theme = theme;
        _services = services;
        _logger = logger;

        OpenCommand = new AsyncRelayCommand(OnOpenAsync, () => IsNotBusy);
        CloneCommand = new AsyncRelayCommand(OnCloneAsync, () => IsNotBusy);
        CreateCommand = new AsyncRelayCommand(OnCreateAsync, () => IsNotBusy);
        OpenListedCommand = new AsyncRelayCommand<ListedRepository>(OnOpenListedAsync, _ => IsNotBusy);
        OpenInNewWindowCommand = new RelayCommand<ListedRepository>(OnOpenInNewWindow);
        ForgetCommand = new AsyncRelayCommand<ListedRepository>(OnForgetAsync);
        CancelCloneCommand = new RelayCommand(OnCancelClone);
        OpenAboutCommand = new AsyncRelayCommand(_about.ShowAsync);
        ToggleThemeCommand = new RelayCommand(_theme.Toggle);
    }

    /// <summary>
    /// Gets every profile, for the picker in the page's header.
    /// </summary>
    public ObservableCollection<IdentityProfile> Profiles { get; } = [];

    /// <summary>
    /// Gets or sets the profile whose repositories are listed.
    /// </summary>
    /// <remarks>
    /// Choosing one is remembered and shows its list. It never changes the identity git commits with:
    /// that is the Profiles page's Use.
    /// </remarks>
    public IdentityProfile? SelectedProfile
    {
        get;
        set
        {
            // The picker pushes a null of its own while its items are being replaced; that, and the
            // page putting back the profile it has just read, are not the user choosing anything.
            if (SetProperty(ref field, value) && value is not null && !_showingProfiles)
            {
                _selection.Select(value.Id);
                _ = ShowListOfAsync(value.Id);
            }
        }
    }

    /// <summary>
    /// Gets the selected profile's repositories, in the user's order.
    /// </summary>
    public ObservableCollection<ListedRepository> Repositories { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the list has anything in it, which the empty state binds to.
    /// </summary>
    public bool HasRepositories => Repositories.Count > 0;

    /// <summary>Gets the command that opens an existing repository from a folder picker.</summary>
    public AsyncRelayCommand OpenCommand { get; }

    /// <summary>Gets the command that clones a repository.</summary>
    public AsyncRelayCommand CloneCommand { get; }

    /// <summary>Gets the command that creates a repository.</summary>
    public AsyncRelayCommand CreateCommand { get; }

    /// <summary>Gets the command that opens a repository from the list.</summary>
    public AsyncRelayCommand<ListedRepository> OpenListedCommand { get; }

    /// <summary>
    /// Gets the command that opens a repository from the list in another instance, leaving this window
    /// as it is.
    /// </summary>
    public RelayCommand<ListedRepository> OpenInNewWindowCommand { get; }

    /// <summary>Gets the command that takes a repository out of the list.</summary>
    public AsyncRelayCommand<ListedRepository> ForgetCommand { get; }

    /// <summary>
    /// Gets the command that shows the About dialog. The start window has no toolbar of its own, and
    /// this page's header is where it keeps its actions.
    /// </summary>
    public AsyncRelayCommand OpenAboutCommand { get; }

    /// <summary>
    /// Gets the command that switches between the dark and light themes and records the choice, as the
    /// repository window's toolbar does.
    /// </summary>
    public RelayCommand ToggleThemeCommand { get; }

    /// <summary>Gets the command that cancels a clone in progress.</summary>
    public RelayCommand CancelCloneCommand { get; }

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);
        await ReloadListAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Re-reads the profiles, and the selected profile's list, from their stores.
    /// </summary>
    /// <returns>A task that completes once the picker and the list have been refreshed.</returns>
    /// <remarks>
    /// Every time the page is shown: a profile added, renamed or deleted on the Profiles page is in the
    /// picker when the reader comes back.
    /// </remarks>
    public async Task ReloadListAsync()
    {
        ProfileChoice choice = await _selection.LoadAsync().ConfigureAwait(true);

        ShowProfiles(choice);

        Replace(await _lists.GetAsync(choice.Selected.Id).ConfigureAwait(true));
    }

    /// <summary>
    /// Moves a repository to another place in the list, which is where the view's drag drops it.
    /// </summary>
    /// <param name="entry">The repository being moved.</param>
    /// <param name="index">Where it goes, counted in the list without it.</param>
    /// <returns>A task that completes once the list shows the new order, which is already stored.</returns>
    public async Task MoveAsync(ListedRepository entry, int index)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string profileId = await SelectedProfileIdAsync().ConfigureAwait(true);

        Replace(await _lists.MoveAsync(profileId, entry.Path, index).ConfigureAwait(true));
    }

    /// <summary>
    /// Opens a repository by path: discovers it, publishes it, adds it to the list and shows its history.
    /// </summary>
    /// <param name="path">A path inside the repository.</param>
    /// <returns><see langword="true"/> when a repository was opened.</returns>
    public async Task<bool> OpenPathAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        RepositoryDiscoveryResult discovery = await _opener.OpenAsync(path).ConfigureAwait(true);

        if (!discovery.IsFound)
        {
            Report("Cannot open that folder", discovery.Message, InfoBarSeverity.Warning);
            return false;
        }

        await ReloadListAsync().ConfigureAwait(true);

        _windows.ShowRepository();
        return true;
    }

    /// <inheritdoc />
    protected override void OnBusyChanged()
    {
        OpenCommand.NotifyCanExecuteChanged();
        CloneCommand.NotifyCanExecuteChanged();
        CreateCommand.NotifyCanExecuteChanged();
        OpenListedCommand.NotifyCanExecuteChanged();
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

    private async Task OnOpenListedAsync(ListedRepository? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (!entry.Exists)
        {
            Report(
                "That repository has moved",
                $"'{entry.Path}' no longer exists. Forget it, or open it from its new location.",
                InfoBarSeverity.Warning);
            return;
        }

        await RunBusyAsync(() => OpenPathAsync(entry.Path)).ConfigureAwait(true);
    }

    private void OnOpenInNewWindow(ListedRepository? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (!entry.Exists)
        {
            Report(
                "That repository has moved",
                $"'{entry.Path}' no longer exists. Forget it, or open it from its new location.",
                InfoBarSeverity.Warning);
            return;
        }

        if (!_launcher.Launch(entry.Path))
        {
            Report(
                "No new window",
                "Another instance of Enigma.GitClient could not be started.",
                InfoBarSeverity.Error);
        }
    }

    private async Task OnForgetAsync(ListedRepository? entry)
    {
        if (entry is null)
        {
            return;
        }

        // Forgetting only removes the entry from this list; it never touches the repository.
        string profileId = await SelectedProfileIdAsync().ConfigureAwait(true);

        Replace(await _lists.RemoveAsync(profileId, entry.Path).ConfigureAwait(true));
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
                string branch = model.InitialBranch.Trim();
                RepositoryHandle repository = await _repositories
                    .InitAsync(model.TargetPath, branch)
                    .ConfigureAwait(true);

                // The first commit is a README holding the repository's name. A commit that fails —
                // no name and email configured, most often — leaves a real repository all the same,
                // so it is opened either way and the reader is told what is missing.
                string? firstCommitProblem = await CommitReadmeAsync(repository).ConfigureAwait(true);

                await RepositoryContext.OpenAsync(repository).ConfigureAwait(true);
                await AddToListAsync(repository).ConfigureAwait(true);

                if (firstCommitProblem is null)
                {
                    Report(
                        "Repository created",
                        $"{repository.Name} is ready on branch {branch}, with its README as the first commit.",
                        InfoBarSeverity.Success);
                }
                else
                {
                    Report(
                        "Repository created without its first commit",
                        $"{repository.Name} is ready on branch {branch}, but its README could not be committed: "
                        + $"{firstCommitProblem} It is staged, ready to commit from the history.",
                        InfoBarSeverity.Warning);
                }

                _windows.ShowRepository();
                return true;
            }
            catch (Exception exception) when (exception is GitCommandException or InvalidOperationException or System.IO.IOException)
            {
                _logger.LogError(exception, "Creating a repository at {Path} failed", model.TargetPath);
                Report("The repository could not be created", exception.Message, InfoBarSeverity.Error);
                return false;
            }
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Commits a new repository's README.
    /// </summary>
    /// <returns><see langword="null"/> once it is committed; otherwise git's reason, for the reader.</returns>
    private async Task<string?> CommitReadmeAsync(RepositoryHandle repository)
    {
        try
        {
            await _repositories.CommitReadmeAsync(repository, repository.Name).ConfigureAwait(true);
            return null;
        }
        catch (Exception exception) when (exception is GitCommandException or System.IO.IOException)
        {
            _logger.LogWarning(exception, "The first commit of {Path} failed", repository.WorkTreePath);
            return FirstCommitProblem(exception);
        }
    }

    /// <summary>
    /// Says why a first commit failed, in the reader's terms.
    /// </summary>
    /// <remarks>
    /// The usual reason gets a sentence of its own: git's "Please tell me who you are" runs to a dozen
    /// lines of commands to type, and the Profiles page is where this client sets a name and email.
    /// </remarks>
    internal static string FirstCommitProblem(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is GitCommandException { StandardError: { } error }
            && (error.Contains("Please tell me who you are", StringComparison.OrdinalIgnoreCase)
                || error.Contains("empty ident name", StringComparison.OrdinalIgnoreCase)
                || error.Contains("unable to auto-detect email address", StringComparison.OrdinalIgnoreCase)))
        {
            return "git has no name and email to commit with. Set them on the Profiles page.";
        }

        return ProfilesPageViewModel.Describe(exception).TrimEnd('.') + ".";
    }

    private async Task OnCloneAsync()
    {
        CloneRepositoryDialogViewModel model = new(_folderDialogs, _repositories, CloneParentDirectory());
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

            // Only once it has worked: a clone that failed says nothing about where the next one goes.
            _settings.Update(current => current with
            {
                CloneParentDirectory = System.IO.Path.GetFullPath(request.ParentDirectory),
            });

            await RepositoryContext.OpenAsync(repository).ConfigureAwait(true);
            await AddToListAsync(repository).ConfigureAwait(true);

            Report("Clone finished", $"{repository.Name} is ready.", InfoBarSeverity.Success);

            _windows.ShowRepository();
            return true;
        }
        catch (OperationCanceledException)
        {
            Report("Clone cancelled", "Nothing was left behind.", InfoBarSeverity.Info);
            return false;
        }
        catch (Exception exception) when (exception is GitCommandException or ArgumentException or InvalidOperationException)
        {
            _logger.LogError(exception, "Cloning {Url} failed", ArgumentRedactor.Redact(request.Url));
            Report("The clone failed", exception.Message, InfoBarSeverity.Error);
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

    private void Report(string title, string message, InfoBarSeverity severity)
        => _infoBar.Notify(title, message, severity);

    /// <summary>
    /// Adds a repository just created or cloned to the selected profile's list, at its end.
    /// </summary>
    private async Task AddToListAsync(RepositoryHandle repository)
    {
        string profileId = await SelectedProfileIdAsync().ConfigureAwait(true);

        Replace(await _lists.AddAsync(profileId, repository.WorkTreePath, repository.Name).ConfigureAwait(true));
    }

    /// <summary>
    /// Asks whose list is in use now — creating the default profile when there is none — rather than
    /// remembering it: a clone started from the profiles page reaches this page before it was ever shown.
    /// </summary>
    private async Task<string> SelectedProfileIdAsync()
    {
        ProfileChoice choice = await _selection.LoadAsync().ConfigureAwait(true);

        return choice.Selected.Id;
    }

    /// <summary>
    /// Puts the profiles in the picker, and the selected one in it, without taking either for a choice.
    /// </summary>
    private void ShowProfiles(ProfileChoice choice)
    {
        _showingProfiles = true;

        try
        {
            // Left alone when nothing changed, so the picker does not flicker through an empty
            // selection on every showing of the page.
            if (!Profiles.SequenceEqual(choice.Profiles))
            {
                Profiles.Clear();

                foreach (IdentityProfile profile in choice.Profiles)
                {
                    Profiles.Add(profile);
                }
            }

            SelectedProfile = choice.Selected;
        }
        finally
        {
            _showingProfiles = false;
        }
    }

    /// <summary>
    /// Shows a profile's list once the reader has picked it.
    /// </summary>
    private async Task ShowListOfAsync(string profileId)
    {
        IReadOnlyList<ListedRepository> entries = await _lists.GetAsync(profileId).ConfigureAwait(true);

        // A quicker second pick may have been made while this one was reading.
        if (string.Equals(SelectedProfile?.Id, profileId, StringComparison.Ordinal))
        {
            Replace(entries);
        }
    }

    private void Replace(IReadOnlyList<ListedRepository> entries)
    {
        Repositories.Clear();

        foreach (ListedRepository entry in entries)
        {
            Repositories.Add(entry);
        }

        OnPropertyChanged(nameof(HasRepositories));
    }

    /// <summary>
    /// Where the next clone is created unless the user says otherwise: the directory the last one
    /// was made in, while it still exists, and the home folder otherwise.
    /// </summary>
    /// <returns>The absolute directory path.</returns>
    /// <remarks>
    /// Public because the profiles page clones from a repository the user picked on a host, with no
    /// dialog to choose a directory in: that clone is the next clone as much as the dialog's is. A
    /// remembered directory that has gone — a drive unplugged, a folder removed — is not offered.
    /// </remarks>
    public string CloneParentDirectory()
    {
        string remembered = _settings.Current.CloneParentDirectory;

        return remembered.Length > 0 && System.IO.Directory.Exists(remembered)
            ? remembered
            : DefaultParentDirectory();
    }

    /// <summary>
    /// Where a new repository is created, and a clone when none has been made yet.
    /// </summary>
    /// <returns>The absolute directory path.</returns>
    private static string DefaultParentDirectory()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return documents.Length == 0 ? Environment.CurrentDirectory : documents;
    }
}
