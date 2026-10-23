using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.ViewModels.Pages;

namespace Enigma.GitClient.Desktop.Views.Pages;

/// <summary>
/// Where a dragged repository row would land, and where that puts it in the list.
/// </summary>
/// <remarks>
/// A row is dropped into a <em>slot</em>: the gap before the row of the same number, or the gap after
/// the last row. The arithmetic is here, as functions, for the reason <see cref="BranchDragGesture"/>'s
/// is: a decision a test can call is a decision that stays right.
/// </remarks>
internal static class RepositoryReorderGesture
{
    /// <summary>
    /// Lays rows out one after the other from a top, as they stand with no gap opened among them.
    /// </summary>
    /// <param name="top">Where the first row starts.</param>
    /// <param name="heights">Each row's height without an open gap — its tile and the space below it.</param>
    /// <returns>Each row's top and height, in list order.</returns>
    /// <remarks>
    /// The slot is found in this geometry rather than in the rows as drawn: the dashed tile that marks
    /// the slot pushes the rows below it down, and reading their new places would move the slot,
    /// which would move the dashed tile, and so on.
    /// </remarks>
    public static IReadOnlyList<(double Top, double Height)> Stack(double top, IReadOnlyList<double> heights)
    {
        ArgumentNullException.ThrowIfNull(heights);

        List<(double Top, double Height)> rows = new(heights.Count);

        foreach (double height in heights)
        {
            rows.Add((top, height));
            top += height;
        }

        return rows;
    }

    /// <summary>
    /// Places the copy of a carried tile: where the pointer holds it, kept inside the layer it is
    /// drawn on.
    /// </summary>
    /// <param name="pointer">Where the pointer is, in the layer's coordinates.</param>
    /// <param name="grab">How far below the tile's top it was grabbed.</param>
    /// <param name="height">The tile's height.</param>
    /// <param name="layer">The layer's height.</param>
    /// <returns>The copy's top, in the layer's coordinates.</returns>
    public static double GhostTop(double pointer, double grab, double height, double layer)
        => Math.Clamp(pointer - grab, 0, Math.Max(0, layer - height));

    /// <summary>
    /// Finds the slot a pointer is over: the gap before the first row whose middle is below it.
    /// </summary>
    /// <param name="y">Where the pointer is, in the list's own coordinates.</param>
    /// <param name="rows">Each row's top and height, in the list's coordinates and in list order.</param>
    /// <returns>A slot from zero (before the first row) to the row count (after the last one).</returns>
    public static int SlotFor(double y, IReadOnlyList<(double Top, double Height)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        for (int index = 0; index < rows.Count; index++)
        {
            if (y < rows[index].Top + (rows[index].Height / 2))
            {
                return index;
            }
        }

        return rows.Count;
    }

    /// <summary>
    /// Says whether dropping a row into a slot would move it at all.
    /// </summary>
    /// <param name="from">Where the row is.</param>
    /// <param name="slot">The slot it would be dropped into.</param>
    /// <returns>
    /// <see langword="false"/> for the gaps on either side of the row itself, which leave it where it is.
    /// </returns>
    public static bool Moves(int from, int slot) => slot != from && slot != from + 1;

    /// <summary>
    /// Turns a slot into the row's new index, counted in the list without the row — which is what the
    /// store moves it to.
    /// </summary>
    /// <param name="from">Where the row is.</param>
    /// <param name="slot">The slot it is dropped into.</param>
    /// <returns>Its new index.</returns>
    public static int TargetIndex(int from, int slot) => slot > from ? slot - 1 : slot;
}

