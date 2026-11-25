using System;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Services;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Hands a window's overlay hosts and its storage provider to the services that draw into them: the
/// dialogs, the progress overlay, the notifications, the tool dialogs and the two pickers.
/// </summary>
/// <remarks>
/// The services are singletons and the windows come and go — the start window hands over to the
/// repository window and back — so each window, once built, is bound before it is shown, and the
/// services draw into whichever window is on screen.
/// </remarks>
public sealed class WindowHostBinder
{
    private readonly IContentDialogService _dialogs;
    private readonly IOverlayService _overlay;
    private readonly IInfoBarService _infoBar;
    private readonly IToolDialogService _tools;
    private readonly IFileDialogService _files;
    private readonly IFolderDialogService _folders;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="dialogs">Draws into the window's dialog host.</param>
    /// <param name="overlay">Draws into its overlay host.</param>
    /// <param name="infoBar">Draws into its notification host.</param>
    /// <param name="tools">Draws into its tool-dialog host, in the repository window.</param>
    /// <param name="files">Asks the window's storage provider for files.</param>
    /// <param name="folders">Asks it for folders.</param>
    public WindowHostBinder(
        IContentDialogService dialogs,
        IOverlayService overlay,
        IInfoBarService infoBar,
        IToolDialogService tools,
        IFileDialogService files,
        IFolderDialogService folders)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(folders);

        _dialogs = dialogs;
        _overlay = overlay;
        _infoBar = infoBar;
        _tools = tools;
        _files = files;
        _folders = folders;
    }

    /// <summary>
    /// Binds a window, before it is shown.
    /// </summary>
    /// <param name="window">The window.</param>
    public void Bind(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window is IHostWindow hosts)
        {
            _dialogs.RegisterHost(hosts.DialogHost);
            _overlay.RegisterHost(hosts.OverlayHost);
            _infoBar.RegisterHost(hosts.InfoBarHost);
        }

        if (window is IToolDialogHostWindow tools)
        {
            _tools.RegisterHost(tools.ToolDialogHost);
        }

        _files.SetStorageProvider(window.StorageProvider);
        _folders.SetStorageProvider(window.StorageProvider);
    }
}
