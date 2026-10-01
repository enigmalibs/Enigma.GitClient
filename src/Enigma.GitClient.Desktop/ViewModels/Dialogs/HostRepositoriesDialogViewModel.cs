using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Security;
using Enigma.GitClient.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.ViewModels.Dialogs;

/// <summary>
/// One repository on a host, as the repositories dialog lists it.
/// </summary>
public sealed class HostRepositoryRowViewModel : ViewModelBase
{
    private readonly HostRepositoriesDialogViewModel _owner;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="owner">The dialog the row belongs to.</param>
    /// <param name="repository">The repository it stands for.</param>
    public HostRepositoryRowViewModel(HostRepositoriesDialogViewModel owner, HostRepository repository)
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
    public RelayCommand<HostRepositoryRowViewModel> CloneCommand => _owner.CloneCommand;

    /// <summary>Gets the command that opens this repository's page on its host.</summary>
    public AsyncRelayCommand<HostRepositoryRowViewModel> OpenCommand => _owner.OpenCommand;

    /// <inheritdoc />
    public override string ToString() => Repository.FullName;
}

/// <summary>
/// ViewModel behind the dialog listing what one connected account can reach, to open or to clone.
/// </summary>
/// <remarks>
/// <para>
/// Repositories and clone links only — no issues, no pull requests. That is the product's scope and
/// it is also why no token scope beyond reading repositories is ever asked for.
/// </para>
/// <para>
/// The dialog does not clone anything itself: choosing <em>Clone</em> names the repository in
/// <see cref="Picked"/> and raises <see cref="CloneRequested"/>, and whoever showed the dialog closes
/// it and runs the clone — with the window's own progress and cancel, which a dialog would cover.
/// </para>
/// </remarks>
public sealed class HostRepositoriesDialogViewModel : ViewModelBase
{
    /// <summary>
    /// How many pages of repositories are fetched before the listing stops asking for more.
    /// </summary>
    /// <remarks>
    /// A hundred per page: ten pages is a thousand repositories, which is past the point where a
    /// list is how anyone finds anything. The search box filters what has been read.
    /// </remarks>
    public const int PageLimit = 10;

    private readonly IRepositoryHostProvider _provider;
    private readonly IHostAccountService _accounts;
    private readonly IHostLinkService _links;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger _logger;
    private readonly CancellationToken _lifetime;

    private readonly List<HostRepositoryRowViewModel> _all = [];

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="account">The account whose repositories are listed.</param>
    /// <param name="provider">The provider that speaks to its host.</param>
    /// <param name="accounts">Supplies the account's token.</param>
    /// <param name="links">Opens a repository's page in a browser.</param>
    /// <param name="infoBar">Reports what went wrong.</param>
    /// <param name="logger">Receives failures reported to the user another way.</param>
    /// <param name="lifetime">Cancels a listing when the application or the repository goes away.</param>
    public HostRepositoriesDialogViewModel(
        HostAccount account,
        IRepositoryHostProvider provider,
        IHostAccountService accounts,
        IHostLinkService links,
        IInfoBarService infoBar,
        ILogger logger,
        CancellationToken lifetime = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        Account = account;
        _provider = provider;
        _accounts = accounts;
        _links = links;
        _infoBar = infoBar;
        _logger = logger;
        _lifetime = lifetime;

        CloneCommand = new RelayCommand<HostRepositoryRowViewModel>(OnClone);
        OpenCommand = new AsyncRelayCommand<HostRepositoryRowViewModel>(OnOpenAsync);
        ClearSearchCommand = new RelayCommand(() => Search = string.Empty, () => Search.Length > 0);

        ShowAllCommand = new RelayCommand(() => Visibility = HostVisibility.All);
        ShowPublicCommand = new RelayCommand(() => Visibility = HostVisibility.Public);
        ShowPrivateCommand = new RelayCommand(() => Visibility = HostVisibility.Private);
    }

    /// <summary>
    /// Raised when a repository was picked to clone.
    /// </summary>
    public event EventHandler? CloneRequested;

    /// <summary>Gets the account whose repositories are listed.</summary>
    public HostAccount Account { get; }

    /// <summary>Gets the dialog's title, which names the account and its host.</summary>
    public string Title => $"{Account.DisplayName} on {_provider.DisplayName}";

    /// <summary>Gets the repositories read, filtered by the search box.</summary>
    public ObservableCollection<HostRepositoryRowViewModel> Repositories { get; } = [];

    /// <summary>Gets the repository picked to clone, or <see langword="null"/> while none is.</summary>
    public HostRepository? Picked { get; private set; }

