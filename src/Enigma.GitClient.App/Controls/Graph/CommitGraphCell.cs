using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Enigma.GitClient.Core.Graph;

namespace Enigma.GitClient.App.Controls.Graph;

/// <summary>
/// Draws one row of the commit graph: the lanes passing through it, the lines arriving at and
/// leaving its commit, and the node itself.
/// </summary>
/// <remarks>
/// <para>
/// One control per row, and each one draws only its own row. That is what lets the history view
/// virtualise: a row can be created, drawn and thrown away without knowing anything about its
/// neighbours, so a hundred-thousand-commit repository costs the same as a hundred-commit one.
/// </para>
/// <para>
/// Connections are drawn as rounded elbows rather than diagonals. A diagonal reads as a slash across
/// the column and becomes unreadable wherever several lanes cross; a curve that leaves and arrives
/// vertically keeps every line traceable, which is the entire point of the view.
/// </para>
/// </remarks>
public sealed class CommitGraphCell : Control
{
    /// <summary>
    /// Defines the <see cref="Row"/> property.
    /// </summary>
    public static readonly StyledProperty<GraphRow?> RowProperty =
        AvaloniaProperty.Register<CommitGraphCell, GraphRow?>(nameof(Row));

    /// <summary>
    /// Defines the <see cref="IsHead"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsHeadProperty =
        AvaloniaProperty.Register<CommitGraphCell, bool>(nameof(IsHead));

    /// <summary>
    /// Defines the <see cref="IsUncommitted"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsUncommittedProperty =
        AvaloniaProperty.Register<CommitGraphCell, bool>(nameof(IsUncommitted));

    /// <summary>
    /// Defines the <see cref="LaneWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LaneWidthProperty =
        AvaloniaProperty.Register<CommitGraphCell, double>(nameof(LaneWidth), 16);

    /// <summary>
    /// Defines the <see cref="LanePadding"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LanePaddingProperty =
        AvaloniaProperty.Register<CommitGraphCell, double>(nameof(LanePadding), 6);

    /// <summary>
    /// Defines the <see cref="NodeRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> NodeRadiusProperty =
        AvaloniaProperty.Register<CommitGraphCell, double>(nameof(NodeRadius), 9);

    /// <summary>
    /// Defines the <see cref="StrokeThickness"/> property.
    /// </summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<CommitGraphCell, double>(nameof(StrokeThickness), 2);

    /// <summary>
    /// Defines the <see cref="CurveRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<double> CurveRadiusProperty =
        AvaloniaProperty.Register<CommitGraphCell, double>(nameof(CurveRadius), 9);

    /// <summary>
    /// Defines the <see cref="MaximumLanes"/> property.
    /// </summary>
    public static readonly StyledProperty<int> MaximumLanesProperty =
        AvaloniaProperty.Register<CommitGraphCell, int>(nameof(MaximumLanes), 14);

    /// <summary>
    /// How far outside the node the HEAD ring is drawn, and how thick it is. The ring is what makes
    /// the node's real extent larger than its radius, so it is what the fitting rule has to allow
    /// for.
    /// </summary>
    private const double HeadRingGap = 3;
    private const double HeadRingThickness = 1.5;

    private readonly GraphPalette _palette = new();

    static CommitGraphCell()
    {
        AffectsRender<CommitGraphCell>(
            RowProperty,
            IsHeadProperty,
            IsUncommittedProperty,
            LaneWidthProperty,
            LanePaddingProperty,
            NodeRadiusProperty,
            StrokeThicknessProperty,
            CurveRadiusProperty);

        AffectsMeasure<CommitGraphCell>(RowProperty, LaneWidthProperty, LanePaddingProperty, MaximumLanesProperty);
    }

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public CommitGraphCell()
    {
        // Lines are one to two pixels wide, so anti-aliasing is what keeps them from looking ragged.
        RenderOptions.SetEdgeMode(this, EdgeMode.Antialias);
    }

