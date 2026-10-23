using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Controls;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.ViewModels;

namespace Enigma.GitClient.Desktop.Views;

/// <summary>
/// The window the application opens on: the repositories, the profiles with their integrations, and
/// the settings.
/// </summary>
public partial class StartWindow : Window, IHostWindow
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public StartWindow()
    {
        // The generated InitializeComponent, for the same reason as MainWindow's: it assigns the
        // x:Name'd host fields.
        InitializeComponent();
        Opened += OnOpened;
        SizeChanged += (_, e) => InfoBarPlacement.Place(HostInfoBar, e.NewSize.Width, NavigationRail.PaneSize);
        InfoBarPlacement.Place(HostInfoBar, Width, NavigationRail.PaneSize);
    }

    /// <inheritdoc />
    ContentDialog IHostWindow.DialogHost => HostDialog;

    /// <inheritdoc />
    Overlay IHostWindow.OverlayHost => HostOverlay;

    /// <inheritdoc />
    InfoBar IHostWindow.InfoBarHost => HostInfoBar;

    /// <summary>
    /// A UI event handler that hands straight over to a Task-returning method; a failure goes to the
    /// safety net rather than through an <c>async void</c> (BUG-9DE3).
    /// </summary>
    private void OnOpened(object? sender, EventArgs e) => InitialiseAsync().Forget();

    private async Task InitialiseAsync()
    {
        if (DataContext is StartWindowViewModel viewModel)
        {
            await viewModel.InitialiseAsync();
        }
    }
}
