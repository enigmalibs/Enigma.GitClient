using Enigma.GitClient.App.Services;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// ViewModel behind the integrations page.
/// </summary>
/// <remarks>
/// Connect a GitHub, GitLab or Azure DevOps account to browse and clone your repositories.
/// </remarks>
public sealed class IntegrationsPageViewModel : PageViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    public IntegrationsPageViewModel(IRepositoryContext repositoryContext)
        : base(repositoryContext)
    {
    }

    /// <summary>
    /// Gets the page's title, shown in its header.
    /// </summary>
    public string Title => "Integrations";

    /// <summary>
    /// Gets the sentence shown while the page has nothing to display.
    /// </summary>
    public string EmptyMessage => "Connect a GitHub, GitLab or Azure DevOps account to browse and clone your repositories.";
}
