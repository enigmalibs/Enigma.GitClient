using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Enigma.GitClient.Desktop.Views.Pages;

namespace Enigma.GitClient.Desktop.Views;

/// <summary>
/// The scrolling a drag held near the top or bottom edge of a list asks for: it starts when the
/// pointer enters the band along an edge, follows how deep into the band it is, and stops when the
/// pointer leaves the band, the drag ends, or there is nothing left to scroll that way.
/// </summary>
/// <remarks>
/// <para>
/// A timer, because the pointer stops reporting while it is held still, and an item held at the
/// bottom of a long list is exactly the gesture that has to keep scrolling. How fast is
/// <see cref="BranchDragGesture.ScrollFor"/>'s to decide.
/// </para>
/// <para>
/// Every list that is dragged in shares it: the views keep only what is theirs, what is dragged and
/// where it may drop.
/// </para>
/// </remarks>
internal sealed class EdgeAutoScroll
{
    /// <summary>
    /// How often the list moves while the pointer is held in a band.
    /// </summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(30);

    private readonly Func<ScrollViewer?> _scroller;
    private readonly Action? _afterStep;
    private readonly DispatcherTimer _timer;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="scroller">
    /// Finds the list's scroll viewer, or <see langword="null"/> while it has none: a list finds its
    /// own in its template, which it may not have applied yet.
    /// </param>
    /// <param name="afterStep">
    /// Called after each step that moved the list, once its layout is up to date: for a view that marks
    /// what is under the pointer as the items go by.
    /// </param>
    public EdgeAutoScroll(Func<ScrollViewer?> scroller, Action? afterStep = null)
    {
        ArgumentNullException.ThrowIfNull(scroller);

        _scroller = scroller;
        _afterStep = afterStep;
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => ScrollOnce();
    }

    /// <summary>
    /// Gets how many pixels each step moves the list: negative towards the top, positive towards the
    /// bottom, zero when it is not scrolling.
    /// </summary>
    public double Step { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the list is being scrolled.
    /// </summary>
    public bool IsScrolling => _timer.IsEnabled;

    /// <summary>
    /// Starts, steers or stops the scrolling for where the pointer now is, measured against the list's
    /// scroll viewer.
    /// </summary>
    /// <param name="e">The pointer's latest move.</param>
    public void Follow(PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_scroller() is not { } scroll)
        {
            Stop();
            return;
        }

        Follow(e.GetPosition(scroll).Y, scroll.Viewport.Height);
    }

    /// <summary>
    /// Starts, steers or stops the scrolling for where the pointer now is.
    /// </summary>
    /// <param name="y">Where the pointer is, from the top of the area the bands are measured in.</param>
    /// <param name="height">How tall that area is.</param>
    public void Follow(double y, double height)
    {
        Step = BranchDragGesture.ScrollFor(y, height);

        if (Step == 0)
        {
            Stop();
            return;
        }

        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    /// <summary>
    /// Stops the scrolling: the pointer left the band, or the drag is over.
    /// </summary>
    public void Stop()
    {
        Step = 0;
        _timer.Stop();
    }

    /// <summary>
    /// Moves the list by one step, no further than its ends. What the timer does on each tick.
    /// </summary>
    internal void ScrollOnce()
    {
        if (_scroller() is not { } scroll)
        {
            Stop();
            return;
        }

        double furthest = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        double moved = Math.Clamp(scroll.Offset.Y + Step, 0, furthest);

        if (moved == scroll.Offset.Y)
        {
            return;
        }

        scroll.Offset = new Vector(scroll.Offset.X, moved);

        if (_afterStep is { } afterStep)
        {
            scroll.UpdateLayout();
            afterStep();
        }
    }
}
