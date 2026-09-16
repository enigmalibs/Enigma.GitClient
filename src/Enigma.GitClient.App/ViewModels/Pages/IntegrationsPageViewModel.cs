using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
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

    /// <inheritdoc />
    public override string ToString() => $"{HostName} {DisplayName}";
}

/// <summary>
/// One repository on a host, as the integrations page lists it.
/// </summary>
public sealed class HostRepositoryRowViewModel : ViewModelBase
{
    private readonly IntegrationsPageViewModel _owner;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="owner">The page the row belongs to.</param>
    /// <param name="repository">The repository it stands for.</param>
    public HostRepositoryRowViewModel(IntegrationsPageViewModel owner, HostRepository repository)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(repository);

        _owner = owner;
        Repository = repository;
    }

    /// <summary>Gets the repository this row stands for.</summary>
    public HostRepository Repository { get; }

    /// <summary>Gets the repository's own name.</summary>
    public string Name => Repository.Name;

    /// <summary>Gets the owner, shown above the name.</summary>
    public string Owner => Repository.Owner;

    /// <summary>Gets a value indicating whether there is an owner to show.</summary>
    public bool HasOwner => Owner.Length > 0;

    /// <summary>Gets what the host says the repository is.</summary>
    public string Description => Repository.Description ?? string.Empty;

    /// <summary>Gets a value indicating whether there is a description to show.</summary>
    public bool HasDescription => Description.Length > 0;

    /// <summary>Gets a value indicating whether the repository is private.</summary>
    public bool IsPrivate => Repository.IsPrivate;

    /// <summary>Gets the branch a clone will land on.</summary>
    public string DefaultBranch => Repository.DefaultBranch;

    /// <summary>Gets how long ago it was last pushed to.</summary>
    public string LastPushed
        => Repository.LastPushed is { } moment ? Formatting.RelativeTime.Format(moment) : string.Empty;

    /// <summary>Gets a value indicating whether the host said when it was last pushed to.</summary>
    public bool HasLastPushed => LastPushed.Length > 0;

    /// <summary>Gets the command that clones this repository.</summary>
    public AsyncRelayCommand<HostRepositoryRowViewModel> CloneCommand => _owner.CloneCommand;

    /// <summary>Gets the command that opens this repository's page on its host.</summary>
    public AsyncRelayCommand<HostRepositoryRowViewModel> OpenCommand => _owner.OpenRepositoryCommand;

    /// <inheritdoc />
    public override string ToString() => Repository.FullName;
}

/// <summary>
/// ViewModel behind the integrations page: the connected accounts, and the repositories they can
/// reach.
/// </summary>
/// <remarks>
/// Repositories and clone links only — no issues, no pull requests. That is the product's scope and
/// it is also why no token scope beyond reading repositories is ever asked for.
/// </remarks>
public sealed class IntegrationsPageViewModel : PageViewModelBase
{
    /// <summary>
    /// How many pages of repositories are fetched before the listing stops asking for more.
    /// </summary>
    /// <remarks>
    /// A hundred per page: ten pages is a thousand repositories, which is past the point where a
    /// list is how anyone finds anything. The search box filters what has been read.
    /// </remarks>
    public const int PageLimit = 10;

    private readonly IHostAccountService _accounts;
    private readonly IHostProviderRegistry _registry;
    private readonly IHostLinkService _links;
    private readonly RepositoriesPageViewModel _repositories;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly IServiceProvider _services;
    private readonly ILogger<IntegrationsPageViewModel> _logger;

    private readonly List<HostRepositoryRowViewModel> _all = [];

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="accounts">The connected accounts and their tokens.</param>
    /// <param name="registry">Finds the provider for an account.</param>
    /// <param name="links">Opens a repository's page in a browser.</param>
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
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        _accounts = accounts;
        _registry = registry;
        _links = links;
        _repositories = repositories;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _services = services;
        _logger = logger;

        AddAccountCommand = new AsyncRelayCommand(OnAddAccountAsync, () => CanAddAccount);
        RemoveAccountCommand = new AsyncRelayCommand<HostAccountRowViewModel>(OnRemoveAccountAsync);
        RefreshCommand = new AsyncRelayCommand(OnRefreshAsync, () => HasAccounts);
        CloneCommand = new AsyncRelayCommand<HostRepositoryRowViewModel>(OnCloneAsync);
        OpenRepositoryCommand = new AsyncRelayCommand<HostRepositoryRowViewModel>(OnOpenRepositoryAsync);
        ClearSearchCommand = new RelayCommand(() => Search = string.Empty, () => Search.Length > 0);

        ShowAllCommand = new RelayCommand(() => Visibility = HostVisibility.All);
        ShowPublicCommand = new RelayCommand(() => Visibility = HostVisibility.Public);
        ShowPrivateCommand = new RelayCommand(() => Visibility = HostVisibility.Private);
    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Integrations";

    /// <summary>Gets the connected accounts.</summary>
    public ObservableCollection<HostAccountRowViewModel> Accounts { get; } = [];

