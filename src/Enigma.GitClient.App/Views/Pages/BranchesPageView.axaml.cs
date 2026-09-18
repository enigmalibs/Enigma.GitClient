using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Enigma.GitClient.App.ViewModels.Pages;

namespace Enigma.GitClient.App.Views.Pages;

/// <summary>
/// The branches and tags page.
/// </summary>
/// <remarks>
/// The code behind this view exists for one gesture: dragging one branch onto another. A drag is a
/// pointer, a drop target and a menu — three things a binding cannot express — and everything it
/// decides is asked of the page's ViewModel, which owns the policy and the commands.
/// </remarks>
public partial class BranchesPageView : UserControl
{
    private ListBoxItem? _highlighted;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public BranchesPageView()
    {
        InitializeComponent();

        // Tunnelling, because the list handles the pointer itself: the press that starts a drag has
        // to be seen on the way down, before the ListBox captures the pointer for its selection.
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>
    /// Starts a drag when the press landed on a branch row.
    /// </summary>
    /// <remarks>
    /// From the press itself, because <see cref="DragDrop.DoDragDropAsync"/> takes the pressed event
    /// — it is the triggering pointer it tracks, and holding those arguments back to a later move
    /// would hand it an event that has already been dispatched. The press is not marked handled, so
    /// the list still selects the row under it, and the platform's own drag session is what decides
    /// that a pointer which never moved was a click rather than a drag.
    /// </remarks>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 1 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (RowAt(e.Source)?.DataContext is not BranchRowViewModel row)
        {
            return;
        }

        DataTransfer data = new();
        data.Add(DataTransferItem.Create(BranchDrop.DragFormat, row));

        // Fire and forget: the drop is what does the work, and the page reports what it did.
        _ = DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        ListBoxItem? container = RowAt(e.Source);

        e.DragEffects = DropFor(e, container) is not null ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;

        Highlight(e.DragEffects == DragDropEffects.Move ? container : null);
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => Highlight(null);

    /// <summary>
    /// Opens the menu of what the dropped pair can do.
    /// </summary>
    /// <remarks>
    /// A menu rather than a dialog: the pair allows more than one thing, and naming both branches in
    /// the items is what makes a drag that went the wrong way round recoverable rather than a
    /// mistake to undo. Dismissing it does nothing at all — which is why the menu is the question
    /// and the service is no longer asked to ask one.
    /// </remarks>
    private void OnDrop(object? sender, DragEventArgs e)
    {
        ListBoxItem? container = RowAt(e.Source);

        Highlight(null);

        if (DropFor(e, container) is not { } drop || container is null)
        {
            return;
        }

        e.Handled = true;

        ContextMenu menu = new()
        {
            Placement = PlacementMode.Pointer,
            ItemsSource = new object[]
            {
                new MenuItem
                {
                    Header = drop.MergeHeader,
                    Command = Page?.MergeDropCommand,
                    CommandParameter = drop,
                },
                new MenuItem
                {
                    Header = drop.FastForwardHeader,
                    Command = Page?.FastForwardDropCommand,
                    CommandParameter = drop,
                },
                new Separator(),
                new MenuItem
                {
                    Header = drop.ReversedHeader,
                    Command = Page?.MergeReversedDropCommand,
                    CommandParameter = drop,
                },
            },
        };

        menu.Open(container);
    }

    /// <summary>
    /// The page this view is showing, or <see langword="null"/> before it has one.
    /// </summary>
    private BranchesPageViewModel? Page => DataContext as BranchesPageViewModel;

    /// <summary>
    /// The pair a drag event stands for, or <see langword="null"/> when it is not one the page
    /// would carry out.
    /// </summary>
    /// <remarks>
    /// The same question on every pointer move and again on the drop, so it must cost nothing: the
    /// policy it asks is static and side-effect-free.
    /// </remarks>
    private static BranchDrop? DropFor(DragEventArgs e, ListBoxItem? container)
    {
        if (e.DataTransfer.TryGetValue(BranchDrop.DragFormat) is not { } source
            || container?.DataContext is not BranchRowViewModel target)
        {
            return null;
        }

        BranchDrop drop = new(source, target);

        return BranchesPageViewModel.CanDrop(drop) ? drop : null;
    }

    /// <summary>
    /// Finds the row container an event landed on, which is normally a part of its template rather
    /// than the container itself.
    /// </summary>
    private static ListBoxItem? RowAt(object? source)
        => source is Visual visual
            ? visual.GetSelfAndVisualAncestors()
                .OfType<ListBoxItem>()
                .FirstOrDefault(container => container.DataContext is BranchRowViewModel)
            : null;

    /// <summary>
    /// Says where the drag would land, and takes the mark off whatever carried it last.
    /// </summary>
    private void Highlight(ListBoxItem? container)
    {
        if (ReferenceEquals(_highlighted, container))
        {
            return;
        }

        _highlighted?.Classes.Set("droptarget", false);
        _highlighted = container;
        _highlighted?.Classes.Set("droptarget", true);
    }
}
