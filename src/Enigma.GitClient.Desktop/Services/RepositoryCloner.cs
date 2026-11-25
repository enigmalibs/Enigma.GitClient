using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Controls;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Clones a repository the way every page that clones does: with the progress overlay and a working
/// cancel, the offer of the global identity, the repository opened and added to the list, and the
/// repository window shown.
/// </summary>
/// <remarks>
/// The repositories page clones from its dialog. The profiles page clones a repository picked on a
/// host, with no dialog. Both go through here, so neither page depends on the other.
/// </remarks>
public interface IRepositoryCloner
{
    /// <summary>
    /// Gets where a clone goes when nothing else is chosen: the profile's base directory, else the
    /// directory of the last clone, else the user's home.
    /// </summary>
    /// <param name="profile">
    /// The profile the clone is for — the one selected on the repositories page, or the one whose
    /// integration it comes from; <see langword="null"/> for none.
    /// </param>
    /// <returns>The absolute directory path.</returns>
    /// <remarks>
    /// A directory set on purpose for the profile comes before one remembered for every profile; a
    /// directory that has gone — a drive unplugged, a folder removed — is not offered.
    /// </remarks>
    string CloneParentDirectory(IdentityProfile? profile = null);

    /// <summary>
    /// Runs a clone with the progress overlay and a working cancel, then opens the repository, adds it
    /// to the selected profile's list and shows the repository window.
    /// </summary>
    /// <param name="request">What to clone.</param>
    /// <returns><see langword="true"/> when the clone finished and the repository was opened.</returns>
    Task<bool> RunCloneAsync(CloneRequest request);

    /// <summary>
    /// Cancels the clone in progress, if there is one. What the overlay's cancel button does.
    /// </summary>
    void CancelClone();

    /// <summary>
    /// Asks whether a new clone or repository should commit as the global identity whatever it later
    /// becomes, and writes it into the repository's own configuration when told to.
    /// </summary>
    /// <param name="repository">The clone or the new repository.</param>
    /// <param name="what">What the repository is to the reader: <c>clone</c> or <c>repository</c>.</param>
    /// <returns>A task that completes once the question is answered and the answer applied.</returns>
    /// <remarks>
    /// Nothing is asked without a complete global identity — there is nothing to copy — and nothing is
    /// written unless the reader says so. A write that fails leaves the repository as git made it, and
    /// says so. Names and emails are never logged.
    /// </remarks>
    Task OfferGlobalIdentityAsync(RepositoryHandle repository, string what);
}

/// <summary>
/// Default <see cref="IRepositoryCloner"/>.
/// </summary>
public sealed class RepositoryCloner : IRepositoryCloner
{
    private readonly IRepositoryService _repositories;
    private readonly IRepositoryContext _context;
    private readonly IRepositoryListStore _lists;
    private readonly IProfileSelection _selection;
    private readonly IGitIdentityService _identity;
    private readonly ISettingsService _settings;
    private readonly IOverlayService _overlay;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly IAppWindows _windows;
    private readonly ILogger<RepositoryCloner> _logger;
    private readonly RelayCommand _cancelCommand;

    private CancellationTokenSource? _cloneCancellation;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositories">Runs the clone.</param>
    /// <param name="context">Opens the clone.</param>
    /// <param name="lists">Adds the clone to the selected profile's list.</param>
    /// <param name="selection">Says which profile's list that is.</param>
    /// <param name="identity">Reads the global identity and writes the repository's own.</param>
    /// <param name="settings">Remembers where the last clone was made.</param>
    /// <param name="overlay">Shows the clone's progress.</param>
    /// <param name="dialogs">Asks about the identity.</param>
    /// <param name="infoBar">Reports the outcome.</param>
    /// <param name="windows">Shows the repository window once the clone is open.</param>
    /// <param name="logger">Receives the detail behind a reported failure.</param>
    public RepositoryCloner(
        IRepositoryService repositories,
        IRepositoryContext context,
        IRepositoryListStore lists,
        IProfileSelection selection,
        IGitIdentityService identity,
        ISettingsService settings,
        IOverlayService overlay,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        IAppWindows windows,
        ILogger<RepositoryCloner> logger)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(logger);

