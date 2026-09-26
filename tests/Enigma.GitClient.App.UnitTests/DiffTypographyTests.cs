using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Enigma.GitClient.App.Controls.Diff;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.Core.Configuration;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// The diff's typography: what the two font preferences turn into, what the application publishes
/// them as, and what a diff row is drawn at as a result.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiffTypographyTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public DiffTypographyTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Runs work with a font preference in force and puts the defaults back afterwards. The
    /// typography is application-wide state, and a test that leaves 24 px behind changes every test
    /// that draws a diff after it.
    /// </summary>
    private void WithFont(AppSettings settings, Action assertions)
        => _fixture.Run(() =>
        {
            try
            {
                DiffTypography.Apply(settings);
                assertions();
            }
            finally
            {
                DiffTypography.Apply(AppSettings.Defaults);
            }
        });

    private static double MeasureWidth(string text, FontFamily family, double size)
        => new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(family),
            size,
            Brushes.Black).Width;

    [Fact]
    public void Typography_PublishesTheDefaultsTheStylesResolve()
    {
        _fixture.Run(() =>
        {
            // Seeded by App.Initialize, so the styles never resolve a missing key.
            Application application = Application.Current!;

            Assert.Equal(14d, Assert.IsType<double>(application.Resources[DiffTypography.FontSizeKey]));
            Assert.Equal(
                DiffTypography.Current.FontFamily,
                Assert.IsType<FontFamily>(application.Resources[DiffTypography.FontFamilyKey]));
            Assert.Equal(
                DiffTypography.Current.GutterWidth,
                Assert.IsType<double>(application.Resources[DiffTypography.GutterWidthKey]));
            Assert.Equal(
                DiffTypography.Current.MarkerWidth,
                Assert.IsType<double>(application.Resources[DiffTypography.MarkerWidthKey]));
            Assert.Equal(
                DiffTypography.Current.LineNumberFontSize,
                Assert.IsType<double>(application.Resources[DiffTypography.LineNumberFontSizeKey]));
        });
    }

    [Fact]
    public void Typography_DefaultsToTheApplicationsMonospaceStack()
    {
        _fixture.Run(() =>
        {
            Application.Current!.Resources.TryGetResource(
                DiffTypography.MonospaceFontFamilyKey,
                null,
                out object? stack);

            Assert.Equal(stack, DiffTypography.Measure(string.Empty, 14).FontFamily);

            // And clearing a chosen family goes straight back to it.
            Assert.Equal(stack, DiffTypography.Measure("   ", 14).FontFamily);
        });
    }

    [Fact]
    public void Typography_IsBiggerThanItWas()
    {
        _fixture.Run(() =>
        {
            // 12 px is what the styles hard-coded before the preference existed.
            DiffMetrics was = DiffTypography.Measure(string.Empty, 12);
            DiffMetrics now = DiffTypography.Measure(string.Empty, AppSettings.Defaults.DiffFontSize);

            Assert.True(now.FontSize > was.FontSize);
            Assert.True(now.CharacterWidth > was.CharacterWidth);
            Assert.True(now.LineNumberFontSize > was.LineNumberFontSize);
        });
    }

    [Theory]
    [InlineData(8)]
    [InlineData(14)]
    [InlineData(20)]
    [InlineData(32)]
    public void Typography_KeepsASixDigitLineNumberInsideItsGutter(double size)
    {
        _fixture.Run(() =>
        {
            DiffMetrics metrics = DiffTypography.Measure(string.Empty, size);

            // The 8 px the diffnumber style pads on the right, which the gutter has to hold too.
            Assert.True(
                MeasureWidth("123456", metrics.FontFamily, metrics.LineNumberFontSize) + 8 <= metrics.GutterWidth,
                $"a six-digit line number does not fit the {metrics.GutterWidth} px gutter at size {size}");
        });
    }

    [Fact]
    public void Typography_GrowsTheGutterAndTheMarkerWithTheFace()
    {
        _fixture.Run(() =>
        {
            DiffMetrics small = DiffTypography.Measure(string.Empty, 10);
            DiffMetrics large = DiffTypography.Measure(string.Empty, 24);

            Assert.True(large.GutterWidth > small.GutterWidth);
            Assert.True(large.MarkerWidth > small.MarkerWidth);
        });
    }

    [Fact]
    public void Typography_ClampsASizeNobodyCouldRead()
    {
        _fixture.Run(() =>
        {
            Assert.Equal(8, DiffTypography.Measure(string.Empty, 1).FontSize);
            Assert.Equal(32, DiffTypography.Measure(string.Empty, 400).FontSize);
        });
    }

    [Fact]
    public void Typography_SurvivesAFamilyThisMachineDoesNotHave()
    {
        _fixture.Run(() =>
        {
            // Avalonia falls back through its own stack rather than failing, so a settings file
            // naming a face that was uninstalled still draws a diff.
            DiffMetrics metrics = DiffTypography.Measure("No Such Face 91827", 14);

            Assert.True(metrics.CharacterWidth > 0);
            Assert.True(metrics.GutterWidth > 0);
        });
    }

    [Fact]
    public void Typography_PublishesAChosenSize()
    {
        WithFont(
            AppSettings.Defaults with { DiffFontSize = 20 },
            () =>
            {
                Assert.Equal(20, DiffTypography.Current.FontSize);
                Assert.Equal(20d, Assert.IsType<double>(Application.Current!.Resources[DiffTypography.FontSizeKey]));
            });
    }

    [Fact]
    public void Typography_TellsTheApplicationTheMetricsMoved()
    {
        _fixture.Run(() =>
        {
            int raised = 0;
            EventHandler handler = (_, _) => raised++;

            DiffTypography.Changed += handler;

            try
            {
                DiffTypography.Apply(AppSettings.Defaults with { DiffFontSize = 18 });
                DiffTypography.Apply(AppSettings.Defaults);
            }
            finally
            {
                DiffTypography.Changed -= handler;
            }

            Assert.Equal(2, raised);
        });
    }

    [Fact]
    public void Typography_MeasuresTheSameFaceOnce()
    {
        _fixture.Run(() =>
        {
            FontFamily family = DiffTypography.Current.FontFamily;

            Assert.Equal(
                DiffTypography.MeasureCharacterWidth(family, 14),
                DiffTypography.MeasureCharacterWidth(family, 14));

            Assert.True(DiffTypography.MeasureCharacterWidth(family, 14) > 0);
        });
    }

    [Fact]
    public void Typography_AlwaysHasAFaceToDrawWith()
    {
        // Deliberately not "the application's stack is monospace": which of its five faces a
        // machine actually has is that machine's business, and a build agent with none of them
        // still has to draw a diff. What must hold everywhere is that the stack measures.
        _fixture.Run(() => Assert.True(
            DiffTypography.MeasureCharacterWidth(new FontFamily(DiffTypography.FallbackFontFamily), 14) > 0));
    }

    [Fact]
    public void Typography_SurvivesAFamilyItCannotRealise()
    {
        _fixture.Run(() =>
        {
            // The system font collection lists families whose glyphs cannot always be created; the
            // list has to walk past one rather than take the settings page down with it.
            Assert.All(
                FontManager.Current.SystemFonts,
                family => Assert.True(DiffTypography.MeasureCharacterWidth(family, 14) > 0));
        });
    }

    [Fact]
    public void Typography_OffersOnlyMonospaceFacesToChooseFrom()
    {
        _fixture.Run(() => Assert.All(
            DiffTypography.MonospaceFamilies(),
            family => Assert.True(DiffTypography.IsMonospace(family), $"{family.Name} is not monospace")));
    }

    [Fact]
    public void Typography_ReachesTheDiffLineControl()
    {
        WithFont(
            AppSettings.Defaults with { DiffFontSize = 26 },
            () =>
            {
                DiffLineText line = new() { Text = "public sealed record Commit(string Hash);" };

                // Styles only apply inside a tree, so the control is measured with the published
                // values put on it the way the style would.
                line.FontFamily = DiffTypography.Current.FontFamily;
                line.FontSize = DiffTypography.Current.FontSize;
                line.Measure(Size.Infinity);

                double large = line.DesiredSize.Width;

                line.FontSize = 12;
                line.InvalidateMeasure();
                line.Measure(Size.Infinity);

                Assert.True(large > line.DesiredSize.Width);
            });
    }

    [Fact]
    public void Typography_ListsTheFacesInNameOrder()
    {
        _fixture.Run(() =>
        {
            // The list is the machine's, so the assertion is about its shape rather than its
            // contents: a build machine may have one monospace face or thirty.
            Assert.All(DiffTypography.MonospaceFamilies(), family => Assert.NotEmpty(family.Name));
            Assert.Equal(
                DiffTypography.MonospaceFamilies().Select(family => family.Name),
                DiffTypography.MonospaceFamilies().Select(family => family.Name).Order(StringComparer.CurrentCulture));
        });
    }
}
