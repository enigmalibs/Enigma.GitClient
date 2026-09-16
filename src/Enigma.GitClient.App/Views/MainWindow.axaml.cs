using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Enigma.GitClient.App.ViewModels;

namespace Enigma.GitClient.App.Views;

/// <summary>
/// The application's only window: the repository strip, the navigation rail, the page area and the
/// three overlay hosts.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public MainWindow()
    {
        // The generated InitializeComponent is used deliberately: it is what assigns the x:Name'd
        // ContentDialog, Overlay and InfoBar host fields. A hand-written one that only calls
        // AvaloniaXamlLoader.Load leaves them null, and every dialog then fails at runtime.
        InitializeComponent();
        Opened += OnOpened;
    }

    /// <summary>
    /// The one place an <c>async void</c> is allowed: a UI event handler that immediately delegates
    /// to a Task-returning method.
    /// </summary>
    private async void OnOpened(object? sender, EventArgs e) => await RunStartupChecksAsync();

    private async Task RunStartupChecksAsync()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitialiseAsync();
        }
    }
}
