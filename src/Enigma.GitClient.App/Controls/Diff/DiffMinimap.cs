using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Enigma.GitClient.App.Controls.Diff;

/// <summary>
/// What a stretch of the map stands for.
/// </summary>
public enum DiffMarkKind
{
    /// <summary>Lines the change added.</summary>
    Added,

    /// <summary>Lines the change removed.</summary>
    Removed,

    /// <summary>The band that introduces a hunk, which is where a change begins.</summary>
    Hunk,
}

/// <summary>
/// One run of consecutive rows of the same kind, as the map draws it.
/// </summary>
/// <param name="Kind">What the run stands for.</param>
/// <param name="FirstRow">The index of its first row in the rendering.</param>
/// <param name="RowCount">How many rows it spans.</param>
/// <remarks>
/// A run rather than a row: a patch holds thousands of lines and a map is a couple of hundred pixels
/// tall, so one rectangle per line would be both invisible and pointless work. What a reader looks
/// for on a map is "where are the changes", and a run is exactly that.
/// </remarks>
public sealed record DiffChangeMark(DiffMarkKind Kind, int FirstRow, int RowCount)
{
    /// <summary>Gets the row after the run's last one.</summary>
    public int EndRow => FirstRow + RowCount;
}

/// <summary>
/// The strip beside a diff that says where its changes are and which part of it is on screen.
/// </summary>
/// <remarks>
/// <para>
/// It knows nothing about lists or scroll viewers: it is given the runs to draw, how many rows the
/// rendering has, and where the viewport starts and ends as fractions of the whole, and it raises
/// <see cref="ScrollRequested"/> when the reader points at a part of the patch. Whoever owns the
/// scrolling answers that — which keeps the plumbing in the one place that already does it and
/// keeps this control testable without a window full of rows.
/// </para>
/// <para>
/// It is not an editor's minimap: it draws the changes, not the code. Rendering thousands of lines
/// of text at two pixels tall costs a great deal and says less than three coloured bars.
/// </para>
/// </remarks>
public sealed class DiffMinimap : Control
{
    /// <summary>
    /// Defines the <see cref="Marks"/> property.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<DiffChangeMark>?> MarksProperty =
        AvaloniaProperty.Register<DiffMinimap, IReadOnlyList<DiffChangeMark>?>(nameof(Marks));

    /// <summary>
    /// Defines the <see cref="RowCount"/> property.
    /// </summary>
    public static readonly StyledProperty<int> RowCountProperty =
        AvaloniaProperty.Register<DiffMinimap, int>(nameof(RowCount));

    /// <summary>
    /// Defines the <see cref="ViewportStart"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ViewportStartProperty =
        AvaloniaProperty.Register<DiffMinimap, double>(nameof(ViewportStart));

    /// <summary>
    /// Defines the <see cref="ViewportEnd"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ViewportEndProperty =
        AvaloniaProperty.Register<DiffMinimap, double>(nameof(ViewportEnd), 1);

    /// <summary>
    /// Defines the <see cref="TrackBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<DiffMinimap, IBrush?>(nameof(TrackBrush));

    /// <summary>
    /// Defines the <see cref="AddedBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> AddedBrushProperty =
        AvaloniaProperty.Register<DiffMinimap, IBrush?>(nameof(AddedBrush));

    /// <summary>
    /// Defines the <see cref="RemovedBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> RemovedBrushProperty =
        AvaloniaProperty.Register<DiffMinimap, IBrush?>(nameof(RemovedBrush));

    /// <summary>
    /// Defines the <see cref="HunkBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> HunkBrushProperty =
        AvaloniaProperty.Register<DiffMinimap, IBrush?>(nameof(HunkBrush));

    /// <summary>
    /// Defines the <see cref="ViewportBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ViewportBrushProperty =
        AvaloniaProperty.Register<DiffMinimap, IBrush?>(nameof(ViewportBrush));

    /// <summary>
    /// Defines the <see cref="ViewportBorderBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ViewportBorderBrushProperty =
        AvaloniaProperty.Register<DiffMinimap, IBrush?>(nameof(ViewportBorderBrush));

