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
/// the list. The ViewModel owns the order; this decides only where the pointer is.
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
    /// the row's buttons is the button's.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pending = null;

        if (e.ClickCount != 1 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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
            _ = page.MoveAsync(dragging.Entry, RepositoryReorderGesture.TargetIndex(dragging.Index, slot));
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
    /// Takes the gesture down: the row being carried, the line, the scrolling, the cursor and the
    /// capture. Every way a drag ends goes through here.
    /// </summary>
    private void StopDragging()
    {
        _dragging = null;

        Mark(null, before: true);
        StopScrolling();

        RepositoryList.Cursor = _listCursor;
        _listCursor = null;

        IPointer? pointer = _captured;
        _captured = null;
        pointer?.Capture(null);
    }

    /// <summary>
    /// Shows where the row would land, and scrolls when the pointer is held near an edge.
    /// </summary>
    /// <param name="pointer">Where the pointer is, in the scroll viewer's coordinates.</param>
    private void Steer(Point pointer)
    {
        _lastPointer = pointer;

        ShowTheLine();

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
    /// Puts the line in the gap the pointer is over — or nowhere, when a drop there would move nothing.
    /// </summary>
    private void ShowTheLine()
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
    /// The slot under a point given in the scroll viewer's coordinates.
    /// </summary>
    private int SlotAt(Point pointer)
    {
        Point inList = RepositoryScroll.TranslatePoint(pointer, RepositoryList) ?? pointer;
        List<(double Top, double Height)> bounds = [];

        foreach (ContentPresenter row in Rows())
        {
            double top = row.TranslatePoint(default, RepositoryList)?.Y ?? row.Bounds.Top;
            bounds.Add((top, row.Bounds.Height));
        }

        return RepositoryReorderGesture.SlotFor(inList.Y, bounds);
    }

    /// <summary>
    /// The row containers, in list order.
    /// </summary>
    private List<ContentPresenter> Rows()
        => [.. RepositoryList.GetRealizedContainers()
            .OfType<ContentPresenter>()
            .OrderBy(RepositoryList.IndexFromContainer)];

    /// <summary>
    /// Moves the line onto a row's gap, and takes it off whichever row had it.
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
    /// Moves the list by one tick's worth, and keeps the line under the pointer as the rows go by.
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

        ShowTheLine();
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
