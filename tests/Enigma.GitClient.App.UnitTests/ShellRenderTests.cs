using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Enigma.GitClient.App.Controls;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.Views.Pages;
using Enigma.Icons.Avalonia;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Builds real controls on a headless Avalonia platform. These are the tests that catch a broken
/// theme key, a control template that does not apply, or a page that cannot be laid out — none of
/// which the compiler or the XAML compiler can see.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ShellRenderTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public ShellRenderTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static void Layout(Control control, double width = 1200, double height = 800)
    {
        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));
        control.UpdateLayout();
    }

    [Fact]
    public void Application_MergesTheEnigmaThemeAndOurOwn()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;

            Assert.True(application.TryFindResource("EnigmaBackgroundBrush", application.ActualThemeVariant, out object? background));
            Assert.IsAssignableFrom<IBrush>(background);

            Assert.True(application.TryFindResource("EnigmaSurfaceBrush", application.ActualThemeVariant, out object? surface));
            Assert.IsAssignableFrom<IBrush>(surface);

            Assert.True(application.TryFindResource("EnigmaBorderSubtleBrush", application.ActualThemeVariant, out _));
            Assert.True(application.TryFindResource("EnigmaForegroundSecondaryBrush", application.ActualThemeVariant, out _));
            Assert.True(application.TryFindResource("EnigmaWarningBackgroundBrush", application.ActualThemeVariant, out _));
        });
    }

    [Fact]
    public void ThemeBrushes_ResolveInBothVariants()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

            try
            {
                foreach (ThemeVariant variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
                {
                    application.RequestedThemeVariant = variant;

                    Assert.True(
                        application.TryFindResource("EnigmaBackgroundBrush", variant, out object? brush),
                        $"EnigmaBackgroundBrush must resolve under {variant}");
                    Assert.NotNull(brush);
                }
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void EmptyState_AppliesItsTemplateAndShowsItsText()
    {
        _fixture.Run(() =>
        {
            EmptyState state = new()
            {
                IconKind = Enigma.Icons.Phosphor.PhosphorIcon.GitCommit,
                Title = "Nothing here yet",
                Message = "Open a repository to see its history.",
            };

            Window window = new() { Content = state, Width = 600, Height = 400 };
            Layout(window, 600, 400);

            Assert.True(state.IsMeasureValid);

            string[] texts = [.. state.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(block => block.Text ?? string.Empty)];

            Assert.Contains("Nothing here yet", texts);
            Assert.Contains("Open a repository to see its history.", texts);
        });
    }

    [Theory]
    [InlineData(typeof(HistoryPageView))]
    [InlineData(typeof(ChangesPageView))]
    [InlineData(typeof(BranchesPageView))]
    [InlineData(typeof(RemotesPageView))]
    [InlineData(typeof(IntegrationsPageView))]
    [InlineData(typeof(SettingsPageView))]
    public void EveryPage_BuildsAndLaysOut(Type pageType)
    {
        _fixture.Run(() =>
        {
            Control page = (Control)Activator.CreateInstance(pageType)!;
            Window window = new() { Content = page };

            Layout(window);

            Assert.True(page.IsMeasureValid);
            Assert.True(page.Bounds.Width > 0);
            Assert.True(page.Bounds.Height > 0);
        });
    }

    // ---------------------------------------------------------------- the icon scale

    /// <summary>
    /// The size an icon of each role is drawn at. The test is the scale: a role whose number moves
    /// here is a deliberate change to how the whole application looks, not a detail of one view.
    /// </summary>
    private static readonly (string Role, double Size)[] Scale =
    [
        ("header", 20),
        ("toolbar", 18),
        ("row", 16),
        ("pill", 12),
    ];

    /// <summary>
    /// The same scale, as a theory's cases.
    /// </summary>
    public static TheoryData<string, double> IconRoles
    {
        get
        {
            TheoryData<string, double> roles = [];

            foreach ((string role, double size) in Scale)
            {
                roles.Add(role, size);
            }

            return roles;
        }
    }

    [Theory]
    [MemberData(nameof(IconRoles))]
    public void IconScale_GivesEachRoleItsOwnSize(string role, double size)
    {
        _fixture.Run(() =>
        {
            Icon icon = new() { Kind = Enigma.Icons.Phosphor.PhosphorIcon.Gear, Classes = { role } };
            Window window = new() { Content = icon, Width = 100, Height = 100 };

            Layout(window, 100, 100);

            Assert.Equal(size, icon.Size);
        });
    }

    [Fact]
    public void AToolbarIconIsBiggerThanARowIconAndBothBeatABadge()
    {
        _fixture.Run(() =>
        {
            double Size(string role)
            {
                Icon icon = new() { Classes = { role } };
                Window window = new() { Content = icon, Width = 100, Height = 100 };
                Layout(window, 100, 100);
                return icon.Size;
            }

            Assert.True(Size("header") > Size("toolbar"));
            Assert.True(Size("toolbar") > Size("row"));
            Assert.True(Size("row") > Size("pill"));
        });
    }

    [Theory]
    [InlineData(typeof(HistoryPageView))]
    [InlineData(typeof(ChangesPageView))]
    [InlineData(typeof(BranchesPageView))]
    [InlineData(typeof(RemotesPageView))]
    [InlineData(typeof(IntegrationsPageView))]
    [InlineData(typeof(SettingsPageView))]
    public void EveryPage_SizesItsToolbarIconsFromTheScale(Type pageType)
    {
        _fixture.Run(() =>
        {
            Control page = (Control)Activator.CreateInstance(pageType)!;
            Window window = new() { Content = page };

            Layout(window);

            Icon[] icons = [.. page.GetVisualDescendants().OfType<Icon>()];

            // Every icon that claims a role is drawn at that role's size — the style reached it,
            // and no local Size in the markup outranked it.
            foreach (Icon icon in icons)
            {
                foreach ((string role, double size) in Scale)
                {
                    if (icon.Classes.Contains(role))
                    {
                        Assert.Equal(size, icon.Size);
                    }
                }
            }

            // And the page says what it is with a title icon of the header size.
            Assert.Contains(icons, icon => icon.Classes.Contains("header") && icon.Size == 20);
        });
    }

    [Fact]
    public void AToolbarButtonsIconIsTheToolbarSize()
    {
        _fixture.Run(() =>
        {
            BranchesPageView page = new();
            Window window = new() { Content = page };

            Layout(window);

            // The refresh, the segmented toggles and "New branch" all sit on the page's strip.
            Icon[] toolbar = [.. page.GetVisualDescendants()
                .OfType<Icon>()
                .Where(icon => icon.Classes.Contains("toolbar"))];

            Assert.True(toolbar.Length >= 4, $"the branches toolbar drew {toolbar.Length} icons");
            Assert.All(toolbar, icon => Assert.Equal(18, icon.Size));
        });
    }
}
