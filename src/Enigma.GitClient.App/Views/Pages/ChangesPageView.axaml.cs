using Avalonia.Controls;
using Avalonia.Input;
using Enigma.GitClient.App.ViewModels.Pages;

namespace Enigma.GitClient.App.Views.Pages;

/// <summary>
/// The working directory page.
/// </summary>
public partial class ChangesPageView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public ChangesPageView() => InitializeComponent();

    /// <summary>
    /// Commits on Ctrl+Enter, which is the shortcut every other client uses and the reason the
    /// message box accepts newlines at all.
    /// </summary>
    /// <param name="sender">The message box.</param>
    /// <param name="e">The key.</param>
    private void OnMessageKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return) || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (DataContext is ChangesPageViewModel page && page.CommitCommand.CanExecute(null))
        {
            page.CommitCommand.Execute(null);
            e.Handled = true;
        }
    }
}
