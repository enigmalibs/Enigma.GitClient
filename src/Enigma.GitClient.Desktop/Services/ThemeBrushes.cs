using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Brushes of the theme that code hands to a control, where markup would write a
/// <c>DynamicResource</c>.
/// </summary>
/// <remarks>
/// The theme's brushes are one instance per key whose colour follows the active variant, so the brush
/// handed over recolours itself on a theme switch.
/// </remarks>
internal static class ThemeBrushes
{
    /// <summary>
    /// The key of Enigma.Avalonia.Desktop's warning brush.
    /// </summary>
    public const string WarningKey = "EnigmaWarningBrush";

    /// <summary>
    /// Gets the theme's warning brush, or <see langword="null"/> outside a running application, where
    /// a control falls back to its own.
    /// </summary>
    public static IBrush? Warning => Find(WarningKey);

    private static IBrush? Find(string key)
        => Application.Current is { } application
            && application.TryFindResource(key, application.ActualThemeVariant, out object? value)
            && value is IBrush brush
                ? brush
                : null;
}
