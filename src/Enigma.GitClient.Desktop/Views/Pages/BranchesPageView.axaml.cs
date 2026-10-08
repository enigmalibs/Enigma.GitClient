using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Desktop.ViewModels.Pages;

namespace Enigma.GitClient.Desktop.Views.Pages;

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
    /// What the pointer should look like while a branch is being carried.
    /// </summary>
    /// <param name="isOverTheList">Whether the pointer is over the branches list.</param>
    /// <returns>
    /// The cursor to wear, or <see langword="null"/> to leave the list its own.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The question the pointer answers is "is this gesture under way here", not "would this exact
    /// pair merge": a branch over its own row, or over a group heading, is still a drag in progress.
    /// Where a drop would actually land is said by the ring on the row, and a drop the policy refuses
    /// still does nothing.
    /// </para>
    /// <para>
    /// <see cref="StandardCursorType.DragMove"/> and never <see cref="StandardCursorType.No"/>: the
    /// refusal pointer is the bug. Off the list the gesture has nothing to say about where it is, so
    /// the list keeps its ordinary cursor rather than being told something else.
    /// </para>
    /// </remarks>
    public static StandardCursorType? CursorFor(bool isOverTheList)
        => isOverTheList ? StandardCursorType.DragMove : null;

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
/// The code behind this view exists for two things a binding cannot express. The first is dragging one
/// branch onto another: a drag is a pointer, a drop target and a menu, and everything it decides is
/// asked of the page's ViewModel, which owns the policy and the commands. The second is the tree's
/// folders, which a click opens or closes and which are never selected, as in the changed files.
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
    /// <summary>
    /// The pointer worn while a branch is being carried, made once: a cursor is a platform handle,
    /// not a value, and a drag asks for it on every pointer move.
    /// </summary>
    private static readonly Cursor Carrying = new(StandardCursorType.DragMove);

    private readonly DispatcherTimer _autoScroll;

    private TreeViewItem? _highlighted;

    // The folder the first press of a click opened or closed, and how it left it: the tree folds a
    // folder on a double-click of its own, which would undo that press (see OnDoubleTapped).
    private (BranchTreeNode Folder, bool Expanded)? _folded;
    private PendingDrag? _pending;
    private BranchRowViewModel? _dragging;
    private IPointer? _captured;
    private Cursor? _listCursor;
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

        // A platform drag session had a way out of its own. An in-house gesture has to bring one, and
        // Escape is what every desktop means by "stop".
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // Handled ones too: the tree's item handles the double-tap it folds the folder on.
        AddHandler(DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Bubble, handledEventsToo: true);

        // The whole line answers a right-click with its menu, the indentation and the chevron included.
        LineMenus.Attach(this);

        BranchTree.SelectionChanged += OnTreeSelectionChanged;

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

        if (e.ClickCount == 1)
        {
            _folded = null;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is not Visual source)
        {
            return;
        }

        // A top-level node or a folder is never selected, as in the changed files (BUG-1B14): a click on
        // its line opens or closes it, and the tree never sees the press, so the branch selected before
        // stays selected. The chevron is a button of the line's own, and folds it as it always did. Both
        // presses of a double-click are kept from the tree, and only the first one folds.
        if (!IsOnAButton(source) && LineAt(source) is { IsFolder: true } folder)
        {
            if (e.ClickCount == 1)
            {
                folder.IsExpanded = !folder.IsExpanded;
                _folded = (folder, folder.IsExpanded);
            }

            e.Handled = true;
            return;
        }

        if (e.ClickCount != 1)
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
    /// Keeps a double-click on a folder to the one fold its first press made.
    /// </summary>
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
    /// Puts the tree back on the branch the page holds when the page refused the folder it selected.
    /// </summary>
    /// <remarks>
    /// A click on a folder never reaches the tree, but the keyboard does: the arrows select whatever
    /// line they reach. The page refuses a folder and says so at once, while the tree is still in the
    /// middle of selecting it and does not listen; so the tree is told again once it is done. Its focus
    /// stays on the folder, which is where the next arrow goes on from.
    /// </remarks>
    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (BranchTree.SelectedItem is not BranchTreeNode { IsFolder: true })
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (Page is { } page && BranchTree.SelectedItem is BranchTreeNode { IsFolder: true })
            {
                BranchTree.SetCurrentValue(TreeView.SelectedItemProperty, page.SelectedItem);
            }
        });
    }

    /// <summary>Whether a press landed on a button of the line — the chevron — rather than the line itself.</summary>
    private static bool IsOnAButton(Visual source)
        => source.GetSelfAndVisualAncestors().TakeWhile(visual => visual is not TreeViewItem).OfType<Button>().Any();

    /// <summary>The node whose line a press landed on: the nearest tree item's.</summary>
    private static BranchTreeNode? LineAt(Visual source)
        => source.GetSelfAndVisualAncestors().OfType<TreeViewItem>().FirstOrDefault()?.DataContext as BranchTreeNode;

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
        _listCursor = BranchTree.Cursor;

        // The capture is what makes the rest of the gesture the page's: every move is reported here
        // afterwards, wherever the pointer goes, and so is the release that ends it.
        _captured = e.Pointer;
        _captured.Capture(BranchTree);

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

        ShowTheGesturesCursor(BranchDragGesture.IsOverTheList(e.GetPosition(BranchTree), BranchTree.Bounds.Size));

        TreeViewItem? container = RowUnder(e);

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

        TreeViewItem? container = RowUnder(e);
        BranchDrop? drop = DropFor(container);

        StopDragging();

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

        if (_dragging is null)
        {
            return;
        }

        // The capture is already gone, and whoever took it owns that pointer now: releasing it here
        // would be taking it away from them.
        _captured = null;

        StopDragging();
    }

    /// <summary>
    /// Escape calls a drag off: nothing is dropped, and the row under the pointer is left alone.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dragging is null || e.Key != Key.Escape)
        {
            return;
        }

        StopDragging();

        e.Handled = true;
    }

    /// <summary>
    /// Takes the gesture down: the row being dragged, the drop ring, the scrolling, the cursor and
    /// the capture. Every way a drag can end goes through here.
    /// </summary>
    private void StopDragging()
    {
        _dragging = null;

        Highlight(null);
        StopScrolling();

        BranchTree.Cursor = _listCursor;
        _listCursor = null;

        IPointer? pointer = _captured;
        _captured = null;
        pointer?.Capture(null);

        ForgetHover();
    }

    /// <summary>
    /// Puts the gesture's pointer on the list, or gives the list its own back.
    /// </summary>
    private void ShowTheGesturesCursor(bool isOverTheList)
        => BranchTree.Cursor = BranchDragGesture.CursorFor(isOverTheList) switch
        {
            StandardCursorType.DragMove => Carrying,
            _ => _listCursor,
        };

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
        foreach (TreeViewItem container in BranchTree.GetRealizedTreeContainers().OfType<TreeViewItem>())
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
        => _listScroll ??= BranchTree.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

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
                    Icon = HistoryPageView.MenuIcon(Enigma.Icons.Phosphor.PhosphorIcon.GitMerge),
                },
                new MenuItem
                {
                    Header = drop.FastForwardHeader,
                    Command = Page?.FastForwardDropCommand,
                    CommandParameter = drop,
                    Icon = HistoryPageView.MenuIcon(Enigma.Icons.Phosphor.PhosphorIcon.FastForward),
                },
                new Separator(),
                new MenuItem
                {
                    Header = drop.ReversedHeader,
                    Command = Page?.MergeReversedDropCommand,
                    CommandParameter = drop,
                    Icon = HistoryPageView.MenuIcon(Enigma.Icons.Phosphor.PhosphorIcon.GitMerge),
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
    private BranchDrop? DropFor(TreeViewItem? container)
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
    private TreeViewItem? RowUnder(PointerEventArgs e)
    {
        Point point = e.GetPosition(BranchTree);

        return BranchDragGesture.IsOverTheList(point, BranchTree.Bounds.Size)
            ? RowAt(BranchTree.InputHitTest(point))
            : null;
    }

    /// <summary>
    /// Finds the branch line's container an event landed on, which is normally a part of its template
    /// rather than the container itself — and only a branch's: a folder is not something to drag or
    /// to drop on.
    /// </summary>
    private static TreeViewItem? RowAt(object? source)
        => source is Visual visual
            && visual.GetSelfAndVisualAncestors().OfType<TreeViewItem>().FirstOrDefault() is { DataContext: BranchRowViewModel } container
            ? container
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
    private void Highlight(TreeViewItem? container)
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
