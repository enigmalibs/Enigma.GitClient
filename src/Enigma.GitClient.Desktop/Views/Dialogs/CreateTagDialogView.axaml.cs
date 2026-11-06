using Avalonia.Controls;

namespace Enigma.GitClient.Desktop.Views.Dialogs;

/// <summary>
/// The content of the "create a tag" dialog.
/// </summary>
/// <remarks>
/// It opens with the caret in the name box, its first field, as every dialog does (<see cref="DialogFocus"/>).
/// </remarks>
public partial class CreateTagDialogView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public CreateTagDialogView() => InitializeComponent();
}