    /// <summary>Gets the repositories of the selected account, filtered by the search box.</summary>
    public ObservableCollection<HostRepositoryRowViewModel> Repositories { get; } = [];

    /// <summary>
    /// Gets or sets the account whose repositories are shown.
    /// </summary>
    public HostAccountRowViewModel? SelectedAccount
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasSelectedAccount));
                _ = LoadRepositoriesAsync();
            }
        }
    }

    /// <summary>
    /// Gets or sets the text the repository list is filtered by.
    /// </summary>
    public string Search
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ClearSearchCommand.NotifyCanExecuteChanged();
                ApplyFilter();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets which repositories the host is asked for.
    /// </summary>
    public HostVisibility Visibility
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsAllVisible));
                OnPropertyChanged(nameof(IsPublicOnly));
                OnPropertyChanged(nameof(IsPrivateOnly));

                _ = LoadRepositoriesAsync();
            }
        }
    } = HostVisibility.All;

    /// <summary>Gets a value indicating whether every repository is listed.</summary>
    public bool IsAllVisible => Visibility == HostVisibility.All;

    /// <summary>Gets a value indicating whether only public repositories are listed.</summary>
    public bool IsPublicOnly => Visibility == HostVisibility.Public;

    /// <summary>Gets a value indicating whether only private repositories are listed.</summary>
    public bool IsPrivateOnly => Visibility == HostVisibility.Private;

    /// <summary>Gets a value indicating whether any account is connected.</summary>
    public bool HasAccounts => Accounts.Count > 0;

    /// <summary>Gets a value indicating whether an account is selected.</summary>
    public bool HasSelectedAccount => SelectedAccount is not null;

    /// <summary>Gets a value indicating whether this build can connect to anything at all.</summary>
    public bool CanAddAccount => _registry.Providers.Count > 0;

    /// <summary>Gets a value indicating whether there is anything in the repository list.</summary>
    public bool HasRepositories => Repositories.Count > 0;

    /// <summary>
    /// Gets the sentence describing the repository list, which is where a truncated listing says so.
    /// </summary>
    public string Summary { get; private set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>Gets the sentence shown while the page has nothing to display.</summary>
    public string EmptyMessage =>
        "Connect a GitHub, GitLab or Azure DevOps account to browse and clone your repositories. "
        + "Only repository access is ever requested — this client does not handle issues or pull requests.";

    /// <summary>Gets the command that connects an account.</summary>
    public AsyncRelayCommand AddAccountCommand { get; }

    /// <summary>Gets the command that disconnects one.</summary>
    public AsyncRelayCommand<HostAccountRowViewModel> RemoveAccountCommand { get; }

    /// <summary>Gets the command that re-reads the selected account's repositories.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Gets the command that clones a repository.</summary>
    public AsyncRelayCommand<HostRepositoryRowViewModel> CloneCommand { get; }

    /// <summary>Gets the command that opens a repository's page on its host.</summary>
    public AsyncRelayCommand<HostRepositoryRowViewModel> OpenRepositoryCommand { get; }

    /// <summary>Gets the command that empties the search box.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>Gets the command that lists everything the account can see.</summary>
    public RelayCommand ShowAllCommand { get; }

    /// <summary>Gets the command that lists only public repositories.</summary>
    public RelayCommand ShowPublicCommand { get; }

    /// <summary>Gets the command that lists only private repositories.</summary>
    public RelayCommand ShowPrivateCommand { get; }

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

            await ReportAsync("Could not read the connected accounts", exception.Message, InfoBarSeverity.Error)
                .ConfigureAwait(true);

            accounts = [];
        }

        string? previous = SelectedAccount?.Account.Id;

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
        RefreshCommand.NotifyCanExecuteChanged();

        SelectedAccount = Find(rows, previous);
    }

    /// <summary>
    /// Re-reads the selected account's repositories.
    /// </summary>
    /// <returns>A task that completes once the list is up to date.</returns>
    public async Task LoadRepositoriesAsync()
    {
        _all.Clear();
        Repositories.Clear();
        Summary = string.Empty;
        OnPropertyChanged(nameof(HasRepositories));

        if (SelectedAccount is not { Provider: { } provider } row)
        {
            return;
        }

        IsBusy = true;

        try
        {
            SecretString? token = await _accounts
                .GetTokenAsync(row.Account, RepositoryContext.RepositoryLifetime)
                .ConfigureAwait(true);

            if (token is null)
            {
                await ReportAsync(
                    "No token for this account",
                    "Disconnect it and connect it again.",
                    InfoBarSeverity.Warning).ConfigureAwait(true);

                return;
            }

            List<HostRepositoryRowViewModel> loaded = [];
            string? cursor = null;
            bool truncated = false;

            for (int page = 0; page < PageLimit; page++)
            {
                HostRepositoryPage current = await provider
                    .ListRepositoriesAsync(
                        row.Account,
                        token,
                        new HostRepositoryQuery(Visibility: Visibility, Cursor: cursor),
                        RepositoryContext.RepositoryLifetime)
                    .ConfigureAwait(true);

                foreach (HostRepository repository in current.Repositories)
                {
                    loaded.Add(new HostRepositoryRowViewModel(this, repository));
                }

                cursor = current.NextCursor;

                if (cursor is null)
                {
                    break;
                }

                truncated = page == PageLimit - 1;
            }

            _all.AddRange(loaded);

            Summary = truncated
                ? $"{Count(loaded.Count)}, and there are more. Narrow the search on the host if what you want is missing."
                : Count(loaded.Count);

            ApplyFilter();
        }
        catch (HostRateLimitException exception)
        {
            await ReportAsync("Rate limited", Describe(exception), InfoBarSeverity.Warning).ConfigureAwait(true);
        }
        catch (HostException exception)
        {
            _logger.LogError(exception, "Listing repositories failed");

            await ReportAsync("Could not list the repositories", exception.Message, InfoBarSeverity.Error)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository changes under the read.
        }
        catch (System.Net.Http.HttpRequestException exception)
        {
            // No answer at all: a wrong instance URL, no network, or a certificate the machine does
            // not trust. The host never got to refuse anything, so it is reported separately.
            _logger.LogWarning(exception, "Reaching the host failed");

            await ReportAsync(
                "Could not reach the host",
                "Check the instance URL and the network connection.",
                InfoBarSeverity.Error).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Describes a rate limit in words that say when, rather than "try again later".
    /// </summary>
    /// <param name="exception">The rate limit.</param>
    /// <returns>The sentence.</returns>
    public static string Describe(HostRateLimitException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.RetryAfter is not { } wait)
        {
            return exception.Message;
        }

        int minutes = (int)Math.Ceiling(wait.TotalMinutes);

        return minutes <= 1
            ? exception.Message + " It should lift within a minute."
            : $"{exception.Message} It should lift in about {minutes.ToString(CultureInfo.CurrentCulture)} minutes.";
    }

    private static HostAccountRowViewModel? Find(List<HostAccountRowViewModel> rows, string? id)
    {
        if (id is not null)
        {
            foreach (HostAccountRowViewModel row in rows)
            {
                if (string.Equals(row.Account.Id, id, StringComparison.Ordinal))
                {
                    return row;
                }
            }
        }

        return rows.Count > 0 ? rows[0] : null;
    }

    private void ApplyFilter()
    {
        string term = Search.Trim();

        Repositories.Clear();

        foreach (HostRepositoryRowViewModel row in _all)
        {
            if (term.Length == 0
                || row.Repository.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.Description.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                Repositories.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasRepositories));
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

            await ReportAsync(
                $"Connected to {provider.DisplayName}",
                $"Signed in as {identity.UserName}.",
                InfoBarSeverity.Success).ConfigureAwait(true);

            await LoadAccountsAsync().ConfigureAwait(true);
            await _links.RefreshAsync().ConfigureAwait(true);
        }
        catch (HostRateLimitException exception)
        {
            await ReportAsync("Rate limited", Describe(exception), InfoBarSeverity.Warning).ConfigureAwait(true);
        }
        catch (HostException exception)
        {
            // Never the token, and never the exception's own detail beyond its message: both have a
            // habit of carrying the request that failed.
            _logger.LogWarning("Connecting an account to {Host} was refused", provider.DisplayName);

            await ReportAsync($"{provider.DisplayName} refused the token", exception.Message, InfoBarSeverity.Error)
                .ConfigureAwait(true);
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

        await ReportAsync("Account disconnected", "Its token has been deleted.", InfoBarSeverity.Info)
            .ConfigureAwait(true);

        await LoadAccountsAsync().ConfigureAwait(true);
        await _links.RefreshAsync().ConfigureAwait(true);
    }

    private Task OnRefreshAsync() => LoadRepositoriesAsync();

    private async Task OnCloneAsync(HostRepositoryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await _repositories.RunCloneAsync(new CloneRequest
        {
            Url = row.Repository.CloneUrl,
            ParentDirectory = RepositoriesPageViewModel.DefaultParentDirectory(),
            DirectoryName = CloneRequest.DeriveDirectoryName(row.Repository.CloneUrl),
        }).ConfigureAwait(true);
    }

    private async Task OnOpenRepositoryAsync(HostRepositoryRowViewModel? row)
    {
        if (row is not null)
        {
            await _links.OpenAsync(row.Repository.WebUrl).ConfigureAwait(true);
        }
    }

    private static string Count(int repositories)
        => repositories == 1
            ? "1 repository"
            : $"{repositories.ToString(CultureInfo.CurrentCulture)} repositories";

    private Task ReportAsync(string title, string message, InfoBarSeverity severity)
        => _infoBar.ShowAsync(bar =>
        {
            bar.Title = title;
            bar.Message = message;
            bar.Severity = severity;
        });
}
