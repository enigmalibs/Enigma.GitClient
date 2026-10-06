using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// The desktops a terminal is looked for on, each in its own way.
/// </summary>
internal enum DesktopPlatform
{
    /// <summary>Windows: Windows Terminal, else the command prompt.</summary>
    Windows,

    /// <summary>macOS: Terminal.</summary>
    MacOS,

    /// <summary>Linux and the other Unix desktops: whichever emulator is there.</summary>
    Linux,
}

/// <summary>
/// The few things the client asks of the desktop around it: the clipboard, handing a path to
/// whatever the user has set up to open it, and a terminal.
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

    /// <summary>
    /// Opens a terminal in a directory: Windows Terminal, or the command prompt where it is not
    /// installed, on Windows; Terminal on macOS; on Linux the emulator <c>$TERMINAL</c> names, else the
    /// first of the usual ones that is there.
    /// </summary>
    /// <param name="directory">The absolute path of the directory the terminal starts in.</param>
    /// <returns><see langword="true"/> when a terminal was launched.</returns>
    Task<bool> OpenTerminalAsync(string directory);
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

    /// <inheritdoc />
    public Task<bool> OpenTerminalAsync(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            return Task.FromResult(false);
        }

        DesktopPlatform platform = OperatingSystem.IsWindows()
            ? DesktopPlatform.Windows
            : OperatingSystem.IsMacOS() ? DesktopPlatform.MacOS : DesktopPlatform.Linux;

        // The first that starts: one that is not installed fails to launch, and the next is tried.
        foreach (ProcessStartInfo candidate in TerminalCandidates(platform, directory, Environment.GetEnvironmentVariable("TERMINAL")))
        {
            if (Launch(candidate))
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// The terminals tried, in order, to open one in a directory.
    /// </summary>
    /// <param name="platform">The desktop the client runs on.</param>
    /// <param name="directory">The directory the terminal starts in.</param>
    /// <param name="terminal">What <c>$TERMINAL</c> says, which Linux tries first.</param>
    /// <returns>How to start each one; the directory goes in an argument of its own, never a command line.</returns>
    internal static IReadOnlyList<ProcessStartInfo> TerminalCandidates(DesktopPlatform platform, string directory, string? terminal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        List<ProcessStartInfo> candidates = [];

        switch (platform)
        {
            case DesktopPlatform.Windows:
                candidates.Add(Started("wt.exe", directory, "-d", directory));

                // Through the shell, so the command prompt gets a console window of its own.
                candidates.Add(new ProcessStartInfo("cmd.exe") { UseShellExecute = true, WorkingDirectory = directory });
                break;

            case DesktopPlatform.MacOS:
                candidates.Add(Started("open", directory, "-a", "Terminal", directory));
                break;

            default:
                if (!string.IsNullOrWhiteSpace(terminal))
                {
                    candidates.Add(Started(terminal.Trim(), directory));
                }

                candidates.Add(Started("x-terminal-emulator", directory));
                candidates.Add(Started("gnome-terminal", directory, $"--working-directory={directory}"));
                candidates.Add(Started("konsole", directory, "--workdir", directory));
                candidates.Add(Started("xfce4-terminal", directory, $"--working-directory={directory}"));
                candidates.Add(Started("xterm", directory));
                break;
        }

        return candidates;
    }

    /// <summary>
    /// A program started directly — no shell between it and its arguments — in a directory.
    /// </summary>
    private static ProcessStartInfo Started(string program, string directory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new(program) { UseShellExecute = false, WorkingDirectory = directory };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static IClipboard? Clipboard
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard
            : null;

    private static bool Launch(ProcessStartInfo startInfo) => Launch(startInfo, Process.Start);

    /// <summary>
    /// Starts something, and says whether it was launched.
    /// </summary>
    /// <param name="startInfo">What to start.</param>
    /// <param name="start">How: <see cref="Process.Start(ProcessStartInfo)"/>, or a test's stand-in for it.</param>
    /// <returns><see langword="true"/> unless the start failed.</returns>
    /// <remarks>
    /// A start that returns no process was still a launch. Through the shell, that is what handing the
    /// request to a process already running looks like: on Windows, Explorer opens every folder in the
    /// instance it already has, and a browser already open takes the address — neither starts a process
    /// of its own, and the folder or the page is on screen (BUG-6EA3). A start that fails throws; a
    /// direct one never returns nothing.
    /// </remarks>
    internal static bool Launch(ProcessStartInfo startInfo, Func<ProcessStartInfo, Process?> start)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(start);

        try
        {
            start(startInfo)?.Dispose();
            return true;
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
