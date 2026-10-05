using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace Enigma.GitClient.Desktop.Controls.Diff;

/// <summary>
/// Keeps the two editors of the side-by-side rendering scrolled together, down and sideways.
/// </summary>
/// <remarks>
/// <para>
/// Down, the two documents have the same lines — the fillers see to it — and the same height, so
/// one offset is the same rows on both sides.
/// </para>
/// <para>
/// Sideways, the two sides have different longest lines, so one of them can go further than the
/// other. The link remembers where the reader last asked to be and puts each side there as far as
/// that side can go. A side that stopped short of it is not taken for a side the reader moved, which
/// is what would otherwise drag the longer side back to the shorter one's end the moment it was
/// scrolled past it.
/// </para>
/// <para>
/// A scroll viewer reports a change after the layout pass that made it, not inside the setter, so a
/// flag around the setter could not tell the link's own moves from the reader's. Comparing with where
/// the link would have put the side can.
/// </para>
/// </remarks>
public sealed class DiffScrollLink
{
    /// <summary>
    /// How far apart two offsets can be and still be the same place. Layout rounds an offset to a
    /// whole device pixel, so one set at 1423.5 comes back as 1423 — and taking that half pixel for a
    /// move of the reader's would drag the other side back with it. A device pixel is at most one
    /// unit at any scaling of 100% or more.
    /// </summary>
    private const double Tolerance = 1;

    private readonly DiffTextEditor _first;
    private readonly DiffTextEditor _second;
    private readonly HashSet<ScrollViewer> _hooked = [];

    private Vector _wanted;

    /// <summary>
    /// Initialises a link between two editors.
    /// </summary>
    /// <param name="first">One side.</param>
    /// <param name="second">The other side.</param>
    public DiffScrollLink(DiffTextEditor first, DiffTextEditor second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        _first = first;
        _second = second;

        Hook(first);
        Hook(second);
    }

    /// <summary>Gets where the reader last asked the two sides to be.</summary>
    public Vector Wanted => _wanted;

    /// <summary>
    /// Works out where a side can be when it is asked to be somewhere: as near as its own extent lets
    /// it.
    /// </summary>
    /// <param name="wanted">Where it is asked to be.</param>
    /// <param name="extent">How large its content is.</param>
    /// <param name="viewport">How much of it is on screen.</param>
    /// <returns>The offset, clamped to what the side can show.</returns>
    public static Vector Reachable(Vector wanted, Size extent, Size viewport)
        => new(
            Math.Clamp(wanted.X, 0, Math.Max(0, extent.Width - viewport.Width)),
            Math.Clamp(wanted.Y, 0, Math.Max(0, extent.Height - viewport.Height)));

    private void Hook(DiffTextEditor editor)
    {
        Attach(editor);

        editor.TemplateApplied += (_, _) => Attach(editor);
    }

    private void Attach(DiffTextEditor editor)
    {
        if (editor.ScrollHost is { } scroll && _hooked.Add(scroll))
        {
            scroll.ScrollChanged += (_, _) => Follow(editor);
        }
    }

    private void Follow(DiffTextEditor source)
    {
        DiffTextEditor target = ReferenceEquals(source, _first) ? _second : _first;

        if (source.ScrollHost is not { } from || target.ScrollHost is not { } to)
        {
            return;
        }

        // The side is where the link put it — or as near as it could get. That is the link's own
        // move coming back, not the reader's.
        if (Near(from.Offset, Reachable(_wanted, from.Extent, from.Viewport)))
        {
            return;
        }

        _wanted = from.Offset;

        Vector next = Reachable(_wanted, to.Extent, to.Viewport);

        if (!Near(to.Offset, next))
        {
            to.Offset = next;
        }
    }

    private static bool Near(Vector a, Vector b)
        => Math.Abs(a.X - b.X) <= Tolerance && Math.Abs(a.Y - b.Y) <= Tolerance;
}
