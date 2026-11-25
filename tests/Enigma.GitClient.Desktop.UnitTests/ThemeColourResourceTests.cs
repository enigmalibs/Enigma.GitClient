using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Desktop.Controls;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Colours that used to be literals now come from the theme (CODE-REVIEW-6E56 XAML-03): the
/// checked-out badge's text, the avatar's initials and tints, and the warning icon of two dialogs.
/// Every key resolves in both variants, and what is drawn follows a theme switch.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ThemeColourResourceTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public ThemeColourResourceTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private sealed class Themed : IDisposable
    {
        private readonly ThemeVariant? _original;

        public Themed(ThemeVariant variant)
        {
            _original = Application.Current!.RequestedThemeVariant;
            Application.Current.RequestedThemeVariant = variant;
        }

        public void Dispose() => Application.Current!.RequestedThemeVariant = _original;
    }

    private static Color ColourOf(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryFindResource(key, variant, out object? value), $"{key} is missing in the {variant} variant");
        return Assert.IsType<Color>(value);
    }

    private static Color BrushColourOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [Theory]
    [InlineData("RefBadgeHeadForegroundColor")]
    [InlineData("AuthorAvatarInitialsColor")]
    public void EveryNewColour_ResolvesInBothVariants(string key)
    {
        _fixture.Run(() =>
        {
            _ = ColourOf(key, ThemeVariant.Dark);
            _ = ColourOf(key, ThemeVariant.Light);
        });
    }

    [Fact]
    public void TheInitials_AreDarkInkOnTheDarkVariant_AndWhiteOnTheLight()
    {
        _fixture.Run(() =>
        {
            Assert.Equal(Color.Parse("#12151A"), ColourOf("AuthorAvatarInitialsColor", ThemeVariant.Dark));
            Assert.Equal(Colors.White, ColourOf("AuthorAvatarInitialsColor", ThemeVariant.Light));
        });
    }

    [Fact]
    public void AnAvatar_IsTintedWithItsLanesBrush_AndFollowsATheme()
    {
        _fixture.Run(() =>
        {
            using Themed dark = new(ThemeVariant.Dark);
            AuthorAvatar avatar = new() { Initials = "AL", Seed = "Ada Lovelace" };
            Window window = new() { Content = avatar };
            window.Show();

            int[] lanes = [0, 1, 2, 3, 4, 5, 7, 8];
            string lane = $"GraphLane{lanes[AuthorAvatar.IndexFor("Ada Lovelace")]}";

            Assert.True(window.TryFindResource(lane + "Brush", out object? brush));
            Assert.Same(brush, avatar.Tint);
            Assert.Equal(ColourOf(lane + "Color", ThemeVariant.Dark), BrushColourOf(avatar.Tint));

            using (new Themed(ThemeVariant.Light))
            {
                Assert.Equal(ColourOf(lane + "Color", ThemeVariant.Light), BrushColourOf(avatar.Tint));
            }

            window.Close();
        });
    }

    [Fact]
    public void TheCheckedOutBadge_TakesItsTextColourFromTheTheme()
    {
        _fixture.Run(() =>
        {
            using Themed light = new(ThemeVariant.Light);
            RefBadge badge = new() { Kind = GitRefKind.LocalBranch, Text = "main", IsCurrent = true };
            Window window = new() { Content = badge };
            window.Show();

            Assert.Contains("head", badge.Classes);
            Assert.Equal(ColourOf("RefBadgeHeadForegroundColor", ThemeVariant.Light), BrushColourOf(badge.Foreground));

            window.Close();
        });
    }

    [Fact]
    public void TheWarningIcon_IsTheLibrarysWarningBrush()
    {
        _fixture.Run(() =>
        {
            Application application = Assert.IsAssignableFrom<Application>(Application.Current);
            Assert.True(application.TryFindResource(ThemeBrushes.WarningKey, application.ActualThemeVariant, out object? warning));
            Assert.Same(warning, ThemeBrushes.Warning);
        });
    }
}
