using Avalonia.Controls;
using Avalonia.Threading;

namespace Enigma.GitClient.Desktop.Views.Dialogs;

/// <summary>
/// The content of the "rename a branch" dialog.
/// </summary>
public partial class RenameBranchDialogView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public RenameBranchDialogView()
    {
        InitializeComponent();

        Loaded += (_, _) => FocusTheName();
    }

    /// <summary>
    /// Puts the caret in the name box as the dialog opens, the current name selected, so typing
    /// replaces it straight away.
    /// </summary>
    /// <remarks>
    /// The dialog is opened from a menu — a branch's badge, a branch's line — and would otherwise
    /// leave the focus behind it. Posted rather than called, as the tag dialog does, because the box
    /// cannot take the focus until it is laid out.
    /// </remarks>
    private void FocusTheName()
        => Dispatcher.UIThread.Post(() =>
        {
            if (NameBox.IsEffectivelyVisible)
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }
        });
}
