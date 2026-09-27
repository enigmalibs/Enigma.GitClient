using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.Views.Dialogs;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Security;
using Enigma.Icons.Phosphor;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// One connected account, as the integrations page lists it.
/// </summary>
public sealed class HostAccountRowViewModel : ViewModelBase
{
    private readonly IntegrationsPageViewModel _owner;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="owner">The page the row belongs to.</param>
    /// <param name="account">The account it stands for.</param>
    /// <param name="provider">The provider that speaks to it.</param>
    public HostAccountRowViewModel(
        IntegrationsPageViewModel owner,
        HostAccount account,
        IRepositoryHostProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(account);

        _owner = owner;
        Account = account;
        Provider = provider;
    }

    /// <summary>Gets the account this row stands for.</summary>
    public HostAccount Account { get; }

    /// <summary>Gets the provider that speaks to it, or <see langword="null"/> in a build without one.</summary>
    public IRepositoryHostProvider? Provider { get; }

    /// <summary>Gets what to call the account.</summary>
    public string DisplayName => Account.DisplayName;

    /// <summary>Gets the login the token belongs to.</summary>
    public string UserName => Account.UserName;

    /// <summary>Gets a value indicating whether the host told us a login.</summary>
    public bool HasUserName => UserName.Length > 0;

    /// <summary>Gets the instance's host name.</summary>
    public string Host => Account.Host;

    /// <summary>Gets what the host is called.</summary>
    public string HostName => Provider?.DisplayName ?? Account.Kind.ToString();

    /// <summary>Gets the icon standing for the kind of host.</summary>
    public PhosphorIcon Icon
        => Account.Kind switch
        {
            HostKind.GitHub => PhosphorIcon.GithubLogo,
            HostKind.GitLab => PhosphorIcon.GitlabLogo,
            HostKind.AzureDevOps => PhosphorIcon.Cloud,
            _ => PhosphorIcon.GlobeSimple,
        };

    /// <summary>Gets the command that disconnects the account.</summary>
    public AsyncRelayCommand<HostAccountRowViewModel> RemoveCommand => _owner.RemoveAccountCommand;

    /// <summary>Gets the command that lists the account's repositories in a dialog.</summary>
    public AsyncRelayCommand<HostAccountRowViewModel> BrowseCommand => _owner.BrowseCommand;

    /// <inheritdoc />
    public override string ToString() => $"{HostName} {DisplayName}";
}

/// <summary>
/// ViewModel behind the integrations page: the connected accounts, each opening the repositories it
/// can reach in a dialog.
/// </summary>
/// <remarks>
/// Repositories and clone links only — no issues, no pull requests. That is the product's scope and
/// it is also why no token scope beyond reading repositories is ever asked for.
/// </remarks>
public sealed class IntegrationsPageViewModel : PageViewModelBase
{
    private readonly IHostAccountService _accounts;
    private readonly IHostProviderRegistry _registry;
    private readonly IHostLinkService _links;
    private readonly IHostRepositoryBrowser _browser;
    private readonly RepositoriesPageViewModel _repositories;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly IServiceProvider _services;
    private readonly ILogger<IntegrationsPageViewModel> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="accounts">The connected accounts and their tokens.</param>
    /// <param name="registry">Finds the provider for an account.</param>
    /// <param name="links">Learns which host the open repository is on again, once the accounts change.</param>
    /// <param name="browser">Lists an account's repositories in a dialog.</param>
    /// <param name="repositories">Runs the clone, with its progress and its cancel.</param>
    /// <param name="dialogs">Raises the connect and disconnect dialogs.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="services">Resolves the dialog's view.</param>
    /// <param name="logger">Receives failures reported to the user another way.</param>
    public IntegrationsPageViewModel(
        IRepositoryContext repositoryContext,
        IHostAccountService accounts,
        IHostProviderRegistry registry,
        IHostLinkService links,
        IHostRepositoryBrowser browser,
        RepositoriesPageViewModel repositories,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        IServiceProvider services,
        ILogger<IntegrationsPageViewModel> logger)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        _accounts = accounts;
        _registry = registry;
        _links = links;
        _browser = browser;
        _repositories = repositories;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _services = services;
        _logger = logger;

