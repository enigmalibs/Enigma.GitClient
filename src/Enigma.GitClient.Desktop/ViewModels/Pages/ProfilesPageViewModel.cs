using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Security;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// One identity profile, as the profiles page lists it.
/// </summary>
public sealed class ProfileRowViewModel : ViewModelBase
{
    private readonly ProfilesPageViewModel _owner;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="owner">The page the row belongs to.</param>
    /// <param name="profile">The profile it stands for.</param>
    public ProfileRowViewModel(ProfilesPageViewModel owner, IdentityProfile profile)
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

    /// <summary>
    /// Gets the identity the profile sets, written the way a commit writes it — or, for a profile that
    /// sets none, a line saying so rather than nothing.
    /// </summary>
    public string Summary => Describe(Profile);

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
                OnPropertyChanged(nameof(CanUse));
            }
        }
    }

    /// <summary>Gets a value indicating whether the profile is not the identity git has now.</summary>
    public bool IsNotCurrent => !IsCurrent;

    /// <summary>
    /// Gets a value indicating whether the profile can be switched to: it is not the current one, and
    /// it has a name and an email to switch to.
    /// </summary>
    public bool CanUse => IsNotCurrent && Profile.HasIdentity;

    /// <summary>Gets the command that makes this profile the global identity.</summary>
    public AsyncRelayCommand<ProfileRowViewModel> UseCommand => _owner.UseProfileCommand;

    /// <summary>Gets the command that edits this profile.</summary>
    public AsyncRelayCommand<ProfileRowViewModel> EditCommand => _owner.EditProfileCommand;

    /// <summary>Gets the command that deletes this profile.</summary>
    public AsyncRelayCommand<ProfileRowViewModel> RemoveCommand => _owner.RemoveProfileCommand;

    /// <summary>Gets the command that connects an account to this profile.</summary>
    public AsyncRelayCommand<ProfileRowViewModel> ConnectCommand => _owner.ConnectAccountCommand;

    /// <summary>Gets the accounts this profile is connected to, in the order they were added.</summary>
    public ObservableCollection<HostAccountRowViewModel> Integrations { get; } = [];

    /// <summary>Gets a value indicating whether the profile has any integration.</summary>
    public bool HasIntegrations => Integrations.Count > 0;

    /// <summary>
    /// Replaces the integrations the row lists.
    /// </summary>
    /// <param name="rows">The profile's integrations.</param>
    internal void ShowIntegrations(IEnumerable<HostAccountRowViewModel> rows)
    {
        Integrations.Clear();

        foreach (HostAccountRowViewModel row in rows)
        {
            Integrations.Add(row);
        }

        OnPropertyChanged(nameof(HasIntegrations));
    }

    /// <inheritdoc />
    public override string ToString() => $"{Label}: {Summary}";

    /// <summary>
    /// Writes what a profile sets, the way the page and its messages show it.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <returns>Its identity as a commit writes it, or "No name or email".</returns>
    internal static string Describe(IdentityProfile profile)
        => profile.HasIdentity ? profile.Identity.ToString() : "No name or email";
}

