using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using AvaloniaEdit.Rendering;

namespace Enigma.GitClient.Desktop.Controls.Diff;

/// <summary>
/// The text view of a <see cref="DiffTextEditor"/>: AvaloniaEdit's own, except that it never asks to
/// be scrolled further than its layout lets it go.
/// </summary>
/// <remarks>
/// <para>
/// Two passes of AvaloniaEdit's layout limit how far a view can be scrolled, and they disagree. Its
/// scroll viewer clamps the offset to the viewport the view was <em>measured</em> with, and the view's
/// own arrange pass clamps it again to the size it was <em>arranged</em> at. In a window the two differ
/// by a rounding. Meanwhile <see cref="TextView.MakeVisible"/> clamps at zero only, and the caret is
/// brought into view with a 5-pixel margin that, at the end of a diff, is past every limit — nothing
/// scrolls below the last line.
/// </para>
/// <para>
/// While a selection is being dragged, that is a fight that never ends (BUG-7823).
/// </para>
/// <list type="number">
/// <item>The arrange pass takes the offset back to its own limit.</item>
/// <item>The drag handler hears the offset change and extends the selection.</item>
/// <item>The caret, now a few pixels out of view, is brought back into view.</item>
/// <item>The offset goes past the limit again, and the next layout pass takes it back again.</item>
/// </list>
/// <para>
/// That is a layout pass per frame for as long as the button is held, which froze the application and
/// rebuilt its visual lines until the memory ran out.
/// </para>
/// <para>
/// This view works the offset out itself (<see cref="OffsetToShow"/>): the base view's rule, clamped
/// to the nearer of the two limits, and nothing at all when that is within a pixel of where the view
/// already is. Nothing is left for either pass to take back, so the drag's next offset change never
/// comes. The caret still ends up on screen; only the part of the margin past the end of the text is
/// given up.
/// </para>
/// </remarks>
public sealed class DiffTextView : TextView
{
    /// <summary>
    /// How close an offset has to be to where the view already is to count as there. A scroll viewer
    /// rounds an offset to a whole device pixel, so one asked for at half a pixel comes back a half
    /// pixel away — and taking that for a move to make would start the fight again.
    /// </summary>
    public const double Tolerance = 1;

    private Size _arranged;

    /// <summary>
    /// Works out the offset that shows a rectangle, the way <see cref="TextView.MakeVisible"/> does,
    /// but never further than every pass of the layout lets the view be scrolled.
    /// </summary>
    /// <param name="rectangle">The rectangle to show, in document coordinates.</param>
    /// <param name="offset">Where the view is scrolled to now.</param>
    /// <param name="measured">The viewport the view was measured with, which its scroll viewer clamps to.</param>
    /// <param name="arranged">The size the view was arranged at, which its arrange pass clamps to.</param>
    /// <param name="extent">How large the content is.</param>
    /// <returns>The offset.</returns>
    public static Vector OffsetToShow(Rect rectangle, Vector offset, Size measured, Size arranged, Size extent)
    {
        // What is on screen is the arranged size, once there is one.
        Size shown = arranged.Width > 0 && arranged.Height > 0 ? arranged : measured;

        return new Vector(
            Axis(rectangle.X, rectangle.Width, offset.X, shown.Width, extent.Width - Math.Max(shown.Width, measured.Width)),
            Axis(rectangle.Y, rectangle.Height, offset.Y, shown.Height, extent.Height - Math.Max(shown.Height, measured.Height)));
    }

    private static double Axis(double start, double length, double offset, double shown, double furthest)
    {
        double end = start + length;
        double target = offset;

        // The base view's own rule: a rectangle before the view is brought to its start — or, when it
        // is too long to fit either way, its middle is — and one after it is brought to its end.
        if (start < offset)
        {
            target = end > offset + shown ? start + (length / 2) : start;
        }
        else if (end > offset + shown)
        {
            target = end - shown;
        }

        return Math.Clamp(target, 0, Math.Max(0, furthest));
    }

    /// <inheritdoc />
    public override void MakeVisible(Rect rectangle)
    {
        IScrollable scroll = this;
        Size extent = scroll.Extent;

        // Before the first measure there is no extent to keep inside; the base's own rule is all there
        // is to go on.
        if (extent.Width <= 0 && extent.Height <= 0)
        {
            base.MakeVisible(rectangle);
            return;
        }

        Vector offset = ScrollOffset;
        Vector target = OffsetToShow(rectangle, offset, scroll.Viewport, _arranged, extent);

        if (Math.Abs(target.X - offset.X) <= Tolerance && Math.Abs(target.Y - offset.Y) <= Tolerance)
        {
            return;
        }

        // What the base does once it has its offset: set it, and tell the scroll viewer to read it.
        scroll.Offset = target;
        ((ILogicalScrollable)this).RaiseScrollInvalidated(EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Kept before the base runs: the base clamps the offset to this size, and the drag handler it
        // notifies brings the caret into view from inside that call.
        _arranged = finalSize;

        return base.ArrangeOverride(finalSize);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextView);
}
