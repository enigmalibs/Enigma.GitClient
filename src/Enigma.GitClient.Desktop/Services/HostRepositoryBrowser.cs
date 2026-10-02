using System;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Shows what a connected account can reach, in a dialog.
/// </summary>
public interface IHostRepositoryBrowser
{
    /// <summary>
    /// Lists an account's repositories in a dialog, and waits until it is closed.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <returns>
    /// The repository picked to clone — the dialog closes on that — or <see langword="null"/> when it
    /// was closed without one.
    /// </returns>
    /// <remarks>
    /// The clone is the caller's to run once the dialog is gone: it has a progress card and a cancel
    /// of its own, which a dialog left open would cover.
    /// </remarks>
    Task<HostRepository?> BrowseAsync(HostAccount account);
}

/// <summary>
/// Default <see cref="IHostRepositoryBrowser"/>.
/// </summary>
public sealed class HostRepositoryBrowser : IHostRepositoryBrowser
{
    private readonly IRepositoryContext _context;
    private readonly IHostAccountService _accounts;
    private readonly IHostProviderRegistry _registry;
    private readonly IHostLinkService _links;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly IServiceProvider _services;
    private readonly ILogger<HostRepositoryBrowser> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">Supplies the lifetime a listing is cancelled with.</param>
    /// <param name="accounts">Supplies the account's token.</param>
    /// <param name="registry">Finds the provider that speaks to the account's host.</param>
    /// <param name="links">Opens a repository's page in a browser.</param>
    /// <param name="dialogs">Shows the dialog.</param>
    /// <param name="infoBar">Reports what went wrong.</param>
    /// <param name="services">Resolves the dialog's view.</param>
    /// <param name="logger">Receives failures reported to the user another way.</param>
    public HostRepositoryBrowser(
        IRepositoryContext context,
        IHostAccountService accounts,
        IHostProviderRegistry registry,
        IHostLinkService links,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        IServiceProvider services,
        ILogger<HostRepositoryBrowser> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _accounts = accounts;
        _registry = registry;
        _links = links;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HostRepository?> BrowseAsync(HostAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (_registry.Find(account.Kind) is not { } provider)
        {
            _infoBar.Notify(
                "Nothing speaks to this host",
                $"This build cannot list the repositories of {account.DisplayName}.",
                InfoBarSeverity.Warning);

            return null;
        }

        HostRepositoriesDialogViewModel model = new(
            account,
            provider,
            _accounts,
            _links,
            _infoBar,
            _logger,
            _context.RepositoryLifetime);

        HostRepositoriesDialogView view =
            _services.GetService(typeof(HostRepositoriesDialogView)) as HostRepositoriesDialogView
            ?? new HostRepositoriesDialogView();

        view.DataContext = model;

        void OnCloneRequested(object? sender, EventArgs e) => _ = _dialogs.HideAsync();

        model.CloneRequested += OnCloneRequested;

        try
        {
            // Open first, then read: the host can take a while to answer, and a dialog that waited
            // for it would appear late for no reason. The list fills in under the reader.
            Task<DialogResult> showing = _dialogs.ShowAsync(dialog =>
            {
                dialog.Title = null;
                dialog.Content = view;
                dialog.PrimaryButtonText = null;
                dialog.SecondaryButtonText = null;
                dialog.CloseButtonText = "Close";
                dialog.DefaultButton = DefaultButton.Close;
            });

            await model.LoadAsync().ConfigureAwait(true);
            await showing.ConfigureAwait(true);
        }
        finally
        {
            model.CloneRequested -= OnCloneRequested;
        }

        return model.Picked;
    }
}
