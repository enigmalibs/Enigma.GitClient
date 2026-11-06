using Avalonia.Controls;

namespace Enigma.GitClient.Desktop.Views.Dialogs;

/// <summary>
/// The content of the "rename a branch" dialog.
/// </summary>
/// <remarks>
/// It opens with the caret in the name box, the current name selected so typing replaces it, as every
/// dialog does with its first field (<see cref="DialogFocus"/>).
/// </remarks>
public partial class RenameBranchDialogView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public RenameBranchDialogView() => InitializeComponent();
}