/// <summary>
/// The landing page: the selected profile's repositories and the open / clone / create actions.
/// </summary>
/// <remarks>
/// <para>
/// The code behind this view exists for one gesture: dragging a repository row to another place in
/// the list. The ViewModel owns the order; this decides only where the pointer is. While it lasts, a
/// copy of the tile follows the pointer, the tile itself is faded in its place, and a dashed tile opens
/// the gap it would land in.
/// </para>
/// <para>
/// It is a gesture the page runs itself from pointer events, not a platform drag-and-drop session,
/// for the reasons the branches list gives: the row never leaves the window, and on an XWayland
/// session a platform drag wears the refusal pointer for its whole length (BUG-11A4).
/// </para>
/// </remarks>
public partial class RepositoriesPageView : UserControl
{
    /// <summary>
    /// The pointer worn while a row is being carried, made once: a cursor is a platform handle.
    /// </summary>
    private static readonly Cursor Carrying = new(StandardCursorType.DragMove);

    private readonly DispatcherTimer _autoScroll;

    private PendingDrag? _pending;
    private PendingDrag? _dragging;
    private IPointer? _captured;
    private Cursor? _listCursor;
    private ContentPresenter? _marked;
    private ContentPresenter? _lifted;
    private double _grab;
    private double _ghostHeight;
    private Point _lastPointer;
    private double _autoScrollBy;
    private TopLevel? _window;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public RepositoriesPageView()
    {
        InitializeComponent();

        // Tunnelling, because the buttons on a row handle the pointer themselves: the press has to be
        // seen on the way down to be told apart from a press on a button.
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Tunnel);

