using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.ViewModels.Pages;

namespace Enigma.GitClient.Desktop.Views.Pages;

/// <summary>
/// The Submodules dialog's page.
/// </summary>
public partial class SubmodulesPageView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public SubmodulesPageView()
    {
        InitializeComponent();

        // The whole line answers a right-click with its menu, the container's padding included.
        LineMenus.Attach(this);

        // A double-click on a line opens that submodule in a new window, as its menu's first item does.
        AddHandler(DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Bubble);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source
            || source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is not { DataContext: SubmoduleRowViewModel row })
        {
            return;
        }

        if (row.OpenCommand.CanExecute(row))
        {
            e.Handled = true;
            row.OpenCommand.ExecuteAsync(row).Forget();
        }
    }
}
