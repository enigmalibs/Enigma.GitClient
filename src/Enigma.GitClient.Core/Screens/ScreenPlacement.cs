using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Screens;

/// <summary>
/// Decides which screen a window opens on, and where on it, in plain integer arithmetic.
/// </summary>
/// <remarks>
/// The application's windows open on the screen the reader is working on — the one the previous window
/// was on, or the one the application was last used on — rather than always on the primary one. The
/// rules live here, away from any windowing system, so they can be tested exactly; the desktop side
/// only reads the screens and moves the window.
/// </remarks>
public static class ScreenPlacement
{
    /// <summary>
    /// Finds the screen a remembered one is now, among the screens there are.
    /// </summary>
    /// <param name="screens">The screens there are now.</param>
    /// <param name="hint">The bounds of the screen remembered, or <see langword="null"/> for none.</param>
    /// <returns>
    /// The screen whose bounds are the hint's; else the one holding the hint's centre; else the one it
    /// overlaps most; else the primary screen; else the first — and <see langword="null"/> only when
    /// there is no screen at all.
    /// </returns>
    /// <remarks>
    /// A screen keeps its bounds while the desktop stays as it is, so the first rule nearly always
    /// answers. The others are for a desktop rearranged since: a screen that moved, grew or shrank is
    /// still found, and one that is gone falls back to the primary screen.
    /// </remarks>
    public static ScreenInfo? Choose(IReadOnlyList<ScreenInfo> screens, ScreenArea? hint)
    {
        ArgumentNullException.ThrowIfNull(screens);

        if (screens.Count == 0)
        {
            return null;
        }

        if (hint is { IsEmpty: false } remembered)
        {
            foreach (ScreenInfo screen in screens)
            {
                if (screen.Bounds == remembered)
                {
                    return screen;
                }
            }

            long centreX = remembered.X + (remembered.Width / 2L);
            long centreY = remembered.Y + (remembered.Height / 2L);

            foreach (ScreenInfo screen in screens)
            {
                if (screen.Bounds.Contains(centreX, centreY))
                {
                    return screen;
                }
            }

            if (MostOverlapped(screens, remembered) is { } overlapped)
            {
                return overlapped;
            }
        }

        return PrimaryOrFirst(screens);
    }

    /// <summary>
    /// Finds the screen a window is on: the one holding most of it.
    /// </summary>
    /// <param name="screens">The screens there are.</param>
    /// <param name="window">The window's frame, in physical pixels.</param>
    /// <returns>
    /// The screen the window overlaps most; the primary screen, or the first, when it overlaps none —
    /// and <see langword="null"/> only when there is no screen at all.
    /// </returns>
    public static ScreenInfo? FromWindow(IReadOnlyList<ScreenInfo> screens, ScreenArea window)
    {
        ArgumentNullException.ThrowIfNull(screens);

        return screens.Count == 0 ? null : MostOverlapped(screens, window) ?? PrimaryOrFirst(screens);
    }

    /// <summary>
    /// Works out where a window's top-left corner goes for it to be centred in a working area.
    /// </summary>
    /// <param name="workingArea">The working area of the screen it goes on.</param>
    /// <param name="width">The window's width, in that screen's physical pixels.</param>
    /// <param name="height">The window's height, in that screen's physical pixels.</param>
    /// <returns>
    /// The corner; along a side the window is larger than the working area, the working area's own edge,
    /// so the title bar stays on the screen.
    /// </returns>
    public static (int X, int Y) CenterIn(ScreenArea workingArea, int width, int height)
    {
        int x = width >= workingArea.Width ? workingArea.X : workingArea.X + ((workingArea.Width - width) / 2);
        int y = height >= workingArea.Height ? workingArea.Y : workingArea.Y + ((workingArea.Height - height) / 2);

        return (x, y);
    }

    private static ScreenInfo? MostOverlapped(IReadOnlyList<ScreenInfo> screens, ScreenArea area)
    {
        ScreenInfo? best = null;
        long most = 0;

        foreach (ScreenInfo screen in screens)
        {
            long overlap = screen.Bounds.OverlapWith(area);

            if (overlap > most)
            {
                most = overlap;
                best = screen;
            }
        }

        return best;
    }

    private static ScreenInfo PrimaryOrFirst(IReadOnlyList<ScreenInfo> screens)
    {
        foreach (ScreenInfo screen in screens)
        {
            if (screen.IsPrimary)
            {
                return screen;
            }
        }

        return screens[0];
    }
}
