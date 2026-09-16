using CommunityToolkit.Mvvm.ComponentModel;

namespace Enigma.GitClient.App.ViewModels;

/// <summary>
/// Base class for every ViewModel in the application.
/// </summary>
/// <remarks>
/// Properties are declared explicitly with the <c>field</c> keyword and commands are get-only
/// properties initialised in the constructor — the MVVM source generators are deliberately not used
/// anywhere in this application.
/// </remarks>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Gets or sets a value indicating whether a long-running operation is in flight, which the
    /// views use to disable their commands and show progress.
    /// </summary>
    public bool IsBusy
    {
        get;
        protected set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                OnBusyChanged();
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the ViewModel is idle. Bound by views that enable content
    /// while nothing is running.
    /// </summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>
    /// Called after <see cref="IsBusy"/> changes, so a derived ViewModel can re-evaluate the
    /// commands that depend on it.
    /// </summary>
    protected virtual void OnBusyChanged()
    {
    }
}
