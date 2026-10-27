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
    /// Posted rather than called, as the tag dialog does, because the box cannot take the focus until
    /// it is laid out.
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
