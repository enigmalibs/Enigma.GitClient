using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// The few things the client asks of the desktop around it: the clipboard, and handing a path to
/// whatever the user has set up to open it.
/// </summary>
/// <remarks>
/// It exists as a service rather than as calls scattered through the ViewModels because both of
/// those reach outside the process — a test must be able to say "the panel asked to open this
/// path" without a file manager appearing on the developer's screen.
/// </remarks>
public interface ISystemInterop
{
    /// <summary>
    /// Puts text on the system clipboard.
    /// </summary>
    /// <param name="text">The text to copy.</param>
    /// <returns>A task that completes once the clipboard has it.</returns>
    Task CopyTextAsync(string text);

    /// <summary>
    /// Opens a file or a directory with whatever the desktop has associated with it.
    /// </summary>
    /// <param name="path">The absolute path to open.</param>
    /// <returns><see langword="true"/> when something was launched.</returns>
    Task<bool> OpenPathAsync(string path);

    /// <summary>
    /// Shows a path in the platform's file manager, selecting it where the platform can.
    /// </summary>
    /// <param name="path">The absolute path to reveal.</param>
    /// <returns><see langword="true"/> when something was launched.</returns>
    Task<bool> RevealPathAsync(string path);

    /// <summary>
    /// Opens a web address in the user's browser.
    /// </summary>
    /// <param name="url">The address, which must be http or https.</param>
    /// <returns><see langword="true"/> when a browser was launched.</returns>
    /// <remarks>
    /// Separate from <see cref="OpenPathAsync"/> because the two check different things: a path has
    /// to exist on disk, and an address has to be one it is safe to hand to a shell — anything but
    /// http and https is refused rather than launched.
    /// </remarks>
    Task<bool> OpenUrlAsync(string url);
}

/// <summary>
/// Default <see cref="ISystemInterop"/>, talking to the running desktop.
/// </summary>
public sealed class SystemInterop : ISystemInterop
{
    /// <inheritdoc />
    public async Task CopyTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        IClipboard? clipboard = Clipboard;

        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    /// <inheritdoc />
    public Task<bool> OpenPathAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return Task.FromResult(false);
        }

        // UseShellExecute is what makes this one call work on all three platforms: it goes through
        // ShellExecute on Windows, "open" on macOS and the freedesktop handler on Linux.
        return Task.FromResult(Launch(new ProcessStartInfo(path) { UseShellExecute = true }));
    }

    /// <inheritdoc />
    public Task<bool> RevealPathAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (OperatingSystem.IsWindows())
        {
            ProcessStartInfo windows = new("explorer.exe") { UseShellExecute = false };

            // "/select," takes the path as one argument with no space after the comma; the argument
            // list keeps it quoted correctly whatever the path contains.
            windows.ArgumentList.Add("/select," + path);

            return Task.FromResult(Launch(windows));
        }

        if (OperatingSystem.IsMacOS())
        {
            ProcessStartInfo mac = new("open") { UseShellExecute = false };
            mac.ArgumentList.Add("-R");
            mac.ArgumentList.Add(path);

            return Task.FromResult(Launch(mac));
        }

        // Linux has no portable "select this file" call, so the containing directory is opened and
        // the user finds the file in it.
        string? directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);

        return string.IsNullOrEmpty(directory)
            ? Task.FromResult(false)
            : Task.FromResult(Launch(new ProcessStartInfo(directory) { UseShellExecute = true }));
    }

    /// <inheritdoc />
    public Task<bool> OpenUrlAsync(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            // Everything else — file:, ssh:, and whatever a malformed remote parses as — is refused
            // rather than handed to the shell.
            return Task.FromResult(false);
        }

        return Task.FromResult(Launch(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true }));
    }

    private static IClipboard? Clipboard
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard
            : null;

    private static bool Launch(ProcessStartInfo startInfo)
    {
        try
        {
            using Process? process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                              or InvalidOperationException
                                              or PlatformNotSupportedException)
        {
            // Nothing is registered for the path, or the desktop has no handler at all. There is
            // nothing useful to do about it and it must never take the window down.
            return false;
        }
    }
}
