using Enigma.GitClient.App.Services;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// ViewModel behind the settings page.
/// </summary>
/// <remarks>
/// Appearance, history, diff and git preferences live here.
/// </remarks>
public sealed class SettingsPageViewModel : PageViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    public SettingsPageViewModel(IRepositoryContext repositoryContext)
        : base(repositoryContext)
    {
    }

    /// <summary>
    /// Gets the page's title, shown in its header.
    /// </summary>
    public string Title => "Settings";

    /// <summary>
    /// Gets the sentence shown while the page has nothing to display.
    /// </summary>
    public string EmptyMessage => "Appearance, history, diff and git preferences live here.";
}