    /// <summary>
    /// Gets or sets the text the list is filtered by.
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

                _ = LoadAsync();
            }
        }
    } = HostVisibility.All;

    /// <summary>Gets a value indicating whether every repository is listed.</summary>
    public bool IsAllVisible => Visibility == HostVisibility.All;

    /// <summary>Gets a value indicating whether only public repositories are listed.</summary>
    public bool IsPublicOnly => Visibility == HostVisibility.Public;

    /// <summary>Gets a value indicating whether only private repositories are listed.</summary>
    public bool IsPrivateOnly => Visibility == HostVisibility.Private;

    /// <summary>Gets a value indicating whether there is anything in the list.</summary>
    public bool HasRepositories => Repositories.Count > 0;

    /// <summary>
    /// Gets the sentence describing the list: how many were read, that there are more, or why there
    /// is nothing.
    /// </summary>
    public string Summary { get; private set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>Gets what the list says while it has nothing to show.</summary>
    public string EmptyMessage
        => IsBusy ? "Reading the repositories…" : "This account has nothing to show, or the filter matched nothing.";

    /// <summary>Gets the command that picks a repository to clone.</summary>
    public RelayCommand<HostRepositoryRowViewModel> CloneCommand { get; }

    /// <summary>Gets the command that opens a repository's page on its host.</summary>
    public AsyncRelayCommand<HostRepositoryRowViewModel> OpenCommand { get; }

    /// <summary>Gets the command that empties the search box.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>Gets the command that lists everything the account can see.</summary>
    public RelayCommand ShowAllCommand { get; }

    /// <summary>Gets the command that lists only public repositories.</summary>
    public RelayCommand ShowPublicCommand { get; }

    /// <summary>Gets the command that lists only private repositories.</summary>
    public RelayCommand ShowPrivateCommand { get; }

    /// <summary>
    /// Reads the account's repositories again, following every page the host offers up to
    /// <see cref="PageLimit"/>.
    /// </summary>
    /// <returns>A task that completes once the list is up to date, or the failure reported.</returns>
    public async Task LoadAsync()
    {
        _all.Clear();
        Repositories.Clear();
        Summary = string.Empty;
        OnPropertyChanged(nameof(HasRepositories));

        IsBusy = true;

        try
        {
            SecretString? token = await _accounts
                .GetTokenAsync(Account, _lifetime)
                .ConfigureAwait(true);

            if (token is null)
            {
                Fail("No token for this account", "Disconnect it and connect it again.", InfoBarSeverity.Warning);
                return;
            }

            List<HostRepositoryRowViewModel> loaded = [];
            string? cursor = null;
            bool truncated = false;

            for (int page = 0; page < PageLimit; page++)
            {
                HostRepositoryPage current = await _provider
                    .ListRepositoriesAsync(
                        Account,
                        token,
                        new HostRepositoryQuery(Visibility: Visibility, Cursor: cursor),
                        _lifetime)
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
            Fail("Rate limited", Describe(exception), InfoBarSeverity.Warning);
        }
        catch (HostException exception)
        {
            _logger.LogError(exception, "Listing repositories failed");

            Fail("Could not list the repositories", exception.Message, InfoBarSeverity.Error);
        }
        catch (OperationCanceledException)
        {
            // Expected when the application closes under the read.
        }
        catch (System.Net.Http.HttpRequestException exception)
        {
            // No answer at all: a wrong instance URL, no network, or a certificate the machine does
            // not trust. The host never got to refuse anything, so it is reported separately.
            _logger.LogWarning(exception, "Reaching the host failed");

            Fail(
                "Could not reach the host",
                "Check the instance URL and the network connection.",
                InfoBarSeverity.Error);
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

    /// <inheritdoc />
    protected override void OnBusyChanged() => OnPropertyChanged(nameof(EmptyMessage));

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

    private void OnClone(HostRepositoryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Picked = row.Repository;
        CloneRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task OnOpenAsync(HostRepositoryRowViewModel? row)
    {
        if (row is not null)
        {
            await _links.OpenAsync(row.Repository.WebUrl).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Reports a failure on the bar and says it in the dialog too, where the reader is looking.
    /// </summary>
    private void Fail(string title, string message, InfoBarSeverity severity)
    {
        Summary = $"{title}. {message}";
        _infoBar.Notify(title, message, severity);
    }

    private static string Count(int repositories)
        => repositories == 1
            ? "1 repository"
            : $"{repositories.ToString(CultureInfo.CurrentCulture)} repositories";
}
