using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Enigma.GitClient.App.Controls.Diff;
using Enigma.GitClient.App.ViewModels.Panels;

namespace Enigma.GitClient.App.Views.Panels;

/// <summary>
/// The colour-coded diff viewer, unified or side by side.
/// </summary>
/// <remarks>
/// The code behind this view exists for one thing the bindings cannot do: the scroll states are
/// counted in characters, and only the view knows how many characters fit — that is a question
/// about the width of a bar and the width of a glyph, both of which live here.
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

    private DiffViewerViewModel? _viewer;

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
}