    /// <summary>
    /// Gets or sets the row to draw.
    /// </summary>
    public GraphRow? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether HEAD points at this row's commit, which is drawn
    /// with an extra ring.
    /// </summary>
    public bool IsHead
    {
        get => GetValue(IsHeadProperty);
        set => SetValue(IsHeadProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this is the pseudo-row standing for the uncommitted
    /// working directory, drawn as a hollow node.
    /// </summary>
    public bool IsUncommitted
    {
        get => GetValue(IsUncommittedProperty);
        set => SetValue(IsUncommittedProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal distance between two lanes.
    /// </summary>
    public double LaneWidth
    {
        get => GetValue(LaneWidthProperty);
        set => SetValue(LaneWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the space before the first lane and after the last.
    /// </summary>
    public double LanePadding
    {
        get => GetValue(LanePaddingProperty);
        set => SetValue(LanePaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets the radius a commit's node is drawn at, before it is fitted to the row and the
    /// lane by <see cref="CalculateNodeRadius"/>.
    /// </summary>
    public double NodeRadius
    {
        get => GetValue(NodeRadiusProperty);
        set => SetValue(NodeRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets how thick the lane lines are drawn.
    /// </summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets how far from the node a connection starts curving.
    /// </summary>
    public double CurveRadius
    {
        get => GetValue(CurveRadiusProperty);
        set => SetValue(CurveRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets how many lanes the column may grow to before it stops widening. Beyond this the
    /// graph scrolls horizontally rather than squeezing the columns beside it out of existence.
    /// </summary>
    public int MaximumLanes
    {
        get => GetValue(MaximumLanesProperty);
        set => SetValue(MaximumLanesProperty, value);
    }

    /// <summary>
    /// Gets the horizontal centre of a lane.
    /// </summary>
    /// <param name="lane">The lane index.</param>
    /// <returns>The x coordinate.</returns>
    public double GetLaneCentre(int lane) => LanePadding + (LaneWidth / 2) + (lane * LaneWidth);

    /// <summary>
    /// Calculates the width a row of a given span needs.
    /// </summary>
    /// <param name="maxLane">The highest lane index occupied.</param>
    /// <param name="laneWidth">The distance between lanes.</param>
    /// <param name="lanePadding">The space before the first lane and after the last.</param>
    /// <param name="maximumLanes">The most lanes the column may grow to.</param>
    /// <returns>The required width.</returns>
    public static double CalculateWidth(int maxLane, double laneWidth, double lanePadding, int maximumLanes)
    {
        int lanes = Math.Clamp(maxLane + 1, 1, Math.Max(1, maximumLanes));
        return (lanes * laneWidth) + (lanePadding * 2);
    }

    /// <summary>
    /// Calculates the radius a node is actually drawn at: the wanted radius, reduced to whatever
    /// the row and the lane can hold.
    /// </summary>
    /// <param name="nodeRadius">The radius asked for.</param>
    /// <param name="rowHeight">The height of the row being drawn.</param>
    /// <param name="laneWidth">The distance between two lanes.</param>
    /// <param name="strokeThickness">How thick the lane lines are.</param>
    /// <param name="isHead">Whether the row carries the HEAD ring, which is drawn outside the node.</param>
    /// <returns>The radius to draw.</returns>
    /// <remarks>
    /// Both limits are reachable from the preferences: the row height goes down to 18 and the lane
    /// width to 8, and at those values a node drawn at its full radius is sliced off by the rows
    /// above and below and sits on the neighbouring lane's line. Shrinking is the graceful answer —
    /// the graph stays readable at every size the settings page offers.
    /// </remarks>
    public static double CalculateNodeRadius(
        double nodeRadius,
        double rowHeight,
        double laneWidth,
        double strokeThickness,
        bool isHead)
    {
        double outside = isHead ? HeadRingGap + HeadRingThickness : strokeThickness;
        double rowLimit = (rowHeight / 2) - outside;

        // The node must stop short of where the next lane's line is drawn, not merely of its centre.
        double laneLimit = laneWidth - (strokeThickness / 2) - 1;

        return Math.Clamp(nodeRadius, 1, Math.Max(1, Math.Min(rowLimit, laneLimit)));
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => new(CalculateWidth(Row?.MaxLane ?? 0, LaneWidth, LanePadding, MaximumLanes), 0);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        GraphRow? row = Row;

        if (row is null || Bounds.Height <= 0)
        {
            return;
        }

        double height = Bounds.Height;
        double middle = height / 2;

        foreach (GraphEdge edge in row.Edges)
        {
            IPen pen = new Pen(_palette.Get(this, edge.Colour), StrokeThickness, lineCap: PenLineCap.Round);

            switch (edge.Kind)
            {
                case GraphEdgeKind.Straight:
                    DrawVertical(context, pen, GetLaneCentre(edge.FromLane), 0, height);
                    break;

                case GraphEdgeKind.MergeIn:
                    DrawIncoming(context, pen, edge, middle);
                    break;

                case GraphEdgeKind.BranchOut:
                    DrawOutgoing(context, pen, edge, middle, height);
                    break;

                default:
                    break;
            }
        }

        DrawNode(context, row, middle);
    }

    /// <summary>
    /// A line arriving from the top edge and ending at the node.
    /// </summary>
    private void DrawIncoming(DrawingContext context, IPen pen, GraphEdge edge, double middle)
    {
        double from = GetLaneCentre(edge.FromLane);
        double to = GetLaneCentre(edge.ToLane);

        if (edge.IsVertical)
        {
            DrawVertical(context, pen, from, 0, middle);
            return;
        }

        double radius = Math.Min(CurveRadius, middle);

        StreamGeometry geometry = new();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(Snap(new Point(from, 0)), isFilled: false);
            path.LineTo(Snap(new Point(from, middle - radius)));

            // The control point sits at the corner, which is what turns the join into a smooth
            // elbow instead of a visible kink.
            path.QuadraticBezierTo(Snap(new Point(from, middle)), Snap(new Point(to, middle)));
            path.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>
    /// A line leaving the node for the bottom edge, on its way to a parent.
    /// </summary>
    private void DrawOutgoing(DrawingContext context, IPen pen, GraphEdge edge, double middle, double height)
    {
        double from = GetLaneCentre(edge.FromLane);
        double to = GetLaneCentre(edge.ToLane);

        if (edge.IsVertical)
        {
            DrawVertical(context, pen, from, middle, height);
            return;
        }

        double radius = Math.Min(CurveRadius, height - middle);

        StreamGeometry geometry = new();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(Snap(new Point(from, middle)), isFilled: false);
            path.QuadraticBezierTo(Snap(new Point(to, middle)), Snap(new Point(to, middle + radius)));
            path.LineTo(Snap(new Point(to, height)));
            path.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawNode(DrawingContext context, GraphRow row, double middle)
    {
        Point centre = Snap(new Point(GetLaneCentre(row.Lane), middle));
        IBrush lane = _palette.Get(this, row.Colour);
        IBrush outline = GraphPalette.Find(this, "GraphNodeOutlineBrush") ?? Brushes.Black;
        double radius = CalculateNodeRadius(NodeRadius, middle * 2, LaneWidth, StrokeThickness, IsHead);

        if (IsUncommitted)
        {
            // The working directory is not a commit, so it is drawn as an outline rather than a
            // filled node — the difference has to be visible at a glance.
            context.DrawEllipse(outline, new Pen(lane, StrokeThickness, DashStyle.Dash), centre, radius, radius);
            return;
        }

        if (IsHead)
        {
            IBrush ring = GraphPalette.Find(this, "GraphHeadRingBrush") ?? Brushes.White;
            double ringRadius = radius + HeadRingGap;
            context.DrawEllipse(null, new Pen(ring, HeadRingThickness), centre, ringRadius, ringRadius);
        }

        if (row.IsMerge)
        {
            // A merge reads as a ring: it is a join, not a point where work happened.
            context.DrawEllipse(outline, new Pen(lane, StrokeThickness + 0.5), centre, radius, radius);
            return;
        }

        context.DrawEllipse(lane, null, centre, radius, radius);
    }

    private void DrawVertical(DrawingContext context, IPen pen, double x, double top, double bottom)
        => context.DrawLine(pen, Snap(new Point(x, top)), Snap(new Point(x, bottom)));

    /// <summary>
    /// Nudges a coordinate onto the pixel grid for an odd-width stroke, so a 1 or 2 pixel line comes
    /// out crisp instead of smeared across two rows of pixels.
    /// </summary>
    private Point Snap(Point point)
    {
        bool oddStroke = ((int)Math.Round(StrokeThickness)) % 2 == 1;
        double offset = oddStroke ? 0.5 : 0;

        return new Point(Math.Floor(point.X) + offset, Math.Floor(point.Y) + offset);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ThemeVariantScope.ActualThemeVariantProperty)
        {
            // The palette caches per variant; a switch must re-read it and repaint.
            _palette.Invalidate();
            InvalidateVisual();
        }
    }
}
