using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Enigma.GitClient.Core.Git;

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
/// Where the programs the client starts are looked for: the Windows folders the system programs live
/// in, and the <c>PATH</c> for everything else.
/// </summary>
/// <param name="System">The Windows system folder (<c>System32</c>), which has <c>cmd.exe</c>.</param>
/// <param name="Windows">The Windows folder, which has <c>explorer.exe</c>.</param>
/// <param name="WindowsApps">The per-user folder of app execution aliases, which has <c>wt.exe</c>.</param>
/// <param name="SearchPath">The <c>PATH</c>, for every other program.</param>
internal sealed record ProgramFolders(string? System, string? Windows, string? WindowsApps, string? SearchPath)
{
    /// <summary>
    /// Gets the folders of the machine the client runs on.
    /// </summary>
    /// <returns>The folders; the Windows ones are empty elsewhere.</returns>
    public static ProgramFolders Current()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return new ProgramFolders(
            Environment.SystemDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            localApplicationData.Length > 0 ? Path.Combine(localApplicationData, "Microsoft", "WindowsApps") : null,
            Environment.GetEnvironmentVariable("PATH"));
    }
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
    /// Opens a file or a directory with whatever the desktop has associated with it — except a file
    /// that would run (<see cref="RunnableFiles"/>), which is shown in the file manager instead.
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
    /// first of the usual ones that is there. Each is started by its full path, never looked up in the
    /// directory.
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

        // Opening a program, a script or a shortcut runs it. Whatever asked, the desktop is only asked
        // to show it: the caller's own gate is not the only one (SEC-03).
        if (File.Exists(path) && RunnableFiles.IsRunnable(path))
        {
            return RevealPathAsync(path);
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
            if (Locate("explorer.exe", DesktopPlatform.Windows, ProgramFolders.Current(), File.Exists) is not { } explorer)
            {
                return Task.FromResult(false);
            }

            ProcessStartInfo windows = new(explorer) { UseShellExecute = false };

            // "/select," takes the path as one argument with no space after the comma; the argument
            // list keeps it quoted correctly whatever the path contains.
            windows.ArgumentList.Add("/select," + path);

            return Task.FromResult(Launch(windows));
        }

        if (OperatingSystem.IsMacOS())
        {
            if (Locate("open", DesktopPlatform.MacOS, ProgramFolders.Current(), File.Exists) is not { } open)
            {
                return Task.FromResult(false);
            }

            ProcessStartInfo mac = new(open) { UseShellExecute = false };
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

        ProgramFolders folders = ProgramFolders.Current();

        // The first that starts: one that is not installed is not a candidate, and one that fails to
        // launch hands over to the next.
        foreach (ProcessStartInfo candidate in TerminalCandidates(
                     platform,
                     directory,
                     Environment.GetEnvironmentVariable("TERMINAL"),
                     program => Locate(program, platform, folders, File.Exists)))
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
    /// <param name="locate">
    /// Finds a program's full path, or answers <see langword="null"/> when it is not installed —
    /// <see cref="Locate"/>, or a test's stand-in. A program that is not found is not a candidate.
    /// </param>
    /// <returns>
    /// How to start each one, always by its full path; the directory goes in an argument of its own,
    /// never a command line.
    /// </returns>
    internal static IReadOnlyList<ProcessStartInfo> TerminalCandidates(
        DesktopPlatform platform,
        string directory,
        string? terminal,
        Func<string, string?> locate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(locate);

        List<ProcessStartInfo> candidates = [];

        void Add(string program, params string[] arguments)
        {
            if (locate(program) is { } path)
            {
                candidates.Add(Started(path, directory, arguments));
            }
        }

        switch (platform)
        {
            case DesktopPlatform.Windows:
                Add("wt.exe", "-d", directory);

                // Through the shell, so the command prompt gets a console window of its own — and by
                // its full path: through the shell, the working directory is also where a bare name is
                // looked for, and the working directory is the repository.
                if (locate("cmd.exe") is { } commandPrompt)
                {
                    candidates.Add(new ProcessStartInfo(commandPrompt) { UseShellExecute = true, WorkingDirectory = directory });
                }

                break;

            case DesktopPlatform.MacOS:
                Add("open", "-a", "Terminal", directory);
                break;

            default:
                if (!string.IsNullOrWhiteSpace(terminal))
                {
                    Add(terminal.Trim());
                }

                Add("x-terminal-emulator");
                Add("gnome-terminal", $"--working-directory={directory}");
                Add("konsole", "--workdir", directory);
                Add("xfce4-terminal", $"--working-directory={directory}");
                Add("xterm");
                break;
        }

        return candidates;
    }

    /// <summary>
    /// Finds the full path of a program the client starts.
    /// </summary>
    /// <param name="program">The program: a file name, or a full path (what <c>$TERMINAL</c> may say).</param>
    /// <param name="platform">The desktop the client runs on.</param>
    /// <param name="folders">Where to look.</param>
    /// <param name="fileExists">Says whether a file exists.</param>
    /// <returns>The program's full path, or <see langword="null"/> when it is not there.</returns>
    /// <remarks>
    /// A program is never started by its bare name. The operating system would look for it in the
    /// current directory first — and, through the shell on Windows, in the working directory — which is
    /// often a repository: content somebody else wrote, where a <c>cmd.exe</c> committed at the root
    /// would run in place of the real one. The Windows system programs come from the folders Windows
    /// keeps them in; everything else from the fully qualified directories of the <c>PATH</c>.
    /// </remarks>
    internal static string? Locate(string program, DesktopPlatform platform, ProgramFolders folders, Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(fileExists);

        if (Path.IsPathFullyQualified(program))
        {
            return fileExists(program) ? program : null;
        }

        // A relative path with a folder in it is relative to the current directory: the very lookup
        // this exists to avoid.
        if (program.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || program.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        if (platform == DesktopPlatform.Windows)
        {
            string? folder = program.ToUpperInvariant() switch
            {
                "CMD.EXE" => folders.System,
                "EXPLORER.EXE" => folders.Windows,
                "WT.EXE" => folders.WindowsApps,
                _ => null,
            };

            if (folder is not null)
            {
                if (!Path.IsPathFullyQualified(folder))
                {
                    return null;
                }

                string candidate = Path.Combine(folder, program);

                return fileExists(candidate) ? candidate : null;
            }
        }

        return ExecutableSearch.FindOnPath(program, folders.SearchPath, fileExists);
    }

    /// <summary>
    /// A program started directly — no shell between it and its arguments — in a directory.
    /// </summary>
    private static ProcessStartInfo Started(string program, string directory, string[] arguments)
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
