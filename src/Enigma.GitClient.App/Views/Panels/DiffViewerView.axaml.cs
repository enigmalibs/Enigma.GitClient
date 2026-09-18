using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

        ReportViewports();
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
