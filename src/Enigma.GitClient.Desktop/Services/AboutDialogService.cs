using System;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.Views.Dialogs;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Shows the About dialog, so that no ViewModel has to know how it is built.
/// </summary>
public interface IAboutDialogService
{
    /// <summary>
    /// Shows the dialog and waits for it to be closed.
    /// </summary>
    /// <returns>A task that completes when the user has dismissed it.</returns>
    /// <remarks>
    /// Nothing is returned: the dialog states what the application is and offers no choice to carry
    /// back.
    /// </remarks>
    Task ShowAsync();
}

/// <summary>
/// Default <see cref="IAboutDialogService"/>: the About view in the window's content dialog.
/// </summary>
/// <remarks>
/// On <see cref="IContentDialogService"/>, the host every question is asked on: About is a dialog of the
/// same kind — shown, read, closed — and both windows register that host.
/// </remarks>
public sealed class AboutDialogService : IAboutDialogService
{
    private readonly IContentDialogService _dialogs;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="dialogs">Hosts the dialog.</param>
    public AboutDialogService(IContentDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        _dialogs = dialogs;
    }

    /// <inheritdoc />
    public async Task ShowAsync()
    {
        AboutView view = new()
        {
            DataContext = new AboutViewModel(),
        };

        // The service resets the host before configuring it — buttons, commands, icon and its brush —
        // so only what this dialog has is set.
        await _dialogs.ShowAsync(dialog =>
        {
            // No title and no icon: the card draws them as a column beside the content, and the view
            // already shows the application's own icon and name above everything else.
            dialog.Title = null;
            dialog.Content = view;

            // One button, and it only dismisses: the dialog asks nothing.
            dialog.CloseButtonText = "Close";
            dialog.DefaultButton = DefaultButton.Close;
        }).ConfigureAwait(true);
    }
}
