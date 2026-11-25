using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
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
/// What the connected accounts need of the profiles page they are shown on.
/// </summary>
/// <remarks>
/// The page implements it and attaches itself once, when it is built: the accounts are a singleton the
/// page takes in its constructor, so they cannot take the page in theirs.
/// </remarks>
public interface IHostAccountsHost
{
    /// <summary>Gets the profiles shown, under which each account is listed.</summary>
    IReadOnlyList<ProfileRowViewModel> Profiles { get; }

    /// <summary>Gets or sets a value indicating whether the page is working, which a sign-in is.</summary>
    bool IsBusy { get; set; }
}

/// <summary>
/// The connected accounts of the profiles page: each listed under its profile or among the earlier
/// integrations, connected, signed in again with a new token, given to a profile, browsed and cloned
/// from, disconnected, and the reset of a token store that cannot be read.
/// </summary>
/// <remarks>
/// The page shows it as its <c>Accounts</c> property and keeps the identity profiles.
/// </remarks>
public sealed class HostAccountsViewModel : ViewModelBase
{
    private readonly IRepositoryContext _context;
    private readonly IHostAccountService _accounts;
    private readonly IHostProviderRegistry _registry;
    private readonly IHostLinkService _links;
    private readonly IHostRepositoryBrowser _browser;
    private readonly ITokenRecovery _recovery;
    private readonly IRepositoryCloner _cloner;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly Func<AddHostAccountDialogView> _accountDialogs;
    private readonly ILogger<HostAccountsViewModel> _logger;

    private IHostAccountsHost? _host;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">Gives the lifetime the reads and writes are cancelled with.</param>
    /// <param name="accounts">Keeps the connected accounts and their tokens.</param>
    /// <param name="registry">Finds the provider for an account.</param>
    /// <param name="links">Learns which host the open repository is on again, once the accounts change.</param>
    /// <param name="browser">Lists an account's repositories in a dialog.</param>
    /// <param name="recovery">Offers the reset when the stored tokens cannot be read.</param>
    /// <param name="cloner">Runs a clone, with its progress and its cancel.</param>
    /// <param name="dialogs">Raises the account dialogs and the confirmations.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="accountDialogs">Builds the account dialog's view, a fresh one each time.</param>
    /// <param name="logger">Receives failures reported to the user another way.</param>
    public HostAccountsViewModel(
        IRepositoryContext context,
        IHostAccountService accounts,
        IHostProviderRegistry registry,
        IHostLinkService links,
        IHostRepositoryBrowser browser,
        ITokenRecovery recovery,
        IRepositoryCloner cloner,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        Func<AddHostAccountDialogView> accountDialogs,
        ILogger<HostAccountsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(cloner);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(accountDialogs);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _accounts = accounts;
        _registry = registry;
        _links = links;
        _browser = browser;
        _recovery = recovery;
        _cloner = cloner;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _accountDialogs = accountDialogs;
        _logger = logger;

        ConnectAccountCommand = new AsyncRelayCommand<ProfileRowViewModel>(OnConnectAccountAsync, _ => CanConnectAccounts);
        DisconnectAccountCommand = new AsyncRelayCommand<HostAccountRowViewModel>(OnDisconnectAccountAsync);
        BrowseRepositoriesCommand = new AsyncRelayCommand<HostAccountRowViewModel>(OnBrowseRepositoriesAsync);
        MoveAccountCommand = new AsyncRelayCommand<AccountMoveTargetViewModel>(OnMoveAccountAsync);
        ReplaceTokenCommand = new AsyncRelayCommand<HostAccountRowViewModel>(OnReplaceTokenAsync);
        ResetStoredTokensCommand = new AsyncRelayCommand(OnResetStoredTokensAsync);

        // A reset made anywhere — the safety net's dialog, a browse, a connect — leaves every line here
        // without its token: they say so at once.
        _recovery.TokensReset += (_, _) => LoadIntegrationsAsync().Forget();
    }

    private IHostAccountsHost Host
        => _host ?? throw new InvalidOperationException("The accounts are shown on a page that attaches itself first.");

    /// <summary>
    /// Attaches the page the accounts are shown on. Called once, by the page's constructor.
    /// </summary>
    /// <param name="host">The page.</param>
    public void AttachTo(IHostAccountsHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (_host is not null && !ReferenceEquals(_host, host))
        {
            throw new InvalidOperationException("The accounts are already shown on another page.");
        }

        _host = host;
    }

