using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Enigma.GitClient.Desktop.ViewModels.Panels;

namespace Enigma.GitClient.Desktop.Views.Panels;

/// <summary>
/// The working tree, in the history's details panel: what is not staged, what is, and the commit.
/// </summary>
public partial class WorkingTreePanelView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public WorkingTreePanelView()
    {
        InitializeComponent();

        // Tunnelling: a TextBox that accepts newlines takes Ctrl+Enter for one of its own, and a
        // handler that waits for the key to bubble up from it never sees the key at all.
        MessageBox.AddHandler(KeyDownEvent, OnMessageKeyDown, RoutingStrategies.Tunnel);
    }

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

        if (DataContext is WorkingTreePanelViewModel panel && panel.CommitCommand.CanExecute(null))
        {
            panel.CommitCommand.Execute(null);
            e.Handled = true;
        }
    }
}
