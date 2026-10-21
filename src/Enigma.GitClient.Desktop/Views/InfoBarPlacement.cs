using System;
using Avalonia;
using Enigma.Avalonia.Desktop.Controls.InfoBar;

namespace Enigma.GitClient.Desktop.Views;

/// <summary>
/// Where a window's info bar sits: in its bottom-left corner, right of the navigation rail, a quarter
/// of the window wide.
/// </summary>
/// <remarks>
/// <para>
/// The library draws the bar as a band across the top of the window, over the repository strip and
/// the page headers — what the reader is working with. Down in the corner it covers only the bottom
/// of a page for the moment it is shown, and starting past the rail it never covers the rail's own
/// footer, the profiles and the settings.
/// </para>
/// <para>
/// Sized from the window in code-behind, as the tool dialog is: a share of the width is not something
/// the markup can say, and wrapping the bar in a grid of star columns would stop it being the root
/// panel's last child, which is what keeps it above the dialogs.
/// </para>
/// </remarks>
internal static class InfoBarPlacement
{
    /// <summary>The share of the window's width the bar takes.</summary>
    internal const double WidthShare = 0.25;

    /// <summary>
    /// The narrowest the bar is drawn: a quarter of a small window would leave a card too narrow to
    /// read. The library's template keeps 24 pixels around the card inside this width.
    /// </summary>
    internal const double MinimumWidth = 360;

    /// <summary>
    /// The bar's width in a window: a quarter of it, at least <see cref="MinimumWidth"/>, and never more
    /// than the room right of the rail.
    /// </summary>
    /// <param name="windowWidth">The window's width.</param>
    /// <param name="railWidth">The navigation rail's width.</param>
    /// <returns>The bar's width, margins included.</returns>
    internal static double WidthFor(double windowWidth, double railWidth)
        => Math.Min(Math.Max(windowWidth * WidthShare, MinimumWidth), Math.Max(0, windowWidth - railWidth));

    /// <summary>
    /// Sizes the bar for the window and moves it past the rail. Its alignment — left, bottom — is set
    /// in the window's markup.
    /// </summary>
    /// <param name="bar">The window's info bar.</param>
    /// <param name="windowWidth">The window's width.</param>
    /// <param name="railWidth">The navigation rail's width.</param>
    internal static void Place(InfoBar bar, double windowWidth, double railWidth)
    {
        ArgumentNullException.ThrowIfNull(bar);

        bar.Width = WidthFor(windowWidth, railWidth);
        bar.Margin = new Thickness(railWidth, 0, 0, 0);
    }
}
