using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Enigma.GitClient.App.ViewModels.Panels;

namespace Enigma.GitClient.App.Views.Panels;

/// <summary>
/// The panel listing what a change touched, as a list or as a tree.
/// </summary>
public partial class ChangedFilesPanelView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public ChangedFilesPanelView()
    {
        InitializeComponent();

        // Bubbling, and not for handled requests: a right-click on a line's own row has already opened
        // that row's menu by the time the request gets here. What arrives is a right-click that landed
        // on the line's container instead.
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Bubble);
    }

    /// <summary>
    /// Opens a line's menu when the right-click landed on the line but outside its row.
    /// </summary>
    /// <param name="sender">The panel.</param>
    /// <param name="e">The request.</param>
    /// <remarks>
    /// The menu belongs to the row template, and the row is the container's content. It sits inside
    /// the list item's padding, and inside the tree item's indentation and chevron, all of which are
    /// the container's own area and carry no menu. A right-click there used to open nothing, so the
    /// menu only seemed to open over the text. The request is handed to the row the container
    /// presents, so the whole line answers with the same menu.
    /// </remarks>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }

        // The nearest container: in a tree, a nested line's own item comes before its folder's.
        Control? container = source.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(control => control is ListBoxItem or TreeViewItem);

        if (container?.DataContext is not ChangedFileNodeViewModel line)
        {
            return;
        }

        Control? row = container.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(control => control.ContextMenu is not null && ReferenceEquals(control.DataContext, line));

        if (row?.ContextMenu is not { } menu)
        {
            return;
        }

        menu.Open(row);
        e.Handled = true;
    }
}
