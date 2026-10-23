using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Enigma.Avalonia.Desktop.Controls;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Screens;
using Enigma.GitClient.Desktop.Services;

namespace Enigma.GitClient.Desktop.UnitTests.Infrastructure;

/// <summary>
/// What a page asked the info bar to show.
/// </summary>
/// <param name="Title">The notification's title.</param>
/// <param name="Message">The notification's message.</param>
/// <param name="Severity">How serious it was.</param>
/// <param name="DisplayDuration">
/// How long the bar was to stay before closing itself, or <see langword="null"/> for a bar that stays
/// until it is dismissed.
/// </param>
public sealed record RecordedNotification(
    string Title,
    string Message,
    InfoBarSeverity Severity,
    TimeSpan? DisplayDuration);

/// <summary>
/// An <see cref="IInfoBarService"/> that records instead of showing.
/// </summary>
/// <remarks>
/// The real service drives an animated control on a live visual tree; driving it from a test is
/// both slow and beside the point. Recording turns "did the page report the failure?" into a
/// straightforward assertion.
/// </remarks>
public sealed class RecordingInfoBarService : IInfoBarService
{
    private readonly List<RecordedNotification> _shown = [];

    private TaskCompletionSource? _open;

    /// <summary>
    /// Gets the notifications a page has raised, oldest first.
    /// </summary>
    public IReadOnlyList<RecordedNotification> Shown => _shown;

    /// <summary>
    /// Gets or sets a value indicating whether a shown bar stays open until <see cref="HideAsync"/>,
    /// as the real one stays until it is closed.
    /// </summary>
    /// <remarks>
    /// Off by default, so <see cref="ShowAsync"/> completes at once. On, the task it returns completes
    /// only when the bar is hidden — every task handed out while the bar was open, as the real bar
    /// completes them all when it closes. That is what lets a test prove the code under it does not
    /// wait for a bar to be closed.
    /// </remarks>
    public bool HoldsOpen { get; set; }

    /// <summary>
    /// Gets a value indicating whether a bar is being held open.
    /// </summary>
    public bool IsOpen => _open is not null;

    /// <summary>
    /// Gets the most recent notification, or <see langword="null"/> when nothing has been shown.
    /// </summary>
    public RecordedNotification? Last => _shown.Count == 0 ? null : _shown[^1];

    /// <inheritdoc />
    public void RegisterHost(InfoBar infoBar)
    {
    }

    /// <inheritdoc />
    public Task ShowAsync(Action<InfoBar>? configure = null)
    {
        InfoBar bar = new();
        configure?.Invoke(bar);

        _shown.Add(new RecordedNotification(
            bar.Title ?? string.Empty,
            bar.Message ?? string.Empty,
            bar.Severity,
            bar.DisplayDuration));

        if (!HoldsOpen)
        {
            return Task.CompletedTask;
        }

        _open ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _open.Task;
    }

    /// <inheritdoc />
    public Task HideAsync()
    {
        TaskCompletionSource? open = _open;
        _open = null;
        open?.TrySetResult();

        return Task.CompletedTask;
    }
}

