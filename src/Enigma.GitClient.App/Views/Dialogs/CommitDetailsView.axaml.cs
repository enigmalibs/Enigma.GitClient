using Avalonia.Controls;

namespace Enigma.GitClient.App.Views.Dialogs;

/// <summary>
/// The content of the commit details dialog: a commit's title, description, author, date and hash, as
/// selectable text.
/// </summary>
/// <remarks>
/// Deliberately logic-free: everything shown comes from
/// <see cref="ViewModels.Dialogs.CommitDetailsViewModel"/>.
/// </remarks>
public partial class CommitDetailsView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public CommitDetailsView() => InitializeComponent();
}
