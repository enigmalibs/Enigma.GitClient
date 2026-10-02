using System;

namespace Enigma.GitClient.Desktop.Views;

/// <summary>
/// How long the splash screen stays on screen: a floor, not a fixed duration.
/// </summary>
/// <remarks>
/// <para>
/// Starting takes as long as it takes — a cold machine, a large repository named on the command line —
/// so the splash cannot be shown for a fixed time without either flashing past on a fast start or
/// padding a slow one. The rule is a minimum: the splash is held until <see cref="MinimumDisplay"/>
/// has passed since it appeared, and not one frame longer.
/// </para>
/// <para>
/// The arithmetic lives here, apart from the window, because it is the part of the hand-over a test can
/// pin down: whether one second minus a second and a fifth is zero rather than a negative delay.
/// </para>
/// </remarks>
public static class SplashTiming
{
    /// <summary>
    /// How long the splash is shown for at the very least, measured from the moment it is shown.
    /// </summary>
    /// <remarks>
    /// One second, as Enigma.MarkdownEditor's: long enough to be read as a splash screen rather than
    /// as a flicker, short enough that nobody waits for it.
    /// </remarks>
    public static readonly TimeSpan MinimumDisplay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How much longer the splash has to be held.
    /// </summary>
    /// <param name="elapsed">How long the splash has been on screen.</param>
    /// <param name="minimum">The floor it is held to — normally <see cref="MinimumDisplay"/>.</param>
    /// <returns>
    /// The delay still owed, or <see cref="TimeSpan.Zero"/> once the floor has been reached. Never
    /// negative: the caller hands it straight to a delay, and a negative one throws.
    /// </returns>
    public static TimeSpan RemainingDelay(TimeSpan elapsed, TimeSpan minimum)
    {
        TimeSpan remaining = minimum - elapsed;

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