    /// <summary>
    /// The least a run may ever be drawn at, whatever the strip's width. A one-line change in a
    /// four-thousand-line file is a fraction of a pixel, and a mark nobody can see is a mark that
    /// is not there.
    /// </summary>
    private const double SmallestMarkHeight = 2;

    /// <summary>
    /// How much of the strip's width is kept clear on each side of the marks, and what the least a
    /// run may be drawn at is measured against.
    /// </summary>
    /// <remarks>
    /// Proportions rather than constants: the strip is wide enough to aim at with a trackpad, and a
    /// two-pixel inset with two-pixel marks — which is what a fourteen-pixel strip wanted — reads
    /// as three hairlines lost in a gutter once the gutter is thirty-six pixels across.
    /// </remarks>
    private const double SideInset = 0.1;

    /// <summary>How tall a mark may be at its smallest, as a fraction of the strip's width.</summary>
    private const double MarkHeightOfWidth = 1.0 / 12;

    /// <summary>How thick the window's outline is, as a fraction of the strip's width.</summary>
    private const double OutlineOfWidth = 1.0 / 18;

    static DiffMinimap()
    {
        AffectsRender<DiffMinimap>(
            MarksProperty,
            RowCountProperty,
            ViewportStartProperty,
            ViewportEndProperty,
            TrackBrushProperty,
            AddedBrushProperty,
            RemovedBrushProperty,
            HunkBrushProperty,
            ViewportBrushProperty,
            ViewportBorderBrushProperty);
    }

    /// <summary>
    /// Raised when the reader points at a part of the patch, carrying where the viewport should
    /// start as a fraction of the whole.
    /// </summary>
    public event EventHandler<double>? ScrollRequested;