        _repositories = repositories;
        _context = context;
        _lists = lists;
        _selection = selection;
        _identity = identity;
        _settings = settings;
        _overlay = overlay;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _windows = windows;
        _logger = logger;
        _cancelCommand = new RelayCommand(CancelClone);
    }

    /// <summary>
    /// The directory a profile's repositories live in, while it is a full path to a directory that
    /// exists; <see langword="null"/> otherwise, which is the same as the profile naming none.
    /// </summary>
    /// <param name="profile">The profile, or <see langword="null"/>.</param>
    /// <returns>The directory, or <see langword="null"/>.</returns>
    public static string? BaseDirectoryOf(IdentityProfile? profile)
    {
        string directory = profile?.BaseDirectory?.Trim() ?? string.Empty;

        return directory.Length > 0
            && System.IO.Path.IsPathFullyQualified(directory)
            && System.IO.Directory.Exists(directory)
                ? directory
                : null;
    }

    /// <summary>
    /// Where a new repository is created, and a clone when none has been made yet.
    /// </summary>
    /// <returns>The absolute directory path.</returns>
    public static string DefaultParentDirectory()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return documents.Length == 0 ? Environment.CurrentDirectory : documents;
    }

    /// <inheritdoc />
    public string CloneParentDirectory(IdentityProfile? profile = null)
    {
        if (BaseDirectoryOf(profile) is { } baseDirectory)
        {
            return baseDirectory;
        }

        string remembered = _settings.Current.CloneParentDirectory;

        return remembered.Length > 0 && System.IO.Directory.Exists(remembered)
            ? remembered
            : DefaultParentDirectory();
    }

    /// <inheritdoc />
    public void CancelClone() => _cloneCancellation?.Cancel();

    /// <inheritdoc />
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
            CancelCommand = _cancelCommand,
        };

        Progress<CloneProgress> progress = new(card.Apply);

        await _overlay.ShowAsync(card).ConfigureAwait(true);
        bool overlayShown = true;

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

            // The progress is over, and the overlay is drawn above the dialogs: it goes before the
            // question is asked.
            await _overlay.HideAsync().ConfigureAwait(true);
            overlayShown = false;

            await OfferGlobalIdentityAsync(repository, "clone").ConfigureAwait(true);

            await _context.OpenAsync(repository).ConfigureAwait(true);
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
            _cloneCancellation = null;

            // Always: an overlay left open makes the whole window unusable.
            if (overlayShown)
            {
                await _overlay.HideAsync().ConfigureAwait(true);
            }
        }
    }

    /// <inheritdoc />
    public async Task OfferGlobalIdentityAsync(RepositoryHandle repository, string what)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(what);

        GitIdentity global;

        try
        {
            global = (await _identity.GetGlobalAsync().ConfigureAwait(true)).Normalised();
        }
        catch (Exception exception) when (exception is GitCommandException or GitNotFoundException)
        {
            _logger.LogWarning(
                "Reading the global git identity for a new {What} failed ({Kind})",
                what,
                exception.GetType().Name);
            return;
        }

        if (!global.IsComplete)
        {
            return;
        }

        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "Use your identity in this repository";
            dialog.Content =
                $"Make {global} the name and email of {repository.Name}?\n\n"
                + "They are written to the repository's own configuration, so its commits are made as "
                + "them whatever the global identity becomes later.";
            dialog.PrimaryButtonText = "Use it";
            dialog.CloseButtonText = "Not now";
            dialog.DefaultButton = DefaultButton.Primary;
        }).ConfigureAwait(true);

        if (answer != DialogResult.Primary)
        {
            return;
        }

        try
        {
            await _identity.SetLocalAsync(repository, global).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is GitCommandException or GitNotFoundException or ArgumentException)
        {
            _logger.LogWarning(
                "Writing the global git identity into a new {What} failed ({Kind})",
                what,
                exception.GetType().Name);

            Report(
                $"The {what} has no identity of its own",
                $"{repository.Name} commits with the global identity. {ProfilesPageViewModel.Describe(exception)}",
                InfoBarSeverity.Warning);
        }
    }

    /// <summary>
    /// Adds the clone to the list of the profile in use now — creating the default profile when there is
    /// none — read as it is stored: a clone started from the profiles page comes before the repositories
    /// page was ever shown.
    /// </summary>
    private async Task AddToListAsync(RepositoryHandle repository)
    {
        ProfileChoice choice = await _selection.LoadAsync().ConfigureAwait(true);

        await _lists.AddAsync(choice.Selected.Id, repository.WorkTreePath, repository.Name).ConfigureAwait(true);
    }

    private void Report(string title, string message, InfoBarSeverity severity)
        => _infoBar.Notify(title, message, severity);
}
