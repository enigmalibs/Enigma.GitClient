using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using AvaloniaEdit.Rendering;

namespace Enigma.GitClient.Desktop.Controls.Diff;

/// <summary>
/// The text view of a <see cref="DiffTextEditor"/>: AvaloniaEdit's own, except that it never asks to
/// be scrolled further than its scroll viewer can take it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TextView.MakeVisible"/> sets the offset that shows a whole rectangle, clamped at zero but
/// not at the far end. The caret is brought into view with a 5-pixel margin, and at the end of a diff
/// that margin is out of reach: nothing scrolls below the last line, and the text is only 3 pixels
/// wider than its widest line. The view then holds an offset its scroll viewer does not allow.
/// </para>
/// <para>
/// While a selection is being dragged, that is a fight that never ends. Every time the scroll viewer
/// coerces the offset back, the drag handler extends the selection again, which brings the caret into
/// view again, which asks for the unreachable offset again. In a window the viewer coerces on every
/// layout, so a selection dragged to the bottom-right of a diff froze the application, rebuilding its
/// visual lines until memory ran out (BUG-7823).
/// </para>
/// <para>
/// This view moves the rectangle inside what can be scrolled to before the base shows it, so it only
/// ever asks for an offset the viewer can hold. The caret still ends up on screen — it is inside the
/// extent — and only the part of the margin that lies past the end of the text is given up.
/// </para>
/// </remarks>
public sealed class DiffTextView : TextView
{
    /// <summary>
    /// Moves a rectangle inside the area a view can be scrolled to show.
    /// </summary>
    /// <param name="rectangle">The rectangle to bring into view, in document coordinates.</param>
    /// <param name="extent">How large the content is.</param>
    /// <param name="viewport">How much of it is on screen.</param>
    /// <returns>
    /// The rectangle, shifted — and, where it is larger than the area, cut — so it lies within the
    /// extent, or within the viewport when the content is smaller than that.
    /// </returns>
    public static Rect Reachable(Rect rectangle, Size extent, Size viewport)
    {
        double width = Math.Max(extent.Width, viewport.Width);
        double height = Math.Max(extent.Height, viewport.Height);

        double clippedWidth = Math.Min(rectangle.Width, width);
        double clippedHeight = Math.Min(rectangle.Height, height);

        return new Rect(
            Math.Clamp(rectangle.X, 0, width - clippedWidth),
            Math.Clamp(rectangle.Y, 0, height - clippedHeight),
            clippedWidth,
            clippedHeight);
    }

    /// <inheritdoc />
    public override void MakeVisible(Rect rectangle)
    {
        IScrollable scroll = this;
        Size extent = scroll.Extent;

        // Before the first measure there is no extent to keep inside; the base's own clamp at zero is
        // all there is to go on.
        if (extent.Width <= 0 && extent.Height <= 0)
        {
            base.MakeVisible(rectangle);
            return;
        }

        base.MakeVisible(Reachable(rectangle, extent, scroll.Viewport));
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextView);
}
