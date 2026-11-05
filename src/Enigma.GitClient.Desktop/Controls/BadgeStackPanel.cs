using System;
using Avalonia;
using Avalonia.Controls;

namespace Enigma.GitClient.Desktop.Controls;

/// <summary>
/// Stacks a history line's badges one under the other, the first one centred in the line's first band.
/// </summary>
/// <remarks>
/// <para>
/// The first band is <see cref="LineHeight"/> tall — the history's row height — and it is where the
/// rest of the line is drawn too: the commit's node, the subject, the author, the date and the sha. The
/// first badge is centred in it, so a line reads across at the same height whatever it carries; every
/// next badge goes under the one before, <see cref="Spacing"/> apart.
/// </para>
/// <para>
/// The panel asks for the badges' height plus, under the last one, the same room as above the first,
/// so a tall line is as balanced as a short one. With one badge that is exactly
/// <see cref="LineHeight"/>, even when the badge is taller than the band — it is clipped, as it was
/// before the badges were stacked — so a line with one badge, or none, never grows.
/// </para>
/// </remarks>
public sealed class BadgeStackPanel : Panel
{
    /// <summary>
    /// Defines the <see cref="LineHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double> LineHeightProperty =
        AvaloniaProperty.Register<BadgeStackPanel, double>(nameof(LineHeight));

    /// <summary>
    /// Defines the <see cref="Spacing"/> property.
    /// </summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<BadgeStackPanel, double>(nameof(Spacing));

    static BadgeStackPanel()
        => AffectsMeasure<BadgeStackPanel>(LineHeightProperty, SpacingProperty);

    /// <summary>
    /// Gets or sets the height of the line's first band, in which the first badge is centred.
    /// </summary>
    public double LineHeight
    {
        get => GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the gap between two badges.
    /// </summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Calculates the panel's height.
    /// </summary>
    /// <param name="count">How many badges are drawn.</param>
    /// <param name="firstHeight">The first badge's height.</param>
    /// <param name="totalHeight">Every badge's height, added up.</param>
    /// <param name="lineHeight">The height of the line's first band.</param>
    /// <param name="spacing">The gap between two badges.</param>
    /// <returns>The height the panel asks for: 0 with no badge, <paramref name="lineHeight"/> with one.</returns>
    public static double CalculateHeight(int count, double firstHeight, double totalHeight, double lineHeight, double spacing)
        => count switch
        {
            <= 0 => 0,
            1 => Math.Max(0, lineHeight),
            _ => (TopOf(firstHeight, lineHeight) * 2) + totalHeight + (spacing * (count - 1)),
        };

    /// <summary>
    /// Says where a badge of a given height starts so that it is centred in the band; never above the
    /// panel's top.
    /// </summary>
    /// <param name="height">The badge's height.</param>
    /// <param name="lineHeight">The height of the band.</param>
    /// <returns>The badge's top.</returns>
    public static double TopOf(double height, double lineHeight) => Math.Max(0, (lineHeight - height) / 2);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        Size unbounded = new(availableSize.Width, double.PositiveInfinity);

        int count = 0;
        double width = 0;
        double first = 0;
        double total = 0;

        foreach (Control child in Children)
        {
            child.Measure(unbounded);

            if (!child.IsVisible)
            {
                continue;
            }

            if (count == 0)
            {
                first = child.DesiredSize.Height;
            }

            count++;
            width = Math.Max(width, child.DesiredSize.Width);
            total += child.DesiredSize.Height;
        }

        return new Size(width, CalculateHeight(count, first, total, LineHeight, Spacing));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double? y = null;

        foreach (Control child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            double height = child.DesiredSize.Height;

            // The first badge is centred in the band, the others follow it.
            double top = y ?? TopOf(height, LineHeight);

            child.Arrange(new Rect(0, top, finalSize.Width, height));
            y = top + height + Spacing;
        }

        return finalSize;
    }
}
