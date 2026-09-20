using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.App.ViewModels.Pages;

namespace Enigma.GitClient.App.Views.Pages;

/// <summary>
/// When a press on a row has become a drag.
/// </summary>
/// <remarks>
/// A decision about a pointer and nothing else, which is why it lives here rather than in the page's
/// ViewModel — and why it is a function rather than a branch inside an event handler: a platform
/// drag session cannot be driven in a headless test, but this can.
/// </remarks>
internal static class BranchDragGesture
{
    /// <summary>
    /// How far the pointer must travel from where it was pressed before the gesture is a drag
    /// rather than a click.
    /// </summary>
    /// <remarks>
    /// Four pixels is what a desktop toolkit means by a drag: far enough that a click with a shaky
    /// hand is still a click, near enough that a deliberate move starts the drag at once.
    /// </remarks>
    public const double Threshold = 4;

    /// <summary>
    /// Says whether the pointer has moved far enough from where it was pressed.
    /// </summary>
    /// <param name="origin">Where the press landed.</param>
    /// <param name="current">Where the pointer is now.</param>
    /// <returns><see langword="true"/> when this is a drag.</returns>
    public static bool IsDrag(Point origin, Point current)
    {
        double x = current.X - origin.X;
        double y = current.Y - origin.Y;

        return (x * x) + (y * y) >= Threshold * Threshold;
    }

    /// <summary>
    /// What the pointer should say while a drag is over the page.
    /// </summary>
    /// <param name="carriesBranch">Whether what is being dragged is one of this list's branches.</param>
    /// <param name="isOverTheList">Whether the pointer is over the branches list.</param>
    /// <returns>The effect to report, which is what the platform draws as a cursor.</returns>
    /// <remarks>
    /// The question the cursor answers is "is this gesture under way here", not "would this exact
    /// pair merge": a branch dragged over its own row, or over a group heading, is still a drag in
    /// progress, and <see cref="DragDropEffects.None"/> there drew the "impossible" pointer over
    /// most of the journey. Where a drop would actually land is said by the ring on the row, which
    /// the policy still decides — and a drop the policy refuses still does nothing.
    /// </remarks>
    public static DragDropEffects EffectFor(bool carriesBranch, bool isOverTheList)
        => carriesBranch && isOverTheList ? DragDropEffects.Move : DragDropEffects.None;

    /// <summary>
    /// How near an edge the pointer has to be for the list to scroll towards it, in pixels.
    /// </summary>
    public const double ScrollBand = 32;

    /// <summary>The furthest one tick scrolls, in pixels, at the very edge of the list.</summary>
    public const double ScrollStep = 24;

    /// <summary>
    /// How far the list should scroll while a drag is held at a given height in it.
    /// </summary>
    /// <param name="y">Where the pointer is, in the list's own coordinates.</param>
    /// <param name="viewportHeight">How tall the part of the list on screen is.</param>
    /// <returns>
    /// Pixels to scroll by: negative towards the top, positive towards the bottom, and zero
    /// anywhere in the middle.
    /// </returns>
    /// <remarks>
    /// The speed grows with how far into the band the pointer is, so nudging the edge creeps and
    /// pushing past it runs — and it never drops to nothing inside the band, because a scroll that
    /// stops just short of the edge is a list that cannot be reached to the end of. The band is
    /// capped at a third of the viewport so that a short list does not become one long edge.
    /// </remarks>
    public static double ScrollFor(double y, double viewportHeight)
    {
        if (viewportHeight <= 0)
        {
            return 0;
        }

        double band = Math.Min(ScrollBand, viewportHeight / 3);

        if (y < band)
        {
            return -ScrollStep * Depth((band - y) / band);
        }

        if (y > viewportHeight - band)
        {
            return ScrollStep * Depth((y - (viewportHeight - band)) / band);
        }

        return 0;
    }

    /// <summary>
    /// How fast a pointer that far into the band moves the list, from a quarter of the step to all
    /// of it.
    /// </summary>
    private static double Depth(double fraction) => Math.Clamp(fraction, 0.25, 1);
}

/// <summary>
/// The branches page.
/// </summary>
/// <remarks>
/// The code behind this view exists for one gesture: dragging one branch onto another. A drag is a
/// pointer, a drop target and a menu — three things a binding cannot express — and everything it
/// decides is asked of the page's ViewModel, which owns the policy and the commands.
/// </remarks>
public partial class BranchesPageView : UserControl
{
    private readonly DispatcherTimer _autoScroll;

