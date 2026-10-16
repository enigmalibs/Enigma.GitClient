using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Enigma.Avalonia.Desktop.Controls;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Screens;
using Enigma.GitClient.Desktop.ViewModels;
using Enigma.GitClient.Desktop.Views;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// The two windows the application can show.
/// </summary>
public enum AppWindowKind
{
    /// <summary>No window has been shown yet.</summary>
    None,

    /// <summary>The start window: the repositories, the integrations and the settings.</summary>
    Start,

    /// <summary>The window working on the open repository.</summary>
    Repository,
}

/// <summary>
/// A window that carries the three overlay hosts the Enigma services draw into.
/// </summary>
/// <remarks>
/// The services are singletons and a window is not, so every window that is shown hands its hosts
/// over again — the library lets the last registration win.
/// </remarks>
public interface IHostWindow
{
    /// <summary>Gets the host the content-dialog service shows its dialogs in.</summary>
    ContentDialog DialogHost { get; }

    /// <summary>Gets the host the overlay service shows its blocking layer in.</summary>
    Overlay OverlayHost { get; }

    /// <summary>Gets the host the info-bar service shows its notifications in.</summary>
    InfoBar InfoBarHost { get; }
}

/// <summary>
/// Owns which window is on screen: the start window, or the window for the open repository.
/// </summary>
/// <remarks>
/// One repository per process. Several repositories side by side are several instances of the
/// application, each with its own start window — which keeps every singleton in the container
/// meaning exactly one thing.
/// </remarks>
public interface IAppWindows
{
    /// <summary>Gets which window is on screen.</summary>
    AppWindowKind Current { get; }

    /// <summary>Gets the window on screen, or <see langword="null"/> before the first one.</summary>
    Window? CurrentWindow { get; }

    /// <summary>
    /// Shows the first window: the repository window when <paramref name="path"/> opens as a
    /// repository, the start window otherwise.
    /// </summary>
    /// <param name="path">A path given on the command line, or <see langword="null"/>.</param>
    /// <param name="splash">
    /// The splash screen covering the start, or <see langword="null"/> when there is none: held until
    /// its floor has passed, then closed once the first window is on screen — or on the way out of a
    /// start that failed.
    /// </param>
    /// <returns>A task that completes once a window is on screen.</returns>
    Task StartAsync(string? path, SplashHandOver? splash = null);

    /// <summary>Shows the start window in place of whatever is on screen.</summary>
    void ShowStart();

    /// <summary>Shows the repository window in place of whatever is on screen.</summary>
    void ShowRepository();
}

/// <summary>
/// Default <see cref="IAppWindows"/>.
/// </summary>
public sealed class AppWindows : IAppWindows
{
    private readonly IServiceProvider _services;
    private readonly IRepositoryOpener _opener;
    private readonly IGitEnvironment _git;
    private readonly IWindowPlacer _placer;
    private readonly ISettingsService _settings;
    private readonly ILogger<AppWindows> _logger;
    private bool _gitChecked;

