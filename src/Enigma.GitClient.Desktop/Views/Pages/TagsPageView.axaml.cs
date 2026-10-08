using Avalonia.Controls;

namespace Enigma.GitClient.Desktop.Views.Pages;

/// <summary>
/// The tags page.
/// </summary>
public partial class TagsPageView : UserControl
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public TagsPageView()
    {
        InitializeComponent();

        // The whole line answers a right-click with its menu, the container's padding included.
        LineMenus.Attach(this);
    }
}
