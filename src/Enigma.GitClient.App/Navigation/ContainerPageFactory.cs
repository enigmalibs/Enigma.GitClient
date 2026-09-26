using System;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Controls.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Enigma.GitClient.App.Navigation;

/// <summary>
/// Builds a navigation page out of the dependency-injection container instead of
/// <c>Activator.CreateInstance</c>, so both the view and its ViewModel can take constructor
/// dependencies.
/// </summary>
public sealed class ContainerPageFactory
{
    private readonly IServiceProvider _services;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="services">The container pages are resolved from.</param>
    public ContainerPageFactory(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <summary>
    /// Resolves a navigation item's page and attaches its ViewModel.
    /// </summary>
    /// <param name="item">The item the rail selected.</param>
    /// <returns>The page, with its <see cref="Control.DataContext"/> already set.</returns>
    public Control Create(NavigationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Control page = (Control)_services.GetRequiredService(item.PageType);
        page.DataContext = _services.GetRequiredService(item.PageViewModelType);

        return page;
    }
}
