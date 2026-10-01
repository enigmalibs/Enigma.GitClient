using Avalonia.Controls;

namespace Enigma.GitClient.Desktop.Views.Dialogs;

/// <summary>
/// The content of the About dialog: the application's icon, what it is, which build is running, and
/// what it is built out of.
/// </summary>
/// <remarks>
/// Deliberately logic-free: everything shown is fixed for the life of the dialog and comes from
/// <see cref="ViewModels.Dialogs.AboutViewModel"/>.
/// </remarks>
public partial class AboutView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public AboutView() => InitializeComponent();
}
