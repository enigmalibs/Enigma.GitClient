using CommunityToolkit.Mvvm.ComponentModel;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// How many lines of a page's list are selected, which every line's menu reads: an action on one item
/// is offered only while no more than one line is selected.
/// </summary>
/// <remarks>
/// One object per page, shared by all its lines, so a line rebuilt on a refresh still sees the page's
/// selection — and a menu already open follows it.
/// </remarks>
public sealed class LineSelection : ObservableObject
{
    /// <summary>Gets how many lines are selected.</summary>
    public int Count
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsMultiple));
            }
        }
    }

    /// <summary>Gets a value indicating whether more than one line is selected.</summary>
    public bool IsMultiple => Count > 1;

    /// <summary>
    /// Says how many lines the page has selected now.
    /// </summary>
    /// <param name="count">How many.</param>
    internal void Update(int count) => Count = count;
}
