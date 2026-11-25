using System;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// What an operation tells and asks the reader: the info bar's reports, the dialogs' questions and the
/// progress overlay — the three every <c>*Operations</c> service took one by one.
/// </summary>
public interface IOperationFeedback
{
    /// <summary>Gets the dialogs an operation asks its questions in.</summary>
    IContentDialogService Dialogs { get; }

    /// <summary>Gets the overlay a long operation shows its progress on.</summary>
    IOverlayService Overlay { get; }

    /// <summary>
    /// Says in the info bar what an operation did, or why it did not.
    /// </summary>
    /// <param name="title">The bar's title.</param>
    /// <param name="message">What happened, in a sentence.</param>
    /// <param name="severity">How it went.</param>
    void Report(string title, string message, InfoBarSeverity severity);

    /// <summary>
    /// Says in the info bar what an operation did, given as one note.
    /// </summary>
    /// <param name="note">The title, the message and how it went.</param>
    void Report((string Title, string Message, InfoBarSeverity Severity) note);
}

/// <summary>
/// Default <see cref="IOperationFeedback"/>.
/// </summary>
public sealed class OperationFeedback : IOperationFeedback
{
    private readonly IInfoBarService _infoBar;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="dialogs">The dialogs an operation asks its questions in.</param>
    /// <param name="overlay">The overlay a long operation shows its progress on.</param>
    /// <param name="infoBar">The info bar an operation reports in.</param>
    public OperationFeedback(IContentDialogService dialogs, IOverlayService overlay, IInfoBarService infoBar)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(infoBar);

        Dialogs = dialogs;
        Overlay = overlay;
        _infoBar = infoBar;
    }

    /// <inheritdoc />
    public IContentDialogService Dialogs { get; }

    /// <inheritdoc />
    public IOverlayService Overlay { get; }

    /// <summary>
    /// The first line of what git wrote while refusing, the line a report quotes.
    /// </summary>
    /// <param name="text">What git wrote, its error output most often.</param>
    /// <returns>Its first line, trimmed; a sentence saying so when git wrote nothing.</returns>
    public static string FirstLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string trimmed = text.Trim();

        if (trimmed.Length == 0)
        {
            return "git reported no reason.";
        }

        int newline = trimmed.IndexOf('\n', StringComparison.Ordinal);

        return newline < 0 ? trimmed : trimmed[..newline].TrimEnd('\r');
    }

    /// <inheritdoc />
    public void Report(string title, string message, InfoBarSeverity severity)
        => _infoBar.Notify(title, message, severity);

    /// <inheritdoc />
    public void Report((string Title, string Message, InfoBarSeverity Severity) note)
        => Report(note.Title, note.Message, note.Severity);
}
