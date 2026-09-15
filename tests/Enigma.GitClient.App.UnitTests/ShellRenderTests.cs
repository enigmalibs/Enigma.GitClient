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
}
