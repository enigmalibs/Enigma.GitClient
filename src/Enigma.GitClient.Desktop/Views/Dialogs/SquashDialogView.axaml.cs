using Avalonia.Controls;
using Avalonia.Threading;

namespace Enigma.GitClient.Desktop.Views.Dialogs;

/// <summary>
/// The content of the squash dialog.
/// </summary>
public partial class SquashDialogView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public SquashDialogView()
    {
        InitializeComponent();

        Loaded += (_, _) => FocusTheTitle();
    }

    /// <summary>
    /// Puts the caret on the message's first line as the dialog opens: the title is the one thing the
    /// dialog cannot propose, and the bullets below it are already written.
    /// </summary>
    /// <remarks>
    /// Every other dialog opens with the caret in its first field (<see cref="DialogFocus"/>), which
    /// here is the branch name; the dialog leaves the focus where this view has put it. Posted rather
    /// than called, because the box cannot take the focus until it is laid out.
    /// </remarks>
    private void FocusTheTitle()
        => Dispatcher.UIThread.Post(() =>
        {
            if (MessageEditor.IsEffectivelyVisible)
            {
                MessageEditor.Focus();
                MessageEditor.CaretIndex = 0;
            }
        });
}
