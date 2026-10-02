using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Enigma.GitClient.Desktop.Views.Pages;

namespace Enigma.GitClient.Desktop.Views.Panels;

/// <summary>
/// The panel listing what a change touched, as a list or as a tree.
/// </summary>
public partial class ChangedFilesPanelView : UserControl
{
    // A plain press on the line that is already selected, which lets go of it when it is released on
    // the same line without having become a drag.
    private PendingToggle? _toggle;

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

        // Tunnelling, because the list and the tree handle the pointer themselves: the press has to be
        // seen before they act on it, while the line under it is still the one that was selected.
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
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
        Control? container = ContainerOf(source);

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

    // ---------------------------------------------------------------- the lines are toggles

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _toggle = null;

        // The second press of a double-click is not a toggle: a double-click on a line leaves it
        // selected, however the first press found it.
        if (e.ClickCount != 1
            || e.KeyModifiers != KeyModifiers.None
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || e.Source is not Visual source)
        {
            return;
        }

        // A button on the line — its Stage or Unstage, a folder's chevron — does its own thing, and
        // leaves the selection alone.
        if (source.GetSelfAndVisualAncestors().TakeWhile(visual => visual is not (ListBoxItem or TreeViewItem)).OfType<Button>().Any())
        {
            return;
        }

        // The list selects a line on the press, and leaves a selected line selected: letting go of
        // it is the panel's, once the press is known to be a click.
        if (LineAt(source) is { } line && DataContext is ChangedFilesPanelViewModel panel && ReferenceEquals(line, panel.SelectedNode))
        {
            _toggle = new PendingToggle(line, e.GetPosition(this));
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        // A press that travels is a drag, and not a click.
        if (_toggle is { } toggle && BranchDragGesture.IsDrag(toggle.Origin, e.GetPosition(this)))
        {
            _toggle = null;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        PendingToggle? toggle = _toggle;
        _toggle = null;

        // Where the release is, not its source: the list may hold the pointer captured since the press.
        if (toggle is not null
            && e.InitialPressMouseButton == MouseButton.Left
            && this.InputHitTest(e.GetPosition(this)) is Visual under
            && ReferenceEquals(LineAt(under), toggle.Line))
        {
            LetGoOf(toggle.Line);
        }
    }

    /// <summary>
    /// Lets go of the selected line a click landed on.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <remarks>
    /// Posted, so the list has finished with the release before the selection moves: a selection
    /// taken away while the list is still handling the gesture could be handed straight back.
    /// </remarks>
    private void LetGoOf(ChangedFileNodeViewModel line)
        => Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is ChangedFilesPanelViewModel panel && ReferenceEquals(panel.SelectedNode, line))
            {
                panel.ToggleSelection(line);
            }
        });

    private static ChangedFileNodeViewModel? LineAt(Visual source) => ContainerOf(source)?.DataContext as ChangedFileNodeViewModel;

    private static Control? ContainerOf(Visual source)
        => source.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(control => control is ListBoxItem or TreeViewItem);

    private sealed record PendingToggle(ChangedFileNodeViewModel Line, Point Origin);
}
