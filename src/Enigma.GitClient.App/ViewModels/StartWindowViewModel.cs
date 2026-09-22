using System;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Navigation;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.Core.Diagnostics;

namespace Enigma.GitClient.App.ViewModels;

/// <summary>
/// The start window: choose a repository, or look after the integrations and the settings, before
/// any repository is open.
/// </summary>
public sealed class StartWindowViewModel : ViewModelBase
{
    private readonly RepositoriesPageViewModel _repositories;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="navigation">Owns the start window's rail.</param>
    /// <param name="repositories">The repositories page, whose recent list is re-read on every showing.</param>
    public StartWindowViewModel(IStartNavigation navigation, RepositoriesPageViewModel repositories)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(repositories);

        StartNavigation = navigation;
        _repositories = repositories;
    }

    /// <summary>Gets the rail the start window binds to.</summary>
    public IStartNavigation StartNavigation { get; }

    /// <summary>Gets the navigation service the rail and the content area bind to.</summary>
    public INavigationService Navigation => StartNavigation.Service;

    /// <summary>Gets the window title.</summary>
    public string WindowTitle => ProductInformation.Name;

    /// <summary>
    /// Runs every time the start window opens: the first time, it selects the repositories page;
    /// afterwards — coming back from a repository — it re-reads the recent list, which that
    /// repository has just moved to the top.
    /// </summary>
    /// <returns>A task that completes once the page is current.</returns>
    public async Task InitialiseAsync()
    {
        if (Navigation.SelectedItem is null)
        {
            StartNavigation.Start();
            return;
        }

        await _repositories.ReloadRecentAsync().ConfigureAwait(true);
    }
}
