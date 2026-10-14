using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The application icon as the assembly ships it: the six frames of <c>app.ico</c>, and the copy of
/// its largest frame the splash and the About dialog show.
/// </summary>
/// <remarks>
/// The frames are generated from <c>packaging/icon/*.svg</c> and copied by hand to three places, so
/// these pin what a regeneration has to keep: the sizes, the encoding, and the copies being copies.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class AppIconTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly HeadlessAvaloniaFixture _fixture;

    public AppIconTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void TheIcon_HoldsSixSquare32BitPngFramesFrom16To256()
    {
        _fixture.Run(() =>
        {
            byte[] icon = ReadAsset("app.ico");
            IReadOnlyList<IconFrame> frames = ReadFrames(icon);

            Assert.Equal(new[] { 16, 32, 48, 64, 128, 256 }, frames.Select(frame => frame.Size));

            foreach (IconFrame frame in frames)
            {
                Assert.Equal(32, frame.BitCount);
                Assert.True(frame.Png.AsSpan().StartsWith(PngSignature), $"the {frame.Size} frame is not a PNG");

                // The PNG's own header says the same size the directory does, and RGBA at 8 bits.
                Assert.Equal(frame.Size, BinaryPrimitives.ReadInt32BigEndian(frame.Png.AsSpan(16, 4)));
                Assert.Equal(frame.Size, BinaryPrimitives.ReadInt32BigEndian(frame.Png.AsSpan(20, 4)));
                Assert.Equal(8, frame.Png[24]);
                Assert.Equal(6, frame.Png[25]);
            }
        });
    }

    [Fact]
    public void TheSplashImage_IsTheIconsLargestFrameByteForByte()
    {
        _fixture.Run(() =>
        {
            byte[] image = ReadAsset("app-256.png");
            IconFrame largest = ReadFrames(ReadAsset("app.ico"))[^1];

            Assert.Equal(largest.Png, image);

            using MemoryStream stream = new(image);
            using Bitmap bitmap = new(stream);
            Assert.Equal(new PixelSize(256, 256), bitmap.PixelSize);
        });
    }

    private static byte[] ReadAsset(string name)
    {
        using Stream asset = AssetLoader.Open(new Uri($"avares://Enigma.GitClient.Desktop/Assets/{name}"));
        using MemoryStream copy = new();
        asset.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// Reads an .ico's directory: a six-byte header (reserved, type 1, count), then sixteen bytes per
    /// frame, in which a width or height of 0 means 256.
    /// </summary>
    private static List<IconFrame> ReadFrames(byte[] icon)
    {
        ReadOnlySpan<byte> data = icon;
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(data[0..]));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(data[2..]));

        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        List<IconFrame> frames = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = data.Slice(6 + (16 * index), 16);
            int width = entry[0] == 0 ? 256 : entry[0];
            int height = entry[1] == 0 ? 256 : entry[1];
            Assert.Equal(width, height);

            int length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            Assert.InRange(offset + length, 0, icon.Length);

            frames.Add(new IconFrame(width, BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]), icon[offset..(offset + length)]));
        }

        return frames;
    }

    private sealed record IconFrame(int Size, int BitCount, byte[] Png);
}
