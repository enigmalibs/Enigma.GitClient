using Avalonia.Controls;

namespace Enigma.GitClient.App.Views;

/// <summary>
/// The window shown while the application starts: the icon, what is starting, and which version of it.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately logic-free and without a ViewModel: it shows two constants and lives for about a
/// second. <see cref="App"/> shows it; <see cref="Services.SplashHandOver"/> holds it to its floor and
/// closes it once the first real window is on screen.
/// </para>
/// <para>
/// The one window in the application that is not resolved from the container: it covers the start,
/// and belongs to none of the windows that follow it.
/// </para>
/// </remarks>
public partial class SplashWindow : Window
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public SplashWindow() => InitializeComponent();
}
