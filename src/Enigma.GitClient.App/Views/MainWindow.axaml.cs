using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Controls;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels;

namespace Enigma.GitClient.App.Views;

/// <summary>
/// The repository window: the repository strip, the navigation rail, the page area and the three
/// overlay hosts.
/// </summary>
public partial class MainWindow : Window, IHostWindow, IToolDialogHostWindow
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
        SizeChanged += (_, e) => SizeToolDialog(e.NewSize);
        SizeToolDialog(new Size(Width, Height));
    }

    /// <inheritdoc />
    ContentDialog IToolDialogHostWindow.ToolDialogHost => ToolDialog;

    /// <summary>
    /// The largest a tool dialog is drawn, and the room it leaves around itself inside the window.
    /// </summary>
    internal static Size ToolDialogSizeFor(Size window)
        => new(
            Math.Max(0, Math.Min(1100, window.Width - 96)),
            Math.Max(0, Math.Min(760, window.Height - 96)));

    /// <summary>
    /// Keeps the tool dialog as large as the window allows: its lists need a bounded height to scroll
    /// in, and a fixed size would overflow a small window.
    /// </summary>
    private void SizeToolDialog(Size window)
    {
        Size size = ToolDialogSizeFor(window);
        ToolDialog.DialogWidth = size.Width;
        ToolDialog.DialogHeight = size.Height;
        ToolDialog.DialogMaxHeight = size.Height;
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
    private async void OnOpened(object? sender, EventArgs e) => await RunStartupChecksAsync();

    private async Task RunStartupChecksAsync()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitialiseAsync();
        }
    }
}
