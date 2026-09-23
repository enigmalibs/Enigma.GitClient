using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// Starts another instance of the application, which is how several repositories are worked on side
/// by side: one repository per instance.
/// </summary>
/// <remarks>
/// A service rather than a call in the ViewModels because it reaches outside the process — a test must
/// be able to say "the page asked for a new window on this repository" without one appearing.
/// </remarks>
public interface IInstanceLauncher
{
    /// <summary>
    /// Starts another instance.
    /// </summary>
    /// <param name="repositoryPath">
    /// A repository for it to open straight away, or <see langword="null"/> for its start window.
    /// </param>
    /// <returns><see langword="true"/> when the process was started.</returns>
    bool Launch(string? repositoryPath = null);
}

/// <summary>
/// Default <see cref="IInstanceLauncher"/>: runs this very executable again.
/// </summary>
public sealed class InstanceLauncher : IInstanceLauncher
{
    private readonly ILogger<InstanceLauncher> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="logger">Receives a start that failed.</param>
    public InstanceLauncher(ILogger<InstanceLauncher> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public bool Launch(string? repositoryPath = null)
    {
        ProcessStartInfo? start = BuildStartInfo(
            Environment.ProcessPath,
            Assembly.GetEntryAssembly()?.Location,
            repositoryPath);

        if (start is null)
        {
            _logger.LogWarning("There is no executable path to start another instance from");
            return false;
        }

        try
        {
            using Process? process = Process.Start(start);
            return process is not null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            _logger.LogError(exception, "Starting another instance of {Executable} failed", start.FileName);
            return false;
        }
    }

    /// <summary>
    /// Works out how to start this application again.
    /// </summary>
    /// <param name="processPath">The running executable.</param>
    /// <param name="entryAssembly">The application's own assembly, for when the executable is the host.</param>
    /// <param name="repositoryPath">The repository the new instance opens, if any.</param>
    /// <returns>The start information, or <see langword="null"/> when there is nothing to start.</returns>
    /// <remarks>
    /// An argument list, never a command line: the path goes to the new process as one argument
    /// whatever it contains, and nothing is handed to a shell. When the application runs as
    /// <c>dotnet Enigma.GitClient.App.dll</c>, the running executable is the host, and it is given
    /// the assembly again.
    /// </remarks>
    internal static ProcessStartInfo? BuildStartInfo(string? processPath, string? entryAssembly, string? repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        ProcessStartInfo start = new(processPath) { UseShellExecute = false };

        bool isHost = string.Equals(
            Path.GetFileNameWithoutExtension(processPath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);

        if (isHost)
        {
            if (string.IsNullOrWhiteSpace(entryAssembly))
            {
                return null;
            }

            start.ArgumentList.Add(entryAssembly);
        }

        if (!string.IsNullOrWhiteSpace(repositoryPath))
        {
            start.ArgumentList.Add(repositoryPath);
        }

        return start;
    }
}
