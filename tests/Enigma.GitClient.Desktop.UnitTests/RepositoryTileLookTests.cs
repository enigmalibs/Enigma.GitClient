using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A repository's tile on the start window: its glyph on the tile size, and its path in a grey that
/// reads, not the faint one that looked like disabled text (FEATURE-4F5C).
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RepositoryTileLookTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public RepositoryTileLookTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static IBrush Brush(string key)
    {
        Application application = Application.Current!;

        Assert.True(application.TryFindResource(key, application.ActualThemeVariant, out object? brush), key);

        return Assert.IsAssignableFrom<IBrush>(brush);
    }

    private static async Task<(Window Window, RepositoriesPageView Page)> ShowOneAsync(TestServices services)
    {
        string path = Path.Combine(services.ConfigurationRoot, "listed");
        Directory.CreateDirectory(path);
        await services.Get<IRepositoryListStore>().AddAsync(IdentityProfile.DefaultId, path, "listed");

        RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
        await model.OnAppearingAsync();

        RepositoriesPageView page = services.Get<RepositoriesPageView>();
        page.DataContext = model;

        Window window = new() { Content = page, Width = 1100, Height = 700 };
        window.Show();
        window.UpdateLayout();

        return (window, page);
    }

    private static T Named<T>(Visual root, string name)
        where T : Control
        => root.GetVisualDescendants().OfType<T>().First(control => control.Name == name);

    [Fact]
    public void TheTilesGlyph_IsOnTheTileSize()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (Window window, RepositoriesPageView page) = await ShowOneAsync(services);

            try
            {
                Icon glyph = Named<Icon>(page, "TileGlyph");

                Assert.Equal(PhosphorIcon.Folder, glyph.Kind);
                Assert.Contains("tile", glyph.Classes);
                Assert.Equal(24, glyph.Size);
                Assert.Equal(24, glyph.Bounds.Width, 0.5);
                Assert.Equal(24, glyph.Bounds.Height, 0.5);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ThePath_ReadsInTheSecondaryGrey_InBothThemes()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            (Window window, RepositoriesPageView page) = await ShowOneAsync(services);

            try
            {
                foreach (ThemeVariant variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
                {
                    application.RequestedThemeVariant = variant;
                    window.UpdateLayout();

                    TextBlock path = Named<TextBlock>(page, "TilePath");

                    Assert.Equal(Path.Combine(services.ConfigurationRoot, "listed"), path.Text);
                    Assert.Same(Brush("EnigmaForegroundSecondaryBrush"), path.Foreground);
                    Assert.NotSame(Brush("EnigmaForegroundTertiaryBrush"), path.Foreground);
                }
            }
            finally
            {
                application.RequestedThemeVariant = original;
                window.Close();
            }
        });
    }
}