        AddAccountCommand = new AsyncRelayCommand(OnAddAccountAsync, () => CanAddAccount);
        RemoveAccountCommand = new AsyncRelayCommand<HostAccountRowViewModel>(OnRemoveAccountAsync);
        BrowseCommand = new AsyncRelayCommand<HostAccountRowViewModel>(OnBrowseAsync);
    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Integrations";

    /// <summary>Gets the connected accounts.</summary>
    public ObservableCollection<HostAccountRowViewModel> Accounts { get; } = [];

    /// <summary>Gets a value indicating whether any account is connected.</summary>
    public bool HasAccounts => Accounts.Count > 0;

    /// <summary>Gets a value indicating whether this build can connect to anything at all.</summary>
    public bool CanAddAccount => _registry.Providers.Count > 0;

    /// <summary>Gets the sentence shown while the page has nothing to display.</summary>
    public string EmptyMessage =>
        "Connect a GitHub, GitLab or Azure DevOps account to browse and clone your repositories. "
        + "Only repository access is ever requested — this client does not handle issues or pull requests.";

    /// <summary>Gets the command that connects an account.</summary>
    public AsyncRelayCommand AddAccountCommand { get; }

    /// <summary>Gets the command that disconnects one.</summary>
    public AsyncRelayCommand<HostAccountRowViewModel> RemoveAccountCommand { get; }

    /// <summary>Gets the command that lists an account's repositories, and clones the one picked.</summary>
    public AsyncRelayCommand<HostAccountRowViewModel> BrowseCommand { get; }

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);
        await LoadAccountsAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Re-reads the connected accounts.
    /// </summary>
    /// <returns>A task that completes once the list is up to date.</returns>
    public async Task LoadAccountsAsync()
    {
        IReadOnlyList<HostAccount> accounts;

        try
        {
            accounts = await _accounts.GetAllAsync(RepositoryContext.RepositoryLifetime).ConfigureAwait(true);
        }
        catch (TokenProtectionException exception)
        {
            _logger.LogError(exception, "Reading the connected accounts failed");

            Report("Could not read the connected accounts", exception.Message, InfoBarSeverity.Error);

            accounts = [];
        }

        List<HostAccountRowViewModel> rows = [];

        foreach (HostAccount account in accounts)
        {
            rows.Add(new HostAccountRowViewModel(this, account, _registry.Find(account.Kind)));
        }

        // Read first, replace after: clearing before the rows are built lets a second refresh
        // interleave and show the same account twice.
        Accounts.Clear();

        foreach (HostAccountRowViewModel row in rows)
        {
            Accounts.Add(row);
        }

        OnPropertyChanged(nameof(HasAccounts));
    }

    // ---------------------------------------------------------------- commands

    private async Task OnAddAccountAsync()
    {
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
                dialog.Title = "Connect an account";
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

        await ConnectAsync(model).ConfigureAwait(true);
    }

    /// <summary>
    /// Validates a token against the host and stores the account when the host accepts it.
    /// </summary>
    /// <param name="model">The filled-in dialog.</param>
    /// <returns>A task that completes once the account is connected, or the failure reported.</returns>
    public async Task ConnectAsync(AddHostAccountDialogViewModel model)
    {
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
                model.DisplayName.Trim().Length > 0 ? model.DisplayName.Trim() : identity.DisplayName);

            await _accounts.AddAsync(named, token, RepositoryContext.RepositoryLifetime).ConfigureAwait(true);

            Report(
                $"Connected to {provider.DisplayName}",
                $"Signed in as {identity.UserName}.",
                InfoBarSeverity.Success);

            await LoadAccountsAsync().ConfigureAwait(true);
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
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OnRemoveAccountAsync(HostAccountRowViewModel? row)
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

        await _accounts.RemoveAsync(row.Account.Id, RepositoryContext.RepositoryLifetime).ConfigureAwait(true);

        Report("Account disconnected", "Its token has been deleted.", InfoBarSeverity.Info);

        await LoadAccountsAsync().ConfigureAwait(true);
        await _links.RefreshAsync().ConfigureAwait(true);
    }

    private async Task OnBrowseAsync(HostAccountRowViewModel? row)
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
            ParentDirectory = RepositoriesPageViewModel.DefaultParentDirectory(),
            DirectoryName = CloneRequest.DeriveDirectoryName(picked.CloneUrl),
        }).ConfigureAwait(true);
    }

    private void Report(string title, string message, InfoBarSeverity severity)
        => _infoBar.Notify(title, message, severity);
}