        // A timer, because the pointer stops reporting while it is held still — and a row held at the
        // bottom of a long list is exactly the gesture that has to keep scrolling.
        _autoScroll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _autoScroll.Tick += (_, _) => ScrollTowardsTheEdge();
    }

    /// <summary>
    /// Gets a value indicating whether a row is being dragged right now.
    /// </summary>
    internal bool IsDragging => _dragging is not null;

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // On the window rather than on this view: nothing on a row takes the focus when it is pressed,
        // so the Escape that calls a drag off may be sent to the window itself.
        _window = TopLevel.GetTopLevel(this);
        _window?.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _window?.RemoveHandler(KeyDownEvent, OnKeyDown);
        _window = null;

        if (_dragging is not null)
        {
            StopDragging();
        }

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// The page this view is showing, or <see langword="null"/> before it has one.
    /// </summary>
    private RepositoriesPageViewModel? Page => DataContext as RepositoriesPageViewModel;

    /// <summary>
    /// Remembers a press on a row's body, which a later move may turn into a drag. A press on one of
    /// the row's buttons is the button's, and nothing is dragged while the list is filtered.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pending = null;

        if (e.ClickCount != 1 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Page is not { CanReorder: true })
        {
            return;
        }

        if (RowAt(e.Source) is not { } row)
        {
            return;
        }

        _pending = row with { Origin = e.GetPosition(this) };
    }

    /// <summary>
    /// Starts the drag once the pointer has actually moved, and steers it afterwards.
    /// </summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is not null)
        {
            Steer(e.GetPosition(RepositoryScroll));
            e.Handled = true;
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
        _dragging = pending;
        _listCursor = RepositoryList.Cursor;
        RepositoryList.Cursor = Carrying;

        // From here the gesture is the page's: every move and the release are reported to the list,
        // wherever the pointer goes.
        _captured = e.Pointer;
        _captured.Capture(RepositoryList);

        Lift(pending);
        Steer(e.GetPosition(RepositoryScroll));
        e.Handled = true;
    }

    /// <summary>
    /// Drops the row into the slot under the pointer.
    /// </summary>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pending = null;

        if (_dragging is not { } dragging)
        {
            return;
        }

        int slot = SlotAt(e.GetPosition(RepositoryScroll));

        StopDragging();
        e.Handled = true;

        if (Page is { } page && RepositoryReorderGesture.Moves(dragging.Index, slot))
        {
            page.MoveAsync(dragging.Entry, RepositoryReorderGesture.TargetIndex(dragging.Index, slot)).Forget();
        }
    }

    /// <summary>
    /// A drag whose pointer was taken away ends where it stands, and moves nothing.
    /// </summary>
    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _pending = null;

        if (_dragging is null)
        {
            return;
        }

        // Whoever took the capture owns that pointer now: releasing it here would take it from them.
        _captured = null;

        StopDragging();
    }

    /// <summary>
    /// Escape calls a drag off: nothing moves.
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
    /// Takes the gesture down: the row being carried and its copy, the dashed tile, the scrolling,
    /// the cursor and the capture. Every way a drag ends goes through here.
    /// </summary>
    private void StopDragging()
    {
        _dragging = null;

        Mark(null, before: true);
        StopScrolling();

        DragGhost.IsVisible = false;
        DragGhost.ItemsSource = null;
        _lifted?.Classes.Set("dragged", false);
        _lifted = null;

        RepositoryList.Cursor = _listCursor;
        _listCursor = null;

        IPointer? pointer = _captured;
        _captured = null;
        pointer?.Capture(null);
    }

    /// <summary>
    /// Fades the row being carried in its place, and puts a copy of its tile under the pointer —
    /// as wide as the row, at the row's left, held where it was grabbed.
    /// </summary>
    private void Lift(PendingDrag dragging)
    {
        if (RepositoryList.ContainerFromIndex(dragging.Index) is not ContentPresenter row)
        {
            return;
        }

        _lifted = row;
        row.Classes.Set("dragged", true);

        Point rowOrigin = row.TranslatePoint(default, DragLayer) ?? default;
        Point press = this.TranslatePoint(dragging.Origin, DragLayer) ?? rowOrigin;

        _grab = press.Y - rowOrigin.Y;
        _ghostHeight = Tile(row)?.Bounds.Height ?? row.Bounds.Height;

        DragGhost.ItemsSource = new[] { dragging.Entry };
        DragGhost.Width = row.Bounds.Width;
        Canvas.SetLeft(DragGhost, rowOrigin.X);
        DragGhost.IsVisible = true;
    }

    /// <summary>
    /// Moves the copy of the carried tile to the pointer, up and down the list.
    /// </summary>
    /// <param name="pointer">Where the pointer is, in the scroll viewer's coordinates.</param>
    private void MoveTheGhost(Point pointer)
    {
        if (!DragGhost.IsVisible)
        {
            return;
        }

        Point inLayer = RepositoryScroll.TranslatePoint(pointer, DragLayer) ?? pointer;

        Canvas.SetTop(
            DragGhost,
            RepositoryReorderGesture.GhostTop(inLayer.Y, _grab, _ghostHeight, DragLayer.Bounds.Height));
    }

    /// <summary>
    /// Shows where the row would land, moves the copy with the pointer, and scrolls when the pointer
    /// is held near an edge.
    /// </summary>
    /// <param name="pointer">Where the pointer is, in the scroll viewer's coordinates.</param>
    private void Steer(Point pointer)
    {
        _lastPointer = pointer;

        OpenTheGap();
        MoveTheGhost(pointer);

        _autoScrollBy = BranchDragGesture.ScrollFor(pointer.Y, RepositoryScroll.Bounds.Height);

        if (_autoScrollBy == 0)
        {
            StopScrolling();
        }
        else if (!_autoScroll.IsEnabled)
        {
            _autoScroll.Start();
        }
    }

    /// <summary>
    /// Opens the dashed tile in the gap the pointer is over — or nowhere, when a drop there would move
    /// nothing.
    /// </summary>
    private void OpenTheGap()
    {
        if (_dragging is not { } dragging)
        {
            return;
        }

        int slot = SlotAt(_lastPointer);
        IReadOnlyList<ContentPresenter> rows = Rows();

        if (!RepositoryReorderGesture.Moves(dragging.Index, slot) || rows.Count == 0)
        {
            Mark(null, before: true);
        }
        else if (slot < rows.Count)
        {
            Mark(rows[slot], before: true);
        }
        else
        {
            Mark(rows[^1], before: false);
        }
    }

    /// <summary>
    /// The slot under a point given in the scroll viewer's coordinates, found among the rows as they
    /// stand with no dashed tile open (<see cref="RepositoryReorderGesture.Stack"/>).
    /// </summary>
    /// <remarks>
    /// The first row's top is where the rows start whatever is open: a dashed tile is laid out inside
    /// a row, never above the first one.
    /// </remarks>
    private int SlotAt(Point pointer)
    {
        Point inList = RepositoryScroll.TranslatePoint(pointer, RepositoryList) ?? pointer;
        List<ContentPresenter> rows = Rows();

        if (rows.Count == 0)
        {
            return 0;
        }

        double top = rows[0].TranslatePoint(default, RepositoryList)?.Y ?? rows[0].Bounds.Top;

        return RepositoryReorderGesture.SlotFor(
            inList.Y,
            RepositoryReorderGesture.Stack(top, [.. rows.Select(UnopenedHeight)]));
    }

    /// <summary>
    /// A row's height with no dashed tile open in it: its tile and the space below the tile.
    /// </summary>
    private static double UnopenedHeight(ContentPresenter row)
        => Tile(row) is { } tile ? tile.Bounds.Height + tile.Margin.Top + tile.Margin.Bottom : row.Bounds.Height;

    /// <summary>
    /// A row's tile, without the dashed tiles above and below it.
    /// </summary>
    private static Border? Tile(ContentPresenter row)
        => row.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("tile"));

    /// <summary>
    /// The row containers, in list order.
    /// </summary>
    private List<ContentPresenter> Rows()
        => [.. RepositoryList.GetRealizedContainers()
            .OfType<ContentPresenter>()
            .OrderBy(RepositoryList.IndexFromContainer)];

    /// <summary>
    /// Opens the dashed tile in a row's gap, and closes it in whichever row had it.
    /// </summary>
    private void Mark(ContentPresenter? row, bool before)
    {
        if (_marked is not null)
        {
            _marked.Classes.Set("insertbefore", false);
            _marked.Classes.Set("insertafter", false);
        }

        _marked = row;

        _marked?.Classes.Set(before ? "insertbefore" : "insertafter", true);
    }

    private void StopScrolling()
    {
        _autoScrollBy = 0;
        _autoScroll.Stop();
    }

    /// <summary>
    /// Moves the list by one tick's worth, and keeps the dashed tile under the pointer as the rows go by.
    /// </summary>
    private void ScrollTowardsTheEdge()
    {
        double furthest = Math.Max(0, RepositoryScroll.Extent.Height - RepositoryScroll.Viewport.Height);
        double moved = Math.Clamp(RepositoryScroll.Offset.Y + _autoScrollBy, 0, furthest);

        if (moved == RepositoryScroll.Offset.Y)
        {
            return;
        }

        RepositoryScroll.Offset = new Vector(RepositoryScroll.Offset.X, moved);
        RepositoryScroll.UpdateLayout();

        OpenTheGap();
    }

    /// <summary>
    /// Finds the row a press landed on, unless it landed on one of the row's buttons.
    /// </summary>
    private PendingDrag? RowAt(object? source)
    {
        if (source is not Visual visual)
        {
            return null;
        }

        foreach (Visual ancestor in visual.GetSelfAndVisualAncestors())
        {
            if (ancestor is Button)
            {
                return null;
            }

            if (ancestor is ContentPresenter { DataContext: ListedRepository entry } row
                && RepositoryList.IndexFromContainer(row) is var index and >= 0)
            {
                return new PendingDrag(entry, index, default);
            }

            if (ReferenceEquals(ancestor, RepositoryList))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// A press on a row, and — once it has moved far enough — the row being carried.
    /// </summary>
    /// <param name="Entry">The repository on the row.</param>
    /// <param name="Index">Where the row is in the list.</param>
    /// <param name="Origin">Where the press landed, which the threshold is measured from.</param>
    private sealed record PendingDrag(ListedRepository Entry, int Index, Point Origin);
}
