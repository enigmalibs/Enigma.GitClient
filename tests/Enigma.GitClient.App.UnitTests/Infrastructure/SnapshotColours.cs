using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Enigma.GitClient.App.UnitTests.Infrastructure;

/// <summary>
/// Counts how many distinct colours a captured frame holds.
/// </summary>
/// <remarks>
/// This is the assertion that separates "the control drew" from "the control laid out and drew
/// nothing": a frame of one flat colour — all black, all white, all transparent — is what a control
/// with a missing theme, a transparent background or a template that never applied looks like, and
/// it passes every other kind of check. Colours are quantised to four bits per channel so that
/// anti-aliasing does not inflate the count.
/// </remarks>
public static class SnapshotColours
{
    /// <summary>
    /// Counts the distinct quantised colours in a saved PNG.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns>How many distinct colours it holds.</returns>
    public static int Count(string path)
    {
        using FileStream stream = File.OpenRead(path);

        return Count(stream);
    }

    /// <summary>
    /// Counts the distinct quantised colours in an encoded image.
    /// </summary>
    /// <param name="source">The stream to read, positioned at the start of the image.</param>
    /// <returns>How many distinct colours it holds.</returns>
    /// <remarks>
    /// The same question as <see cref="Count(string)"/> for a frame that was never written to disk —
    /// one control rendered on its own, rather than a whole window snapshotted for review.
    /// </remarks>
    public static int Count(Stream source)
    {
        using WriteableBitmap writeable = WriteableBitmap.Decode(source);
        using ILockedFramebuffer buffer = writeable.Lock();

        HashSet<int> colours = [];

        unsafe
        {
            byte* pixels = (byte*)buffer.Address;

            for (int y = 0; y < buffer.Size.Height; y++)
            {
                byte* row = pixels + (y * buffer.RowBytes);

                for (int x = 0; x < buffer.Size.Width; x++)
                {
                    colours.Add(((row[(x * 4) + 2] >> 4) << 8) | ((row[(x * 4) + 1] >> 4) << 4) | (row[x * 4] >> 4));
                }
            }
        }

        return colours.Count;
    }
}
