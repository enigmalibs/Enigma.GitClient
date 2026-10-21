using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Controls.Navigation;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The info bar in either window's bottom-left corner: right of the navigation rail, a quarter of the
/// window wide, and following the window when it is resized.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class InfoBarPlacementTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public InfoBarPlacementTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(1920, 72, 480)]
    [InlineData(1440, 88, 360)]
    [InlineData(900, 72, 360)]
    [InlineData(400, 72, 328)]
    [InlineData(50, 72, 0)]
    public void WidthFor_IsAQuarterOfTheWindow_AtLeastTheMinimum_AndNeverPastItsEdge(
        double window,
        double rail,
        double expected)
        => Assert.Equal(expected, InfoBarPlacement.WidthFor(window, rail));

    [Theory]
    [InlineData(typeof(MainWindow))]
    [InlineData(typeof(StartWindow))]
    public void InEitherWindow_TheBarSitsBottomLeft_RightOfTheRail_AndFollowsAResize(Type windowType)
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            Window window = (Window)services.Provider.GetRequiredService(windowType);
            InfoBar bar = ((IHostWindow)window).InfoBarHost;
            NavigationView rail = window.FindControl<NavigationView>("NavigationRail")
                ?? throw new InvalidOperationException("The window has no navigation rail.");

            // Restored rather than maximised, so the window takes the sizes the test gives it.
            window.WindowState = WindowState.Normal;
            window.Width = 1600;
            window.Height = 900;
            window.Show();

            try
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.Equal(HorizontalAlignment.Left, bar.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Bottom, bar.VerticalAlignment);
                Assert.Equal(rail.PaneSize, bar.Margin.Left);
                Assert.Equal(400, bar.Width);

                window.Width = 2000;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.Equal(500, bar.Width);
                Assert.Equal(rail.PaneSize, bar.Margin.Left);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
