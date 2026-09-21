using System;
using System.Linq;
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
/// When a press on a row has become a drag, and what the page does while one is under way.
/// </summary>
/// <remarks>
/// Decisions about a pointer and nothing else, which is why they live here rather than in the page's
/// ViewModel — and why they are functions rather than branches inside an event handler: a decision a
/// test can call is a decision that stays right.
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
    /// Whether a point, in the list's own coordinates, is over the list at all.
    /// </summary>
    /// <param name="pointer">Where the pointer is, in the list's own coordinates.</param>
    /// <param name="listSize">How big the list is.</param>
    /// <returns><see langword="true"/> when the pointer is still over the list.</returns>
    /// <remarks>
    /// While the list has the pointer captured, every move is reported wherever it happens — over the
    /// toolbar, over another page, off the window — so the page has to ask this itself. It is what
    /// decides whether the gesture still has anything to say about where it is.
    /// </remarks>
    public static bool IsOverTheList(Point pointer, Size listSize) => new Rect(listSize).Contains(pointer);

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
/// <para>
/// The code behind this view exists for one gesture: dragging one branch onto another. A drag is a
/// pointer, a drop target and a menu — three things a binding cannot express — and everything it
/// decides is asked of the page's ViewModel, which owns the policy and the commands.
/// </para>
/// <para>
/// It is <em>not</em> a platform drag-and-drop session, and that is deliberate. This gesture never
/// leaves the window: a branch row is dropped on another row of the same list, and what it carries is
/// the live row object. Avalonia delivers such a drag in process — its X11 source resolves our own
/// window as an in-process target and never sends an Xdnd message to anyone — while still taking
/// ownership of the X drag selection, which a compositor bridging that drag onward reads as a drag
/// nobody has accepted, and paints the refusal pointer for the whole gesture. Two devs tried to change
/// what this page answers; the pointer is not drawn from anything this page answers. Driven from
/// pointer events, the gesture keeps everything it had — the drop ring, the auto-scroll, the menu —
/// the cursor becomes an ordinary cursor, and for the first time the whole thing can be driven by a
/// test.
/// </para>
/// </remarks>
public partial class BranchesPageView : UserControl
{
    private readonly DispatcherTimer _autoScroll;

    private ListBoxItem? _highlighted;
    private PendingDrag? _pending;
    private BranchRowViewModel? _dragging;
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

