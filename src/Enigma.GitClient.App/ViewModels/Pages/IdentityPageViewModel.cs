using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.Views.Dialogs;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Identity;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// One identity profile, as the identity page lists it.
/// </summary>
public sealed class IdentityProfileRowViewModel : ViewModelBase
{
    private readonly IdentityPageViewModel _owner;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="owner">The page the row belongs to.</param>
    /// <param name="profile">The profile it stands for.</param>
    public IdentityProfileRowViewModel(IdentityPageViewModel owner, IdentityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(profile);

        _owner = owner;
        Profile = profile;
    }

    /// <summary>Gets the profile this row stands for.</summary>
    public IdentityProfile Profile { get; }

    /// <summary>Gets what the profile is called.</summary>
    public string Label => Profile.Label;

    /// <summary>Gets the identity the profile sets, written the way a commit writes it.</summary>
    public string Summary => Profile.Identity.ToString();

    /// <summary>
    /// Gets a value indicating whether the profile is the identity git has now.
    /// </summary>
    public bool IsCurrent
    {
        get;
        internal set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsNotCurrent));
            }
        }
    }

    /// <summary>Gets a value indicating whether the profile can be switched to.</summary>
    public bool IsNotCurrent => !IsCurrent;

    /// <summary>Gets the command that makes this profile the global identity.</summary>
    public AsyncRelayCommand<IdentityProfileRowViewModel> UseCommand => _owner.UseProfileCommand;

    /// <summary>Gets the command that edits this profile.</summary>
    public AsyncRelayCommand<IdentityProfileRowViewModel> EditCommand => _owner.EditProfileCommand;

    /// <summary>Gets the command that deletes this profile.</summary>
    public AsyncRelayCommand<IdentityProfileRowViewModel> RemoveCommand => _owner.RemoveProfileCommand;

    /// <inheritdoc />
    public override string ToString() => $"{Label}: {Summary}";
}

/// <summary>
/// ViewModel behind the identity page: the name and email git records on every commit, and the
/// profiles that switch them.
/// </summary>
/// <remarks>
/// <para>
/// The page edits git's own configuration rather than a copy of it: what it shows is what git read a
/// moment ago, and a save is a <c>git config</c> write. Nothing is saved while typing — each save
/// rewrites a configuration file — so each section has its own Save, enabled once the values differ
/// from git's and are usable.
/// </para>
/// <para>
/// The current profile is not stored anywhere: it is whichever profile matches the identity git has,
/// so it stays true after the configuration was changed in a terminal.
/// </para>
/// <para>
/// Names and emails are never logged: they identify a person. A failure is logged by what failed.
/// </para>
/// </remarks>
public sealed class IdentityPageViewModel : PageViewModelBase
{
    private readonly IGitIdentityService _identity;
    private readonly IIdentityProfileStore _profiles;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly IServiceProvider _services;
    private readonly ILogger<IdentityPageViewModel> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="identity">Reads and writes git's identity.</param>
    /// <param name="profiles">Keeps the identity profiles.</param>
    /// <param name="dialogs">Raises the profile dialog and the delete confirmation.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="services">Resolves the dialog's view.</param>
    /// <param name="logger">Receives failures reported to the user another way.</param>
    public IdentityPageViewModel(
        IRepositoryContext repositoryContext,
        IGitIdentityService identity,
        IIdentityProfileStore profiles,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        IServiceProvider services,
        ILogger<IdentityPageViewModel> logger)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        _identity = identity;
        _profiles = profiles;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _services = services;
        _logger = logger;

        SaveGlobalCommand = new AsyncRelayCommand(OnSaveGlobalAsync, CanSaveGlobal);
        AddProfileCommand = new AsyncRelayCommand(OnAddProfileAsync, () => !IsBusy);
        EditProfileCommand = new AsyncRelayCommand<IdentityProfileRowViewModel>(OnEditProfileAsync);
        RemoveProfileCommand = new AsyncRelayCommand<IdentityProfileRowViewModel>(OnRemoveProfileAsync);
        UseProfileCommand = new AsyncRelayCommand<IdentityProfileRowViewModel>(OnUseProfileAsync);
    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Git identity";

    // ---------------------------------------------------------------- the global identity

