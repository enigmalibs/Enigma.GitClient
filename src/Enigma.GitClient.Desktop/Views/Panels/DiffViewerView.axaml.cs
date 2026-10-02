using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Desktop.Controls.Diff;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Enigma.GitClient.Desktop.Views.Pages;

namespace Enigma.GitClient.Desktop.Views.Panels;

/// <summary>
/// The colour-coded diff viewer, unified or side by side.
/// </summary>
/// <remarks>
/// <para>
/// The code behind this view exists for two things the bindings cannot do. The scroll states are
/// counted in characters, and only the view knows how many characters fit — that is a question
/// about the width of a bar and the width of a glyph, both of which live here.
/// </para>
/// <para>
/// And the reader selects text by pointing at it: where a press lands and where the pointer is
/// dragged to are questions about realised rows and glyphs. The selection itself is the ViewModel's
/// (<see cref="DiffRenderOptions.Selection"/>), and nothing here ever changes the text.
/// </para>
/// </remarks>
public partial class DiffViewerView : UserControl
{
    /// <summary>
    /// How many columns one notch of the wheel moves a pane, which is what a text editor does.
    /// </summary>
    private const double WheelColumns = 3;

    /// <summary>
    /// How many rows are kept above the first change when a file opens. Enough that the change is
    /// not on the very first pixel, little enough that it is still where the eye lands.
    /// </summary>
    private const int ContextRows = 2;

    /// <summary>
    /// The scroll each map drives, once its list has a template to find one in.
    /// </summary>
    private readonly Dictionary<DiffMinimap, ScrollViewer> _scrolls = [];

    private readonly DispatcherTimer _selectionScroll;

    private DiffViewerViewModel? _viewer;
    private TextGesture? _selecting;
    private IPointer? _selectionPointer;
    private Point _selectionPointerAt;
    private double _selectionScrollBy;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public DiffViewerView()
    {
        InitializeComponent();

        UnifiedBar.SizeChanged += OnBarResized;
        LeftBar.SizeChanged += OnBarResized;
        RightBar.SizeChanged += OnBarResized;

        // Tunnelling, because the lists handle the wheel themselves: a sideways wheel has to be
        // claimed on the way down or the vertical scroll swallows it.
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);

        // Tunnelling too, and never handled on the press: the list goes on selecting the row under
        // it, as it always has. A selection only takes the pointer once it is being dragged.
        AddHandler(PointerPressedEvent, OnSelectionPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnSelectionMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnSelectionReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, OnSelectionCaptureLost, RoutingStrategies.Tunnel);

        // The pointer stops reporting while it is held still, and a selection held past the bottom
        // of the list is exactly the gesture that has to keep scrolling.
        _selectionScroll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _selectionScroll.Tick += (_, _) => ScrollWhileSelecting();

