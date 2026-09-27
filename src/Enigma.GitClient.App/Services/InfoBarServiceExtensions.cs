using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// The one way the application reports something on the info bar.
/// </summary>
public static class InfoBarServiceExtensions
{
    /// <summary>
    /// How long a success or an informational message stays before it closes itself.
    /// </summary>
    public static readonly TimeSpan TransientDisplayDuration = TimeSpan.FromSeconds(5);

    extension(IInfoBarService infoBar)
    {
        /// <summary>
        /// Shows a message on the info bar and returns at once.
        /// </summary>
        /// <remarks>
        /// The task the info bar service hands back completes only when the bar closes — for a
        /// warning or an error, when the user closes it. Nothing waits for that here, so a report
        /// never holds up the refresh, the busy state or the result that comes after it. A success
        /// or an informational message closes itself after <see cref="TransientDisplayDuration"/>;
        /// a warning or an error stays until it is dismissed, because it asks the user to look.
        /// </remarks>
        /// <param name="title">The message's title.</param>
        /// <param name="message">The message itself.</param>
        /// <param name="severity">How serious it is, which also decides whether it closes itself.</param>
        /// <exception cref="InvalidOperationException">No host bar has been registered.</exception>
        public void Notify(string title, string message, InfoBarSeverity severity)
        {
            ArgumentNullException.ThrowIfNull(title);
            ArgumentNullException.ThrowIfNull(message);

            void Configure(InfoBar bar)
            {
                bar.Title = title;
                bar.Message = message;
                bar.Severity = severity;
            }

            Task showing = severity is InfoBarSeverity.Success or InfoBarSeverity.Info
                ? infoBar.ShowAsync(TransientDisplayDuration, Configure)
                : infoBar.ShowAsync(Configure);

            // A bar that cannot be shown at all fails before it opens: the service throws for a
            // missing host inside its async method, so the task is already faulted. Surface that
            // here, where awaiting it used to, rather than leave it in a task nobody observes.
            if (showing is { IsFaulted: true, Exception: { } failure })
            {
                ExceptionDispatchInfo.Throw(failure.InnerExceptions[0]);
            }
        }
    }
}
