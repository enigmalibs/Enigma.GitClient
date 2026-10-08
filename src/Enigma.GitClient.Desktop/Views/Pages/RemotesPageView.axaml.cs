using Avalonia.Controls;

namespace Enigma.GitClient.Desktop.Views.Pages;

/// <summary>
/// The remotes page.
/// </summary>
public partial class RemotesPageView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public RemotesPageView()
    {
        InitializeComponent();

        // The whole line answers a right-click with its menu, the container's padding included.
        LineMenus.Attach(this);
    }
}
