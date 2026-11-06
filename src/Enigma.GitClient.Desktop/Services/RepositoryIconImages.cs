using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Enigma.Icons;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// What a repository's tile draws for its icon: a picture, or a glyph drawn like the application's own.
/// Exactly one of the two is set.
/// </summary>
/// <param name="Image">A raster image, or an SVG that names its own colours, drawn as it is.</param>
/// <param name="Glyph">
/// An SVG that names no colour of its own — the usual shape of an icon meant to take the colour of the
/// text around it — as a one-icon set named <see cref="RepositoryIconImages.GlyphName"/>, drawn in the
/// theme's foreground like every glyph of the application, so it reads in the dark theme and the light.
/// </param>
public sealed record RepositoryIconArt(IImage? Image, IIconSet? Glyph);

/// <summary>
/// Reads repository icons — the stored copies <see cref="IRepositoryIconStore"/> keeps — into what a
/// tile can draw, and remembers each until its file changes.
/// </summary>
/// <remarks>
/// <para>
/// Pictures go through the desktop's own decoder (Skia): PNG, JPEG, BMP, GIF (its first frame), ICO
/// and WebP. They are decoded 48 pixels wide, twice the tile's 24, so they stay sharp on a high-density
/// screen and a large picture costs no more memory than a small one.
/// </para>
/// <para>
/// SVG goes through the icon library's parser (<see cref="SvgIconParser"/>), which takes paths, the
/// basic shapes, groups, transforms, fills, strokes and opacity, and refuses external entities. It does
/// not draw gradients, text, <c>&lt;use&gt;</c>, clip paths or embedded pictures.
/// </para>
/// <para>
/// Static, as the icon library's own glyph cache is: an image is immutable, every tile showing the same
/// file can share it, and a value converter in markup has no container to be given one by.
/// </para>
/// </remarks>
public static class RepositoryIconImages
{
    /// <summary>
    /// The width pictures are decoded at, in pixels.
    /// </summary>
    public const int DecodeWidth = 48;

    /// <summary>
    /// The name of the one icon in a <see cref="RepositoryIconArt.Glyph"/> set.
    /// </summary>
    public const string GlyphName = "icon";

    private static readonly ConcurrentDictionary<string, CachedArt> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets a converter from a stored icon's path to the picture to draw, or <see langword="null"/>
    /// when there is none, it is a glyph, or it cannot be read.
    /// </summary>
    public static IValueConverter ImageOf { get; } =
        new FuncValueConverter<string?, IImage?>(file => Load(file)?.Image);

    /// <summary>
    /// Gets a converter from a stored icon's path to the glyph set to draw, or <see langword="null"/>
    /// when there is none, it is a picture, or it cannot be read.
    /// </summary>
    public static IValueConverter GlyphOf { get; } =
        new FuncValueConverter<string?, IIconSet?>(file => Load(file)?.Glyph);

    /// <summary>
    /// Reads a stored icon, from the cache while its file is unchanged.
    /// </summary>
    /// <param name="file">The icon's path; <see langword="null"/> or empty for none.</param>
    /// <returns>What to draw, or <see langword="null"/> when the file is gone or cannot be read.</returns>
    public static RepositoryIconArt? Load(string? file)
    {
        if (string.IsNullOrEmpty(file))
        {
            return null;
        }

        FileInfo info = new(file);

        if (!info.Exists)
        {
            Cache.TryRemove(file, out _);
            return null;
        }

        if (Cache.TryGetValue(file, out CachedArt? cached)
            && cached.Stamp == info.LastWriteTimeUtc
            && cached.Length == info.Length)
        {
            return cached.Art;
        }

        RepositoryIconArt? art;

        try
        {
            art = Decode(File.ReadAllBytes(file), info.Extension);
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            art = null;
        }

        Cache[file] = new CachedArt(info.LastWriteTimeUtc, info.Length, art);

        return art;
    }

    /// <summary>
    /// Whether a file's content reads as an icon of its kind.
    /// </summary>
    /// <param name="bytes">The file's content.</param>
    /// <param name="extension">The file's extension, which says how to read it.</param>
    /// <param name="failure">Why it does not, when it does not.</param>
    /// <returns><see langword="true"/> when the tile could draw it.</returns>
    public static bool CanDecode(byte[] bytes, string extension, out Exception? failure)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            RepositoryIconArt art = Decode(bytes, extension);

