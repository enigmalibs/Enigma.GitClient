using System;
using System.Collections.Generic;
using Enigma.Avalonia.Desktop.Controls.Navigation;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Navigation;

/// <summary>
/// The pages the navigation rail offers.
/// </summary>
public enum ShellPage
{
    /// <summary>Recent repositories, and the open / clone / create actions.</summary>
    Repositories,

    /// <summary>The commit graph.</summary>
    History,

    /// <summary>The working directory.</summary>
    Changes,

    /// <summary>Branches and tags.</summary>
    Branches,

    /// <summary>Remotes, synchronisation and stashes.</summary>
    Remotes,

    /// <summary>Hosting integrations.</summary>
    Integrations,

    /// <summary>Preferences.</summary>
    Settings,
}

/// <summary>
/// Owns the navigation rail: which pages exist, and how anything in the application moves between
/// them.
/// </summary>
/// <remarks>
/// A page ViewModel must be able to say "now show the history" without reaching for the window's
/// ViewModel, which is why the rail lives in a service rather than in <c>MainWindowViewModel</c>.
/// </remarks>
public interface IShellNavigation
{
    /// <summary>
    /// Gets the underlying navigation service the view binds to.
    /// </summary>
    INavigationService Service { get; }

    /// <summary>
    /// Selects the page the application opens on.
    /// </summary>
    /// <remarks>
    /// Deliberately not done in the constructor. Selecting a page builds it, and a page's ViewModel
    /// may itself depend on this service — resolving it from inside this service's own constructor
    /// would construct a second instance and build the rail twice.
    /// </remarks>
    void Start();

    /// <summary>
    /// Navigates to a page.
    /// </summary>
    /// <param name="page">The page to show.</param>
    void GoTo(ShellPage page);

    /// <summary>
    /// Gets the page currently selected in the rail.
    /// </summary>
    ShellPage Current { get; }
}

/// <summary>
/// Default <see cref="IShellNavigation"/>. Builds the rail once and resolves every page from the
/// container.
/// </summary>
public sealed class ShellNavigation : IShellNavigation
{
    private readonly Dictionary<ShellPage, NavigationItem> _items = [];

    /// <summary>
    /// Initialises a new instance, building the rail.
    /// </summary>
    /// <param name="navigation">The navigation service to drive.</param>
    /// <param name="services">The container pages are resolved from.</param>
    /// <param name="logger">Receives navigation failures, which the service reports rather than throws.</param>
    public ShellNavigation(
        INavigationService navigation,
        IServiceProvider services,
        ILogger<ShellNavigation> logger)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        Service = navigation;
        navigation.PageFactory = new ContainerPageFactory(services).Create;
        navigation.NavigationFailed += (_, e) =>
            logger.LogError(e.Exception, "Navigation failed during {Phase}", e.Phase);

        Add(navigation.Items, ShellPage.Repositories, "Repositories", PhosphorIcon.Folders, typeof(RepositoriesPageView), typeof(RepositoriesPageViewModel));
        Add(navigation.Items, ShellPage.History, "History", PhosphorIcon.GitCommit, typeof(HistoryPageView), typeof(HistoryPageViewModel));
        Add(navigation.Items, ShellPage.Changes, "Changes", PhosphorIcon.FileText, typeof(ChangesPageView), typeof(ChangesPageViewModel));
        Add(navigation.Items, ShellPage.Branches, "Branches", PhosphorIcon.GitBranch, typeof(BranchesPageView), typeof(BranchesPageViewModel));
        Add(navigation.Items, ShellPage.Remotes, "Remotes", PhosphorIcon.CloudArrowUp, typeof(RemotesPageView), typeof(RemotesPageViewModel));

        Add(navigation.FooterItems, ShellPage.Integrations, "Integrations", PhosphorIcon.GlobeSimple, typeof(IntegrationsPageView), typeof(IntegrationsPageViewModel));
        Add(navigation.FooterItems, ShellPage.Settings, "Settings", PhosphorIcon.Gear, typeof(SettingsPageView), typeof(SettingsPageViewModel));
    }

    /// <inheritdoc />
    public INavigationService Service { get; }

    /// <inheritdoc />
    public ShellPage Current { get; private set; } = ShellPage.Repositories;

    /// <inheritdoc />
    public void Start() => GoTo(ShellPage.Repositories);

    /// <inheritdoc />
    public void GoTo(ShellPage page)
    {
        if (_items.TryGetValue(page, out NavigationItem? item))
        {
            Service.SelectedItem = item;
            Current = page;
        }
    }

    private void Add(
        ICollection<NavigationItem> target,
        ShellPage page,
        string header,
        PhosphorIcon icon,
        Type pageType,
        Type viewModelType)
    {
        NavigationItem item = new()
        {
            Header = header,
            IconData = PhosphorIconSet.Instance.GetGlyph(icon, PhosphorWeight.Regular).ToGeometry(),
            PageType = pageType,
            PageViewModelType = viewModelType,
        };

        _items[page] = item;
        target.Add(item);
    }
}
