using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using IOPath = System.IO.Path;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Resolves the <c>git</c> executable from the configured override, the <c>PATH</c>, or the usual
/// installation directories. The result is cached for the lifetime of the instance.
/// </summary>
public sealed class GitExecutable : IGitExecutable
{
    private readonly IOptions<GitExecutableOptions> _options;
    private readonly object _gate = new();
    private string? _resolved;
    private bool _resolutionAttempted;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="options">The executable options.</param>
    public GitExecutable(IOptions<GitExecutableOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public string Path
    {
        get
        {
            if (TryResolve(out string? path))
            {
                return path!;
            }

            throw new GitNotFoundException(
                "No 'git' executable could be found. Install git 2.20 or newer and make sure it is on the PATH, " +
                "or set an explicit path in the application settings.");
        }
    }

    /// <inheritdoc />
    public bool TryResolve(out string? path)
    {
        lock (_gate)
        {
            if (!_resolutionAttempted)
            {
                _resolved = Resolve(_options.Value.ExecutablePath);
                _resolutionAttempted = true;
            }

            path = _resolved;
            return path is not null;
        }
    }

    private static string? Resolve(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            // An explicit override is honoured verbatim, so a user can point at any build of git.
            return configuredPath;
        }

        foreach (string candidate in EnumerateCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Fall back to the bare name and let the operating system search the PATH. Resolution only
        // truly fails when the process cannot be started, which the runner reports.
        return LocateOnPath() ?? "git";
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield break;
        }

        foreach (string variable in new[] { "ProgramFiles", "ProgramFiles(x86)", "LocalAppData" })
        {
            string? root = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            yield return IOPath.Combine(root, "Git", "cmd", "git.exe");
            yield return IOPath.Combine(root, "Programs", "Git", "cmd", "git.exe");
        }
    }

    private static string? LocateOnPath()
    {
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string fileName = isWindows ? "git.exe" : "git";

        foreach (string directory in pathVariable.Split(IOPath.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try
            {
                candidate = IOPath.Combine(directory.Trim(), fileName);
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is skipped rather than failing resolution outright.
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