    /// <summary>
    /// Gets the global identity as git last reported it.
    /// </summary>
    public GitIdentity GlobalIdentity
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsGlobalUnset));
                OnPropertyChanged(nameof(GlobalSummary));
                OnGlobalEdited();
                UpdateCurrentProfile();
            }
        }
    } = GitIdentity.Empty;

    /// <summary>Gets or sets the global name, as typed.</summary>
    public string GlobalName
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                OnGlobalEdited();
            }
        }
    } = string.Empty;

    /// <summary>Gets or sets the global email, as typed.</summary>
    public string GlobalEmail
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                OnGlobalEdited();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the typed values differ from what git has, trimmed.
    /// </summary>
    public bool IsGlobalChanged => !TypedGlobal.Equals(GlobalIdentity.Normalised());

    /// <summary>
    /// Gets what is wrong with the typed values, or an empty string. Only said once something has been
    /// typed: an identity git does not have yet is not a mistake of the reader's.
    /// </summary>
    public string GlobalError => IsGlobalChanged ? GitIdentityRules.Validate(TypedGlobal) ?? string.Empty : string.Empty;

    /// <summary>Gets a value indicating whether there is something wrong to say.</summary>
    public bool HasGlobalError => GlobalError.Length > 0;

    /// <summary>
    /// Gets a value indicating whether git has no complete global identity, which is when it refuses to
    /// commit in a repository that does not set one.
    /// </summary>
    public bool IsGlobalUnset => !GlobalIdentity.IsComplete;

    /// <summary>Gets the sentence saying who commits are made as.</summary>
    public string GlobalSummary
        => IsGlobalUnset
            ? "Not set. git refuses to commit until a name and an email are set, here or in the repository."
            : $"New commits are made as {GlobalIdentity}, unless a repository sets its own identity.";

    /// <summary>Gets the command that writes the typed values to the global configuration.</summary>
    public AsyncRelayCommand SaveGlobalCommand { get; }

    private GitIdentity TypedGlobal => new GitIdentity(GlobalName, GlobalEmail).Normalised();

    // ---------------------------------------------------------------- profiles

    /// <summary>Gets the identity profiles, in the order they were added.</summary>
    public ObservableCollection<IdentityProfileRowViewModel> Profiles { get; } = [];

    /// <summary>Gets a value indicating whether there is any profile.</summary>
    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>
    /// Gets the profile that matches the global identity git has, or <see langword="null"/> when none
    /// does.
    /// </summary>
    public IdentityProfile? CurrentProfile
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasCurrentProfile));
            }
        }
    }

    /// <summary>Gets a value indicating whether a profile matches the global identity.</summary>
    public bool HasCurrentProfile => CurrentProfile is not null;

    /// <summary>Gets the command that adds a profile, starting from the global identity.</summary>
    public AsyncRelayCommand AddProfileCommand { get; }

    /// <summary>Gets the command that edits a profile.</summary>
    public AsyncRelayCommand<IdentityProfileRowViewModel> EditProfileCommand { get; }

    /// <summary>Gets the command that deletes a profile, after asking.</summary>
    public AsyncRelayCommand<IdentityProfileRowViewModel> RemoveProfileCommand { get; }

    /// <summary>Gets the command that makes a profile the global identity.</summary>
    public AsyncRelayCommand<IdentityProfileRowViewModel> UseProfileCommand { get; }

    // ---------------------------------------------------------------- loading

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Reads the identity from git, and the profiles from their file, again.
    /// </summary>
    /// <returns>A task that completes once the page shows what git and the file have.</returns>
    /// <remarks>
    /// Values the reader has typed and not saved are kept: coming back to the page must not throw
    /// away an edit. Values nobody touched follow git.
    /// </remarks>
    public async Task LoadAsync()
    {
        IsBusy = true;

        try
        {
            await LoadGlobalAsync().ConfigureAwait(true);
            await LoadProfilesAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <inheritdoc />
    protected override void OnBusyChanged()
    {
        SaveGlobalCommand.NotifyCanExecuteChanged();
        AddProfileCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadGlobalAsync()
    {
        try
        {
            GitIdentity loaded = await _identity.GetGlobalAsync(RepositoryContext.RepositoryLifetime).ConfigureAwait(true);
            bool edited = IsGlobalChanged;

            GlobalIdentity = loaded;

            if (!edited)
            {
                GlobalName = loaded.Name;
                GlobalEmail = loaded.Email;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the application is closing.
        }
        catch (Exception exception) when (exception is GitCommandException or GitNotFoundException)
        {
            _logger.LogWarning("Reading the global git identity failed ({Kind})", exception.GetType().Name);

            await ReportAsync("Could not read the git identity", Describe(exception), InfoBarSeverity.Error)
                .ConfigureAwait(true);
        }
    }

    private async Task LoadProfilesAsync()
    {
        IReadOnlyList<IdentityProfile> profiles;

        try
        {
            profiles = await _profiles.GetAllAsync(RepositoryContext.RepositoryLifetime).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Reading the identity profiles failed");

            await ReportAsync("Could not read the identity profiles", exception.Message, InfoBarSeverity.Error)
                .ConfigureAwait(true);

            return;
        }

        // Read first, replace after, as the integrations page does: an interleaved second load must
        // not show a profile twice.
        List<IdentityProfileRowViewModel> rows = [];

        foreach (IdentityProfile profile in profiles)
        {
            rows.Add(new IdentityProfileRowViewModel(this, profile));
        }

        Profiles.Clear();

        foreach (IdentityProfileRowViewModel row in rows)
        {
            Profiles.Add(row);
        }

        OnPropertyChanged(nameof(HasProfiles));
        UpdateCurrentProfile();
    }

    private void UpdateCurrentProfile()
    {
        IdentityProfile? current = null;

        foreach (IdentityProfileRowViewModel row in Profiles)
        {
            row.IsCurrent = row.Profile.Matches(GlobalIdentity);

            if (row.IsCurrent && current is null)
            {
                current = row.Profile;
            }
        }

        CurrentProfile = current;
    }

    // ---------------------------------------------------------------- commands: the global identity

    private bool CanSaveGlobal() => !IsBusy && IsGlobalChanged && GitIdentityRules.Validate(TypedGlobal) is null;

    private async Task OnSaveGlobalAsync()
    {
        GitIdentity typed = TypedGlobal;

        if (GitIdentityRules.Validate(typed) is not null)
        {
            return;
        }

        if (await WriteGlobalAsync(typed).ConfigureAwait(true))
        {
            await ReportAsync("Global identity saved", $"New commits are made as {typed}.", InfoBarSeverity.Success)
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Writes an identity to the global configuration and, once git has it, shows it.
    /// </summary>
    /// <returns><see langword="true"/> when git wrote it.</returns>
    private async Task<bool> WriteGlobalAsync(GitIdentity identity)
    {
        IsBusy = true;

        try
        {
            await _identity.SetGlobalAsync(identity, RepositoryContext.RepositoryLifetime).ConfigureAwait(true);

            GitIdentity written = identity.Normalised();

            GlobalIdentity = written;
            GlobalName = written.Name;
            GlobalEmail = written.Email;

            return true;
        }
        catch (OperationCanceledException)
        {
            // Expected when the application is closing.
            return false;
        }
        catch (Exception exception) when (exception is GitCommandException or GitNotFoundException or ArgumentException)
        {
            // What was typed stays in the fields, so a retry is one click.
            _logger.LogWarning("Saving the global git identity failed ({Kind})", exception.GetType().Name);

            await ReportAsync("Could not save the git identity", Describe(exception), InfoBarSeverity.Error)
                .ConfigureAwait(true);

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------------------------------------------------------------- commands: profiles

    private async Task OnUseProfileAsync(IdentityProfileRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (await WriteGlobalAsync(row.Profile.Identity).ConfigureAwait(true))
        {
            await ReportAsync(
                $"Using {row.Label}",
                $"New commits are made as {row.Summary}.",
                InfoBarSeverity.Success).ConfigureAwait(true);
        }
    }

    private async Task OnAddProfileAsync()
    {
        // Starting from what git has: saving today's identity as a profile is the usual first step.
        IdentityProfileDialogViewModel model = new(string.Empty, GlobalIdentity);

        if (await ShowProfileDialogAsync(model, "Add a profile", "Add").ConfigureAwait(true))
        {
            await SaveProfileAsync(model.ToProfile(existing: null), "Profile added").ConfigureAwait(true);
        }
    }

    private async Task OnEditProfileAsync(IdentityProfileRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        IdentityProfileDialogViewModel model = new(row.Label, row.Profile.Identity);

        if (await ShowProfileDialogAsync(model, "Edit the profile", "Save").ConfigureAwait(true))
        {
            await SaveProfileAsync(model.ToProfile(row.Profile), "Profile saved").ConfigureAwait(true);
        }
    }

    private async Task OnRemoveProfileAsync(IdentityProfileRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "Delete this profile";
            dialog.Content =
                $"Delete the profile {row.Label} ({row.Summary})?\n\n"
                + "Only the profile goes: your git configuration keeps whatever identity it has.";
            dialog.PrimaryButtonText = "Delete";
            dialog.CloseButtonText = "Keep it";
            dialog.DefaultButton = DefaultButton.Close;
        }).ConfigureAwait(true);

        if (answer != DialogResult.Primary)
        {
            return;
        }

        try
        {
            await _profiles.RemoveAsync(row.Profile.Id, RepositoryContext.RepositoryLifetime).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Deleting an identity profile failed");

            await ReportAsync("Could not delete the profile", exception.Message, InfoBarSeverity.Error)
                .ConfigureAwait(true);

            return;
        }

        await ReportAsync("Profile deleted", $"{row.Label} is no longer in the list.", InfoBarSeverity.Info)
            .ConfigureAwait(true);

        await LoadProfilesAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Shows the profile dialog, its primary button enabled only while the fields are valid.
    /// </summary>
    /// <returns><see langword="true"/> when it was confirmed with valid fields.</returns>
    private async Task<bool> ShowProfileDialogAsync(IdentityProfileDialogViewModel model, string title, string primary)
    {
        IdentityProfileDialogView view =
            _services.GetService(typeof(IdentityProfileDialogView)) as IdentityProfileDialogView
            ?? new IdentityProfileDialogView();

        view.DataContext = model;

        ContentDialog? shown = null;

        void OnValidationChanged(object? sender, EventArgs e)
        {
            if (shown is not null)
            {
                shown.IsPrimaryButtonEnabled = model.IsValid;
            }
        }

        model.ValidationChanged += OnValidationChanged;

        try
        {
            DialogResult result = await _dialogs.ShowAsync(dialog =>
            {
                shown = dialog;
                dialog.Title = title;
                dialog.Content = view;
                dialog.PrimaryButtonText = primary;
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
                dialog.IsPrimaryButtonEnabled = model.IsValid;
            }).ConfigureAwait(true);

            return result == DialogResult.Primary && model.IsValid;
        }
        finally
        {
            model.ValidationChanged -= OnValidationChanged;
        }
    }

    private async Task SaveProfileAsync(IdentityProfile profile, string title)
    {
        IdentityProfile stored;

        try
        {
            stored = await _profiles.SaveAsync(profile, RepositoryContext.RepositoryLifetime).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning("Saving an identity profile failed ({Kind})", exception.GetType().Name);

            await ReportAsync("Could not save the profile", Describe(exception), InfoBarSeverity.Error)
                .ConfigureAwait(true);

            return;
        }

        await ReportAsync(title, $"{stored.Label}: {stored.Identity}.", InfoBarSeverity.Success).ConfigureAwait(true);
        await LoadProfilesAsync().ConfigureAwait(true);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Says what went wrong in git's own words where there are some — not the exception's message,
    /// which repeats the command line and so the values.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The sentence.</returns>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            GitCommandException { StandardError: { } error } when error.Trim().Length > 0 => error.Trim(),
            GitCommandException command => $"git stopped with exit code {command.ExitCode}.",
            ArgumentException { ParamName: { } parameter } argument
                => argument.Message.Replace($" (Parameter '{parameter}')", string.Empty, StringComparison.Ordinal),
            _ => exception.Message,
        };
    }

    private void OnGlobalEdited()
    {
        OnPropertyChanged(nameof(IsGlobalChanged));
        OnPropertyChanged(nameof(GlobalError));
        OnPropertyChanged(nameof(HasGlobalError));
        SaveGlobalCommand.NotifyCanExecuteChanged();
    }

    private Task ReportAsync(string title, string message, InfoBarSeverity severity)
        => _infoBar.ShowAsync(bar =>
        {
            bar.Title = title;
            bar.Message = message;
            bar.Severity = severity;
        });
}
