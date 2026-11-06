using System;
using System.Linq;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Services;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// The questions the application asks the same way everywhere.
/// </summary>
public static class ContentDialogServiceExtensions
{
    /// <summary>
    /// The class that paints a dialog's confirm button red (<c>Themes/Styles.axaml</c>).
    /// </summary>
    public const string DangerClass = "danger";

    /// <summary>
    /// How tall a question's card grows before its text scrolls inside it, in pixels: a batch of
    /// deletions lists every item, and a list of fifty must not push the buttons off the window.
    /// </summary>
    public const double LongQuestionHeight = 560;

    /// <summary>How many lines a question has before its card stops growing.</summary>
    private const int LongQuestionLines = 14;

    extension(IContentDialogService dialogs)
    {
        /// <summary>
        /// Asks whether to go ahead with something that throws work away, and confirms it in red.
        /// </summary>
        /// <param name="title">The question's title.</param>
        /// <param name="message">What will be lost, and that it cannot be undone.</param>
        /// <param name="confirmText">The confirm button, which is red.</param>
        /// <param name="cancelText">The harmless button, which has the focus.</param>
        /// <returns><see langword="true"/> only when the confirm button was pressed.</returns>
        /// <remarks>
        /// A plain yes or no, asked as <see cref="ShowDestructiveAsync"/> asks it.
        /// </remarks>
        public async Task<bool> ConfirmDestructiveAsync(string title, string message, string confirmText, string cancelText = "Cancel")
        {
            ArgumentNullException.ThrowIfNull(title);
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(confirmText);
            ArgumentNullException.ThrowIfNull(cancelText);

            DialogResult result = await dialogs.ShowDestructiveAsync(dialog =>
            {
                dialog.Title = title;
                dialog.Content = message;
                dialog.PrimaryButtonText = confirmText;
                dialog.CloseButtonText = cancelText;
            }).ConfigureAwait(true);

            return result == DialogResult.Primary;
        }

        /// <summary>
        /// Asks a question whose main button throws work away — removes something, discards changes,
        /// resets — with that button in red and the harmless one focused.
        /// </summary>
        /// <param name="configure">Sets the question's title, content and buttons.</param>
        /// <returns>The button the question was answered with.</returns>
        /// <remarks>
        /// <para>
        /// The harmless button — the close button, Cancel — is the default whatever
        /// <paramref name="configure"/> says, so it has the focus as the question opens
        /// (<c>Views/DialogFocus</c>) and a stray Enter keeps the work.
        /// </para>
        /// <para>
        /// The host dialog is shared by every question the window asks, and its service does not reset
        /// its classes, so the red is put on for this question and taken off again whatever the answer —
        /// the next question is not red. A long question — a batch of deletions listing every item —
        /// scrolls inside a card no taller than <see cref="LongQuestionHeight"/>, and the height is given
        /// back afterwards too.
        /// </para>
        /// </remarks>
        public async Task<DialogResult> ShowDestructiveAsync(Action<ContentDialog> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            ContentDialog? shown = null;
            double height = double.NaN;

            try
            {
                return await dialogs.ShowAsync(dialog =>
                {
                    shown = dialog;
                    height = dialog.DialogMaxHeight;
                    dialog.Classes.Add(DangerClass);

                    configure(dialog);

                    dialog.DefaultButton = DefaultButton.Close;

                    if (dialog.Content is string message && message.Count(character => character == '\n') >= LongQuestionLines)
                    {
                        dialog.DialogMaxHeight = LongQuestionHeight;
                    }
                }).ConfigureAwait(true);
            }
            finally
            {
                if (shown is not null)
                {
                    shown.Classes.Remove(DangerClass);
                    shown.DialogMaxHeight = height;
                }
            }
        }
    }
}
