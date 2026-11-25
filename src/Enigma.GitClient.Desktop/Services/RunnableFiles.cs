using System;
using System.Collections.Generic;
using System.IO;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Says whether opening a file runs something: a program, a script, an installer, or a shortcut to one.
/// </summary>
/// <remarks>
/// <para>
/// "Open" hands a path to the desktop, which does whatever is associated with its extension — and for
/// these, that is "run it". A file in a commit or a working tree is content somebody else may have
/// written, so a client that opens it on a click is one click away from running it while reading a
/// diff. Those files are shown in the file manager instead, where opening them is the reader's own,
/// deliberate act.
/// </para>
/// <para>
/// On Windows the list is what Windows runs: its <c>PATHEXT</c> (the usual defaults when it is not
/// set), the script hosts and installers that run on a double-click — a <c>.js</c> file runs in the
/// Windows Script Host there — and the shortcuts that point at something else. Elsewhere it is the
/// launchers, scripts and bundles a Linux or macOS desktop runs when asked to open them. Matching is by
/// extension, case-insensitively: it is what the desktop goes by.
/// </para>
/// </remarks>
public static class RunnableFiles
{
    private const string DefaultPathExtensions = ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC;.PS1";

    private static readonly HashSet<string> OnWindows = new(StringComparer.OrdinalIgnoreCase)
    {
        // Programs, scripts and installers.
        ".com", ".exe", ".bat", ".cmd", ".scr", ".pif", ".cpl", ".msi", ".msp", ".mst", ".msc",
        ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".reg", ".jar",
        ".application", ".appref-ms", ".appx", ".appxbundle", ".msix", ".msixbundle", ".gadget",

        // Shortcuts and links that open something else.
        ".lnk", ".url", ".scf", ".website", ".settingcontent-ms", ".library-ms", ".searchconnector-ms",
    };

    private static readonly HashSet<string> Elsewhere = new(StringComparer.OrdinalIgnoreCase)
    {
        ".desktop", ".sh", ".command", ".app", ".run", ".appimage", ".jar", ".tool",
    };

    /// <summary>
    /// Says whether opening a file on this machine would run something.
    /// </summary>
    /// <param name="path">The file's path or name.</param>
    /// <returns><see langword="true"/> when it must be shown, not opened.</returns>
    public static bool IsRunnable(string path)
        => IsRunnable(path, OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable("PATHEXT"));

    /// <summary>
    /// Says whether opening a file would run something on a given desktop.
    /// </summary>
    /// <param name="path">The file's path or name.</param>
    /// <param name="isWindows">Whether the desktop is Windows.</param>
    /// <param name="pathExtensions">What <c>PATHEXT</c> says on Windows, or <see langword="null"/> for the usual defaults.</param>
    /// <returns><see langword="true"/> when it must be shown, not opened.</returns>
    public static bool IsRunnable(string path, bool isWindows, string? pathExtensions)
    {
        ArgumentNullException.ThrowIfNull(path);

        // Windows drops trailing dots and spaces when it opens a file: "setup.exe." runs as setup.exe.
        string extension = Path.GetExtension(isWindows ? path.TrimEnd('.', ' ') : path);

        if (extension.Length == 0)
        {
            return false;
        }

        if (!isWindows)
        {
            return Elsewhere.Contains(extension);
        }

        if (OnWindows.Contains(extension))
        {
            return true;
        }

        string listed = string.IsNullOrWhiteSpace(pathExtensions) ? DefaultPathExtensions : pathExtensions;

        foreach (string candidate in listed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
