using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Enigma.GitClient.Desktop.Views;

/// <summary>
/// Makes the whole of a line in a list or a tree answer a right-click with the line's own menu.
/// </summary>
/// <remarks>
/// The menu belongs to the row template, and the row is the container's content. It sits inside the
/// list item's padding, and inside the tree item's indentation and chevron, all of which are the
/// container's own area and carry no menu. A right-click there used to open nothing, so the menu only
/// seemed to open over the text. Every view whose lines carry their menus attaches this — the changed
/// files, the branches, the tags, the remotes — so they all answer the same way.
/// </remarks>
internal static class LineMenus
{
    /// <summary>
    /// Hands a right-click that landed on a line's container, outside its row, to the row's menu.
    /// </summary>
    /// <param name="view">The view holding the lines.</param>
    public static void Attach(Control view)
    {
        ArgumentNullException.ThrowIfNull(view);

        // Bubbling, and not for handled requests: a right-click on a line's own row has already opened
        // that row's menu by the time the request gets here. What arrives is a right-click that landed
        // on the line's container instead.
        view.AddHandler(InputElement.ContextRequestedEvent, OnContextRequested, RoutingStrategies.Bubble);
    }

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }

        // The nearest container: in a tree, a nested line's own item comes before its folder's.
        Control? container = source.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(control => control is ListBoxItem or TreeViewItem);

        if (container?.DataContext is not { } line)
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