        Maps(UnifiedList, UnifiedMinimap);
        Maps(SideBySideList, SideBySideMinimap);
    }

    // ---------------------------------------------------------------- the minimap

    /// <summary>
    /// Ties a rendering's minimap to the rendering's own scroll, in both directions.
    /// </summary>
    /// <param name="list">The list showing the patch.</param>
    /// <param name="map">The map beside it.</param>
    /// <remarks>
    /// Here rather than in the control, and here rather than in the ViewModel: what part of a patch
    /// is on screen is a fact about a realised list, which is a thing only the view has. The map is
    /// told it in fractions and knows nothing about where they came from.
    /// </remarks>
    private void Maps(ListBox list, DiffMinimap map)
    {
        list.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<ScrollViewer>("PART_ScrollViewer") is not { } scroll)
            {
                return;
            }

            _scrolls[map] = scroll;
            scroll.ScrollChanged += (_, _) => Report(scroll, map);

            Report(scroll, map);
        };

        map.ScrollRequested += (_, start) =>
        {
            if (_scrolls.TryGetValue(map, out ScrollViewer? scroll))
            {
                ScrollTo(scroll, start);
            }
        };
    }

    /// <summary>
    /// Tells a map which part of its patch is on screen.
    /// </summary>
    private static void Report(ScrollViewer scroll, DiffMinimap map)
    {
        double extent = scroll.Extent.Height;

        if (extent <= 0)
        {
            // Nothing to scroll: the whole of it is on screen, which is what a full window says.
            map.ViewportStart = 0;
            map.ViewportEnd = 1;
            return;
        }

        map.ViewportStart = Math.Clamp(scroll.Offset.Y / extent, 0, 1);
        map.ViewportEnd = Math.Clamp((scroll.Offset.Y + scroll.Viewport.Height) / extent, 0, 1);
    }

    /// <summary>
    /// Scrolls a rendering so its view starts at a fraction of the patch.
    /// </summary>
    private static void ScrollTo(ScrollViewer scroll, double start)
    {
        double furthest = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);

        scroll.Offset = new Vector(
            scroll.Offset.X,
            Math.Clamp(start * scroll.Extent.Height, 0, furthest));
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        DiffTypography.Changed += OnTypographyChanged;

        ReportViewports();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DiffTypography.Changed -= OnTypographyChanged;
        StopSelecting();

        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewer is not null)
        {
            _viewer.PatchChanged -= OnPatchChanged;
        }

        _viewer = DataContext as DiffViewerViewModel;

        if (_viewer is not null)
        {
            _viewer.PatchChanged += OnPatchChanged;
        }

        ReportViewports();
        ShowFirstChange();
    }

    // ---------------------------------------------------------------- where a file opens

    private void OnPatchChanged(object? sender, EventArgs e) => ShowFirstChange();

    /// <summary>
    /// Puts both renderings where their first change is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A list keeps the offset it had, so without this a newly opened file starts wherever the last
    /// one was left — a third of the way down a long patch is a plausible-looking place to land in
    /// a short one, which is what made the position look random.
    /// </para>
    /// <para>
    /// Posted, and laid out first: the rows have only just been added, and the extent this divides
    /// is measured from realised ones. Both renderings are moved, not only the one on screen, so
    /// switching between them after the file opens does not land somewhere else.
    /// </para>
    /// </remarks>
    private void ShowFirstChange()
        => Dispatcher.UIThread.Post(
            () =>
            {
                if (DataContext is not DiffViewerViewModel viewer)
                {
                    return;
                }

                Align(UnifiedMinimap, viewer.UnifiedFirstChangeRow, viewer.UnifiedRows.Count);
                Align(SideBySideMinimap, viewer.SideBySideFirstChangeRow, viewer.SideBySideRows.Count);
            },
            DispatcherPriority.Background);

    /// <summary>
    /// Scrolls one rendering to the row its first change begins at.
    /// </summary>
    /// <param name="map">The rendering's map, which is what its scroll is known by.</param>
    /// <param name="firstChangeRow">The row that change begins at.</param>
    /// <param name="rowCount">How many rows the rendering has.</param>
    private void Align(DiffMinimap map, int firstChangeRow, int rowCount)
    {
        if (!_scrolls.TryGetValue(map, out ScrollViewer? scroll))
        {
            return;
        }

        scroll.UpdateLayout();

        if (rowCount <= 0)
        {
            scroll.Offset = new Vector(scroll.Offset.X, 0);
            return;
        }

        double start = (double)Math.Max(0, firstChangeRow - ContextRows) / rowCount;

        // More than once, because the extent a virtualising panel reports is an estimate made from
        // the rows it has realised: the first move is what realises the rows around the change, and
        // the next is measured against them. Two passes settle it; the third is the guard, and it
        // stops as soon as a pass moves the view by less than a row.
        for (int pass = 0; pass < 3; pass++)
        {
            double before = scroll.Offset.Y;

            ScrollTo(scroll, start);
            scroll.UpdateLayout();

            if (Math.Abs(scroll.Offset.Y - before) < 1)
            {
                return;
            }
        }
    }

    private void OnTypographyChanged(object? sender, EventArgs e) => ReportViewports();

    private void OnBarResized(object? sender, SizeChangedEventArgs e) => ReportViewports();

    /// <summary>
    /// Tells each pane how many characters of it are on screen.
    /// </summary>
    /// <remarks>
    /// A bar spans its whole pane, so what it can show is its own width less the gutters and the
    /// marker beside the text — the unified rendering carries two line-number columns, each pane of
    /// the side-by-side rendering carries one.
    /// </remarks>
    private void ReportViewports()
    {
        if (DataContext is not DiffViewerViewModel viewer)
        {
            return;
        }

        DiffMetrics metrics = DiffTypography.Current;
        double side = metrics.GutterWidth + metrics.MarkerWidth;

        viewer.Render.UnifiedScroll.Viewport =
            Columns(UnifiedBar.Bounds.Width - metrics.GutterWidth - side, metrics);

        // The narrower of the two panes: they share one offset, so neither may be scrolled past
        // what it can itself show. In practice the two are equal — the panes are a 50/50 split —
        // but the smaller is the honest answer when they are not.
        viewer.Render.SideBySideScroll.Viewport = Math.Min(
            Columns(LeftBar.Bounds.Width - side, metrics),
            Columns(RightBar.Bounds.Width - side, metrics));
    }

    private static double Columns(double pixels, DiffMetrics metrics)
        => pixels <= 0 ? 0 : pixels / metrics.CharacterWidth;

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not DiffViewerViewModel viewer)
        {
            return;
        }

        // A tilting wheel says so itself; an ordinary one says it with shift, the way every editor
        // reads it.
        double delta = e.Delta.X != 0
            ? e.Delta.X
            : e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? e.Delta.Y : 0;

        if (delta == 0)
        {
            return;
        }

        DiffScrollState pane = PaneUnder(viewer);

        if (!pane.IsScrollable)
        {
            return;
        }

        pane.Offset -= delta * WheelColumns;
        e.Handled = true;
    }

    /// <summary>
    /// Works out which scroll a sideways wheel moves, which is the one the rendering on screen has.
    /// </summary>
    /// <remarks>
    /// The side-by-side rendering has one scroll for both panes, so where the pointer is over it no
    /// longer matters: a wheel anywhere in it moves both sides together, which is what the two
    /// being synchronised means.
    /// </remarks>
    private static DiffScrollState PaneUnder(DiffViewerViewModel viewer)
        => viewer.IsUnified ? viewer.Render.UnifiedScroll : viewer.Render.SideBySideScroll;

    // ---------------------------------------------------------------- selecting text

    /// <summary>
    /// Gets a value indicating whether the reader is dragging a text selection right now.
    /// </summary>
    internal bool IsSelectingText => _selecting is { IsDragging: true };

    /// <summary>
    /// Starts a selection where a press on a line's text lands, or extends the one there is with Shift.
    /// </summary>
    private void OnSelectionPressed(object? sender, PointerPressedEventArgs e)
    {
        _selecting = null;

        if (e.ClickCount != 1
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || DataContext is not DiffViewerViewModel viewer
            || ListOf(e.Source) is not { } list
            || ContainerOf(e.Source) is not { DataContext: DiffRowViewModel { IsLine: true } row } container
            || LineAt(container, e.GetPosition(container)) is not { } line)
        {
            return;
        }

        DiffTextPosition position = new(row.Index, line.IndexAt(e.GetPosition(line)));
        DiffTextSelection selection = viewer.Render.Selection;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && selection.IsActive && selection.Pane == line.Pane)
        {
            selection.ExtendTo(position);
        }
        else
        {
            // A plain click starts an empty selection, which is what takes the last one away.
            selection.Begin(line.Pane, position);
        }

        _selecting = new TextGesture(list, line.Pane, e.GetPosition(list), IsDragging: false);
    }

    /// <summary>
    /// Extends the selection to the character under the pointer, once it has moved far enough to be a
    /// drag rather than a click.
    /// </summary>
    private void OnSelectionMoved(object? sender, PointerEventArgs e)
    {
        if (_selecting is not { } gesture)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            StopSelecting();
            return;
        }

        Point point = e.GetPosition(gesture.List);

        if (!gesture.IsDragging)
        {
            if (!BranchDragGesture.IsDrag(gesture.Origin, point))
            {
                return;
            }

            gesture = gesture with { IsDragging = true };
            _selecting = gesture;

            // From here every move is reported to the list, wherever the pointer goes — over the
            // other pane, the toolbar, off the window — and the selection follows it there.
            _selectionPointer = e.Pointer;
            _selectionPointer.Capture(gesture.List);
        }

        _selectionPointerAt = point;

        ExtendSelection(gesture, point);
        FollowTheEdge(gesture, point);

        e.Handled = true;
    }

    private void OnSelectionReleased(object? sender, PointerReleasedEventArgs e)
    {
        bool dragged = _selecting is { IsDragging: true };

        StopSelecting();

        // A click is still the list's; a drag was the selection's.
        if (dragged)
        {
            e.Handled = true;
        }
    }

    private void OnSelectionCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_selecting is not { IsDragging: true })
        {
            return;
        }

        // Whoever took the capture owns that pointer now: releasing it here would take it from them.
        _selectionPointer = null;

        StopSelecting();
    }

    /// <summary>
    /// Ends the gesture. What it selected stays selected.
    /// </summary>
    private void StopSelecting()
    {
        _selecting = null;
        _selectionScrollBy = 0;
        _selectionScroll.Stop();

        IPointer? pointer = _selectionPointer;
        _selectionPointer = null;
        pointer?.Capture(null);
    }

    /// <summary>
    /// Moves the selection's free end to the place under a point: on the row at that height — the first
    /// or the last one on screen when the pointer is above or below them — and in the pane the
    /// selection is in, wherever the pointer is sideways.
    /// </summary>
    /// <param name="gesture">The gesture under way.</param>
    /// <param name="point">Where the pointer is, in the list's coordinates.</param>
    private void ExtendSelection(TextGesture gesture, Point point)
    {
        if (DataContext is not DiffViewerViewModel viewer || RowNearest(gesture.List, point.Y) is not { } container)
        {
            return;
        }

        if (container.DataContext is not DiffRowViewModel row)
        {
            return;
        }

        int column = 0;

        if (row.IsLine && LineIn(container, gesture.Pane) is { } line
            && gesture.List.TranslatePoint(point, line) is { } inLine)
        {
            column = line.IndexAt(inLine);
        }

        viewer.Render.Selection.ExtendTo(new DiffTextPosition(row.Index, column));
    }

    /// <summary>
    /// Starts, steers or stops the scrolling a selection held near an edge asks for.
    /// </summary>
    private void FollowTheEdge(TextGesture gesture, Point point)
    {
        if (ScrollOf(gesture.List) is not { } scroll || gesture.List.TranslatePoint(point, scroll) is not { } inScroll)
        {
            return;
        }

        _selectionScrollBy = BranchDragGesture.ScrollFor(inScroll.Y, scroll.Viewport.Height);

        if (_selectionScrollBy == 0)
        {
            _selectionScroll.Stop();
        }
        else if (!_selectionScroll.IsEnabled)
        {
            _selectionScroll.Start();
        }
    }

    /// <summary>
    /// Moves the list by one tick's worth and takes the selection along to the rows that came into view.
    /// </summary>
    private void ScrollWhileSelecting()
    {
        if (_selecting is not { IsDragging: true } gesture || ScrollOf(gesture.List) is not { } scroll)
        {
            _selectionScroll.Stop();
            return;
        }

        double furthest = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        double moved = Math.Clamp(scroll.Offset.Y + _selectionScrollBy, 0, furthest);

        if (moved == scroll.Offset.Y)
        {
            return;
        }

        scroll.Offset = new Vector(scroll.Offset.X, moved);
        scroll.UpdateLayout();

        ExtendSelection(gesture, _selectionPointerAt);
    }

    /// <summary>
    /// The rendering's list an event landed in, or <see langword="null"/> when it landed elsewhere.
    /// </summary>
    private ListBox? ListOf(object? source)
        => source is Visual visual
            ? visual.GetSelfAndVisualAncestors().OfType<ListBox>().FirstOrDefault(list => list == UnifiedList || list == SideBySideList)
            : null;

    private static ListBoxItem? ContainerOf(object? source)
        => source is Visual visual ? visual.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() : null;

    /// <summary>
    /// The line whose text column a point is in: inside the line's pane, and not on its gutter or
    /// marker. A press on the numbers is still a press on the row.
    /// </summary>
    /// <param name="container">The row.</param>
    /// <param name="point">The point, in the row's coordinates.</param>
    private static DiffLineText? LineAt(ListBoxItem container, Point point)
    {
        foreach (DiffLineText line in container.GetVisualDescendants().OfType<DiffLineText>())
        {
            if (!line.IsEffectivelyVisible
                || line.GetVisualAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("diffrow")) is not { } pane
                || pane.TranslatePoint(default, container) is not { } paneAt
                || line.TranslatePoint(default, container) is not { } lineAt)
            {
                continue;
            }

            Rect paneBounds = new(paneAt, pane.Bounds.Size);

            if (paneBounds.Contains(point) && point.X >= lineAt.X)
            {
                return line;
            }
        }

        return null;
    }

    private static DiffLineText? LineIn(ListBoxItem container, DiffPane pane)
        => container.GetVisualDescendants().OfType<DiffLineText>().FirstOrDefault(line => line.Pane == pane && line.IsEffectivelyVisible);

    /// <summary>
    /// The realised row at a height in the list, or the nearest one when the height is above or below
    /// them all.
    /// </summary>
    private static ListBoxItem? RowNearest(ListBox list, double y)
    {
        ListBoxItem? nearest = null;
        double distance = double.MaxValue;

        foreach (ListBoxItem container in list.GetRealizedContainers().OfType<ListBoxItem>())
        {
            if (!container.IsEffectivelyVisible || container.TranslatePoint(default, list) is not { } at)
            {
                continue;
            }

            double top = at.Y;
            double bottom = at.Y + container.Bounds.Height;

            if (y >= top && y < bottom)
            {
                return container;
            }

            double away = y < top ? top - y : y - bottom;

            if (away < distance)
            {
                distance = away;
                nearest = container;
            }
        }

        return nearest;
    }

    private ScrollViewer? ScrollOf(ListBox list)
    {
        DiffMinimap map = list == UnifiedList ? UnifiedMinimap : SideBySideMinimap;

        return _scrolls.TryGetValue(map, out ScrollViewer? scroll) ? scroll : null;
    }

    /// <summary>
    /// A press on a line's text, and — once it has moved far enough — the drag extending the selection.
    /// </summary>
    /// <param name="List">The rendering's list.</param>
    /// <param name="Pane">The pane the selection is in.</param>
    /// <param name="Origin">Where the press landed, in the list's coordinates.</param>
    /// <param name="IsDragging">Whether the pointer has moved past the drag threshold.</param>
    private sealed record TextGesture(ListBox List, DiffPane Pane, Point Origin, bool IsDragging);
}
