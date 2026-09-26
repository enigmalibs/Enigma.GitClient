using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// The secondary pages the history opens over itself.
/// </summary>
public enum ToolDialog
{
    /// <summary>Branches.</summary>
    Branches,

    /// <summary>Tags.</summary>
    Tags,

    /// <summary>Remotes, synchronisation and stashes.</summary>
    Remotes,
}

/// <summary>
/// A window with a dialog host of its own for the tool dialogs, under the one every operation asks its
/// questions in.
/// </summary>
public interface IToolDialogHostWindow
{
    /// <summary>Gets the host the tool dialogs are shown in.</summary>
    ContentDialog ToolDialogHost { get; }
}

/// <summary>
/// Shows the branches, tags and remotes pages as dialogs over the repository window.
/// </summary>
/// <remarks>
/// A host of its own, not <see cref="IContentDialogService"/>'s: that service drives one dialog and
/// replaces its content on every question, and the operations these pages run all ask questions —
/// delete this branch?, rename it to what?, which upstream? Shown on the same host, the branches
/// dialog would be wiped out by the first confirmation it raised. On a host of its own, placed under
/// that one, the confirmation opens above it and the dialog is still there when it closes.
/// </remarks>
public interface IToolDialogService
{
    /// <summary>
    /// Gets a value indicating whether a tool dialog is on screen.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Hands over the control the dialogs are shown in. The last registration wins, as it does for the
    /// library's own hosts.
    /// </summary>
    /// <param name="host">The host.</param>
    void RegisterHost(ContentDialog host);

    /// <summary>
    /// Shows a page as a dialog, and waits until it is closed.
    /// </summary>
    /// <param name="dialog">Which page.</param>
    /// <returns>A task that completes once the dialog has closed.</returns>
    Task ShowAsync(ToolDialog dialog);
}

/// <summary>
/// Default <see cref="IToolDialogService"/>.
/// </summary>
public sealed class ToolDialogService : IToolDialogService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ToolDialogService> _logger;
    private ContentDialog? _host;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="services">The container the pages and their ViewModels are resolved from.</param>
    /// <param name="logger">Receives a page that failed to appear, which is reported rather than thrown.</param>
    public ToolDialogService(IServiceProvider services, ILogger<ToolDialogService> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsOpen { get; private set; }

    /// <inheritdoc />
    public void RegisterHost(ContentDialog host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <inheritdoc />
    public async Task ShowAsync(ToolDialog dialog)
    {
        ContentDialog host = _host
            ?? throw new InvalidOperationException("The tool dialog host has not been registered. Call RegisterHost first.");

        // One at a time: a second button press while one is open would replace it under the reader.
        if (IsOpen)
        {
            return;
        }

        (Type viewType, Type viewModelType) = dialog switch
        {
            ToolDialog.Branches => (typeof(BranchesPageView), typeof(BranchesPageViewModel)),
            ToolDialog.Tags => (typeof(TagsPageView), typeof(TagsPageViewModel)),
            ToolDialog.Remotes => (typeof(RemotesPageView), typeof(RemotesPageViewModel)),
            _ => throw new ArgumentOutOfRangeException(nameof(dialog), dialog, "Not a tool dialog."),
        };

        Control page = (Control)_services.GetRequiredService(viewType);
        object viewModel = _services.GetRequiredService(viewModelType);
        page.DataContext = viewModel;

        INavigationViewModel? lifecycle = viewModel as INavigationViewModel;

        IsOpen = true;

        // The page carries its own title, so the card does not repeat it.
        host.Title = null;
        host.IconData = null;
        host.Content = page;
        host.PrimaryButtonText = null;
        host.SecondaryButtonText = null;
        host.CloseButtonText = "Close";
        host.DefaultButton = DefaultButton.Close;

        try
        {
            // Open first, then let the page run its own lifecycle, exactly as the rail ran it — this
            // is where it subscribes to the repository and reads what it lists. Some pages read git
            // there, and a dialog that waited for them would appear late for no reason; the page
            // fills in under the reader instead.
            Task<DialogResult> showing = host.ShowAsync();

            await AppearAsync(lifecycle, dialog).ConfigureAwait(true);

            await showing.ConfigureAwait(true);
        }
        finally
        {
            // Freed, so the page's view does not outlive the dialog and a next showing starts clean.
            if (host.IsOpen)
            {
                await host.HideAsync().ConfigureAwait(true);
            }

            host.Content = null;
            IsOpen = false;

            if (lifecycle is not null)
            {
                await lifecycle.OnDisappearingAsync().ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    /// Runs the page's appearing step, reporting a failure rather than throwing it.
    /// </summary>
    /// <remarks>
    /// What the rail's navigation does with the same step: a page that could not read its list is a
    /// page with an empty list and a line in the log, not a button that takes the window down. The
    /// dialog stays open, and the page's own refresh is there to try again.
    /// </remarks>
    private async Task AppearAsync(INavigationViewModel? lifecycle, ToolDialog dialog)
    {
        if (lifecycle is null)
        {
            return;
        }

        try
        {
            await lifecycle.OnAppearingAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "The {Dialog} dialog failed to appear", dialog);
        }
    }
}
