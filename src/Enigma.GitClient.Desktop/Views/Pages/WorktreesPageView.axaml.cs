using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Enigma.GitClient.Desktop.ViewModels.Pages;

namespace Enigma.GitClient.Desktop.Views.Pages;

/// <summary>
/// The Worktrees dialog's page.
/// </summary>
public partial class WorktreesPageView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public WorktreesPageView()
    {
        InitializeComponent();

        // The whole line answers a right-click with its menu, the container's padding included.
        LineMenus.Attach(this);

        // A double-click on a line opens that worktree in a new window, as its menu's first item does.
        AddHandler(DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Bubble);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source
            || source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is not { DataContext: WorktreeRowViewModel row })
        {
            return;
        }

        if (row.OpenCommand.CanExecute(row))
        {
            e.Handled = true;
            _ = row.OpenCommand.ExecuteAsync(row);
        }
    }
}
