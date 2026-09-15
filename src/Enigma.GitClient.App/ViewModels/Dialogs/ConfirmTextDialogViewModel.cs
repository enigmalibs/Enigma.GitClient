using System;

namespace Enigma.GitClient.App.ViewModels.Dialogs;

/// <summary>
/// A confirmation that has to be typed rather than clicked.
/// </summary>
/// <remarks>
/// Reserved for the actions that cannot be undone by any other part of the client. A click is
/// something a hand does by accident; typing a name is not.
/// </remarks>
public sealed class ConfirmTextDialogViewModel : ViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="message">What is about to happen, in full.</param>
    /// <param name="expected">The word that has to be typed back.</param>
    /// <param name="prompt">The label above the box.</param>
    public ConfirmTextDialogViewModel(string message, string expected, string prompt)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(expected);
        ArgumentNullException.ThrowIfNull(prompt);

        Message = message;
        Expected = expected;
        Prompt = prompt;
    }

    /// <summary>Gets what is about to happen.</summary>
    public string Message { get; }

    /// <summary>Gets the word that has to be typed back.</summary>
    public string Expected { get; }

    /// <summary>Gets the label above the box.</summary>
    public string Prompt { get; }

    /// <summary>
    /// Gets or sets what has been typed.
    /// </summary>
    public string Typed
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsConfirmed));
                ConfirmationChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether what was typed matches, exactly.
    /// </summary>
    public bool IsConfirmed => string.Equals(Typed.Trim(), Expected, StringComparison.Ordinal);

    /// <summary>
    /// Raised whenever the answer to <see cref="IsConfirmed"/> may have changed.
    /// </summary>
    public event EventHandler? ConfirmationChanged;
}
