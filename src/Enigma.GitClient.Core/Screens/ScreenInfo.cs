namespace Enigma.GitClient.Core.Screens;

/// <summary>
/// One screen of the desktop, as a window is placed on it.
/// </summary>
/// <param name="Bounds">The whole screen, in physical pixels.</param>
/// <param name="WorkingArea">The part a window may use: the screen less its taskbars and docks.</param>
/// <param name="Scaling">How many physical pixels one device-independent pixel takes on it.</param>
/// <param name="IsPrimary">Whether it is the primary screen.</param>
public sealed record ScreenInfo(ScreenArea Bounds, ScreenArea WorkingArea, double Scaling, bool IsPrimary);
