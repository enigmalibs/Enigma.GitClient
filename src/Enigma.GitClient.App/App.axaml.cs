using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Controls.Diff;
using Enigma.GitClient.App.DependencyInjection;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.Views;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App;

/// <summary>
/// The Avalonia <see cref="Application"/> for Enigma.GitClient, and the composition root.
/// </summary>
/// <remarks>
/// <para>
/// The host is <em>started</em>, never run: Avalonia's classic desktop lifetime runs the
/// application, and the host only supplies configuration, logging and services.
/// </para>
/// <para>
/// Every <c>using</c> directive in this project sits at file scope, above the namespace
/// declaration, and no type is ever written as an inline <c>Avalonia.Xxx</c> reference: inside a
/// namespace starting with <c>Enigma.</c>, <c>Avalonia</c> would bind to the
/// <c>Enigma.Avalonia</c> namespace shipped by Enigma.Avalonia.Desktop.
/// </para>
/// </remarks>
public partial class App : Application
{
    private IHost? _host;

    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // The diff's typography is published as resources, and the styles resolve those keys long
        // before a settings file has been read. Seeding them with the defaults here is what keeps
        // that one source of truth: no second copy of the numbers in App.axaml to drift from the
        // ones DiffTypography derives.
        DiffTypography.Apply(AppSettings.Defaults);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            IHost host = BuildHost();
            host.Start();
            _host = host;

            IServiceProvider services = host.Services;

            // Before the window: the theme is a preference, and a window that paints dark and then
            // flips to light is a worse first impression than one that starts right.
            ISettingsService settings = services.GetRequiredService<ISettingsService>();
            settings.LoadAsync().GetAwaiter().GetResult();
            ApplyTheme(settings.Current.Theme);
            DiffTypography.Apply(settings.Current);
            settings.Changed += (_, e) => ApplyTheme(e.Settings.Theme);
            settings.Changed += (_, e) => DiffTypography.Apply(e.Settings);

            MainWindow window = services.GetRequiredService<MainWindow>();
            window.DataContext = services.GetRequiredService<MainWindowViewModel>();

            RegisterHosts(services, window);

            desktop.MainWindow = window;
            desktop.Exit += OnExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Puts the chosen theme on the application.
    /// </summary>
    /// <param name="preference">What the user chose.</param>
    /// <remarks>
    /// <c>ThemeVariant.Default</c> is Avalonia's "follow the operating system", which is what the
    /// preference calls System.
    /// </remarks>
    internal static void ApplyTheme(ThemePreference preference)
    {
        if (Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = preference switch
        {
            // Written unqualified on purpose: inside a namespace beginning Enigma., an inline
            // Avalonia.Styling reference binds to Enigma.Avalonia and does not compile.
            ThemePreference.Dark => ThemeVariant.Dark,
            ThemePreference.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
    }

    /// <summary>
    /// Builds the application host. Exposed so the headless tests can assert the container resolves
    /// everything the shell needs without starting a window.
    /// </summary>
    /// <returns>The built, unstarted host.</returns>
    internal static IHost BuildHost()
    {
        // A desktop application must not depend on its working directory, so configuration is read
        // from beside the executable.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Logging.SetMinimumLevel(LogLevel.Information);

        ConfigureServices(builder.Services);

        return builder.Build();
    }

    /// <summary>
    /// Registers everything the application needs. Kept separate from <see cref="BuildHost"/> so a
    /// test can validate the whole container without an Avalonia platform behind it.
    /// </summary>
    /// <param name="services">The collection to register into.</param>
    internal static void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddGitClientCore();
        services.AddEnigmaDesktopServices();
        services.AddGitClientApp();
    }

    /// <summary>
    /// Hands the window's overlay hosts and storage provider to the services that need them. All
    /// five run before the window is shown: a service whose host is still unregistered throws the
    /// moment a page asks it for a dialog, an overlay or a picker.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="window">The main window.</param>
    private static void RegisterHosts(IServiceProvider services, MainWindow window)
    {
        services.GetRequiredService<IContentDialogService>().RegisterHost(window.HostDialog);
        services.GetRequiredService<IOverlayService>().RegisterHost(window.HostOverlay);
        services.GetRequiredService<IInfoBarService>().RegisterHost(window.HostInfoBar);
        services.GetRequiredService<IFileDialogService>().SetStorageProvider(window.StorageProvider);
        services.GetRequiredService<IFolderDialogService>().SetStorageProvider(window.StorageProvider);
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        IHost? host = _host;

        if (host is null)
        {
            return;
        }

        _host = null;

        // The process is tearing down and no message pump remains, so blocking here is correct.
        (host.Services.GetService<IRepositoryContext>() as IDisposable)?.Dispose();
        host.StopAsync().GetAwaiter().GetResult();
        host.Dispose();
    }
}
