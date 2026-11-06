using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons;
using Enigma.Icons.Avalonia;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A repository's own icon: stored as a copy named after the repository, read as a picture or an SVG,
/// and drawn on its tile in place of the folder glyph (FEATURE-4F5C).
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RepositoryIconTests
{
    private const string TwoColourSvg =
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><rect width="12" height="24" fill="#ff0000"/><rect x="12" width="12" height="24" fill="#0000ff"/></svg>""";

    private const string OneColourSvg =
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M2 2h20v20H2z" fill="currentColor"/></svg>""";

    private const string NothingToDrawSvg =
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><text x="2" y="12">hi</text></svg>""";

    private readonly HeadlessAvaloniaFixture _fixture;

    public RepositoryIconTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- pictures to choose

    private static byte[] Png(Color colour, int size = 16)
    {
        using RenderTargetBitmap bitmap = new(new PixelSize(size, size));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(new SolidColorBrush(colour), new Rect(0, 0, size, size));
        }

        using MemoryStream stream = new();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);

        return stream.ToArray();
    }

    private static byte[] Jpeg()
    {
        using RenderTargetBitmap bitmap = new(new PixelSize(16, 16));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(Brushes.Orange, new Rect(0, 0, 16, 16));
        }

        using MemoryStream stream = new();
        bitmap.Save(stream, JpegBitmapEncoderOptions.Default);

        return stream.ToArray();
    }

    /// <summary>
    /// A 2 × 2, 24-bit bitmap, written by hand: rows of 6 bytes padded to 8, bottom row first.
    /// </summary>
    private static byte[] Bmp()
    {
        const int rowBytes = 8;
        const int pixelBytes = rowBytes * 2;

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(14 + 40 + pixelBytes);
        writer.Write(0);
        writer.Write(14 + 40);

        writer.Write(40);
        writer.Write(2);
        writer.Write(2);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(pixelBytes);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        for (int row = 0; row < 2; row++)
        {
            writer.Write(new byte[] { 0, 128, 0, 0, 128, 0, 0, 0 });
        }

        writer.Flush();

        return stream.ToArray();
    }

    /// <summary>
    /// An icon file holding one PNG image, as Windows' own icons hold their large sizes.
    /// </summary>
    private static byte[] Ico(byte[] png)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)1);

        writer.Write((byte)16);
        writer.Write((byte)16);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(png.Length);
        writer.Write(6 + 16);

        writer.Write(png);
        writer.Flush();

        return stream.ToArray();
    }

    private static string Write(TestServices services, string name, byte[] content)
    {
        string folder = Path.Combine(services.ConfigurationRoot, "chosen");
        Directory.CreateDirectory(folder);

        string file = Path.Combine(folder, name);
        File.WriteAllBytes(file, content);

        return file;
    }

    private static string Write(TestServices services, string name, string content)
        => Write(services, name, Encoding.UTF8.GetBytes(content));

    private static string Repository(TestServices services, string name = "listed")
    {
        string path = Path.Combine(services.ConfigurationRoot, "workspace", name);
        Directory.CreateDirectory(path);

        return path;
    }

    private static string IconFolder(TestServices services)
        => Path.Combine(services.ConfigurationRoot, RepositoryIconStore.DirectoryName);

    // ---------------------------------------------------------------- the store

    [Fact]
    public void TheKey_IsTheRepositorysPath_WithoutATrailingSeparator()
    {
        string path = Path.Combine(Path.GetTempPath(), "some", "repository");

        Assert.Equal(16, RepositoryIconStore.KeyOf(path).Length);
        Assert.Matches("^[0-9A-F]{16}$", RepositoryIconStore.KeyOf(path));
        Assert.Equal(RepositoryIconStore.KeyOf(path), RepositoryIconStore.KeyOf(path + Path.DirectorySeparatorChar));
        Assert.NotEqual(RepositoryIconStore.KeyOf(path), RepositoryIconStore.KeyOf(path + "2"));
    }

    [Fact]
    public void Setting_StoresACopyNamedAfterTheRepository_ThatOutlivesTheOriginal()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IRepositoryIconStore store = services.Get<IRepositoryIconStore>();
            string repository = Repository(services);
            string chosen = Write(services, "Logo.PNG", Png(Colors.Red));

            Assert.Null(store.Find(repository));

            string stored = await store.SetAsync(repository, chosen);

            Assert.Equal(Path.Combine(IconFolder(services), RepositoryIconStore.KeyOf(repository) + ".png"), stored);
            Assert.Equal(File.ReadAllBytes(chosen), File.ReadAllBytes(stored));
            Assert.Equal(stored, store.Find(repository));

            File.Delete(chosen);

            Assert.Equal(stored, store.Find(repository));
            Assert.Equal([stored], Directory.GetFiles(IconFolder(services)));
        });
    }

    [Fact]
    public void SettingAnotherKind_ReplacesTheOld_AndResettingRemovesIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IRepositoryIconStore store = services.Get<IRepositoryIconStore>();
            string repository = Repository(services);
            string other = Repository(services, "other");

            await store.SetAsync(repository, Write(services, "logo.png", Png(Colors.Red)));
            await store.SetAsync(other, Write(services, "other.png", Png(Colors.Green)));
            string svg = await store.SetAsync(repository, Write(services, "logo.svg", TwoColourSvg));

            Assert.EndsWith(".svg", svg, StringComparison.Ordinal);
            Assert.Equal(svg, store.Find(repository));
            Assert.Equal(2, Directory.GetFiles(IconFolder(services)).Length);

            await store.ResetAsync(repository);

            Assert.Null(store.Find(repository));
            Assert.NotNull(store.Find(other));
            Assert.Single(Directory.GetFiles(IconFolder(services)));
        });
    }

    [Fact]
    public void EveryKindOfPicture_IsTaken()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IRepositoryIconStore store = services.Get<IRepositoryIconStore>();
            string repository = Repository(services);

            foreach ((string name, byte[] content) in new (string, byte[])[]
            {
                ("logo.png", Png(Colors.Red)),
                ("logo.jpg", Jpeg()),
                ("logo.jpeg", Jpeg()),
                ("logo.bmp", Bmp()),
                ("logo.ico", Ico(Png(Colors.Blue))),
                ("logo.svg", Encoding.UTF8.GetBytes(TwoColourSvg)),
            })
            {
                string stored = await store.SetAsync(repository, Write(services, name, content));

                Assert.Equal(Path.GetExtension(name), Path.GetExtension(stored));
                Assert.NotNull(RepositoryIconImages.Load(stored));
            }
        });
    }

    [Fact]
    public void ARefusedFile_IsSaidWhy_AndTheIconItReplacedStays()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IRepositoryIconStore store = services.Get<IRepositoryIconStore>();
            string repository = Repository(services);
            string kept = await store.SetAsync(repository, Write(services, "logo.png", Png(Colors.Red)));

            string tooLarge = Write(services, "huge.png", []);

            using (FileStream stream = File.OpenWrite(tooLarge))
            {
                stream.SetLength(RepositoryIconStore.MaxImageBytes + 1);
            }

            (string File, string Says)[] refusals =
            [
                (Write(services, "notes.txt", "not a picture"), "not a kind of image"),
                (Path.Combine(services.ConfigurationRoot, "chosen", "gone.png"), "does not exist"),
                (tooLarge, "at most"),
                (Write(services, "damaged.png", "this is no PNG"), "not an image the client can read"),
                (Write(services, "words.svg", NothingToDrawSvg), "not an image the client can read"),
                (Write(services, "broken.svg", "<svg"), "not an image the client can read"),
            ];

            foreach ((string file, string says) in refusals)
            {
                RepositoryIconException refusal = await Assert.ThrowsAsync<RepositoryIconException>(
                    () => store.SetAsync(repository, file, TestContext.Current.CancellationToken));

                Assert.Contains(says, refusal.Message, StringComparison.Ordinal);
                Assert.Equal(kept, store.Find(repository));
            }

            Assert.Equal([kept], Directory.GetFiles(IconFolder(services)));
        });
    }

    // ---------------------------------------------------------------- reading it

    [Fact]
    public void APicture_IsReadTwiceTheTilesWidth()
    {
        _fixture.Run(() =>
        {
            RepositoryIconArt art = RepositoryIconImages.Decode(Png(Colors.Red, size: 200), ".png");

            Bitmap bitmap = Assert.IsAssignableFrom<Bitmap>(art.Image);
            Assert.Equal(RepositoryIconImages.DecodeWidth, bitmap.PixelSize.Width);
            Assert.Null(art.Glyph);

            Assert.IsAssignableFrom<Bitmap>(RepositoryIconImages.Decode(Ico(Png(Colors.Blue)), ".ico").Image);
            Assert.IsAssignableFrom<Bitmap>(RepositoryIconImages.Decode(Bmp(), ".bmp").Image);
        });
    }

    [Fact]
    public void AnSvgThatNamesItsColours_IsDrawnInThem()
    {
        _fixture.Run(() =>
        {
            RepositoryIconArt art = RepositoryIconImages.Decode(Encoding.UTF8.GetBytes(TwoColourSvg), ".svg");

            DrawingImage image = Assert.IsType<DrawingImage>(art.Image);
            Assert.Null(art.Glyph);
            Assert.Equal(new Rect(0, 0, 24, 24), image.Viewbox);

            Color[] colours = [.. Assert.IsType<DrawingGroup>(image.Drawing).Children
                .OfType<GeometryDrawing>()
                .Select(drawing => Assert.IsAssignableFrom<ISolidColorBrush>(drawing.Brush).Color)];

            Assert.Equal([Color.Parse("#ff0000"), Color.Parse("#0000ff")], colours);
        });
    }

    [Fact]
    public void AnSvgThatNamesNoColour_IsAGlyph_DrawnInTheThemesForeground()
    {
        _fixture.Run(() =>
        {
            RepositoryIconArt art = RepositoryIconImages.Decode(Encoding.UTF8.GetBytes(OneColourSvg), ".svg");

            Assert.Null(art.Image);
            IIconSet glyph = Assert.IsAssignableFrom<IIconSet>(art.Glyph);
            Assert.NotEmpty(glyph.GetGlyph(RepositoryIconImages.GlyphName).Layers);
        });
    }

    [Fact]
    public void Loading_AMissingOrDamagedFile_GivesNothing_AndAChangedFileIsReadAgain()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            Assert.Null(RepositoryIconImages.Load(null));
            Assert.Null(RepositoryIconImages.Load(Path.Combine(services.ConfigurationRoot, "nowhere.png")));
            Assert.Null(RepositoryIconImages.Load(Write(services, "damaged.png", "no picture")));

            string file = Write(services, "logo.svg", OneColourSvg);
            RepositoryIconArt first = Assert.IsType<RepositoryIconArt>(RepositoryIconImages.Load(file));

            Assert.Same(first, RepositoryIconImages.Load(file));

            File.WriteAllText(file, TwoColourSvg);
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));

            RepositoryIconArt second = Assert.IsType<RepositoryIconArt>(RepositoryIconImages.Load(file));

            Assert.NotSame(first, second);
            Assert.IsType<DrawingImage>(second.Image);
        });
    }

    // ---------------------------------------------------------------- on the tile

    private static async Task<(RepositoriesPageViewModel Model, RepositoriesPageView Page, Window Window)> ShowAsync(
        TestServices services,
        params string[] repositories)
    {
        foreach (string repository in repositories)
        {
            await services.Get<IRepositoryListStore>().AddAsync(IdentityProfile.DefaultId, repository, Path.GetFileName(repository));
        }

        RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
        await model.OnAppearingAsync();

        RepositoriesPageView page = services.Get<RepositoriesPageView>();
        page.DataContext = model;

        Window window = new() { Content = page, Width = 1100, Height = 700 };
        window.Show();
        window.UpdateLayout();

        return (model, page, window);
    }

    private static T InTile<T>(RepositoriesPageView page, string repositoryName, string name)
        where T : Control
        => page.GetVisualDescendants()
            .OfType<ItemsControl>()
            .Single(list => list.Name == "RepositoryList")
            .GetVisualDescendants()
            .OfType<T>()
            .First(control => control.Name == name && control.DataContext is ListedRepository entry && entry.Name == repositoryName);

    [Fact]
    public void ATile_DrawsItsRepositorysIcon_AndTheFolderGlyphWithoutOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IRepositoryIconStore store = services.Get<IRepositoryIconStore>();
            string picture = Repository(services, "picture");
            string glyph = Repository(services, "glyph");
            string plain = Repository(services, "plain");

            await store.SetAsync(picture, Write(services, "logo.png", Png(Colors.Red)));
            await store.SetAsync(glyph, Write(services, "mark.svg", OneColourSvg));

            (RepositoriesPageViewModel model, RepositoriesPageView page, Window window) = await ShowAsync(services, picture, glyph, plain);

            try
            {
                Assert.Equal(store.Find(picture), model.Repositories.Single(entry => entry.Name == "picture").IconFile);
                Assert.Null(model.Repositories.Single(entry => entry.Name == "plain").IconFile);

                // A picture.
                Assert.False(InTile<Icon>(page, "picture", "TileGlyph").IsVisible);
                Image image = InTile<Image>(page, "picture", "TileImage");
                Assert.True(image.IsVisible);
                Assert.IsAssignableFrom<Bitmap>(image.Source);
                Assert.Equal(24, image.Bounds.Width, 0.5);
                Assert.False(InTile<Icon>(page, "picture", "TileCustomGlyph").IsVisible);

                // A glyph, in the tile's size.
                Assert.False(InTile<Icon>(page, "glyph", "TileGlyph").IsVisible);
                Assert.False(InTile<Image>(page, "glyph", "TileImage").IsVisible);
                Icon custom = InTile<Icon>(page, "glyph", "TileCustomGlyph");
                Assert.True(custom.IsVisible);
                Assert.Equal(24, custom.Size);
                Assert.Equal(RepositoryIconImages.GlyphName, custom.IconName);

                // None: the folder.
                Assert.True(InTile<Icon>(page, "plain", "TileGlyph").IsVisible);
                Assert.False(InTile<Image>(page, "plain", "TileImage").IsVisible);
                Assert.False(InTile<Icon>(page, "plain", "TileCustomGlyph").IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AStoredIconDamagedSince_LeavesTheFolderGlyph()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IRepositoryIconStore store = services.Get<IRepositoryIconStore>();
            string repository = Repository(services);
            string stored = await store.SetAsync(repository, Write(services, "logo.png", Png(Colors.Red)));

            File.WriteAllText(stored, "damaged since");
            File.SetLastWriteTimeUtc(stored, DateTime.UtcNow.AddMinutes(1));

            (RepositoriesPageViewModel model, RepositoriesPageView page, Window window) = await ShowAsync(services, repository);

            try
            {
                Assert.Null(Assert.Single(model.Repositories).IconFile);
                Assert.True(InTile<Icon>(page, "listed", "TileGlyph").IsVisible);
                Assert.False(InTile<Image>(page, "listed", "TileImage").IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
