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
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.Views;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

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
    /// <returns>A task that completes once a window is on screen.</returns>
    Task StartAsync(string? path);

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
    private readonly ILogger<AppWindows> _logger;
    private bool _gitChecked;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="services">Builds the windows, which are transient, and their ViewModels.</param>
    /// <param name="opener">Opens the repository a command-line path points at.</param>
    /// <param name="git">Probes the host's git installation once, when the first window opens.</param>
    /// <param name="logger">Receives startup failures.</param>
    public AppWindows(
        IServiceProvider services,
        IRepositoryOpener opener,
        IGitEnvironment git,
        ILogger<AppWindows> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(opener);
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(logger);

        _services = services;
        _opener = opener;
        _git = git;
        _logger = logger;
    }

    /// <inheritdoc />
    public AppWindowKind Current { get; private set; }

    /// <inheritdoc />
    public Window? CurrentWindow { get; private set; }

    /// <inheritdoc />
    public async Task StartAsync(string? path)
    {
        string? failure = null;

        if (path is { Length: > 0 })
        {
            try
            {
                RepositoryDiscoveryResult discovery = await _opener.OpenAsync(path).ConfigureAwait(true);

                if (discovery.IsFound)
                {
                    ShowRepository();
                    return;
                }

                failure = discovery.Message;
            }
            catch (Exception exception) when (exception is GitCommandException or IOException or ArgumentException)
            {
                _logger.LogWarning(exception, "The repository at {Path} could not be opened", path);
                failure = exception.Message;
            }
        }

        ShowStart();

        if (failure is not null)
        {
            await _services.GetRequiredService<IInfoBarService>().ShowAsync(bar =>
            {
                bar.Title = "Cannot open that folder";
                bar.Message = $"{path}: {failure}";
                bar.Severity = InfoBarSeverity.Warning;
            }).ConfigureAwait(true);
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

        // Shown before the previous one is closed, so there is never a moment without a window —
        // which is what the lifetime reads as "the last window closed".
        window.Show();
        previous?.Close();
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