            if (art.Image is IDisposable disposable)
            {
                disposable.Dispose();
            }

            failure = null;
            return true;
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            failure = exception;
            return false;
        }
    }

    /// <summary>
    /// Reads an icon's content.
    /// </summary>
    /// <param name="bytes">The file's content.</param>
    /// <param name="extension">The file's extension: <c>.svg</c>, or a picture's.</param>
    /// <returns>What to draw.</returns>
    public static RepositoryIconArt Decode(byte[] bytes, string extension)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeSvg(bytes);
        }

        using MemoryStream stream = new(bytes, writable: false);

        return new RepositoryIconArt(DecodePicture(stream), null);
    }

    private static Bitmap DecodePicture(Stream stream)
    {
        try
        {
            return Bitmap.DecodeToWidth(stream, DecodeWidth, BitmapInterpolationMode.HighQuality);
        }
        catch (NullReferenceException exception)
        {
            // Skia finds no codec for data it cannot read, and Avalonia dereferences the missing one:
            // that is the decoder's way of saying "not a picture".
            throw new InvalidDataException("The data is not a picture the desktop's decoder reads.", exception);
        }
    }

    private static RepositoryIconArt DecodeSvg(byte[] bytes)
    {
        IconGlyph glyph;

        using (MemoryStream stream = new(bytes, writable: false))
        {
            glyph = SvgIconParser.Parse(stream);
        }

        if (!glyph.Layers.Any(NamesItsOwnColour))
        {
            string svg = System.Text.Encoding.UTF8.GetString(bytes);

            return new RepositoryIconArt(null, SvgIconSet.FromSvgSources([new KeyValuePair<string, string>(GlyphName, svg)]));
        }

        DrawingGroup drawing = new();

        foreach (IconLayer layer in glyph.Layers)
        {
            drawing.Children.Add(LayerDrawing(layer));
        }

        return new RepositoryIconArt(
            new DrawingImage(drawing)
            {
                Viewbox = new Rect(glyph.ViewBox.X, glyph.ViewBox.Y, glyph.ViewBox.Width, glyph.ViewBox.Height),
            },
            null);
    }

    /// <summary>
    /// A layer drawn in its own colours. One the file leaves uncoloured is black, as SVG's default
    /// fill is; a paint the client cannot read (a gradient) is black too.
    /// </summary>
    private static Drawing LayerDrawing(IconLayer layer)
    {
        string data = layer.FillRule == IconFillRule.EvenOdd ? "F0 " + layer.PathData : "F1 " + layer.PathData;

        GeometryDrawing shape = new()
        {
            Geometry = Geometry.Parse(data),
            Brush = layer.IsFilled ? BrushOf(layer.Fill) : null,
            Pen = layer.IsStroked
                ? new Pen(
                    BrushOf(layer.Stroke),
                    layer.StrokeWidth ?? 1,
                    lineCap: layer.StrokeLineCap switch
                    {
                        IconLineCap.Round => PenLineCap.Round,
                        IconLineCap.Square => PenLineCap.Square,
                        _ => PenLineCap.Flat,
                    },
                    lineJoin: layer.StrokeLineJoin switch
                    {
                        IconLineJoin.Round => PenLineJoin.Round,
                        IconLineJoin.Bevel => PenLineJoin.Bevel,
                        _ => PenLineJoin.Miter,
                    })
                : null,
        };

        return layer.Opacity >= 1
            ? shape
            : new DrawingGroup { Opacity = layer.Opacity, Children = { shape } };
    }

    private static bool NamesItsOwnColour(IconLayer layer)
        => (layer.IsFilled && TryParseColour(layer.Fill, out _))
            || (layer.IsStroked && TryParseColour(layer.Stroke, out _));

    private static IBrush BrushOf(string? paint)
        => TryParseColour(paint, out Color colour) ? new ImmutableSolidColorBrush(colour) : Brushes.Black;

    private static bool TryParseColour([NotNullWhen(true)] string? paint, out Color colour)
    {
        colour = default;

        return !string.IsNullOrWhiteSpace(paint) && Color.TryParse(paint.Trim(), out colour);
    }

    private static bool IsUnreadable(Exception exception)
        => exception is IOException or UnauthorizedAccessException or InvalidDataException
            or FormatException or ArgumentException or InvalidOperationException
            or NotSupportedException or SvgParseException;

    private sealed record CachedArt(DateTime Stamp, long Length, RepositoryIconArt? Art);
}
