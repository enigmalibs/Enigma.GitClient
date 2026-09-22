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
/// The pages the repository window's rail offers.
/// </summary>
public enum ShellPage
{
    /// <summary>The commit graph.</summary>
    History,

    /// <summary>The working directory.</summary>
    Changes,

    /// <summary>The conflicts of a merge in progress, which is the only time it exists.</summary>
    Conflicts,

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
    /// Selects the page the repository window opens on, which is the history: choosing a repository
    /// is the start window's business.
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

    /// <summary>
    /// Shows or hides the conflicts page.
    /// </summary>
    /// <param name="visible">Whether a merge is waiting to be resolved.</param>
    /// <remarks>
    /// The rail has no room for a page that is only meaningful during a merge, and a permanently
    /// greyed entry teaches nothing. The item appears when a merge stops on conflicts and goes away
    /// when the merge does, which is also how the user learns the page exists.
    /// </remarks>
    void SetConflictsVisible(bool visible);
}

/// <summary>
/// Default <see cref="IShellNavigation"/>. Builds the rail once and resolves every page from the
/// container.
/// </summary>
public sealed class ShellNavigation : IShellNavigation
{
    private readonly Dictionary<ShellPage, NavigationItem> _items = [];
    private readonly NavigationItem _conflicts;

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

        Add(navigation.Items, ShellPage.History, "History", PhosphorIcon.GitCommit, typeof(HistoryPageView), typeof(HistoryPageViewModel));
        Add(navigation.Items, ShellPage.Changes, "Changes", PhosphorIcon.FileText, typeof(ChangesPageView), typeof(ChangesPageViewModel));

        // Built like the others, but held back until a merge conflicts.
        _conflicts = Build(ShellPage.Conflicts, "Conflicts", PhosphorIcon.GitMerge, typeof(ConflictResolutionPageView), typeof(ConflictResolutionPageViewModel));

        Add(navigation.FooterItems, ShellPage.Integrations, "Integrations", PhosphorIcon.GlobeSimple, typeof(IntegrationsPageView), typeof(IntegrationsPageViewModel));
        Add(navigation.FooterItems, ShellPage.Settings, "Settings", PhosphorIcon.Gear, typeof(SettingsPageView), typeof(SettingsPageViewModel));
    }

    /// <inheritdoc />
    public INavigationService Service { get; }

    /// <inheritdoc />
    public ShellPage Current { get; private set; } = ShellPage.History;

    /// <inheritdoc />
    public void Start() => GoTo(ShellPage.History);

    /// <inheritdoc />
    public void SetConflictsVisible(bool visible)
    {
        bool present = Service.Items.Contains(_conflicts);

        if (visible == present)
        {
            return;
        }

        if (visible)
        {
            Service.Items.Add(_conflicts);
            return;
        }

        // Leaving first: removing the selected item would leave the rail with a selection that is
        // no longer in it, and the page it built still on screen.
        if (Current == ShellPage.Conflicts)
        {
            GoTo(ShellPage.History);
        }

        Service.Items.Remove(_conflicts);
    }

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
        => target.Add(Build(page, header, icon, pageType, viewModelType));

    private NavigationItem Build(
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

        return item;
    }
}
