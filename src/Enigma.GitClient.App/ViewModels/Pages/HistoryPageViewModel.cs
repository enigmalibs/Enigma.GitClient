using Enigma.GitClient.App.Services;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// ViewModel behind the commit history page.
/// </summary>
/// <remarks>
/// The commit graph appears here once a repository is open.
/// </remarks>
public sealed class HistoryPageViewModel : PageViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    public HistoryPageViewModel(IRepositoryContext repositoryContext)
        : base(repositoryContext)
    {
    }

    /// <summary>
    /// Gets the page's title, shown in its header.
    /// </summary>
    public string Title => "Commit history";

    /// <summary>
    /// Gets the sentence shown while the page has nothing to display.
    /// </summary>
    public string EmptyMessage => "Open a repository to see its history, with the graph, the authors, the timestamps and the short hashes.";
}
