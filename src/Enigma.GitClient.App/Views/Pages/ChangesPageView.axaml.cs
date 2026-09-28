using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Enigma.GitClient.App.ViewModels.Pages;

namespace Enigma.GitClient.App.Views.Pages;

/// <summary>
/// The working directory page.
/// </summary>
public partial class ChangesPageView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public ChangesPageView()
    {
        InitializeComponent();

        // Tunnelling, and on the page: Escape goes back to the history whatever inside the page has the
        // key — the commit message, the file filters, a list — and before any of them can take it.
        AddHandler(KeyDownEvent, OnPageKeyDown, RoutingStrategies.Tunnel);

        Loaded += (_, _) => TakeTheFocus();
    }

    /// <summary>
    /// Puts the focus on the page when it is shown, unless something in it already has it.
    /// </summary>
    /// <remarks>
    /// This is what makes Escape work on the first press. A page shown from the rail leaves the focus
    /// on the rail, whose keys never pass through this page. It is posted rather than called, because
    /// the page cannot take the focus until it is laid out.
    /// </remarks>
    private void TakeTheFocus()
        => Dispatcher.UIThread.Post(() =>
        {
            if (IsEffectivelyVisible && !IsKeyboardFocusWithin)
            {
                Focus();
            }
        });

    /// <summary>
    /// Goes back to the history on Escape.
    /// </summary>
    /// <param name="sender">The page.</param>
    /// <param name="e">The key.</param>
    private void OnPageKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        // A question asked over the page belongs to the question: a discard confirmation must not
        // send the reader to the history.
        if (this.IsBehindOpenDialog)
        {
            return;
        }

        if (DataContext is ChangesPageViewModel page && page.BackToHistoryCommand.CanExecute(null))
        {
            page.BackToHistoryCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Commits on Ctrl+Enter, which is the shortcut every other client uses and the reason the
    /// message box accepts newlines at all.
    /// </summary>
    /// <param name="sender">The message box.</param>
    /// <param name="e">The key.</param>
    private void OnMessageKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return) || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (DataContext is ChangesPageViewModel page && page.CommitCommand.CanExecute(null))
        {
            page.CommitCommand.Execute(null);
            e.Handled = true;
        }
    }
}