/// <summary>
/// ViewModel behind the profiles page: the name and email git records on every commit, the
/// profiles that switch them and the hosting accounts each of them is connected to, and the identity
/// the open repository sets for itself.
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
/// The repository's section exists only while a repository is open — in a repository's window, never
/// in the start window — and its writes go through the repository's write lock like every other.
/// </para>
/// <para>
/// Every integration belongs to one profile, and is connected, browsed and disconnected under it. An
/// integration that belongs to none — connected before integrations belonged to profiles, or left
/// behind by a profile deleted elsewhere — is listed apart, as an earlier integration, until it is
/// moved into a profile. Nothing guesses whose it is.
/// </para>
/// <para>
/// Names and emails are never logged: they identify a person. A failure is logged by what failed.
/// </para>
/// </remarks>
public sealed class ProfilesPageViewModel : PageViewModelBase
{
    private readonly IGitIdentityService _identity;
    private readonly IIdentityProfileStore _profiles;
    private readonly IRepositoryListStore _lists;
    private readonly IProfileSelection _selection;
    private readonly IHostAccountService _accounts;
    private readonly IHostProviderRegistry _registry;
    private readonly IHostLinkService _links;
    private readonly IHostRepositoryBrowser _browser;
    private readonly RepositoriesPageViewModel _repositories;
    private readonly IContentDialogService _dialogs;
    private readonly IFolderDialogService _folderDialogs;
    private readonly IInfoBarService _infoBar;
    private readonly IServiceProvider _services;
    private readonly ILogger<ProfilesPageViewModel> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="identity">Reads and writes git's identity.</param>
    /// <param name="profiles">Keeps the identity profiles.</param>
    /// <param name="lists">Keeps each profile's list of repositories, which goes with the profile.</param>
    /// <param name="selection">Shows the list of the profile used, so the start window's picker agrees.</param>
    /// <param name="accounts">Keeps the connected accounts and their tokens.</param>
    /// <param name="registry">Finds the provider for an account.</param>
    /// <param name="links">Learns which host the open repository is on again, once the accounts change.</param>
    /// <param name="browser">Lists an account's repositories in a dialog.</param>
    /// <param name="repositories">Runs a clone, with its progress and its cancel.</param>
    /// <param name="dialogs">Raises the profile and account dialogs and the confirmations.</param>
    /// <param name="folderDialogs">Raises the folder picker the profile dialog chooses a base directory in.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="services">Resolves the dialog's view.</param>
    /// <param name="logger">Receives failures reported to the user another way.</param>
    public ProfilesPageViewModel(
        IRepositoryContext repositoryContext,
        IGitIdentityService identity,
        IIdentityProfileStore profiles,
        IRepositoryListStore lists,
        IProfileSelection selection,
        IHostAccountService accounts,
        IHostProviderRegistry registry,
        IHostLinkService links,
        IHostRepositoryBrowser browser,
        RepositoriesPageViewModel repositories,
        IContentDialogService dialogs,
        IFolderDialogService folderDialogs,
        IInfoBarService infoBar,
        IServiceProvider services,
        ILogger<ProfilesPageViewModel> logger)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(folderDialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        _identity = identity;
        _profiles = profiles;
        _lists = lists;
        _selection = selection;
        _accounts = accounts;
        _registry = registry;
        _links = links;
        _browser = browser;
        _repositories = repositories;
        _dialogs = dialogs;
        _folderDialogs = folderDialogs;
        _infoBar = infoBar;
        _services = services;
        _logger = logger;

        SaveGlobalCommand = new AsyncRelayCommand(OnSaveGlobalAsync, CanSaveGlobal);
        AddProfileCommand = new AsyncRelayCommand(OnAddProfileAsync, () => !IsBusy);
        EditProfileCommand = new AsyncRelayCommand<ProfileRowViewModel>(OnEditProfileAsync);
        RemoveProfileCommand = new AsyncRelayCommand<ProfileRowViewModel>(OnRemoveProfileAsync);
        UseProfileCommand = new AsyncRelayCommand<ProfileRowViewModel>(OnUseProfileAsync);
        SaveLocalCommand = new AsyncRelayCommand(OnSaveLocalAsync, CanSaveLocal);
        RemoveLocalCommand = new AsyncRelayCommand(OnRemoveLocalAsync, () => !IsBusy && IsRepositoryOpen && HasLocalIdentity);
        CopyFromCurrentProfileCommand = new RelayCommand(OnCopyFromCurrentProfile, () => IsRepositoryOpen && HasCurrentProfile);
        ConnectAccountCommand = new AsyncRelayCommand<ProfileRowViewModel>(OnConnectAccountAsync, _ => CanConnectAccounts);
        DisconnectAccountCommand = new AsyncRelayCommand<HostAccountRowViewModel>(OnDisconnectAccountAsync);
        BrowseRepositoriesCommand = new AsyncRelayCommand<HostAccountRowViewModel>(OnBrowseRepositoriesAsync);
        MoveAccountCommand = new AsyncRelayCommand<AccountMoveTargetViewModel>(OnMoveAccountAsync);
    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Profiles";

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
                OnPropertyChanged(nameof(LocalSummary));
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
    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = [];

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
                CopyFromCurrentProfileCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets a value indicating whether a profile matches the global identity.</summary>
    public bool HasCurrentProfile => CurrentProfile is not null;

    /// <summary>Gets the command that adds a profile, starting from the global identity.</summary>
    public AsyncRelayCommand AddProfileCommand { get; }

    /// <summary>Gets the command that edits a profile.</summary>
    public AsyncRelayCommand<ProfileRowViewModel> EditProfileCommand { get; }

    /// <summary>Gets the command that deletes a profile, after asking.</summary>
    public AsyncRelayCommand<ProfileRowViewModel> RemoveProfileCommand { get; }

    /// <summary>Gets the command that makes a profile the global identity.</summary>
    public AsyncRelayCommand<ProfileRowViewModel> UseProfileCommand { get; }

    // ---------------------------------------------------------------- integrations

    /// <summary>
    /// Gets the integrations that belong to no profile: connected before integrations belonged to
    /// profiles, or left behind by a profile that no longer exists.
    /// </summary>
    public ObservableCollection<HostAccountRowViewModel> EarlierIntegrations { get; } = [];

    /// <summary>Gets a value indicating whether there is any earlier integration to place.</summary>
    public bool HasEarlierIntegrations => EarlierIntegrations.Count > 0;

    /// <summary>Gets a value indicating whether this build can connect to any host at all.</summary>
    public bool CanConnectAccounts => _registry.Providers.Count > 0;

    /// <summary>Gets the command that connects an account to a profile.</summary>
    public AsyncRelayCommand<ProfileRowViewModel> ConnectAccountCommand { get; }

    /// <summary>Gets the command that disconnects an account, after asking.</summary>
    public AsyncRelayCommand<HostAccountRowViewModel> DisconnectAccountCommand { get; }

    /// <summary>Gets the command that lists an account's repositories, and clones the one picked.</summary>
    public AsyncRelayCommand<HostAccountRowViewModel> BrowseRepositoriesCommand { get; }

    /// <summary>Gets the command that gives an earlier integration to a profile.</summary>
    public AsyncRelayCommand<AccountMoveTargetViewModel> MoveAccountCommand { get; }

    // ---------------------------------------------------------------- the repository's own identity

    /// <summary>Gets the open repository's name, empty when none is open.</summary>
    public string RepositoryName => RepositoryContext.Repository?.Name ?? string.Empty;

    /// <summary>
    /// Gets the identity the open repository's own configuration sets, as git last reported it —
    /// only what it sets itself, not what it inherits.
    /// </summary>
    public GitIdentity LocalIdentity
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasLocalIdentity));
                OnPropertyChanged(nameof(LocalSummary));
                OnLocalEdited();
                RemoveLocalCommand.NotifyCanExecuteChanged();
            }
        }
    } = GitIdentity.Empty;

    /// <summary>Gets or sets the repository's name, as typed.</summary>
    public string LocalName
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                OnLocalEdited();
            }
        }
    } = string.Empty;

    /// <summary>Gets or sets the repository's email, as typed.</summary>
    public string LocalEmail
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                OnLocalEdited();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the repository's configuration sets a name or an email of its
    /// own.
    /// </summary>
    public bool HasLocalIdentity => !LocalIdentity.IsEmpty;

    /// <summary>Gets a value indicating whether the typed values differ from what the repository sets.</summary>
    public bool IsLocalChanged => !TypedLocal.Equals(LocalIdentity.Normalised());

    /// <summary>Gets what is wrong with the typed values, once something has been typed; empty otherwise.</summary>
    public string LocalError => IsLocalChanged ? GitIdentityRules.Validate(TypedLocal) ?? string.Empty : string.Empty;

    /// <summary>Gets a value indicating whether there is something wrong to say.</summary>
    public bool HasLocalError => LocalError.Length > 0;

    /// <summary>Gets the sentence saying who commits in the open repository are made as.</summary>
    public string LocalSummary
    {
        get
        {
            if (LocalIdentity.IsComplete)
            {
                return $"Commits in {RepositoryName} are made as {LocalIdentity}, whatever the global identity is.";
            }

            if (HasLocalIdentity)
            {
                return $"{RepositoryName} sets only part of an identity; git takes the rest from the global one.";
            }

            return IsGlobalUnset
                ? $"{RepositoryName} has no identity of its own and there is no global one: git refuses to commit here."
                : $"{RepositoryName} has no identity of its own: its commits use the global one, {GlobalIdentity}.";
        }
    }

    /// <summary>Gets the command that writes the typed values to the repository's own configuration.</summary>
    public AsyncRelayCommand SaveLocalCommand { get; }

    /// <summary>Gets the command that removes the repository's own identity.</summary>
    public AsyncRelayCommand RemoveLocalCommand { get; }

    /// <summary>Gets the command that fills the repository's fields from the current profile.</summary>
    public RelayCommand CopyFromCurrentProfileCommand { get; }

    private GitIdentity TypedLocal => new GitIdentity(LocalName, LocalEmail).Normalised();

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
            await LoadLocalAsync().ConfigureAwait(true);
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
        SaveLocalCommand.NotifyCanExecuteChanged();
        RemoveLocalCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// A different repository, or none: its section starts over from what git says about the new one,
    /// and anything typed for the old one goes with it.
    /// </summary>
    protected override void OnRepositoryChanged()
    {
        base.OnRepositoryChanged();

        OnPropertyChanged(nameof(RepositoryName));
        ShowLocal(GitIdentity.Empty);
        CopyFromCurrentProfileCommand.NotifyCanExecuteChanged();

        _ = LoadLocalAsync();
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

            Report("Could not read the git identity", Describe(exception), InfoBarSeverity.Error);
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

            Report("Could not read the identity profiles", exception.Message, InfoBarSeverity.Error);

            return;
        }

        // Read first, replace after: an interleaved second load must
        // not show a profile twice.
        List<ProfileRowViewModel> rows = [];

        foreach (IdentityProfile profile in profiles)
        {
            rows.Add(new ProfileRowViewModel(this, profile));
        }

        Profiles.Clear();

        foreach (ProfileRowViewModel row in rows)
        {
            Profiles.Add(row);
        }

        OnPropertyChanged(nameof(HasProfiles));
        UpdateCurrentProfile();

        await LoadIntegrationsAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Reads the connected accounts again and puts each under its profile, or among the earlier
    /// integrations when its profile is not in the list.
    /// </summary>
    /// <returns>A task that completes once every row shows what the store has.</returns>
    public async Task LoadIntegrationsAsync()
    {
        IReadOnlyList<HostAccount> accounts;

        try
        {
            accounts = await _accounts.GetAllAsync(RepositoryContext.RepositoryLifetime).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (TokenProtectionException exception)
        {
            _logger.LogError(exception, "Reading the connected accounts failed");

            Report("Could not read the connected accounts", exception.Message, InfoBarSeverity.Error);

            return;
        }

        List<IdentityProfile> profiles = [];

        foreach (ProfileRowViewModel row in Profiles)
        {
            profiles.Add(row.Profile);
        }

        List<HostAccountRowViewModel> earlier = [];

        foreach (ProfileRowViewModel profile in Profiles)
        {
            List<HostAccountRowViewModel> own = [];

            foreach (HostAccount account in accounts)
            {
                if (account.BelongsTo(profile.Profile.Id))
                {
                    own.Add(new HostAccountRowViewModel(this, account, _registry.Find(account.Kind)));
                }
            }

            profile.ShowIntegrations(own);
        }

        foreach (HostAccount account in accounts)
        {
            if (!profiles.Exists(profile => account.BelongsTo(profile.Id)))
            {
                earlier.Add(new HostAccountRowViewModel(this, account, _registry.Find(account.Kind), profiles));
            }
        }

        EarlierIntegrations.Clear();

        foreach (HostAccountRowViewModel row in earlier)
        {
            EarlierIntegrations.Add(row);
        }

        OnPropertyChanged(nameof(HasEarlierIntegrations));
    }

    private async Task LoadLocalAsync()
    {
        if (RepositoryContext.Repository is not { } repository)
        {
            ShowLocal(GitIdentity.Empty);
            return;
        }

        try
        {
            GitIdentity loaded = await _identity.GetLocalAsync(repository, RepositoryContext.RepositoryLifetime)
                .ConfigureAwait(true);

            // The repository may have changed while git was reading.
            if (!Equals(RepositoryContext.Repository, repository))
            {
                return;
            }

            bool edited = IsLocalChanged;

            LocalIdentity = loaded;

            if (!edited)
            {
                LocalName = loaded.Name;
                LocalEmail = loaded.Email;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the read.
        }
        catch (Exception exception) when (exception is GitCommandException or GitNotFoundException)
        {
            _logger.LogWarning("Reading the repository's git identity failed ({Kind})", exception.GetType().Name);

            Report("Could not read this repository's identity", Describe(exception), InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// Shows an identity as the repository's, typed values included.
    /// </summary>
    private void ShowLocal(GitIdentity identity)
    {
        LocalIdentity = identity;
        LocalName = identity.Name;
        LocalEmail = identity.Email;
    }

    private void UpdateCurrentProfile()
    {
        IdentityProfile? current = null;

        foreach (ProfileRowViewModel row in Profiles)
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
            Report("Global identity saved", $"New commits are made as {typed}.", InfoBarSeverity.Success);
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

            Report("Could not save the git identity", Describe(exception), InfoBarSeverity.Error);

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------------------------------------------------------------- commands: profiles

    private async Task OnUseProfileAsync(ProfileRowViewModel? row)
    {
        // A profile without a name and email has nothing to switch to: writing its empty identity
        // would unset git's own.
        if (row is null || !row.Profile.HasIdentity)
        {
            return;
        }

        if (await WriteGlobalAsync(row.Profile.Identity).ConfigureAwait(true))
        {
            // The start window's picker shows the profile git now commits as.
            _selection.Select(row.Profile.Id);

            Report(
                $"Using {row.Label}",
                $"New commits are made as {row.Summary}.",
                InfoBarSeverity.Success);
        }
    }

    private async Task OnAddProfileAsync()
    {
        // Starting from what git has: saving today's identity as a profile is the usual first step.
        IdentityProfileDialogViewModel model = new(_folderDialogs, string.Empty, GlobalIdentity);

        if (await ShowProfileDialogAsync(model, "Add a profile", "Add").ConfigureAwait(true))
        {
            await SaveProfileAsync(model.ToProfile(existing: null), "Profile added").ConfigureAwait(true);
        }
    }

    private async Task OnEditProfileAsync(ProfileRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        IdentityProfileDialogViewModel model = new(_folderDialogs, row.Label, row.Profile.Identity, row.Profile.BaseDirectory);

        if (await ShowProfileDialogAsync(model, "Edit the profile", "Save").ConfigureAwait(true))
        {
            await SaveProfileAsync(model.ToProfile(row.Profile), "Profile saved").ConfigureAwait(true);
        }
    }

    private async Task OnRemoveProfileAsync(ProfileRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        string integrations = row.Integrations.Count switch
        {
            0 => string.Empty,
            1 => $"\n\nIts integration, {row.Integrations[0].DisplayName}, is disconnected and its token deleted.",
            _ => $"\n\nIts {row.Integrations.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)} integrations are disconnected and their tokens deleted.",
        };

        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "Delete this profile";
            dialog.Content =
                $"Delete the profile {row.Label} ({row.Summary})?\n\n"
                + "Your git configuration keeps whatever identity it has. Its list of repositories goes "
                + "with it; the repositories themselves stay where they are."
                + integrations;
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

            Report("Could not delete the profile", exception.Message, InfoBarSeverity.Error);

            return;
        }

        // The profile first: should its accounts then fail to go, they show up as earlier
        // integrations — visible, and still removable — rather than under a profile that is gone.
        try
        {
            // Its list of repositories goes with it: a list nobody can select again would only sit
            // in the file. The store keeps a failed write to itself, as losing a list is no failure.
            await _lists.RemoveProfileAsync(row.Profile.Id, RepositoryContext.RepositoryLifetime).ConfigureAwait(true);

            await _accounts.RemoveForProfileAsync(row.Profile.Id, RepositoryContext.RepositoryLifetime).ConfigureAwait(true);

            Report("Profile deleted", $"{row.Label} is no longer in the list.", InfoBarSeverity.Info);
        }
        catch (OperationCanceledException)
        {
            // Expected when the application is closing.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or TokenProtectionException)
        {
            _logger.LogWarning(exception, "Disconnecting a deleted profile's integrations failed");

            Report(
                "Could not disconnect the profile's integrations",
                "They are listed under Earlier integrations, where they can be disconnected.",
                InfoBarSeverity.Error);
        }

        await LoadProfilesAsync().ConfigureAwait(true);

        if (row.HasIntegrations)
        {
            await _links.RefreshAsync().ConfigureAwait(true);
        }
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

            Report("Could not save the profile", Describe(exception), InfoBarSeverity.Error);

            return;
        }

        Report(title, $"{stored.Label}: {ProfileRowViewModel.Describe(stored)}.", InfoBarSeverity.Success);
        await LoadProfilesAsync().ConfigureAwait(true);
    }

    // ---------------------------------------------------------------- commands: integrations

    private async Task OnConnectAccountAsync(ProfileRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        AddHostAccountDialogViewModel model = new(_registry.Providers);

        AddHostAccountDialogView view =
            _services.GetService(typeof(AddHostAccountDialogView)) as AddHostAccountDialogView
            ?? new AddHostAccountDialogView();

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
                dialog.Title = $"Connect an account to {row.Label}";
                dialog.Content = view;
                dialog.PrimaryButtonText = "Connect";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
                dialog.IsPrimaryButtonEnabled = model.IsValid;
            }).ConfigureAwait(true);

            if (result != DialogResult.Primary || !model.IsValid)
            {
                return;
            }
        }
        finally
        {
            model.ValidationChanged -= OnValidationChanged;
        }

        await ConnectAsync(row, model).ConfigureAwait(true);
    }

    /// <summary>
    /// Validates a token against the host and, when the host accepts it, stores the account under a
    /// profile.
    /// </summary>
    /// <param name="profile">The profile the account is for.</param>
    /// <param name="model">The filled-in dialog.</param>
    /// <returns>A task that completes once the account is connected, or the failure reported.</returns>
    public async Task ConnectAsync(ProfileRowViewModel profile, AddHostAccountDialogViewModel model)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(model);

        if (model.SelectedProvider is not { } provider)
        {
            return;
        }

        HostAccount account = model.ToAccount();
        SecretString token = model.ToToken();

        IsBusy = true;

        try
        {
            // The host is asked who the token belongs to before anything is stored: a token that
            // does not work is a token worth refusing now rather than at the first listing.
            HostIdentity identity = await provider
                .ValidateCredentialAsync(account, token, RepositoryContext.RepositoryLifetime)
                .ConfigureAwait(true);

            HostAccount named = new(
                account.Id,
                account.Kind,
                account.BaseUri,
                identity.UserName,
                model.DisplayName.Trim().Length > 0 ? model.DisplayName.Trim() : identity.DisplayName,
                profile.Profile.Id);

            await _accounts.AddAsync(named, token, RepositoryContext.RepositoryLifetime).ConfigureAwait(true);

            Report(
                $"Connected to {provider.DisplayName}",
                $"{profile.Label} signs in as {identity.UserName}.",
                InfoBarSeverity.Success);

            await LoadIntegrationsAsync().ConfigureAwait(true);
            await _links.RefreshAsync().ConfigureAwait(true);
        }
        catch (HostRateLimitException exception)
        {
            Report("Rate limited", HostRepositoriesDialogViewModel.Describe(exception), InfoBarSeverity.Warning);
        }
        catch (HostException exception)
        {
            // Never the token, and never the exception's own detail beyond its message: both have a
            // habit of carrying the request that failed.
            _logger.LogWarning("Connecting an account to {Host} was refused", provider.DisplayName);

            Report($"{provider.DisplayName} refused the token", exception.Message, InfoBarSeverity.Error);
        }
        catch (OperationCanceledException)
        {
            // Expected when the application is closing.
        }
        catch (System.Net.Http.HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Reaching the host failed");

            Report(
                "Could not reach the host",
                "Check the instance URL and the network connection.",
                InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OnDisconnectAccountAsync(HostAccountRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "Disconnect this account";
            dialog.Content =
                $"Disconnect {row.DisplayName} and delete its stored token?\n\n"
                + "Nothing on the host is touched, and repositories you have already cloned keep working.";
            dialog.PrimaryButtonText = "Disconnect";
            dialog.CloseButtonText = "Keep it";
            dialog.DefaultButton = DefaultButton.Close;
        }).ConfigureAwait(true);

        if (answer != DialogResult.Primary)
        {
            return;
        }

        try
        {
            await _accounts.RemoveAsync(row.Account.Id, RepositoryContext.RepositoryLifetime).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or TokenProtectionException)
        {
            _logger.LogWarning(exception, "Disconnecting an account failed");

            Report("Could not disconnect the account", exception.Message, InfoBarSeverity.Error);

            return;
        }

        Report("Account disconnected", "Its token has been deleted.", InfoBarSeverity.Info);

        await LoadIntegrationsAsync().ConfigureAwait(true);
        await _links.RefreshAsync().ConfigureAwait(true);
    }

    private async Task OnBrowseRepositoriesAsync(HostAccountRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (await _browser.BrowseAsync(row.Account).ConfigureAwait(true) is not { } picked)
        {
            return;
        }

        await _repositories.RunCloneAsync(new CloneRequest
        {
            Url = picked.CloneUrl,
            ParentDirectory = _repositories.CloneParentDirectory(),
            DirectoryName = CloneRequest.DeriveDirectoryName(picked.CloneUrl),
            Account = row.Account,
        }).ConfigureAwait(true);
    }

    private async Task OnMoveAccountAsync(AccountMoveTargetViewModel? target)
    {
        if (target is null)
        {
            return;
        }

        try
        {
            if (!await _accounts
                    .AssignAsync(target.Account.Account.Id, target.Profile.Id, RepositoryContext.RepositoryLifetime)
                    .ConfigureAwait(true))
            {
                // Disconnected in another window meanwhile: the list is what is out of date.
                await LoadIntegrationsAsync().ConfigureAwait(true);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Moving an account to a profile failed");

            Report("Could not move the integration", exception.Message, InfoBarSeverity.Error);

            return;
        }

        Report(
            $"{target.Account.DisplayName} belongs to {target.Profile.Label}",
            "It is listed under that profile now.",
            InfoBarSeverity.Success);

        await LoadIntegrationsAsync().ConfigureAwait(true);
    }

    // ---------------------------------------------------------------- commands: the repository

    private bool CanSaveLocal()
        => !IsBusy && IsRepositoryOpen && IsLocalChanged && GitIdentityRules.Validate(TypedLocal) is null;

    private async Task OnSaveLocalAsync()
    {
        GitIdentity typed = TypedLocal;

        if (!IsRepositoryOpen || GitIdentityRules.Validate(typed) is not null)
        {
            return;
        }

        string name = RepositoryName;

        if (await WriteLocalAsync((repository, token) => _identity.SetLocalAsync(repository, typed, token), typed)
                .ConfigureAwait(true))
        {
            Report(
                $"{name} has its own identity",
                $"Its commits are made as {typed}, whatever the global identity is.",
                InfoBarSeverity.Success);
        }
    }

    private async Task OnRemoveLocalAsync()
    {
        if (!IsRepositoryOpen)
        {
            return;
        }

        string name = RepositoryName;

        if (await WriteLocalAsync(_identity.RemoveLocalAsync, GitIdentity.Empty).ConfigureAwait(true))
        {
            Report(
                $"{name} uses the global identity again",
                IsGlobalUnset
                    ? "There is no global identity yet: set one above, or git refuses to commit here."
                    : $"Its commits are made as {GlobalIdentity}.",
                InfoBarSeverity.Info);
        }
    }

    /// <summary>
    /// Runs a write to the repository's configuration under its write lock and, once git has done it,
    /// shows the result.
    /// </summary>
    /// <returns><see langword="true"/> when git wrote it.</returns>
    private async Task<bool> WriteLocalAsync(
        Func<RepositoryHandle, CancellationToken, Task> write,
        GitIdentity result)
    {
        IsBusy = true;

        try
        {
            // Nothing in the references changes, so there is nothing to re-read afterwards.
            await RepositoryContext.RunExclusiveAsync(write, refreshAfter: false).ConfigureAwait(true);

            ShowLocal(result.Normalised());

            return true;
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the write.
            return false;
        }
        catch (Exception exception) when (exception is GitCommandException or GitNotFoundException
                                              or ArgumentException or InvalidOperationException)
        {
            // What was typed stays in the fields, so a retry is one click.
            _logger.LogWarning("Writing the repository's git identity failed ({Kind})", exception.GetType().Name);

            Report("Could not change this repository's identity", Describe(exception), InfoBarSeverity.Error);

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnCopyFromCurrentProfile()
    {
        if (CurrentProfile is not { } profile)
        {
            return;
        }

        // Filled, not written: the reader sees what Save will write, and can still change it.
        LocalName = profile.Name;
        LocalEmail = profile.Email;
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

    private void OnLocalEdited()
    {
        OnPropertyChanged(nameof(IsLocalChanged));
        OnPropertyChanged(nameof(LocalError));
        OnPropertyChanged(nameof(HasLocalError));
        SaveLocalCommand.NotifyCanExecuteChanged();
    }

    private void Report(string title, string message, InfoBarSeverity severity)
        => _infoBar.Notify(title, message, severity);
}
