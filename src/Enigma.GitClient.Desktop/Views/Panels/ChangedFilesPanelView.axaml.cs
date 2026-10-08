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

    // The folder the first press of a click opened or closed, and how it left it: the tree folds a
    // folder on a double-click of its own, which would undo that press (see OnDoubleTapped).
    private (ChangedFileNodeViewModel Folder, bool Expanded)? _folded;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public ChangedFilesPanelView()
    {
        InitializeComponent();

        FilesTree.SelectionChanged += OnTreeSelectionChanged;

        // Handled ones too: the tree's item handles the double-tap it folds the folder on.
        AddHandler(DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Bubble, handledEventsToo: true);

        // The whole line answers a right-click with its menu, the container's padding included.
        LineMenus.Attach(this);

        // Tunnelling, because the list and the tree handle the pointer themselves: the press has to be
        // seen before they act on it, while the line under it is still the one that was selected.
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
    }

    // ---------------------------------------------------------------- the lines are toggles

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _toggle = null;

        if (e.ClickCount == 1)
        {
            _folded = null;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is not Visual source)
        {
            return;
        }

        // A button on the line — its Stage or Unstage, a folder's chevron — does its own thing, and
        // leaves the selection alone.
        if (source.GetSelfAndVisualAncestors().TakeWhile(visual => visual is not (ListBoxItem or TreeViewItem)).OfType<Button>().Any())
        {
            return;
        }

        // A folder is never selected (BUG-1B14): a click on its line opens or closes it instead, and
        // the tree never sees the press, so the file selected before keeps the selection and its diff.
        // Both presses of a double-click are kept from the tree, and only the first one folds.
        if (LineAt(source) is { IsDirectory: true } folder)
        {
            if (e.ClickCount == 1)
            {
                folder.IsExpanded = !folder.IsExpanded;
                _folded = (folder, folder.IsExpanded);
            }

            e.Handled = true;
            return;
        }

        // The second press of a double-click is not a toggle: a double-click on a line leaves it
        // selected, however the first press found it.
        if (e.ClickCount != 1 || e.KeyModifiers != KeyModifiers.None)
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
    /// Keeps a double-click on a folder to the one fold its first press made.
    /// </summary>
    /// <param name="sender">The panel.</param>
    /// <param name="e">The double-tap.</param>
    /// <remarks>
    /// The tree's item folds a folder on a double-tap of its own, after the first press already did:
    /// left alone, a double-click would open a folder and close it again. Whatever the tree did, the
    /// folder is left as the press left it.
    /// </remarks>
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_folded is { } folded && e.Source is Visual source && ReferenceEquals(LineAt(source), folded.Folder))
        {
            folded.Folder.IsExpanded = folded.Expanded;
        }
    }

    /// <summary>
    /// Puts the tree back on the line the panel holds when the panel refused the one it selected.
    /// </summary>
    /// <param name="sender">The tree.</param>
    /// <param name="e">The change.</param>
    /// <remarks>
    /// A click on a folder never reaches the tree, but the keyboard does: the arrows select whatever line
    /// they reach. The panel refuses a folder (BUG-1B14) and says so at once, while the tree is still in
    /// the middle of selecting it and does not listen; so the tree is told again once it is done. Its
    /// focus stays on the folder, which is where the next arrow goes on from.
    /// </remarks>
    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FilesTree.SelectedItem is not ChangedFileNodeViewModel { IsDirectory: true })
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is ChangedFilesPanelViewModel panel
                && FilesTree.SelectedItem is ChangedFileNodeViewModel { IsDirectory: true })
            {
                FilesTree.SetCurrentValue(TreeView.SelectedItemProperty, panel.TreeSelection);
            }
        });
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