    /// <summary>
    /// Disconnects every account of a profile being deleted.
    /// </summary>
    /// <param name="profileId">The profile.</param>
    /// <returns>A task that completes once they are gone.</returns>
    public Task RemoveForProfileAsync(string profileId)
        => _accounts.RemoveForProfileAsync(profileId, _context.RepositoryLifetime);

    /// <summary>
    /// Learns which host the open repository is on again, once accounts went with a profile.
    /// </summary>
    /// <returns>A task that completes once it is known.</returns>
    public Task RefreshHostLinksAsync() => _links.RefreshAsync();

    /// <summary>
    /// Gets the integrations that belong to no profile: connected before integrations belonged to
    /// profiles, or left behind by a profile that no longer exists.
    /// </summary>
    public RangeObservableCollection<HostAccountRowViewModel> EarlierIntegrations { get; } = [];

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

    /// <summary>Gets the command that gives an integration a new token.</summary>
    public AsyncRelayCommand<HostAccountRowViewModel> ReplaceTokenCommand { get; }

    /// <summary>Gets the command that offers to delete every stored token, the way out of a store that cannot be read.</summary>
    public AsyncRelayCommand ResetStoredTokensCommand { get; }

    /// <summary>
    /// Gets a value indicating whether the stored tokens cannot be read at all — what a Windows
    /// password reset leaves behind — which the band at the top of the page says, with the reset.
    /// </summary>
    public bool IsTokenStoreUnreadable { get; private set => SetProperty(ref field, value); }

