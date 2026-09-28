using System;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Services;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// The questions the application asks the same way everywhere.
/// </summary>
public static class ContentDialogServiceExtensions
{
    /// <summary>
    /// The class that paints a dialog's confirm button red (<c>Themes/Styles.axaml</c>).
    /// </summary>
    public const string DangerClass = "danger";

    extension(IContentDialogService dialogs)
    {
        /// <summary>
        /// Asks whether to go ahead with something that throws work away, and confirms it in red.
        /// </summary>
        /// <param name="title">The question's title.</param>
        /// <param name="message">What will be lost, and that it cannot be undone.</param>
        /// <param name="confirmText">The confirm button, which is red.</param>
        /// <returns><see langword="true"/> only when the confirm button was pressed.</returns>
        /// <remarks>
        /// A plain yes or no: nothing to type, and the harmless button is the default, so a stray
        /// Enter keeps the work. The host dialog is shared by every question the window asks, and its
        /// service does not reset its classes, so the red is put on for this question and taken off
        /// again whatever the answer — the next question is not red.
        /// </remarks>
        public async Task<bool> ConfirmDestructiveAsync(string title, string message, string confirmText)
        {
            ArgumentNullException.ThrowIfNull(title);
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(confirmText);

            ContentDialog? shown = null;

            try
            {
                DialogResult result = await dialogs.ShowAsync(dialog =>
                {
                    shown = dialog;
                    dialog.Classes.Add(DangerClass);

                    dialog.Title = title;
                    dialog.Content = message;
                    dialog.PrimaryButtonText = confirmText;
                    dialog.CloseButtonText = "Cancel";
                    dialog.DefaultButton = DefaultButton.Close;
                }).ConfigureAwait(true);

                return result == DialogResult.Primary;
            }
            finally
            {
                shown?.Classes.Remove(DangerClass);
            }
        }
    }
}