    // The screen the current window was last seen on while not minimised: where the next one goes
    // when this one hands over from the taskbar.
    private ScreenArea? _lastScreen;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="services">Builds the windows, which are transient, and their ViewModels.</param>
    /// <param name="opener">Opens the repository a command-line path points at.</param>
    /// <param name="git">Probes the host's git installation once, when the first window opens.</param>
    /// <param name="placer">Finds the screen a window is on, and puts the next window on it.</param>
    /// <param name="settings">Remembers the screen the application was last used on, for the next start.</param>
    /// <param name="logger">Receives startup failures.</param>
    public AppWindows(
        IServiceProvider services,
        IRepositoryOpener opener,
        IGitEnvironment git,
        IWindowPlacer placer,
        ISettingsService settings,
        ILogger<AppWindows> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(opener);
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(placer);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _services = services;
        _opener = opener;
        _git = git;
        _placer = placer;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Gets the bounds of the screen the current window was last seen on while not minimised, or
    /// <see langword="null"/> before any window was.
    /// </summary>
    public ScreenArea? LastScreen => _lastScreen;

    /// <summary>
    /// Puts a window of a fresh start — the splash, or the first window — on the screen the application
    /// was last used on: the primary screen when that one is gone or was never recorded.
    /// </summary>
    /// <param name="placer">Reads the screens and moves the window.</param>
    /// <param name="window">The window, before it is shown.</param>
    /// <param name="hint">The remembered screen's bounds, or <see langword="null"/>.</param>
    /// <returns>The screen it was put on, or <see langword="null"/> when the platform knows of none.</returns>
    public static ScreenInfo? PlaceForStart(IWindowPlacer placer, Window window, ScreenArea? hint)
    {
        ArgumentNullException.ThrowIfNull(placer);
        ArgumentNullException.ThrowIfNull(window);

        if (ScreenPlacement.Choose(placer.ScreensOf(window), hint) is not { } screen)
        {
            return null;
        }

        placer.PlaceOn(window, screen);
        return screen;
    }

    /// <inheritdoc />
    public AppWindowKind Current { get; private set; }

    /// <inheritdoc />
    public Window? CurrentWindow { get; private set; }

    /// <inheritdoc />
    public async Task StartAsync(string? path, SplashHandOver? splash = null)
    {
        string? failure = null;
        bool opened = false;

        try
        {
            if (path is { Length: > 0 })
            {
                try
                {
                    RepositoryDiscoveryResult discovery = await _opener.OpenAsync(path).ConfigureAwait(true);

                    opened = discovery.IsFound;
                    failure = opened ? null : discovery.Message;
                }
                catch (Exception exception) when (exception is GitCommandException or IOException or ArgumentException)
                {
                    _logger.LogWarning(exception, "The repository at {Path} could not be opened", path);
                    failure = exception.Message;
                }
            }

            // After the slow part and before the first window: a start that took longer than the floor
            // waits for nothing, and one fast enough to make the splash flash waits out the rest.
            if (splash is not null)
            {
                await splash.WaitForMinimumAsync().ConfigureAwait(true);
            }

            if (opened)
            {
                ShowRepository();
            }
            else
            {
                ShowStart();
            }
        }
        finally
        {
            // Once the window that replaces it is on screen, so the splash is never the last window to
            // close — and here, in a finally, so a start that threw still takes it away and ends.
            splash?.Close();
        }

        if (failure is not null)
        {
            _services.GetRequiredService<IInfoBarService>()
                .Notify("Cannot open that folder", $"{path}: {failure}", InfoBarSeverity.Warning);
        }
    }

    /// <inheritdoc />
    public void ShowStart() => Show(AppWindowKind.Start, typeof(StartWindow), typeof(StartWindowViewModel));

    /// <inheritdoc />
    public void ShowRepository() => Show(AppWindowKind.Repository, typeof(MainWindow), typeof(MainWindowViewModel));

    private void Show(AppWindowKind kind, Type windowType, Type viewModelType)
    {
        if (Current == kind && CurrentWindow is not null)
        {
            return;
        }

        Window window = (Window)_services.GetRequiredService(windowType);
        Window? previous = CurrentWindow;

        // Before the new window binds: the pages are built once and kept, and a page still inside
        // the window being replaced cannot be put into the next one — a control has one parent.
        // Taking the ViewModel away empties that window's content, which frees its page.
        if (previous is not null)
        {
            previous.DataContext = null;
        }

        window.DataContext = _services.GetRequiredService(viewModelType);

        RegisterHosts(window);

        CurrentWindow = window;
        Current = kind;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = window;
        }

        if (!_gitChecked)
        {
            _gitChecked = true;
            window.Opened += OnFirstWindowOpened;
        }

        // On the screen the previous window was on, rather than wherever the platform puts a new
        // one — the primary screen. The first window of a start goes where the application was last
        // used.
        if (previous is not null)
        {
            if (ScreenToHandOverTo(previous, window) is { } screen)
            {
                _placer.PlaceOn(window, screen);
                Remember(screen.Bounds);
            }
        }
        else if (PlaceForStart(_placer, window, _settings.Current.LastScreen) is { } first)
        {
            Remember(first.Bounds);
        }

        window.Opened += OnWindowSeen;
        window.PositionChanged += OnWindowSeen;
        window.Closing += OnWindowClosing;

        // Shown before the previous one is closed, so there is never a moment without a window —
        // which is what the lifetime reads as "the last window closed".
        window.Show();

        if (previous is not null)
        {
            previous.Opened -= OnWindowSeen;
            previous.PositionChanged -= OnWindowSeen;
            previous.Closing -= OnWindowClosing;
            previous.Close();
        }
    }

