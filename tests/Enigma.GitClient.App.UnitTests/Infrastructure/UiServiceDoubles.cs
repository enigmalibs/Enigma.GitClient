using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Controls;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;

namespace Enigma.GitClient.App.UnitTests.Infrastructure;

/// <summary>
/// What a page asked the info bar to show.
/// </summary>
/// <param name="Title">The notification's title.</param>
/// <param name="Message">The notification's message.</param>
/// <param name="Severity">How serious it was.</param>
public sealed record RecordedNotification(string Title, string Message, InfoBarSeverity Severity);

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

    /// <summary>
    /// Gets the notifications a page has raised, oldest first.
    /// </summary>
    public IReadOnlyList<RecordedNotification> Shown => _shown;

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

        _shown.Add(new RecordedNotification(bar.Title ?? string.Empty, bar.Message ?? string.Empty, bar.Severity));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task HideAsync() => Task.CompletedTask;
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

    /// <inheritdoc />
    public Task HideAsync() => Task.CompletedTask;
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
        return Task.FromResult(true);
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
}
