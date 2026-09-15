using Enigma.GitClient.App.Services;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// ViewModel behind the working directory page.
/// </summary>
/// <remarks>
/// Stage, unstage and commit your work here.
/// </remarks>
public sealed class ChangesPageViewModel : PageViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    public ChangesPageViewModel(IRepositoryContext repositoryContext)
        : base(repositoryContext)
    {
    }

    /// <summary>
    /// Gets the page's title, shown in its header.
    /// </summary>
    public string Title => "Working directory";

    /// <summary>
    /// Gets the sentence shown while the page has nothing to display.
    /// </summary>
    public string EmptyMessage => "Open a repository to see what has changed in its working directory.";
}
