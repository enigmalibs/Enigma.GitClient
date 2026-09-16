using System;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;

namespace Enigma.GitClient.App.ViewModels;

/// <summary>
/// Base class for the ViewModel behind a navigable page.
/// </summary>
/// <remarks>
/// Every page observes <see cref="IRepositoryContext"/> rather than holding its own handle, so a
/// repository change updates the whole shell at once. Page ViewModels are registered as singletons,
/// so a page revisited through the rail keeps the state it had.
/// </remarks>
public abstract class PageViewModelBase : ViewModelBase, INavigationViewModel
{
    private bool _subscribed;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository every page observes.</param>
    protected PageViewModelBase(IRepositoryContext repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(repositoryContext);
        RepositoryContext = repositoryContext;
    }

    /// <summary>
    /// Gets the repository the application is looking at.
    /// </summary>
    public IRepositoryContext RepositoryContext { get; }

    /// <summary>
    /// Gets a value indicating whether a repository is open, which pages bind their empty state to.
    /// </summary>
    public bool IsRepositoryOpen => RepositoryContext.IsRepositoryOpen;

    /// <inheritdoc />
    public virtual Task OnAppearingAsync(object? parameter = null)
    {
        EnsureSubscribed();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task<bool> OnDisappearingAsync() => Task.FromResult(true);

    /// <summary>
    /// Called when a different repository is opened or the current one is closed. The base
    /// implementation raises the property changes every page's empty state depends on.
    /// </summary>
    protected virtual void OnRepositoryChanged()
        => OnPropertyChanged(nameof(IsRepositoryOpen));

    /// <summary>
    /// Called after the repository's reference state has been re-read.
    /// </summary>
    protected virtual void OnRepositoryStateRefreshed()
    {
    }

    /// <summary>
    /// Subscribes to the repository context once, the first time the page appears. Doing it lazily
    /// rather than in the constructor keeps a page that is never visited from doing any work.
    /// </summary>
    protected void EnsureSubscribed()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        RepositoryContext.RepositoryChanged += (_, _) => OnRepositoryChanged();
        RepositoryContext.StateRefreshed += (_, _) => OnRepositoryStateRefreshed();
    }
}
