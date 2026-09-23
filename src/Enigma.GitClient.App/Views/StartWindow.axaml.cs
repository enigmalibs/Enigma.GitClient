using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Controls;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels;

namespace Enigma.GitClient.App.Views;

/// <summary>
/// The window the application opens on: the repositories, the integrations and the settings.
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
    }

    /// <inheritdoc />
    ContentDialog IHostWindow.DialogHost => HostDialog;

    /// <inheritdoc />
    Overlay IHostWindow.OverlayHost => HostOverlay;

    /// <inheritdoc />
    InfoBar IHostWindow.InfoBarHost => HostInfoBar;

    /// <summary>
    /// The one place an <c>async void</c> is allowed: a UI event handler that immediately delegates
    /// to a Task-returning method.
    /// </summary>
    private async void OnOpened(object? sender, EventArgs e) => await InitialiseAsync();

    private async Task InitialiseAsync()
    {
        if (DataContext is StartWindowViewModel viewModel)
        {
            await viewModel.InitialiseAsync();
        }
    }
}
