using Avalonia.Controls;
using Avalonia.Threading;

namespace Enigma.GitClient.Desktop.Views.Dialogs;

/// <summary>
/// The content of the "create a tag" dialog.
/// </summary>
public partial class CreateTagDialogView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public CreateTagDialogView()
    {
        InitializeComponent();

        Loaded += (_, _) => FocusTheName();
    }

    /// <summary>
    /// Puts the caret in the name box as the dialog opens, so the name can be typed straight away.
    /// </summary>
    /// <remarks>
    /// The dialog moves the focus nowhere of its own accord: it would stay on whatever opened it — the
    /// toolbar's button, the line's menu — behind the dialog. Posted rather than called, as the changes
    /// page takes its own focus, because the box cannot take the focus until it is laid out.
    /// </remarks>
    private void FocusTheName()
        => Dispatcher.UIThread.Post(() =>
        {
            if (NameBox.IsEffectivelyVisible)
            {
                NameBox.Focus();
            }
        });
}