/// <summary>
/// An <see cref="IOverlayService"/> that records instead of showing.
/// </summary>
public sealed class RecordingOverlayService : IOverlayService
{
    /// <summary>
    /// Gets the control most recently shown on the overlay.
    /// </summary>
    public Control? Content { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the overlay is currently up. A page that leaves this true
    /// after an operation has left the window unusable.
    /// </summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Gets how many times the overlay has been shown.
    /// </summary>
    public int ShowCount { get; private set; }

    /// <inheritdoc />
    public void RegisterHost(Overlay presenter)
    {
    }

    /// <inheritdoc />
    public Task ShowAsync(Control control)
    {
        Content = control;
        IsOpen = true;
        ShowCount++;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task HideAsync()
    {
        IsOpen = false;
        return Task.CompletedTask;
    }
}

/// <summary>
/// An <see cref="IContentDialogService"/> that answers immediately with a scripted result.
/// </summary>
public sealed class ScriptedContentDialogService : IContentDialogService
{
    private readonly List<ContentDialog> _shown = [];

    private readonly Queue<DialogResult> _script = new();

    /// <summary>
    /// Gets or sets the result every dialog returns once <see cref="Script"/> has run out.
    /// </summary>
    public DialogResult Result { get; set; } = DialogResult.None;

    /// <summary>
    /// Gets the dialogs a page has raised, already configured.
    /// </summary>
    public IReadOnlyList<ContentDialog> Shown => _shown;

    /// <summary>
    /// Gets the last dialog raised, or <see langword="null"/> when none was.
    /// </summary>
    public ContentDialog? Last => _shown.Count == 0 ? null : _shown[^1];

    /// <summary>
    /// Gets or sets a hook run on each dialog once it is configured, for a test that has to fill a
    /// form in before answering it.
    /// </summary>
    public Action<ContentDialog>? OnShown { get; set; }

    /// <summary>
    /// Queues the answers a sequence of dialogs receives, in order.
    /// </summary>
    /// <param name="results">The answers.</param>
    public void Script(params DialogResult[] results)
    {
        ArgumentNullException.ThrowIfNull(results);

        foreach (DialogResult result in results)
        {
            _script.Enqueue(result);
        }
    }

    /// <inheritdoc />
    public void RegisterHost(ContentDialog dialog)
    {
    }

    /// <inheritdoc />
    public Task<DialogResult> ShowMessageAsync(string title, string message, string closeButtonText = "OK")
        => Task.FromResult(_script.Count > 0 ? _script.Dequeue() : Result);

    /// <inheritdoc />
    public Task<DialogResult> ShowAsync(Action<ContentDialog> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        ContentDialog dialog = new();
        configure(dialog);
        _shown.Add(dialog);
        OnShown?.Invoke(dialog);

        return Task.FromResult(_script.Count > 0 ? _script.Dequeue() : Result);
    }

    /// <summary>
    /// Gets how many times a dialog was closed by the code rather than by an answer.
    /// </summary>
    public int Hidden { get; private set; }

    /// <inheritdoc />
    public Task HideAsync()
    {
        Hidden++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// An <see cref="ISystemInterop"/> that records what was asked of the desktop instead of doing it.
/// </summary>
/// <remarks>
/// The real one puts things on the developer's clipboard and opens their file manager, which is not
/// something a test run may do.
/// </remarks>
public sealed class RecordingSystemInterop : ISystemInterop
{
    /// <summary>Gets the texts copied to the clipboard, oldest first.</summary>
    public List<string> Copied { get; } = [];

    /// <summary>Gets the paths handed to the desktop's default handler.</summary>
    public List<string> Opened { get; } = [];

    /// <summary>Gets the paths the file manager was asked to show.</summary>
    public List<string> Revealed { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether a browser is pretended to exist, so a test can drive
    /// the "nothing opened it" path too.
    /// </summary>
    public bool OpensUrls { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether opening a path is pretended to work.
    /// </summary>
    public bool OpensPaths { get; set; } = true;

    /// <summary>Gets the directories a terminal was asked for, oldest first.</summary>
    public List<string> Terminals { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether a terminal is pretended to exist.
    /// </summary>
    public bool OpensTerminals { get; set; } = true;

    /// <inheritdoc />
    public Task CopyTextAsync(string text)
    {
        Copied.Add(text);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> OpenPathAsync(string path)
    {
        Opened.Add(path);
        return Task.FromResult(OpensPaths);
    }

    /// <inheritdoc />
    public Task<bool> RevealPathAsync(string path)
    {
        Revealed.Add(path);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> OpenUrlAsync(string url)
    {
        Opened.Add(url);
        return Task.FromResult(OpensUrls);
    }

    /// <inheritdoc />
    public Task<bool> OpenTerminalAsync(string directory)
    {
        Terminals.Add(directory);
        return Task.FromResult(OpensTerminals);
    }
}

/// <summary>
/// An <see cref="IAppWindows"/> that records which window was asked for instead of opening one.
/// </summary>
/// <remarks>
/// A page test is about the page. A real window swap would leave windows open on the headless
/// platform after the test has finished; the swap itself is covered by its own tests.
/// </remarks>
public sealed class RecordingAppWindows : IAppWindows
{
    /// <summary>Gets every window asked for, oldest first.</summary>
    public List<AppWindowKind> Requested { get; } = [];

    /// <summary>Gets the paths <see cref="StartAsync"/> was given.</summary>
    public List<string?> Started { get; } = [];

    /// <inheritdoc />
    public AppWindowKind Current { get; private set; }

    /// <inheritdoc />
    public Window? CurrentWindow { get; private set; }

    /// <inheritdoc />
    public event EventHandler? CurrentWindowChanged;

    /// <summary>
    /// Puts a window on screen as the real coordinator would, its hosts registered, for a test that needs
    /// one — the safety net's, which shows its dialog on the current window's host.
    /// </summary>
    /// <param name="window">The window, or <see langword="null"/> for none.</param>
    public void SetCurrentWindow(Window? window)
    {
        CurrentWindow = window;
        CurrentWindowChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public Task StartAsync(string? path, SplashHandOver? splash = null, ScreenArea? screen = null)
    {
        Started.Add(path);
        splash?.Close();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void ShowStart() => Record(AppWindowKind.Start);

    /// <inheritdoc />
    public void ShowRepository() => Record(AppWindowKind.Repository);

    private void Record(AppWindowKind kind)
    {
        Requested.Add(kind);
        Current = kind;
    }
}

/// <summary>
/// An <see cref="IInstanceLauncher"/> that records what it was asked to start instead of starting it.
/// </summary>
public sealed class RecordingInstanceLauncher : IInstanceLauncher
{
    /// <summary>Gets the repository paths each launch was given, <see langword="null"/> for none.</summary>
    public List<string?> Launched { get; } = [];

    /// <summary>Gets or sets a value indicating whether a launch pretends to succeed.</summary>
    public bool Succeeds { get; set; } = true;

    /// <inheritdoc />
    public bool Launch(string? repositoryPath = null)
    {
        Launched.Add(repositoryPath);
        return Succeeds;
    }
}

/// <summary>
/// An <see cref="IFolderDialogService"/> that records where each folder picker was asked to start and
/// answers with the folder a test chose, instead of showing a picker.
/// </summary>
/// <remarks>
/// Avalonia 12 will not let a test implement <see cref="IStorageProvider"/> or
/// <see cref="IStorageFolder"/>, so the provider is the one a headless window exposes — created the
/// first time a picker needs it, which is always on the headless fixture's thread — and its paths are
/// the real file system's.
/// </remarks>
public sealed class RecordingFolderDialogService : IFolderDialogService, IDisposable
{
    private readonly List<FolderPickerOpenOptions> _requests = [];
    private Window? _window;
    private IStorageProvider? _provider;

    /// <inheritdoc />
    public IStorageProvider? StorageProvider => _provider ??= OpenProvider();

    /// <summary>Gets the options every picker was raised with, in order.</summary>
    public IReadOnlyList<FolderPickerOpenOptions> Requests => _requests;

    /// <summary>
    /// Gets the directory the last picker was asked to start in, or <see langword="null"/> when it was
    /// given none (or when no picker was raised).
    /// </summary>
    public string? LastStartLocation
        => _requests.Count == 0 ? null : _requests[^1].SuggestedStartLocation?.TryGetLocalPath()?.TrimEnd('/', '\\');

    /// <summary>
    /// Gets or sets the folder every picker answers with, or <see langword="null"/> for a picker the
    /// reader cancels.
    /// </summary>
    public string? Answer { get; set; }

    /// <inheritdoc />
    public void SetStorageProvider(IStorageProvider storageProvider) => _provider = storageProvider;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IStorageFolder>> ShowOpenFolderDialogAsync(FolderPickerOpenOptions options)
    {
        _requests.Add(options);

        if (Answer is null || await StorageProvider!.TryGetFolderFromPathAsync(Answer) is not { } folder)
        {
            return [];
        }

        return [folder];
    }

    /// <inheritdoc />
    public void Dispose() => _window?.Close();

    private IStorageProvider OpenProvider()
    {
        _window = new Window();
        _window.Show();

        return _window.StorageProvider;
    }
}

/// <summary>
/// An <see cref="IUnhandledErrorReporter"/> that records what reached the safety net instead of logging
/// and showing it — attached to <see cref="UnhandledExceptionHooks"/> by a test that checks what goes
/// there, and detached again.
/// </summary>
public sealed class RecordingUnhandledErrorReporter : IUnhandledErrorReporter
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(Exception Exception, UnhandledErrorSource Source)> _reports = new();

    /// <summary>Gets every report, oldest first.</summary>
    public IReadOnlyList<(Exception Exception, UnhandledErrorSource Source)> Reports => [.. _reports];

    /// <summary>Gets a value indicating whether shutdown was announced.</summary>
    public bool IsShuttingDown { get; private set; }

    /// <inheritdoc />
    public void Report(Exception exception, UnhandledErrorSource source) => _reports.Enqueue((exception, source));

    /// <inheritdoc />
    public void ShutdownStarted() => IsShuttingDown = true;
}

/// <summary>
/// An <see cref="ITokenRecovery"/> that records the resets asked for instead of asking and deleting —
/// for a test about where the reset is offered, not about the reset itself.
/// </summary>
public sealed class RecordingTokenRecovery : ITokenRecovery
{
    /// <summary>Gets how many times the reset was offered.</summary>
    public int Offered { get; private set; }

    /// <summary>Gets or sets what the offer answers.</summary>
    public bool Confirms { get; set; } = true;

    /// <summary>Gets or sets the status <see cref="CheckAsync"/> answers with.</summary>
    public Enigma.GitClient.Core.Security.TokenStoreStatus Status { get; set; } = Enigma.GitClient.Core.Security.TokenStoreStatus.Empty;

    /// <inheritdoc />
    public event EventHandler? TokensReset;

    /// <inheritdoc />
    public string Describe(Enigma.GitClient.Core.Security.TokenProtectionException exception)
        => "Reset the stored tokens.";

    /// <inheritdoc />
    public Task<bool> ConfirmAndResetAsync()
    {
        Offered++;

        if (Confirms)
        {
            TokensReset?.Invoke(this, EventArgs.Empty);
        }

        return Task.FromResult(Confirms);
    }

    /// <inheritdoc />
    public Task<Enigma.GitClient.Core.Security.TokenStoreStatus> CheckAsync(System.Threading.CancellationToken cancellationToken = default)
        => Task.FromResult(Status);
}
