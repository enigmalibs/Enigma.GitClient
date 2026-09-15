using System;
using Avalonia;

namespace Enigma.GitClient.App;

/// <summary>
/// Entry point of the Enigma.GitClient desktop application.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Starts the Avalonia classic desktop lifetime.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Builds the Avalonia application. Also used by the XAML previewer and by headless tests.
    /// </summary>
    /// <returns>The configured <see cref="AppBuilder"/>.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
