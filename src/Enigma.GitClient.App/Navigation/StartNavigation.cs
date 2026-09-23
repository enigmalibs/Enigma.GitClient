using System;
using System.Collections.Generic;
using Enigma.Avalonia.Desktop.Controls.Navigation;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Navigation;

/// <summary>
/// The pages the start window's rail offers.
/// </summary>
public enum StartPage
{
    /// <summary>Recent repositories, and the open / clone / create actions.</summary>
    Repositories,

    /// <summary>Hosting integrations.</summary>
    Integrations,

    /// <summary>Preferences.</summary>
    Settings,
}

/// <summary>
/// Owns the start window's rail.
/// </summary>
/// <remarks>
/// A navigation service of its own, not the repository window's: the two rails hold different pages,
/// and a service holds one selection.
/// </remarks>
public interface IStartNavigation
{
    /// <summary>Gets the underlying navigation service the view binds to.</summary>
    INavigationService Service { get; }

    /// <summary>Gets the page currently selected in the rail.</summary>
    StartPage Current { get; }

    /// <summary>
    /// Selects the first page, once. Deliberately not done in the constructor, for the same reason
    /// as <see cref="IShellNavigation.Start"/>: selecting a page builds it.
    /// </summary>
    void Start();

    /// <summary>
    /// Navigates to a page.
    /// </summary>
    /// <param name="page">The page to show.</param>
    void GoTo(StartPage page);
}

/// <summary>
/// Default <see cref="IStartNavigation"/>.
/// </summary>
public sealed class StartNavigation : IStartNavigation
{
    /// <summary>
    /// The key the start window's <see cref="INavigationService"/> is registered under.
    /// </summary>
    public const string ServiceKey = "start";

    private readonly Dictionary<StartPage, NavigationItem> _items = [];

    /// <summary>
    /// Initialises a new instance, building the rail.
    /// </summary>
    /// <param name="navigation">The start window's own navigation service.</param>
    /// <param name="services">The container pages are resolved from.</param>
    /// <param name="logger">Receives navigation failures, which the service reports rather than throws.</param>
    public StartNavigation(
        [FromKeyedServices(ServiceKey)] INavigationService navigation,
        IServiceProvider services,
        ILogger<StartNavigation> logger)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        Service = navigation;
        navigation.PageFactory = new ContainerPageFactory(services).Create;
        navigation.NavigationFailed += (_, e) =>
            logger.LogError(e.Exception, "Navigation failed during {Phase}", e.Phase);

        Add(navigation.Items, StartPage.Repositories, "Repositories", PhosphorIcon.Folders, typeof(RepositoriesPageView), typeof(RepositoriesPageViewModel));
        Add(navigation.FooterItems, StartPage.Integrations, "Integrations", PhosphorIcon.GlobeSimple, typeof(IntegrationsPageView), typeof(IntegrationsPageViewModel));
        Add(navigation.FooterItems, StartPage.Settings, "Settings", PhosphorIcon.Gear, typeof(SettingsPageView), typeof(SettingsPageViewModel));
    }

    /// <inheritdoc />
    public INavigationService Service { get; }

    /// <inheritdoc />
    public StartPage Current { get; private set; } = StartPage.Repositories;

    /// <inheritdoc />
    public void Start()
    {
        if (Service.SelectedItem is null)
        {
            GoTo(StartPage.Repositories);
        }
    }

    /// <inheritdoc />
    public void GoTo(StartPage page)
    {
        if (_items.TryGetValue(page, out NavigationItem? item))
        {
            Service.SelectedItem = item;
            Current = page;
        }
    }

    private void Add(
        ICollection<NavigationItem> target,
        StartPage page,
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
