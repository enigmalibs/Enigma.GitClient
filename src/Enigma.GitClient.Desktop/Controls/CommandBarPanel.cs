using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Enigma.GitClient.Desktop.Controls;

/// <summary>
/// Lays a command bar out in three slots — what the bar is about on the left, its commands in the
/// middle, a flexible block on the right — and keeps the commands at the bar's true centre whenever
/// both sides fit beside them.
/// </summary>
/// <remarks>
/// <para>
/// The slots are the first three children, in that order: the <b>start</b>, the <b>centre</b> and the
/// <b>end</b>. A fourth child is not laid out.
/// </para>
/// <para>
/// A <see cref="Grid"/> cannot say this. Centred in the space its neighbours leave
/// (<c>Auto,*,Auto</c>), the commands drift towards the narrower side by half the difference between
/// the two; between two equal star columns (<c>*,Auto,*</c>), a wide end is starved to nothing while
/// the start's column stands empty.
/// </para>
/// <list type="bullet">
/// <item>The start hugs the left edge at its desired width.</item>
/// <item>
/// The centre sits at the true centre when the start fits on its left and the end's
/// <see cref="Layoutable.MinWidth"/> on its right; otherwise it moves as little as it must, never over
/// the start, and leaving the end its minimum while there is room for that.
/// </item>
/// <item>
/// The end hugs the right edge and is given the width right of the centre, up to its
/// <see cref="Layoutable.MaxWidth"/> — or its desired width when it has none — so a search box in it is
/// as wide as it can be on a wide bar and is the first to give way on a narrow one. Narrower than its
/// minimum, it runs past the right edge rather than over the commands.
/// </item>
/// <item>Every slot is as tall as the bar; its own vertical alignment places it in that height.</item>
/// <item>
/// A bar too narrow for the centre at its full width — beside the start and the end's minimum — is
/// <see cref="IsCompact"/>, the <c>:compact</c> pseudo-class, which styles answer by drawing the centre
/// smaller (the commands without their names). It is decided on the full width the centre last had, so
/// a centre made smaller by it does not undo it.
/// </item>
/// </list>
/// </remarks>
public sealed class CommandBarPanel : Panel
{
    /// <summary>
    /// Defines the <see cref="Spacing"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<CommandBarPanel, double>(nameof(Spacing), 10);

    private double _endNaturalWidth;
    private double _centreFullWidth;

    static CommandBarPanel() => AffectsMeasure<CommandBarPanel>(SpacingProperty);

    /// <summary>
    /// Gets or sets the least room left between two slots that both have something in them.
    /// </summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the bar is too narrow for the centre at its full width, which
    /// styles see as the <c>:compact</c> pseudo-class.
    /// </summary>
    public bool IsCompact { get; private set; }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        Size unbounded = new(double.PositiveInfinity, availableSize.Height);

        Control? start = Slot(0);
        Control? centre = Slot(1);
        Control? end = Slot(2);

        start?.Measure(unbounded);
        centre?.Measure(unbounded);
        end?.Measure(unbounded);
        _endNaturalWidth = end?.DesiredSize.Width ?? 0;

        for (int index = 3; index < Children.Count; index++)
        {
            Children[index].Measure(default);
        }

        double startWidth = start?.DesiredSize.Width ?? 0;

        if (!IsCompact)
        {
            _centreFullWidth = centre?.DesiredSize.Width ?? 0;
        }

        bool compact = !double.IsInfinity(availableSize.Width)
            && startWidth + GapAfter(startWidth) + _centreFullWidth + GapBefore(end)
                + Math.Min(MinWidthOf(end), MaxWidthOf(end)) > availableSize.Width;

        if (compact != IsCompact)
        {
            IsCompact = compact;
            PseudoClasses.Set(":compact", compact);

            // The styles have just shown or hidden things deep in the centre, inside controls whose
            // measure is still marked valid — and a control shown again before it was ever measured
            // hidden asks nobody to measure it. The centre is measured afresh, all the way down.
            if (centre is not null)
            {
                foreach (Visual descendant in centre.GetVisualDescendants())
                {
                    (descendant as Layoutable)?.InvalidateMeasure();
                }

                centre.InvalidateMeasure();
                centre.Measure(unbounded);
            }
        }

        double centreWidth = centre?.DesiredSize.Width ?? 0;
        double endWidth = _endNaturalWidth;

        if (!double.IsInfinity(availableSize.Width))
        {
            (_, endWidth) = Place(availableSize.Width, startWidth, centreWidth, end);
            end?.Measure(new Size(Math.Max(endWidth, MinWidthOf(end)), availableSize.Height));
        }

        double height = Math.Max(
            start?.DesiredSize.Height ?? 0,
            Math.Max(centre?.DesiredSize.Height ?? 0, end?.DesiredSize.Height ?? 0));

        return new Size(startWidth + GapAfter(startWidth) + centreWidth + GapBefore(end) + endWidth, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        Control? start = Slot(0);
        Control? centre = Slot(1);
        Control? end = Slot(2);

        double startWidth = start?.DesiredSize.Width ?? 0;
        double centreWidth = centre?.DesiredSize.Width ?? 0;
        (double centreLeft, double endWidth) = Place(finalSize.Width, startWidth, centreWidth, end);

        start?.Arrange(new Rect(0, 0, startWidth, finalSize.Height));
        centre?.Arrange(new Rect(centreLeft, 0, centreWidth, finalSize.Height));

        // Anchored at its left: an end held at its minimum overflows the bar's edge, not the commands.
        end?.Arrange(new Rect(finalSize.Width - endWidth, 0, Math.Max(endWidth, MinWidthOf(end)), finalSize.Height));

        for (int index = 3; index < Children.Count; index++)
        {
            Children[index].Arrange(default);
        }

        return finalSize;
    }

    /// <summary>
    /// Where the centre starts and how wide the end is, on a bar <paramref name="width"/> wide.
    /// </summary>
    private (double CentreLeft, double EndWidth) Place(double width, double startWidth, double centreWidth, Control? end)
    {
        double endMax = MaxWidthOf(end);
        double endMin = Math.Min(MinWidthOf(end), endMax);
        double afterStart = GapAfter(startWidth);
        double beforeEnd = GapBefore(end);

        // What the start and the centre leave for the end, and the least of it the end is kept.
        double room = Math.Max(0, width - startWidth - afterStart - centreWidth - beforeEnd);
        double endFloor = Math.Min(endMin, room);

        double lowest = startWidth + afterStart;
        double highest = width - endFloor - beforeEnd - centreWidth;
        double centreLeft = Math.Max(lowest, Math.Min((width - centreWidth) / 2, highest));

        double endWidth = Math.Clamp(width - centreLeft - centreWidth - beforeEnd, 0, endMax);

        return (centreLeft, endWidth);
    }

    private Control? Slot(int index) => index < Children.Count ? Children[index] : null;

    private double GapAfter(double startWidth) => startWidth > 0 ? Spacing : 0;

    private double GapBefore(Control? end) => MaxWidthOf(end) > 0 ? Spacing : 0;

    private static double MinWidthOf(Control? end) => end is { IsVisible: true } ? end.MinWidth : 0;

    /// <summary>
    /// The widest the end is drawn: its <see cref="Layoutable.MaxWidth"/>, or what it asks for when it
    /// has none. An end that is not shown takes nothing.
    /// </summary>
    private double MaxWidthOf(Control? end)
        => end is not { IsVisible: true } ? 0
            : double.IsInfinity(end.MaxWidth) ? _endNaturalWidth
            : end.MaxWidth;
}
