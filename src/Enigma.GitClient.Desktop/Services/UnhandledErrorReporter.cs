using System;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// The safety net: every exception nothing else caught ends up here, is logged, and is shown in a
/// dialog that says the application is still running.
/// </summary>
/// <remarks>
/// A safety net, not the error handling: a failure the application knows about is said where it
/// happens, in that place's own words, and never reaches this.
/// </remarks>
public interface IUnhandledErrorReporter
{
    /// <summary>
    /// Logs an exception and shows it, from any thread.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <param name="source">Where it was caught.</param>
    void Report(Exception exception, UnhandledErrorSource source);

    /// <summary>
    /// Says the application is closing: from here on a cancellation is never worth a word.
    /// </summary>
    void ShutdownStarted();
}

/// <summary>
/// Default <see cref="IUnhandledErrorReporter"/>.
/// </summary>
/// <remarks>
/// <para>
/// The log is written at once, on whatever thread reported, because that thread may be about to end
/// the process. What is shown is decided on the UI thread, by <see cref="UnhandledErrorPolicy"/>.
/// </para>
/// <para>
/// The dialog is shown on the current window's dialog host, which every question of that window
/// shares: showing it over another question would replace that question's content, so it waits until
/// the host is free, and before the first window — under the splash — until there is one. If the
/// dialog itself fails, it is logged and no dialog is tried again in this session: the net must not
/// loop on itself.
/// </para>
/// </remarks>
public sealed class UnhandledErrorReporter : IUnhandledErrorReporter
{
    private readonly IAppWindows _windows;
    private readonly IContentDialogService _dialogs;
    private readonly ISystemInterop _interop;
    private readonly ILogger<UnhandledErrorReporter> _logger;
    private readonly UnhandledErrorPolicy _policy;

    // The host the batch on screen is shown in, while it is; and which showing that is, so a showing
    // given up for a window that went away does not close the next one when it finally returns.
    private ContentDialog? _presentedOn;
    private int _showing;

    // The question the next dialog waits behind, while it waits.
    private ContentDialog? _waitingFor;

    private bool _waitingForWindow;
    private bool _broken;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="windows">Says which window is on screen, and when another one is.</param>
    /// <param name="dialogs">Shows the dialog on that window.</param>
    /// <param name="interop">Copies the details to the clipboard.</param>
    /// <param name="time">Measures how long ago an error was seen.</param>
    /// <param name="logger">Receives every exception, in full.</param>
    public UnhandledErrorReporter(
        IAppWindows windows,
        IContentDialogService dialogs,
        ISystemInterop interop,
        TimeProvider time,
        ILogger<UnhandledErrorReporter> logger)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _windows = windows;
        _dialogs = dialogs;
        _interop = interop;
        _logger = logger;
        _policy = new UnhandledErrorPolicy(time);

        _windows.CurrentWindowChanged += OnCurrentWindowChanged;
    }

    /// <inheritdoc />
    public void Report(Exception exception, UnhandledErrorSource source)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (_policy.IsIgnoredCancellation(exception))
        {
            _logger.LogDebug(exception, "A cancellation reached the safety net ({Source}); not an error", source);
            return;
        }

        _logger.LogError(exception, "An unhandled exception reached the safety net ({Source})", source);

        Dispatcher.UIThread.Post(() => Offer(exception));
    }

    /// <inheritdoc />
    public void ShutdownStarted() => _policy.IsShuttingDown = true;

    private void Offer(Exception exception)
    {
        switch (_policy.Offer(exception))
        {
            case UnhandledErrorDecision.Queue:
                PresentAsync().Forget();
                break;
            case UnhandledErrorDecision.Suppress:
                _logger.LogDebug("The same error was shown less than {Window} ago; it is not shown again", UnhandledErrorPolicy.RepeatWindow);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Shows the pending batch, once there is a window with a free host. Never throws: nothing awaits it.
    /// </summary>
    private async Task PresentAsync()
    {
        if (_broken || _presentedOn is not null || _policy.Pending is null)
        {
            return;
        }

        if ((_windows.CurrentWindow as IHostWindow)?.DialogHost is not { } host)
        {
            // Under the splash, or between two windows: the next window brings a host.
            _waitingForWindow = true;
            return;
        }

        if (host.IsOpen)
        {
            WaitFor(host);
            return;
        }

        if (_policy.BeginShow() is not { } batch)
        {
            return;
        }

        int showing = ++_showing;
        _presentedOn = host;

        try
        {
            UnhandledErrorView view = new() { DataContext = new UnhandledErrorViewModel(batch, _interop) };

            await _dialogs.ShowAsync(dialog =>
            {
                dialog.Title = "Something went wrong";
                dialog.Content = view;
                dialog.CloseButtonText = "Close";
                dialog.DefaultButton = DefaultButton.Close;
                dialog.IconData = PhosphorIconSet.Instance
                    .GetGlyph(PhosphorIcon.Warning, PhosphorWeight.Regular)
                    .ToGeometry();
                dialog.IconBrush = new SolidColorBrush(Color.Parse("#E8A33D"));
            }).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            // Logged, never reported: a dialog that cannot be shown would otherwise report itself again
            // and again.
            _broken = true;
            _logger.LogError(exception, "The safety net's dialog could not be shown; later errors are only logged");
        }

        if (showing != _showing)
        {
            // Given up already, for a window that went away with the dialog on it.
            return;
        }

        _presentedOn = null;
        _policy.EndShow();

        if (_policy.Pending is not null)
        {
            PresentAsync().Forget();
        }
    }

    /// <summary>
    /// Waits for the question on the host to be answered, then tries again.
    /// </summary>
    private void WaitFor(ContentDialog host)
    {
        if (ReferenceEquals(_waitingFor, host))
        {
            return;
        }

        _waitingFor = host;
        host.Closed += OnWaitedClosed;
    }

    private void OnWaitedClosed(object? sender, DialogResult e)
    {
        if (sender is ContentDialog host)
        {
            host.Closed -= OnWaitedClosed;
        }

        _waitingFor = null;

        // Posted: the question's own continuation runs first, and may ask the next question at once.
        Dispatcher.UIThread.Post(() => PresentAsync().Forget(), DispatcherPriority.Background);
    }

    /// <summary>
    /// Another window is on screen: what waited for a host has one, and a dialog left on the window that
    /// went away is given up, so its batch does not hold the net forever.
    /// </summary>
    private void OnCurrentWindowChanged(object? sender, EventArgs e)
    {
        ContentDialog? host = (_windows.CurrentWindow as IHostWindow)?.DialogHost;

        if (_presentedOn is not null && !ReferenceEquals(_presentedOn, host))
        {
            _showing++;
            _presentedOn = null;
            _policy.EndShow();
        }

        if (_waitingFor is not null && !ReferenceEquals(_waitingFor, host))
        {
            _waitingFor.Closed -= OnWaitedClosed;
            _waitingFor = null;
        }

        if (_waitingForWindow || _policy.Pending is not null)
        {
            _waitingForWindow = false;
            Dispatcher.UIThread.Post(() => PresentAsync().Forget(), DispatcherPriority.Background);
        }
    }
}
