using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Enigma.GitClient.Core.Screens;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The window that replaces another opens on the screen the other was on — on a desktop of two screens
/// the test lays out, the second at 150 %, with real windows on the headless platform.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class WindowScreenTests
{
    private static readonly ScreenInfo First = new(new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1.0, IsPrimary: true);
    private static readonly ScreenInfo Second = new(new(1920, 0, 2560, 1440), new(1920, 0, 2560, 1400), 1.5, IsPrimary: false);

    private readonly HeadlessAvaloniaFixture _fixture;

    public WindowScreenTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A desktop of the test's choosing.
    /// </summary>
    private sealed class FixedScreens(params ScreenInfo[] screens) : IScreenReader
    {
        public IReadOnlyList<ScreenInfo> Read(Window window) => screens;
    }

    /// <summary>
    /// The real window coordinator, over the desktop given.
    /// </summary>
    private static TestServices Build(params ScreenInfo[] screens)
        => TestServices.Build(configure: services =>
        {
            services.RemoveAll<IAppWindows>();
            services.AddSingleton<IAppWindows, AppWindows>();
            services.RemoveAll<IScreenReader>();
            services.AddSingleton<IScreenReader>(new FixedScreens(screens));
        });

    private static PixelPoint CentredOn(ScreenInfo screen, double width, double height)
    {
        (int x, int y) = ScreenPlacement.CenterIn(
            screen.WorkingArea,
            (int)Math.Round(width * screen.Scaling),
            (int)Math.Round(height * screen.Scaling));

        return new PixelPoint(x, y);
    }

    [Fact]
    public void TheFirstWindow_IsPlacedAsBefore()
    {
        _fixture.Run(() =>
        {
            using TestServices services = Build(First, Second);
            IAppWindows windows = services.Get<IAppWindows>();

            try
            {
                windows.ShowStart();

                Assert.Equal(WindowStartupLocation.CenterScreen, windows.CurrentWindow!.WindowStartupLocation);
            }
            finally
            {
                windows.CurrentWindow?.Close();
            }
        });
    }

    [Fact]
    public void TheRepositoryWindow_OpensOnTheScreenTheStartWindowWasOn_AtThatScreensScaling()
    {
        _fixture.Run(() =>
        {
            using TestServices services = Build(First, Second);
            IAppWindows windows = services.Get<IAppWindows>();

            try
            {
                windows.ShowStart();
                windows.CurrentWindow!.Position = new PixelPoint(2200, 100);

                windows.ShowRepository();

                MainWindow repository = Assert.IsType<MainWindow>(windows.CurrentWindow);
                Assert.Equal(WindowStartupLocation.Manual, repository.WindowStartupLocation);
                Assert.Equal(CentredOn(Second, repository.Width, repository.Height), repository.Position);

                // Still maximised: placed on the screen first, it maximises there.
                Assert.Equal(WindowState.Maximized, repository.WindowState);
            }
            finally
            {
                windows.CurrentWindow?.Close();
            }
        });
    }

    [Fact]
    public void CloseRepository_BringsTheStartWindowBackOnTheScreenTheRepositoryWindowWasOn()
    {
        _fixture.Run(() =>
        {
            using TestServices services = Build(First, Second);
            IAppWindows windows = services.Get<IAppWindows>();

            try
            {
                windows.ShowStart();
                windows.CurrentWindow!.Position = new PixelPoint(2200, 100);
                windows.ShowRepository();

                windows.ShowStart();

                StartWindow start = Assert.IsType<StartWindow>(windows.CurrentWindow);
                Assert.Equal(WindowStartupLocation.Manual, start.WindowStartupLocation);
                Assert.Equal(CentredOn(Second, start.Width, start.Height), start.Position);
            }
            finally
            {
                windows.CurrentWindow?.Close();
            }
        });
    }

    [Fact]
    public void AMinimisedWindow_HandsOverToTheScreenItWasLastSeenOn()
    {
        _fixture.Run(() =>
        {
            using TestServices services = Build(First, Second);
            AppWindows windows = (AppWindows)services.Get<IAppWindows>();

            try
            {
                windows.ShowStart();
                windows.CurrentWindow!.Position = new PixelPoint(2200, 100);
                windows.ShowRepository();

                Assert.Equal(Second.Bounds, windows.LastScreen);

                // Minimised, it says nothing about a screen by where it is.
                windows.CurrentWindow!.WindowState = WindowState.Minimized;
                windows.ShowStart();

                Window start = windows.CurrentWindow!;
                Assert.Equal(CentredOn(Second, start.Width, start.Height), start.Position);
            }
            finally
            {
                windows.CurrentWindow?.Close();
            }
        });
    }

    [Fact]
    public void OnOneScreen_TheWindowIsCentredOnItAsBefore()
    {
        _fixture.Run(() =>
        {
            using TestServices services = Build(First);
            IAppWindows windows = services.Get<IAppWindows>();

            try
            {
                windows.ShowStart();
                windows.ShowRepository();

                Window repository = windows.CurrentWindow!;
                Assert.Equal(CentredOn(First, repository.Width, repository.Height), repository.Position);
            }
            finally
            {
                windows.CurrentWindow?.Close();
            }
        });
    }

    [Fact]
    public void WithNoScreenKnown_NothingIsPlaced()
    {
        _fixture.Run(() =>
        {
            using TestServices services = Build();
            IAppWindows windows = services.Get<IAppWindows>();

            try
            {
                windows.ShowStart();
                windows.ShowRepository();

                Assert.Equal(WindowStartupLocation.CenterScreen, windows.CurrentWindow!.WindowStartupLocation);
            }
            finally
            {
                windows.CurrentWindow?.Close();
            }
        });
    }

    [Fact]
    public void ThePlacer_FindsAWindowsScreen_ByWhatHoldsMostOfIt()
    {
        _fixture.Run(() =>
        {
            WindowPlacer placer = new(new FixedScreens(First, Second));
            Window window = new() { Width = 1000, Height = 600 };

            try
            {
                window.Show();

                window.Position = new PixelPoint(1620, 100);
                Assert.Equal(Second, placer.ScreenOf(window));

                window.Position = new PixelPoint(1220, 100);
                Assert.Equal(First, placer.ScreenOf(window));
            }
            finally
            {
                window.Close();
            }
        });
    }
}