    /// <summary>
    /// Gets or sets the runs to draw.
    /// </summary>
    public IReadOnlyList<DiffChangeMark>? Marks
    {
        get => GetValue(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    /// <summary>
    /// Gets or sets how many rows the rendering has, which is what the runs are positioned against.
    /// </summary>
    public int RowCount
    {
        get => GetValue(RowCountProperty);
        set => SetValue(RowCountProperty, value);
    }

    /// <summary>Gets or sets where the visible part of the patch begins, from 0 to 1.</summary>
    public double ViewportStart
    {
        get => GetValue(ViewportStartProperty);
        set => SetValue(ViewportStartProperty, value);
    }

    /// <summary>Gets or sets where the visible part of the patch ends, from 0 to 1.</summary>
    public double ViewportEnd
    {
        get => GetValue(ViewportEndProperty);
        set => SetValue(ViewportEndProperty, value);
    }

    /// <summary>Gets or sets the brush the strip itself is painted in.</summary>
    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>Gets or sets the brush an added run is drawn in.</summary>
    public IBrush? AddedBrush
    {
        get => GetValue(AddedBrushProperty);
        set => SetValue(AddedBrushProperty, value);
    }

    /// <summary>Gets or sets the brush a removed run is drawn in.</summary>
    public IBrush? RemovedBrush
    {
        get => GetValue(RemovedBrushProperty);
        set => SetValue(RemovedBrushProperty, value);
    }

    /// <summary>Gets or sets the brush a hunk band is drawn in.</summary>
    public IBrush? HunkBrush
    {
        get => GetValue(HunkBrushProperty);
        set => SetValue(HunkBrushProperty, value);
    }

    /// <summary>Gets or sets the brush the window over the visible rows is filled with.</summary>
    public IBrush? ViewportBrush
    {
        get => GetValue(ViewportBrushProperty);
        set => SetValue(ViewportBrushProperty, value);
    }

    /// <summary>Gets or sets the brush that window is outlined in.</summary>
    public IBrush? ViewportBorderBrush
    {
        get => GetValue(ViewportBorderBrushProperty);
        set => SetValue(ViewportBorderBrushProperty, value);
    }

    /// <summary>
    /// Works out where the viewport should start for a pointer at a given height.
    /// </summary>
    /// <param name="y">Where the pointer is, in the map's own coordinates.</param>
    /// <param name="height">How tall the map is.</param>
    /// <param name="viewportFraction">How much of the patch is on screen, from 0 to 1.</param>
    /// <returns>Where the viewport should start, from 0 to 1.</returns>
    /// <remarks>
    /// The pointer is the middle of the window rather than its top: pointing at a change is asking
    /// to look at it, and a window that started there would put it on the first line, with its
    /// context off screen above.
    /// </remarks>
    public static double StartFor(double y, double height, double viewportFraction)
    {
        if (height <= 0)
        {
            return 0;
        }

        double centre = Math.Clamp(y / height, 0, 1);
        double half = Math.Clamp(viewportFraction, 0, 1) / 2;

        return Math.Clamp(centre - half, 0, Math.Max(0, 1 - Math.Clamp(viewportFraction, 0, 1)));
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect bounds = new(Bounds.Size);

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (TrackBrush is { } track)
        {
            context.FillRectangle(track, bounds);
        }

        DrawMarks(context, bounds);
        DrawViewport(context, bounds);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Pointer.Capture(this);
        RequestScrollTo(e.GetPosition(this).Y);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        // Only while the press that started the drag is still held, which is what the capture says.
        if (ReferenceEquals(e.Pointer.Captured, this))
        {
            RequestScrollTo(e.GetPosition(this).Y);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (ReferenceEquals(e.Pointer.Captured, this))
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Asks for the patch to be scrolled so the pointer's height is in the middle of the view.
    /// </summary>
    /// <param name="y">Where the pointer is, in the map's own coordinates.</param>
    internal void RequestScrollTo(double y)
        => ScrollRequested?.Invoke(this, StartFor(y, Bounds.Height, ViewportEnd - ViewportStart));

    private void DrawMarks(DrawingContext context, Rect bounds)
    {
        if (Marks is not { Count: > 0 } marks || RowCount <= 0)
        {
            return;
        }

        double inset = bounds.Width * SideInset;
        double width = Math.Max(1, bounds.Width - (inset * 2));
        double smallest = Math.Max(SmallestMarkHeight, bounds.Width * MarkHeightOfWidth);

        foreach (DiffChangeMark mark in marks)
        {
            IBrush? brush = mark.Kind switch
            {
                DiffMarkKind.Added => AddedBrush,
                DiffMarkKind.Removed => RemovedBrush,
                DiffMarkKind.Hunk => HunkBrush,
                _ => null,
            };

            if (brush is null)
            {
                continue;
            }

            double top = bounds.Height * ((double)mark.FirstRow / RowCount);
            double height = Math.Max(smallest, bounds.Height * ((double)mark.RowCount / RowCount));

            // A run at the very end must not be drawn past the bottom by its own minimum height.
            top = Math.Min(top, Math.Max(0, bounds.Height - height));

            context.FillRectangle(brush, new Rect(inset, top, width, height));
        }
    }

    private void DrawViewport(DrawingContext context, Rect bounds)
    {
        double start = Math.Clamp(ViewportStart, 0, 1);
        double end = Math.Clamp(ViewportEnd, start, 1);

        // Nothing to say when the whole patch is on screen: a window around everything is noise.
        if (end - start >= 1)
        {
            return;
        }

        double top = bounds.Height * start;
        double height = Math.Max(
            Math.Max(SmallestMarkHeight, bounds.Width * MarkHeightOfWidth),
            bounds.Height * (end - start));

        top = Math.Min(top, Math.Max(0, bounds.Height - height));

        Rect window = new(0, top, bounds.Width, height);

        if (ViewportBrush is { } fill)
        {
            context.FillRectangle(fill, window);
        }

        if (ViewportBorderBrush is { } border)
        {
            // The outline grows with the strip too: one pixel around a thirty-six-pixel window is
            // the wash's edge rather than a frame around where the reader is.
            double thickness = Math.Max(1, bounds.Width * OutlineOfWidth);

            context.DrawRectangle(null, new Pen(border, thickness), window.Deflate(thickness / 2));
        }
    }
}