    private ListBoxItem? _highlighted;
    private PendingDrag? _pending;
    private ScrollViewer? _listScroll;
    private double _autoScrollBy;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public BranchesPageView()
    {
        InitializeComponent();

        // Tunnelling, because the list handles the pointer itself: the press and the move that
        // turns it into a drag have to be seen on the way down, before the ListBox captures the
        // pointer for its selection.
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Tunnel);

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // A timer, because the pointer stops reporting while it is held still — and a branch held
        // over the bottom of the list is exactly the gesture that has to keep scrolling.
        _autoScroll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _autoScroll.Tick += (_, _) => ScrollTowardsTheEdge();
    }

    /// <summary>
    /// Remembers a press on a branch row, which a later move may turn into a drag.
    /// </summary>
    /// <remarks>
    /// The press itself starts nothing. It used to call <see cref="DragDrop.DoDragDropAsync"/>
    /// straight away, which opened a platform drag session for what was very often an ordinary
    /// click — and a drag session takes the pointer, so the row under it never received the exit
    /// that clears its hover. That is the grey plate a row kept after the selection had moved on,
    /// and the "no" cursor that flashed on a plain click.
    ///
    /// The press is not marked handled, so the list still selects the row under it.
    /// </remarks>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pending = null;

        if (e.ClickCount != 1 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (RowAt(e.Source)?.DataContext is not BranchRowViewModel row)
        {
            return;
        }

        _pending = new PendingDrag(row, e, e.GetPosition(this));
    }

    /// <summary>
    /// Starts the drag once the pointer has actually moved.
    /// </summary>
    /// <remarks>
    /// The session is still started from the press, because that is what
    /// <see cref="DragDrop.DoDragDropAsync"/> takes — it is the pointer it tracks, and a pointer
    /// that is still down is still that one. What moved is <em>when</em> it is started: the press
    /// alone is a click until the pointer says otherwise.
    /// </remarks>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pending is not { } pending)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pending = null;
            return;
        }

        if (!BranchDragGesture.IsDrag(pending.Origin, e.GetPosition(this)))
        {
            return;
        }

        _pending = null;

        _ = DragAsync(pending.Trigger, BranchDrop.TransferFor(pending.Row));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => _pending = null;

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _pending = null;

    /// <summary>
    /// Runs the drag session, and tidies up after it.
    /// </summary>
    /// <remarks>
    /// The drop is what does the work and the page reports what it did, so nothing here reads the
    /// result. What the session does leave behind is a hover state on whatever row it took the
    /// pointer from — the exit never arrived — so the rows are told to forget it when it ends.
    /// </remarks>
    private async Task DragAsync(PointerPressedEventArgs trigger, DataTransfer data)
    {
        try
        {
            await DragDrop.DoDragDropAsync(trigger, data, DragDropEffects.Move);
        }
        finally
        {
            ForgetHover();
            StopScrolling();
        }
    }

    /// <summary>
    /// Takes the hover state off every realised row.
    /// </summary>
    /// <remarks>
    /// A pseudo-class rather than a property, because the property is the input system's: the
    /// pointer never left as far as it is concerned, so nothing else is going to clear this. The
    /// next pointer move puts the state back on the row it is really over.
    /// </remarks>
    private void ForgetHover()
    {
        foreach (ListBoxItem container in BranchList.GetRealizedContainers().OfType<ListBoxItem>())
        {
            ((IPseudoClasses)container.Classes).Set(":pointerover", false);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        ListBoxItem? container = RowAt(e.Source);

        // Two different questions, deliberately: the cursor says whether the gesture is under way
        // here, and the ring says where it would land.
        e.DragEffects = BranchDragGesture.EffectFor(
            e.DataTransfer.TryGetValue(BranchDrop.DragFormat) is not null,
            IsOverTheList(e.Source));

        e.Handled = true;

        Highlight(DropFor(e, container) is not null ? container : null);
        FollowTheEdge(e);
    }

    // ---------------------------------------------------------------- scrolling while dragging

    /// <summary>
    /// Starts, steers or stops the scrolling that a drag held near an edge asks for.
    /// </summary>
    private void FollowTheEdge(DragEventArgs e)
    {
        if (ListScroll() is not { } scroll)
        {
            StopScrolling();
            return;
        }

        _autoScrollBy = BranchDragGesture.ScrollFor(e.GetPosition(scroll).Y, scroll.Viewport.Height);

        if (_autoScrollBy == 0)
        {
            StopScrolling();
            return;
        }

        if (!_autoScroll.IsEnabled)
        {
            _autoScroll.Start();
        }
    }

    private void StopScrolling()
    {
        _autoScrollBy = 0;
        _autoScroll.Stop();
    }

    /// <summary>
    /// Moves the list by one tick's worth, and stops when there is nothing left that way.
    /// </summary>
    private void ScrollTowardsTheEdge()
    {
        if (ListScroll() is not { } scroll)
        {
            StopScrolling();
            return;
        }

        double furthest = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        double moved = Math.Clamp(scroll.Offset.Y + _autoScrollBy, 0, furthest);

        if (moved == scroll.Offset.Y)
        {
            return;
        }

        scroll.Offset = new Vector(scroll.Offset.X, moved);
    }

    /// <summary>
    /// The branches list's own scroll, once the list has a template to find one in.
    /// </summary>
    private ScrollViewer? ListScroll()
        => _listScroll ??= BranchList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    /// <summary>
    /// Whether an event landed inside the branches list.
    /// </summary>
    private bool IsOverTheList(object? source)
        => source is Visual visual
            && visual.GetSelfAndVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, BranchList));

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        Highlight(null);
        StopScrolling();
    }

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
        StopScrolling();

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
    /// A press on a row that has not moved far enough to be a drag.
    /// </summary>
    /// <param name="Row">The row the press landed on.</param>
    /// <param name="Trigger">The press itself, which is what starts the platform's drag session.</param>
    /// <param name="Origin">Where it landed, which the threshold is measured from.</param>
    private sealed record PendingDrag(BranchRowViewModel Row, PointerPressedEventArgs Trigger, Point Origin);

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
