using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Enigma.GitClient.App.Views;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// The splash screen on screen while the application starts, and what handing the screen over from it
/// takes: waiting out its floor, then closing it.
/// </summary>
/// <remarks>
/// <para>
/// Given to <see cref="IAppWindows.StartAsync"/>, because the window service is what knows the moment
/// the first real window is on screen. The order it keeps is not cosmetic: the application ends when
/// its last window closes, so the splash must close <em>after</em> the window that replaces it has been
/// shown — and must close on a start that failed, too, or it would sit on screen for ever.
/// </para>
/// <para>
/// Time is read off a <see cref="TimeProvider"/>, wall-clock differences rather than timestamps, so a
/// test clock can stand still and prove the splash is held.
/// </para>
/// </remarks>
public sealed class SplashHandOver
{
    private readonly Window _splash;
    private readonly TimeSpan _minimum;
    private readonly TimeProvider _time;
    private readonly DateTimeOffset _shownAt;
    private bool _closed;

    /// <summary>
    /// Initialises a new instance, counting from now.
    /// </summary>
    /// <param name="splash">The splash, already shown.</param>
    /// <param name="minimum">How long it is held at the very least — normally <see cref="SplashTiming.MinimumDisplay"/>.</param>
    /// <param name="time">The clock the floor is measured on.</param>
    public SplashHandOver(Window splash, TimeSpan minimum, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(splash);
        ArgumentNullException.ThrowIfNull(time);

        _splash = splash;
        _minimum = minimum;
        _time = time;
        _shownAt = time.GetUtcNow();
    }

    /// <summary>
    /// Waits for whatever is left of the splash's floor, which is nothing at all on a start slow enough
    /// to have used it up.
    /// </summary>
    /// <returns>A task that completes once the splash may be replaced.</returns>
    public Task WaitForMinimumAsync()
    {
        TimeSpan remaining = SplashTiming.RemainingDelay(_time.GetUtcNow() - _shownAt, _minimum);

        return remaining <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(remaining, _time);
    }

    /// <summary>
    /// Closes the splash. A second call does nothing.
    /// </summary>
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _splash.Close();
    }
}
