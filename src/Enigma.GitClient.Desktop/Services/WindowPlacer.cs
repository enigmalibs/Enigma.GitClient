using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Enigma.GitClient.Core.Screens;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Reads the screens of the desktop a window is on.
/// </summary>
/// <remarks>
/// The one piece of window placement that needs a windowing system, behind its own seam: what a screen
/// is and where a window goes on it is plain arithmetic in <see cref="ScreenPlacement"/>, and a test
/// stands a desktop of its own choosing in here.
/// </remarks>
public interface IScreenReader
{
    /// <summary>
    /// Reads the screens there are.
    /// </summary>
    /// <param name="window">A window of the application, which the platform answers through.</param>
    /// <returns>The screens; empty where the platform says nothing about them.</returns>
    IReadOnlyList<ScreenInfo> Read(Window window);
}

/// <summary>
/// Default <see cref="IScreenReader"/>: Avalonia's own <see cref="Screens"/>.
/// </summary>
public sealed class AvaloniaScreenReader : IScreenReader
{
    /// <inheritdoc />
    public IReadOnlyList<ScreenInfo> Read(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Screens is not { } screens)
        {
            return [];
        }

        List<ScreenInfo> read = new(screens.ScreenCount);

        foreach (Screen screen in screens.All)
        {
            read.Add(new ScreenInfo(ToArea(screen.Bounds), ToArea(screen.WorkingArea), screen.Scaling, screen.IsPrimary));
        }

        return read;
    }

    private static ScreenArea ToArea(PixelRect rectangle) => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
}

/// <summary>
/// Finds the screen a window is on, and puts a window on a screen before it is shown.
/// </summary>
public interface IWindowPlacer
{
    /// <summary>
    /// Reads the screens there are.
    /// </summary>
    /// <param name="window">A window of the application.</param>
    /// <returns>The screens; empty where the platform says nothing about them.</returns>
    IReadOnlyList<ScreenInfo> ScreensOf(Window window);

    /// <summary>
    /// Finds the screen holding most of a window.
    /// </summary>
    /// <param name="window">The window, shown.</param>
    /// <returns>The screen, or <see langword="null"/> where the platform says nothing about screens.</returns>
    ScreenInfo? ScreenOf(Window window);

    /// <summary>
    /// Puts a window that is not shown yet in the middle of a screen's working area.
    /// </summary>
    /// <param name="window">The window, before <see cref="Window.Show()"/>.</param>
    /// <param name="screen">The screen it goes on.</param>
    /// <remarks>
    /// Sized at the target screen's own scaling, so a window moving between a 100 % and a 150 % screen
    /// is centred on the one it lands on. A window that maximises keeps its state: placed on the screen
    /// first, it maximises there.
    /// </remarks>
    void PlaceOn(Window window, ScreenInfo screen);
}

/// <summary>
/// Default <see cref="IWindowPlacer"/>.
/// </summary>
public sealed class WindowPlacer : IWindowPlacer
{
    // What a window that states no size is measured as: never seen in this application, whose windows
    // all have one.
    private const double FallbackWidth = 800;
    private const double FallbackHeight = 600;

    private readonly IScreenReader _screens;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="screens">Reads the desktop's screens.</param>
    public WindowPlacer(IScreenReader screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        _screens = screens;
    }

    /// <inheritdoc />
    public IReadOnlyList<ScreenInfo> ScreensOf(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return _screens.Read(window);
    }

    /// <inheritdoc />
    public ScreenInfo? ScreenOf(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return ScreenPlacement.FromWindow(ScreensOf(window), FrameOf(window));
    }

    /// <inheritdoc />
    public void PlaceOn(Window window, ScreenInfo screen)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(screen);

        (double width, double height) = SizeOf(window);

        (int x, int y) = ScreenPlacement.CenterIn(
            screen.WorkingArea,
            (int)Math.Round(width * screen.Scaling),
            (int)Math.Round(height * screen.Scaling));

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(x, y);
    }

    /// <summary>
    /// The window's frame on the desktop, in physical pixels.
    /// </summary>
    internal static ScreenArea FrameOf(Window window)
    {
        Size size = window.FrameSize is { Width: > 0, Height: > 0 } frame ? frame : window.ClientSize;

        if (size.Width <= 0 || size.Height <= 0)
        {
            (double width, double height) = SizeOf(window);
            size = new Size(width, height);
        }

        double scaling = window.DesktopScaling > 0 ? window.DesktopScaling : 1;

        return new ScreenArea(
            window.Position.X,
            window.Position.Y,
            (int)Math.Round(size.Width * scaling),
            (int)Math.Round(size.Height * scaling));
    }

    /// <summary>
    /// The size a window asks for, in device-independent pixels: its restore size for a window that
    /// opens maximised.
    /// </summary>
    private static (double Width, double Height) SizeOf(Window window)
        => (double.IsNaN(window.Width) || window.Width <= 0 ? FallbackWidth : window.Width,
            double.IsNaN(window.Height) || window.Height <= 0 ? FallbackHeight : window.Height);
}
