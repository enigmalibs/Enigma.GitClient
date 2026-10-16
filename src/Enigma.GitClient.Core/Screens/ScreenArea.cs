using System;
using System.Text.Json.Serialization;

namespace Enigma.GitClient.Core.Screens;

/// <summary>
/// A rectangle on the desktop, in physical pixels: a screen's bounds or working area, or a window's
/// frame.
/// </summary>
/// <param name="X">The left edge; negative for a screen left of the primary one.</param>
/// <param name="Y">The top edge; negative for a screen above the primary one.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct ScreenArea(int X, int Y, int Width, int Height)
{
    /// <summary>Gets the edge just past the right of the rectangle.</summary>
    [JsonIgnore]
    public long Right => (long)X + Width;

    /// <summary>Gets the edge just past the bottom of the rectangle.</summary>
    [JsonIgnore]
    public long Bottom => (long)Y + Height;

    /// <summary>Gets a value indicating whether the rectangle covers anything at all.</summary>
    [JsonIgnore]
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Answers whether a point is inside the rectangle: its left and top edges included, its right and
    /// bottom ones not, so two screens side by side never both hold the same point.
    /// </summary>
    /// <param name="x">The point's left coordinate.</param>
    /// <param name="y">The point's top coordinate.</param>
    /// <returns><see langword="true"/> when the point is inside.</returns>
    public bool Contains(long x, long y) => !IsEmpty && x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>
    /// Measures how much of the rectangle another one covers.
    /// </summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>The area the two share, in square pixels; 0 when they do not meet.</returns>
    public long OverlapWith(ScreenArea other)
    {
        long width = Math.Min(Right, other.Right) - Math.Max(X, other.X);
        long height = Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y);

        return width <= 0 || height <= 0 ? 0 : width * height;
    }
}
