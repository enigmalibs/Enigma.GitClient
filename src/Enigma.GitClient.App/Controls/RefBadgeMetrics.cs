using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Media;

namespace Enigma.GitClient.App.Controls;

/// <summary>
/// How wide the strip of <see cref="RefBadge"/>s on a history row is.
/// </summary>
/// <remarks>
/// <para>
/// The history draws its badges in a column of their own, and a column is one width for the whole
/// page — an <c>Auto</c> column is measured per row, which is precisely what puts the subject, the
/// author and the sha at a different horizontal position on every line that carries a badge. To
/// pick that one width, something has to know how wide a badge actually is, and a badge's width is
/// its label in the badge's own face plus the chrome the control template draws around it.
/// </para>
/// <para>
/// The constants below mirror <c>Themes/Controls.axaml</c>'s <see cref="RefBadge"/> template: a
/// 5 px horizontal padding on each side, an 11 px icon, and a 4 px gap between the icon and the
/// label. They are checked by a test rather than bound, because a per-badge binding to a theme
/// resource would measure thousands of rows through the resource system to answer one number that
/// only changes when the template does.
/// </para>
/// <para>
/// Measured through <see cref="FormattedText"/> and cached per string, like
/// <c>Diff.DiffTypography</c> — and like it, a face the font manager cannot realise falls back to
/// an estimate instead of throwing, so a ViewModel built before there is an Avalonia platform (a
/// plain unit test, for instance) still gets an answer.
/// </para>
/// </remarks>
public static class RefBadgeMetrics
{
    /// <summary>The label's font size, as the badge template draws it.</summary>
    public const double FontSize = 11;

    /// <summary>The icon's size, as the badge template draws it.</summary>
    public const double IconSize = 11;

    /// <summary>The gap between the icon and the label inside a badge.</summary>
    public const double IconSpacing = 4;

    /// <summary>The padding on each side of a badge's content.</summary>
    public const double HorizontalPadding = 5;

    /// <summary>The gap between two badges on the same row.</summary>
    public const double BadgeSpacing = 4;

    /// <summary>
    /// The widest a single badge's label may be drawn before it is ellipsised, which is
    /// <see cref="RefBadge.MaximumTextWidth"/>'s default.
    /// </summary>
    public const double MaximumLabelWidth = 180;

    /// <summary>
    /// What a character is taken to be worth when the face cannot be measured at all.
    /// </summary>
    private const double EstimatedCharacterWidth = FontSize * 0.55;

    private static readonly ConcurrentDictionary<string, double> Labels = new();

    /// <summary>
    /// Measures the strip of badges a row draws.
    /// </summary>
    /// <param name="badges">The row's badges, in the order they are drawn.</param>
    /// <returns>The width the strip occupies, or 0 when the row has no badge.</returns>
    public static double Measure(IReadOnlyList<ViewModels.Pages.RefBadgeItem>? badges)
    {
        if (badges is null || badges.Count == 0)
        {
            return 0;
        }

        double width = 0;

        foreach (ViewModels.Pages.RefBadgeItem badge in badges)
        {
            width += MeasureBadge(badge.Name);
        }

        return width + (BadgeSpacing * (badges.Count - 1));
    }

    /// <summary>
    /// Measures one badge.
    /// </summary>
    /// <param name="label">The reference's short name.</param>
    /// <returns>The badge's width.</returns>
    public static double MeasureBadge(string? label)
        => (HorizontalPadding * 2)
            + IconSize
            + IconSpacing
            + Math.Min(MaximumLabelWidth, MeasureLabel(label ?? string.Empty));

    private static double MeasureLabel(string label)
    {
        if (label.Length == 0)
        {
            return 0;
        }

        return Labels.GetOrAdd(label, static text =>
        {
            try
            {
                return new FormattedText(
                    text,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    FontSize,
                    Brushes.Black).Width;
            }
            catch (InvalidOperationException)
            {
                // No font manager, or a face it lists but cannot realise. A column width is not
                // worth taking a page down for.
                return text.Length * EstimatedCharacterWidth;
            }
        });
    }
}