    /// <summary>
    /// Remembers a screen: for the next hand-over, and in the settings for the next start. Writing a
    /// screen that is already remembered changes nothing and costs no write.
    /// </summary>
    private void Remember(ScreenArea screen)
    {
        _lastScreen = screen;
        _settings.Update(current => current with { LastScreen = screen });
    }

    /// <summary>
    /// Remembers the screen the current window is on as it closes — the quit by ✕ the next start
    /// opens after. A minimised window says nothing by where it is, so the last screen it was seen on
    /// stands.
    /// </summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (sender is Window window
            && ReferenceEquals(window, CurrentWindow)
            && window.WindowState != WindowState.Minimized
            && _placer.ScreenOf(window) is { } screen)
        {
            Remember(screen.Bounds);
        }
    }

    /// <summary>
    /// Finds, among the screens there are now, the one a window handing over is on — the one it was
    /// last seen on when it is minimised, where the taskbar's own position says nothing.
    /// </summary>
    /// <param name="previous">The window handing over.</param>
    /// <param name="next">The window taking over, which reads the screens.</param>
    /// <returns>The screen, or <see langword="null"/> when none is known.</returns>
    private ScreenInfo? ScreenToHandOverTo(Window previous, Window next)
    {
        ScreenArea? hint = previous.WindowState == WindowState.Minimized
            ? _lastScreen
            : _placer.ScreenOf(previous)?.Bounds ?? _lastScreen;

        return hint is null ? null : ScreenPlacement.Choose(_placer.ScreensOf(next), hint);
    }

    /// <summary>
    /// Remembers the screen the current window is on, each time it opens or moves while not minimised.
    /// </summary>
    private void OnWindowSeen(object? sender, EventArgs e)
    {
        if (sender is Window window
            && ReferenceEquals(window, CurrentWindow)
            && window.WindowState != WindowState.Minimized
            && _placer.ScreenOf(window) is { } screen)
        {
            Remember(screen.Bounds);
        }
    }

    /// <summary>
    /// Hands a window's overlay hosts and its storage provider to the services that draw into them.
    /// </summary>
    /// <param name="window">The window about to be shown.</param>
    private void RegisterHosts(Window window)
    {
        if (window is IHostWindow hosts)
        {
            _services.GetRequiredService<IContentDialogService>().RegisterHost(hosts.DialogHost);
            _services.GetRequiredService<IOverlayService>().RegisterHost(hosts.OverlayHost);
            _services.GetRequiredService<IInfoBarService>().RegisterHost(hosts.InfoBarHost);
        }

        if (window is IToolDialogHostWindow tools)
        {
            _services.GetRequiredService<IToolDialogService>().RegisterHost(tools.ToolDialogHost);
        }

        _services.GetRequiredService<IFileDialogService>().SetStorageProvider(window.StorageProvider);
        _services.GetRequiredService<IFolderDialogService>().SetStorageProvider(window.StorageProvider);
    }

    /// <summary>
    /// The one place an <c>async void</c> is allowed: a UI event handler that immediately delegates
    /// to a Task-returning method.
    /// </summary>
    private async void OnFirstWindowOpened(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Opened -= OnFirstWindowOpened;
        }

        await CheckGitAsync();
    }

    /// <summary>
    /// Says so, once, when git is not usable: the whole application drives it.
    /// </summary>
    /// <returns>A task that completes once the check has run.</returns>
    private async Task CheckGitAsync()
    {
        GitAvailability availability = await _git.GetAvailabilityAsync().ConfigureAwait(true);

        if (availability.IsUsable)
        {
            _logger.LogInformation("Using git {Version} at {Path}", availability.Version, availability.ExecutablePath);
            return;
        }

        _logger.LogError("git is not usable: {Message}", availability.Message);

        await _services.GetRequiredService<IContentDialogService>().ShowAsync(dialog =>
        {
            dialog.Title = "git is required";
            dialog.Content = availability.Message
                + "\n\nEnigma.GitClient drives the real git executable, so your existing keys, "
                + "credential helpers and configuration keep working. Install git 2.20 or newer, "
                + "then restart the application.";
            dialog.IconData = PhosphorIconSet.Instance
                .GetGlyph(PhosphorIcon.Warning, PhosphorWeight.Regular)
                .ToGeometry();
            dialog.IconBrush = new SolidColorBrush(Color.Parse("#E8A33D"));
            dialog.CloseButtonText = "Close";
        }).ConfigureAwait(true);
    }
}
