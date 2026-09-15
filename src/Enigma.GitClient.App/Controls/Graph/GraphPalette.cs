using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Enigma.GitClient.App.Controls.Graph;

/// <summary>
/// Resolves a lane's palette index to a brush from the current theme.
/// </summary>
/// <remarks>
/// The layout engine in Core deals only in palette indices, so the theme owns what a lane actually
/// looks like. Resolution is cached per theme variant: a graph row redraws often, and a resource
/// lookup per lane per frame is waste, while a cache that ignores the variant would freeze the
/// colours at the first render.
/// </remarks>
public sealed class GraphPalette
{
    /// <summary>
    /// How many lane colours the theme provides. The layout engine is told this number so it can
    /// avoid repeating a colour until it genuinely has to.
    /// </summary>
    public const int Size = 10;

    private static readonly IBrush Fallback = Brushes.Gray;

    private IBrush[]? _cached;
    private ThemeVariant? _cachedVariant;

    /// <summary>
    /// Gets the brush for a palette index, resolving the theme's resources on first use.
    /// </summary>
    /// <param name="control">The control whose resource scope and theme variant are used.</param>
    /// <param name="colour">The palette index; out-of-range values wrap.</param>
    /// <returns>The lane's brush.</returns>
    public IBrush Get(Control control, int colour)
    {
        ArgumentNullException.ThrowIfNull(control);

        IReadOnlyList<IBrush> brushes = Resolve(control);
        int index = ((colour % brushes.Count) + brushes.Count) % brushes.Count;

        return brushes[index];
    }

    /// <summary>
    /// Gets the whole palette for the control's current theme variant.
    /// </summary>
    /// <param name="control">The control whose resource scope and theme variant are used.</param>
    /// <returns>The lane brushes, in palette order.</returns>
    public IReadOnlyList<IBrush> Resolve(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        ThemeVariant variant = control.ActualThemeVariant;

        if (_cached is not null && Equals(_cachedVariant, variant))
        {
            return _cached;
        }

        IBrush[] brushes = new IBrush[Size];

        for (int index = 0; index < Size; index++)
        {
            brushes[index] = Find(control, $"GraphLane{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}Brush", variant)
                ?? Fallback;
        }

        _cached = brushes;
        _cachedVariant = variant;

        return brushes;
    }

    /// <summary>
    /// Invalidates the cache, so the next resolution re-reads the theme.
    /// </summary>
    public void Invalidate()
    {
        _cached = null;
        _cachedVariant = null;
    }

    /// <summary>
    /// Looks a brush up in a control's resource scope.
    /// </summary>
    /// <param name="control">The control to search from.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="variant">The theme variant to resolve under.</param>
    /// <returns>The brush, or <see langword="null"/> when the key is not there.</returns>
    public static IBrush? Find(Control control, string key, ThemeVariant? variant = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(key);

        return control.TryFindResource(key, variant ?? control.ActualThemeVariant, out object? value) && value is IBrush brush
            ? brush
            : null;
    }
}