        // A timer, because the pointer stops reporting while it is held still — and a branch held
        // over the bottom of the list is exactly the gesture that has to keep scrolling.
        _autoScroll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _autoScroll.Tick += (_, _) => ScrollTowardsTheEdge();
    }

    /// <summary>
    /// Gets the menu the last drop opened, or <see langword="null"/> when no drop has offered one.
    /// </summary>
    /// <remarks>
    /// The view's own, kept rather than dropped on the floor so that a test can read what a release
    /// offered without opening a popup and looking inside it.
    /// </remarks>
    internal ContextMenu? DropMenu { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a branch is being dragged right now.
    /// </summary>
    internal bool IsDragging => _dragging is not null;

    /// <summary>
    /// Remembers a press on a branch row, which a later move may turn into a drag.
    /// </summary>
    /// <remarks>
    /// The press itself starts nothing: a press that never moves is a click, and the list is left to
    /// select the row under it. The press is not marked handled for exactly that reason.
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

        _pending = new PendingDrag(row, e.GetPosition(this));
    }

    /// <summary>
    /// Starts the drag once the pointer has actually moved, and steers it afterwards.
    /// </summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is not null)
        {
            Steer(e);
            return;
        }

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
        _dragging = pending.Row;

        // The capture is what makes the rest of the gesture the page's: every move is reported here
        // afterwards, wherever the pointer goes, and so is the release that ends it.
        e.Pointer.Capture(BranchList);

        Steer(e);
    }

    /// <summary>
    /// Says where the drag is: the ring on the row it would land on, and the scrolling an edge asks
    /// for.
    /// </summary>
    /// <remarks>
    /// The row is hit-tested rather than read from the event, because the capture has made this view
    /// the source of every pointer event for the length of the gesture. The pointer is the only thing
    /// that still knows where it is.
    /// </remarks>
    private void Steer(PointerEventArgs e)
    {
        e.Handled = true;

        ListBoxItem? container = RowUnder(e);

        Highlight(DropFor(container) is not null ? container : null);
        FollowTheEdge(e);
    }

    /// <summary>
    /// Ends the gesture, and offers what the pair can do when it landed on one.
    /// </summary>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pending = null;

        if (_dragging is null)
        {
            return;
        }

        ListBoxItem? container = RowUnder(e);
        BranchDrop? drop = DropFor(container);

        StopDragging(e.Pointer);

        e.Handled = true;

        if (drop is not null && container is not null)
        {
            Offer(drop, container);
        }
    }

    /// <summary>
    /// A drag whose pointer has been taken away — by another control, by the window losing it — ends
    /// where it stands, and drops nothing.
    /// </summary>
    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _pending = null;

        if (_dragging is not null)
        {
            StopDragging(pointer: null);
        }
    }

    /// <summary>
    /// Takes the gesture down: the row being dragged, the drop ring, the scrolling and the capture.
    /// </summary>
    private void StopDragging(IPointer? pointer)
    {
        _dragging = null;

        Highlight(null);
        StopScrolling();

        pointer?.Capture(null);

        ForgetHover();
    }

    /// <summary>
    /// Takes the hover state off every realised row.
    /// </summary>
    /// <remarks>
    /// A pseudo-class rather than a property, because the property is the input system's: a row that
    /// was hovered when the capture was taken never received the exit that clears it. The next
    /// pointer move puts the state back on the row it is really over.
    /// </remarks>
    private void ForgetHover()
    {
        foreach (ListBoxItem container in BranchList.GetRealizedContainers().OfType<ListBoxItem>())
        {
            ((IPseudoClasses)container.Classes).Set(":pointerover", false);
        }
    }

    // ---------------------------------------------------------------- scrolling while dragging

    /// <summary>
    /// Starts, steers or stops the scrolling that a drag held near an edge asks for.
    /// </summary>
    private void FollowTheEdge(PointerEventArgs e)
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

    // ---------------------------------------------------------------- the drop

    /// <summary>
    /// Opens the menu of what the dropped pair can do.
    /// </summary>
    /// <remarks>
    /// A menu rather than a dialog: the pair allows more than one thing, and naming both branches in
    /// the items is what makes a drag that went the wrong way round recoverable rather than a
    /// mistake to undo. Dismissing it does nothing at all — which is why the menu is the question
    /// and the service is no longer asked to ask one.
    /// </remarks>
    private void Offer(BranchDrop drop, Control target)
    {
        DropMenu = new ContextMenu
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

        DropMenu.Open(target);
    }

    /// <summary>
    /// The page this view is showing, or <see langword="null"/> before it has one.
    /// </summary>
    private BranchesPageViewModel? Page => DataContext as BranchesPageViewModel;

    /// <summary>
    /// The pair the drag stands for right now, or <see langword="null"/> when it is not one the page
    /// would carry out.
    /// </summary>
    /// <remarks>
    /// The same question on every pointer move and again on the release, so it must cost nothing: the
    /// policy it asks is static and side-effect-free.
    /// </remarks>
    private BranchDrop? DropFor(ListBoxItem? container)
    {
        if (_dragging is not { } source || container?.DataContext is not BranchRowViewModel target)
        {
            return null;
        }

        BranchDrop drop = new(source, target);

        return BranchesPageViewModel.CanDrop(drop) ? drop : null;
    }

    /// <summary>
    /// The row the pointer is over, or <see langword="null"/> when it is over none.
    /// </summary>
    private ListBoxItem? RowUnder(PointerEventArgs e)
    {
        Point point = e.GetPosition(BranchList);

        return BranchDragGesture.IsOverTheList(point, BranchList.Bounds.Size)
            ? RowAt(BranchList.InputHitTest(point))
            : null;
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
    /// <param name="Origin">Where it landed, which the threshold is measured from.</param>
    private sealed record PendingDrag(BranchRowViewModel Row, Point Origin);

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
