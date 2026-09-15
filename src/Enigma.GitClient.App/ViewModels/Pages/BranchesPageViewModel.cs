using Enigma.GitClient.App.Services;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// ViewModel behind the branches and tags page.
/// </summary>
/// <remarks>
/// Create, rename, delete and check out branches and tags here.
/// </remarks>
public sealed class BranchesPageViewModel : PageViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    public BranchesPageViewModel(IRepositoryContext repositoryContext)
        : base(repositoryContext)
    {
    }

    /// <summary>
    /// Gets the page's title, shown in its header.
    /// </summary>
    public string Title => "Branches and tags";

    /// <summary>
    /// Gets the sentence shown while the page has nothing to display.
    /// </summary>
    public string EmptyMessage => "Open a repository to manage its branches and tags.";
}
