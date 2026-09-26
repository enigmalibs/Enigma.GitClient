using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// A context menu item's title is always drawn whole: a line's menu names branches, and a branch
/// name cut off in the middle is the one piece of the item the reader needed.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ContextMenuWidthTests
{
    /// <summary>
    /// The kind of title a history line's menu builds for two long branch names — far wider than the
    /// 456 pixels FluentTheme lets a menu grow to, and still narrower than a screen.
    /// </summary>
    private const string LongTitle =
        "Merge \"feature/2026-09-24-toolbar-refresh-theme-menus\" into \"release/2026.10-long-lived-branch\"";

    private readonly HeadlessAvaloniaFixture _fixture;

    public ContextMenuWidthTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// How wide the title is when nothing constrains it, in the face and size the item draws it in.
    /// </summary>
    private static double NaturalWidth(TextBlock drawn)
    {
        TextBlock free = new()
        {
            Text = drawn.Text,
            FontFamily = drawn.FontFamily,
            FontSize = drawn.FontSize,
            FontWeight = drawn.FontWeight,
            FontStyle = drawn.FontStyle,
        };

        free.Measure(Size.Infinity);

        return free.DesiredSize.Width;
    }

    [Fact]
    public void ALongItemTitle_IsDrawnWhole()
    {
        _fixture.Run(() =>
        {
            ContextMenu menu = new()
            {
                ItemsSource = new object[]
                {
                    new MenuItem { Header = "Check out this commit (detaches HEAD)" },
                    new MenuItem { Header = LongTitle },
                },
            };

            Border target = new() { Width = 200, Height = 100, ContextMenu = menu };
            Window window = new() { Content = target, Width = 1600, Height = 900 };
            window.Show();
            window.UpdateLayout();

            menu.Open(target);

            try
            {
                MenuItem item = menu.GetVisualDescendants()
                    .OfType<MenuItem>()
                    .Single(candidate => Equals(candidate.Header, LongTitle));

                ContentPresenter header = item.GetVisualDescendants()
                    .OfType<ContentPresenter>()
                    .First(presenter => presenter.Name == "PART_HeaderPresenter");

                TextBlock text = header.GetVisualDescendants().OfType<TextBlock>().First();
                double natural = NaturalWidth(text);

                Assert.True(natural > 456, $"the title is only {natural} px: the test proves nothing");

                // The whole title fits in what the item gave it, on one line.
                Assert.True(
                    header.Bounds.Width >= natural - 0.5,
                    $"the header got {header.Bounds.Width} px of the {natural} px its title needs");
                Assert.True(
                    text.Bounds.Width >= natural - 0.5,
                    $"the title was drawn {text.Bounds.Width} px wide of {natural} px");
            }
            finally
            {
                menu.Close();
                window.Close();
            }
        });
    }

    [Fact]
    public void AContextMenu_IsNotCappedAtFluentsFlyoutWidth()
    {
        _fixture.Run(() =>
        {
            ContextMenu menu = new() { ItemsSource = new object[] { new MenuItem { Header = "Copy path" } } };
            Border target = new() { Width = 200, Height = 100, ContextMenu = menu };
            Window window = new() { Content = target, Width = 800, Height = 600 };
            window.Show();

            menu.Open(target);

            try
            {
                Assert.True(double.IsPositiveInfinity(menu.MaxWidth), $"the menu is capped at {menu.MaxWidth} px");
            }
            finally
            {
                menu.Close();
                window.Close();
            }
        });
    }

    [Fact]
    public void AnItemTitleWiderThanTheScreen_WrapsRatherThanBeingCut()
    {
        _fixture.Run(() =>
        {
            MenuItem item = new() { Header = LongTitle };
            ContextMenu menu = new() { ItemsSource = new object[] { item } };
            Border target = new() { Width = 200, Height = 100, ContextMenu = menu };
            Window window = new() { Content = target, Width = 800, Height = 600 };
            window.Show();

            menu.Open(target);

            try
            {
                ContentPresenter header = item.GetVisualDescendants()
                    .OfType<ContentPresenter>()
                    .First(presenter => presenter.Name == "PART_HeaderPresenter");

                // The last line of defence, for a title no screen is wide enough for: it breaks onto
                // a second line instead of running out of the menu.
                Assert.Equal(TextWrapping.Wrap, header.TextWrapping);
            }
            finally
            {
                menu.Close();
                window.Close();
            }
        });
    }
}
