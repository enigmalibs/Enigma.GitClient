using System;
using Avalonia;
using Avalonia.Styling;
using Enigma.GitClient.Core.Configuration;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// The toolbar's theme switch, which both windows carry.
/// </summary>
public interface IThemeSwitcher
{
    /// <summary>
    /// Switches to the variant that is not on screen, and makes it the preference.
    /// </summary>
    void Toggle();
}

/// <summary>
/// Default <see cref="IThemeSwitcher"/>.
/// </summary>
/// <remarks>
/// <para>
/// From "follow the system" too: a click on the switch is a choice, and the settings page is where the
/// system is followed again.
/// </para>
/// <para>
/// The variant is put on the application here rather than left to the settings' change handler the
/// application wires at startup: the switch has to work wherever it runs, and applying the same variant
/// a second time is nothing.
/// </para>
/// </remarks>
public sealed class ThemeSwitcher : IThemeSwitcher
{
    private readonly ISettingsService _settings;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="settings">Records the theme the switch chose.</param>
    public ThemeSwitcher(ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
    }

    /// <inheritdoc />
    public void Toggle()
    {
        Application? application = Application.Current;

        if (application is null)
        {
            return;
        }

        bool toLight = application.ActualThemeVariant == ThemeVariant.Dark;

        application.RequestedThemeVariant = toLight ? ThemeVariant.Light : ThemeVariant.Dark;

        ThemePreference chosen = toLight ? ThemePreference.Light : ThemePreference.Dark;
        _settings.Update(current => current with { Theme = chosen });
    }
}
