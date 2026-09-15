using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Enigma.GitClient.App;

/// <summary>
/// The Avalonia <see cref="Application"/> for Enigma.GitClient.
/// </summary>
/// <remarks>
/// <para>
/// This bootstrap is deliberately minimal at this stage: it proves the Avalonia and
/// Enigma.Avalonia.Desktop package graph resolves and the theme dictionary merges. The navigation
/// shell, the host controls and the dependency-injection composition root arrive with the app-shell
/// work item.
/// </para>
/// <para>
/// Every <c>using</c> directive in this project sits at file scope, above the namespace
/// declaration, and no type is ever written as an inline <c>Avalonia.Xxx</c> reference: inside a
/// namespace starting with <c>Enigma.</c>, <c>Avalonia</c> would bind to the
/// <c>Enigma.Avalonia</c> namespace shipped by Enigma.Avalonia.Desktop instead.
/// </para>
/// </remarks>
public partial class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "Enigma.GitClient",
                Width = 1280,
                Height = 800,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
