using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;

namespace Enigma.GitClient.App.Views;

/// <summary>
/// What a view needs to know about the window it is in.
/// </summary>
public static class VisualExtensions
{
    extension(Visual visual)
    {
        /// <summary>
        /// Gets a value indicating whether a content dialog is open in the visual's window.
        /// </summary>
        /// <remarks>
        /// A content dialog does not take the focus when it opens, so a key pressed while it asks its
        /// question is still routed to whatever had the focus before: the page underneath. A page that
        /// answers Escape itself has to leave the key alone while a question is being asked over it.
        /// </remarks>
        public bool IsBehindOpenDialog
            => TopLevel.GetTopLevel(visual) is { } top
               && top.GetVisualDescendants().OfType<ContentDialog>().Any(dialog => dialog.IsOpen);
    }
}