    /// <summary>Gets what the band says: why the stored tokens cannot be read, and what fixes it.</summary>
    public string TokenStoreProblem { get; private set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>
    /// Reads the connected accounts again and puts each under its profile, or among the earlier
    /// integrations when its profile is not in the list.
    /// </summary>
    /// <returns>A task that completes once every row shows what the store has.</returns>
    public async Task LoadIntegrationsAsync()
    {
        if (_host is null)
        {
            return;
        }

        IReadOnlyList<HostAccount> accounts;
        TokenStoreStatus? tokens;

        try
        {
            accounts = await _accounts.GetAllAsync(_context.RepositoryLifetime).ConfigureAwait(true);
            tokens = await ReadTokenStatusAsync().ConfigureAwait(true);
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

        // Before the reader hits a failure: a store this account can no longer read says so at the top of
        // the page, with the way out (BUG-3024).
        IsTokenStoreUnreadable = tokens?.Failure is not null;
        TokenStoreProblem = tokens?.Failure is { } failure ? _recovery.Describe(failure) : string.Empty;

        List<IdentityProfile> profiles = [];

        foreach (ProfileRowViewModel row in Host.Profiles)
        {
            profiles.Add(row.Profile);
        }

        List<HostAccountRowViewModel> earlier = [];

        foreach (ProfileRowViewModel profile in Host.Profiles)
        {
            List<HostAccountRowViewModel> own = [];

            foreach (HostAccount account in accounts)
            {
                if (account.BelongsTo(profile.Profile.Id))
                {
                    own.Add(new HostAccountRowViewModel(this, account, _registry.Find(account.Kind), tokenState: TokenStateOf(tokens, account)));
                }
            }

            profile.ShowIntegrations(own);
        }

        foreach (HostAccount account in accounts)
        {
            if (!profiles.Exists(profile => account.BelongsTo(profile.Id)))
            {
                earlier.Add(new HostAccountRowViewModel(this, account, _registry.Find(account.Kind), profiles, TokenStateOf(tokens, account)));
            }
        }

        EarlierIntegrations.ReplaceWith(earlier);

        OnPropertyChanged(nameof(HasEarlierIntegrations));
    }

    /// <summary>
    /// Reads whether the stored tokens can be read, without handing any of them out.
    /// </summary>
    /// <returns>The status, or <see langword="null"/> when the store could not be looked at.</returns>
    private async Task<TokenStoreStatus?> ReadTokenStatusAsync()
    {
        try
        {
            return await _recovery.CheckAsync(_context.RepositoryLifetime).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not knowing is not a problem to show: the lines claim nothing either way.
            _logger.LogWarning(exception, "Reading whether the stored tokens can be read failed");
            return null;
        }
    }

    private static HostAccountTokenState TokenStateOf(TokenStoreStatus? tokens, HostAccount account)
    {
        if (tokens is null)
        {
            return HostAccountTokenState.Stored;
        }

        if (!tokens.IsReadable)
        {
            return HostAccountTokenState.Unreadable;
        }

        if (!tokens.Holds(account.TokenKey))
        {
            return HostAccountTokenState.Missing;
        }

        return tokens.CanRead(account.TokenKey) ? HostAccountTokenState.Stored : HostAccountTokenState.Unreadable;
    }

    // ---------------------------------------------------------------- commands

    private async Task OnConnectAccountAsync(ProfileRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        AddHostAccountDialogViewModel model = new(_registry.Providers);

        if (await AskForTokenAsync(model, $"Connect an account to {row.Label}", "Connect").ConfigureAwait(true))
        {
            await ConnectAsync(row, model).ConfigureAwait(true);
        }
    }

    private async Task OnReplaceTokenAsync(HostAccountRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.Provider is not { } provider)
        {
            Report(
                "Nothing speaks to this host",
                $"This build cannot sign in to {row.DisplayName}.",
                InfoBarSeverity.Warning);

            return;
        }

        // The account's own host and instance, which a replaced token does not change.
        AddHostAccountDialogViewModel model = new(_registry.Providers)
        {
            SelectedProvider = provider,
            CanChooseInstance = false,
        };

        model.InstanceUrl = row.Account.BaseUri.ToString().TrimEnd('/');
        model.DisplayName = row.DisplayName;

        if (await AskForTokenAsync(model, $"Replace the token of {row.DisplayName}", "Replace").ConfigureAwait(true))
        {
            await ReplaceTokenAsync(row, model).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Shows the dialog a token is typed in.
    /// </summary>
    /// <returns><see langword="true"/> when it was filled in and confirmed.</returns>
    private async Task<bool> AskForTokenAsync(AddHostAccountDialogViewModel model, string title, string primary)
    {
        AddHostAccountDialogView view = _accountDialogs();

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

    private async Task OnResetStoredTokensAsync()
    {
        if (await _recovery.ConfirmAndResetAsync().ConfigureAwait(true))
        {
            await LoadIntegrationsAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Validates a token against the host and, when the host accepts it, stores the account under a
    /// profile.
    /// </summary>
    /// <param name="profile">The profile the account is for.</param>
    /// <param name="model">The filled-in dialog.</param>
    /// <returns>A task that completes once the account is connected, or the failure reported.</returns>
    /// <remarks>
    /// An account already listed with the same login, instance and profile gets the new token rather than
    /// a second line beside it (BUG-3024). Stored tokens this account can no longer read are said so,
    /// with the reset offered; once it is confirmed, the token just typed — which the host has already
    /// accepted — is stored without asking for it again.
    /// </remarks>
    public Task ConnectAsync(ProfileRowViewModel profile, AddHostAccountDialogViewModel model)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(model);

        return SignInAsync(model, profile.Profile.Id, profile.Label, replacing: null);
    }

    /// <summary>
    /// Validates a new token for an integration already listed against its host and, when the host
    /// accepts it, stores it in place of the one it had — missing after a reset, or unreadable.
    /// </summary>
    /// <param name="row">The integration.</param>
    /// <param name="model">The filled-in dialog.</param>
    /// <returns>A task that completes once the token is replaced, or the failure reported.</returns>
    public Task ReplaceTokenAsync(HostAccountRowViewModel row, AddHostAccountDialogViewModel model)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(model);

        return SignInAsync(model, row.Account.ProfileId, row.DisplayName, replacing: row.Account);
    }

    /// <summary>
    /// Asks the host who a token belongs to and, when it accepts it, stores it: for an account already
    /// listed when <paramref name="replacing"/> names one, as a connection otherwise.
    /// </summary>
    private async Task SignInAsync(
        AddHostAccountDialogViewModel model,
        string? profileId,
        string label,
        HostAccount? replacing)
    {
        if (model.SelectedProvider is not { } provider)
        {
            return;
        }

        HostAccount account = replacing ?? model.ToAccount();
        SecretString token = model.ToToken();

        Host.IsBusy = true;

        try
        {
            // The host is asked who the token belongs to before anything is stored: a token that
            // does not work is a token worth refusing now rather than at the first listing.
            HostIdentity identity = await provider
                .ValidateCredentialAsync(account, token, _context.RepositoryLifetime)
                .ConfigureAwait(true);

            string typedName = model.DisplayName.Trim();

            HostAccount named = new(
                account.Id,
                account.Kind,
                account.BaseUri,
                identity.UserName,
                typedName.Length > 0 ? typedName : identity.DisplayName,
                profileId);

            if (await StoreAsync(named, token, renamed: typedName.Length > 0, replacing).ConfigureAwait(true) is not { } replaced)
            {
                return;
            }

            Report(
                replaced ? $"Token replaced for {provider.DisplayName}" : $"Connected to {provider.DisplayName}",
                $"{label} signs in as {identity.UserName}.",
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Storing a connected account failed");

            Report("Could not store the account", exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            Host.IsBusy = false;
        }
    }

    /// <summary>
    /// Stores an account the host has accepted, with its token: in place of the same account when it is
    /// already listed, as a new one otherwise.
    /// </summary>
    /// <param name="account">The account, named after what the host said.</param>
    /// <param name="token">The token.</param>
    /// <param name="renamed">Whether the reader typed a name, which then replaces the listed account's.</param>
    /// <param name="replacing">The account whose token is replaced, or <see langword="null"/> to look for it.</param>
    /// <returns>
    /// <see langword="true"/> when a listed account's token was replaced, <see langword="false"/> when a
    /// new account was added, or <see langword="null"/> when nothing was stored — said on the bar.
    /// </returns>
    private async Task<bool?> StoreAsync(HostAccount account, SecretString token, bool renamed, HostAccount? replacing)
    {
        CancellationToken lifetime = _context.RepositoryLifetime;

        for (bool offered = false; ; offered = true)
        {
            try
            {
                if (replacing is not null)
                {
                    // That line's token, whoever the host says it belongs to now.
                    HostAccount kept = new(
                        replacing.Id,
                        replacing.Kind,
                        replacing.BaseUri,
                        account.UserName,
                        renamed ? account.DisplayName : replacing.DisplayName,
                        replacing.ProfileId);

                    await _accounts.UpdateAsync(kept, token, lifetime).ConfigureAwait(true);
                    return true;
                }

                IReadOnlyList<HostAccount> listed = await _accounts.GetAllAsync(lifetime).ConfigureAwait(true);

                foreach (HostAccount existing in listed)
                {
                    if (existing.IsSameIdentityAs(account))
                    {
                        HostAccount kept = renamed
                            ? new HostAccount(existing.Id, existing.Kind, existing.BaseUri, existing.UserName, account.DisplayName, existing.ProfileId)
                            : existing;

                        await _accounts.UpdateAsync(kept, token, lifetime).ConfigureAwait(true);
                        return true;
                    }
                }

                await _accounts.AddAsync(account, token, lifetime).ConfigureAwait(true);
                return false;
            }
            catch (TokenProtectionException exception) when (exception.IsStoreWide && !offered)
            {
                // A store this account can no longer read takes no new token either. The reset is the
                // way out, and only the reader decides to lose every other token for it.
                _logger.LogWarning(exception, "The stored tokens cannot be read; the reset is offered");

                if (!await _recovery.ConfirmAndResetAsync().ConfigureAwait(true))
                {
                    Report("The token was not stored", _recovery.Describe(exception), InfoBarSeverity.Error);
                    return null;
                }
            }
            catch (TokenProtectionException exception)
            {
                _logger.LogWarning(exception, "The token could not be stored");

                Report("The token was not stored", _recovery.Describe(exception), InfoBarSeverity.Error);
                return null;
            }
        }
    }

    private async Task OnDisconnectAccountAsync(HostAccountRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        DialogResult answer = await _dialogs.ShowDestructiveAsync(dialog =>
        {
            dialog.Title = "Disconnect this account";
            dialog.Content =
                $"Disconnect {row.DisplayName} and delete its stored token?\n\n"
                + "Nothing on the host is touched, and repositories you have already cloned keep working.";
            dialog.PrimaryButtonText = "Disconnect";
            dialog.CloseButtonText = "Keep it";
        }).ConfigureAwait(true);

        if (answer != DialogResult.Primary)
        {
            return;
        }

        try
        {
            await _accounts.RemoveAsync(row.Account.Id, _context.RepositoryLifetime).ConfigureAwait(true);
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

        // The integration's own profile decides where it goes, whichever profile the home page has
        // selected: an integration listed under "Work" clones into Work's base directory.
        IdentityProfile? owner = null;

        foreach (ProfileRowViewModel profile in Host.Profiles)
        {
            if (row.Account.BelongsTo(profile.Profile.Id))
            {
                owner = profile.Profile;
                break;
            }
        }

        await _cloner.RunCloneAsync(new CloneRequest
        {
            Url = picked.CloneUrl,
            ParentDirectory = _cloner.CloneParentDirectory(owner),
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
                    .AssignAsync(target.Account.Account.Id, target.Profile.Id, _context.RepositoryLifetime)
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

    private void Report(string title, string message, InfoBarSeverity severity)
        => _infoBar.Notify(title, message, severity);
}
