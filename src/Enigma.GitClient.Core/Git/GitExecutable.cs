using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using IOPath = System.IO.Path;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Resolves the <c>git</c> executable from the configured override, the usual installation
/// directories, or the <c>PATH</c>. The result is cached for the lifetime of the instance.
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
                return path;
            }

            throw new GitNotFoundException(
                "No 'git' executable could be found. Install git 2.20 or newer and make sure it is on the PATH, " +
                "or set an explicit path in the application settings.");
        }
    }

    /// <inheritdoc />
    public bool TryResolve([NotNullWhen(true)] out string? path)
    {
        lock (_gate)
        {
            if (!_resolutionAttempted)
            {
                _resolved = Resolve(
                    _options.Value.ExecutablePath,
                    Environment.GetEnvironmentVariable,
                    File.Exists,
                    OperatingSystem.IsWindows());
                _resolutionAttempted = true;
            }

            path = _resolved;
            return path is not null;
        }
    }

    /// <summary>
    /// Applies the resolution rules to a configured path, an environment and a file system.
    /// </summary>
    /// <param name="configuredPath">The path the settings name, honoured verbatim when it is set.</param>
    /// <param name="environment">Reads an environment variable (<c>PATH</c>, <c>ProgramFiles</c>, …).</param>
    /// <param name="fileExists">Says whether a file exists.</param>
    /// <param name="isWindows">Whether the usual Windows installation directories are searched, and <c>git.exe</c> is looked for.</param>
    /// <returns>
    /// The executable's path, or <see langword="null"/> when nothing was found. Never the bare name:
    /// handed to the operating system, it would be looked for in the current directory first — often a
    /// repository, whose content somebody else wrote.
    /// </returns>
    public static string? Resolve(
        string? configuredPath,
        Func<string, string?> environment,
        Func<string, bool> fileExists,
        bool isWindows)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(fileExists);

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            // An explicit override is honoured verbatim, so a user can point at any build of git.
            return configuredPath;
        }

        if (isWindows)
        {
            foreach (string candidate in WindowsCandidates(environment))
            {
                if (fileExists(candidate))
                {
                    return candidate;
                }
            }
        }

        return ExecutableSearch.FindOnPath(isWindows ? "git.exe" : "git", environment("PATH"), fileExists);
    }

    private static IEnumerable<string> WindowsCandidates(Func<string, string?> environment)
    {
        foreach (string variable in new[] { "ProgramFiles", "ProgramFiles(x86)", "LocalAppData" })
        {
            string? root = environment(variable);
            if (string.IsNullOrWhiteSpace(root) || !IOPath.IsPathFullyQualified(root))
            {
                continue;
            }

            yield return IOPath.Combine(root, "Git", "cmd", "git.exe");
            yield return IOPath.Combine(root, "Programs", "Git", "cmd", "git.exe");
        }
    }
}
