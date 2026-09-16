using System;
using System.IO;
using System.Runtime.Versioning;

namespace Enigma.GitClient.Core.Configuration;

/// <summary>
/// Where the application keeps the state that belongs to the user rather than to a repository.
/// </summary>
public interface IAppPaths
{
    /// <summary>
    /// Gets the directory holding the user's configuration, creating it if it does not exist.
    /// </summary>
    string ConfigurationDirectory { get; }

    /// <summary>
    /// Builds the absolute path of a file inside the configuration directory, creating the
    /// directory if needed.
    /// </summary>
    /// <param name="fileName">The file's name.</param>
    /// <returns>The absolute path.</returns>
    string GetConfigurationFile(string fileName);
}

/// <summary>
/// Default <see cref="IAppPaths"/>: <c>$XDG_CONFIG_HOME/Enigma.GitClient</c> on Linux and
/// <c>%APPDATA%\Enigma.GitClient</c> on Windows, which is what
/// <see cref="Environment.SpecialFolder.ApplicationData"/> resolves to on each.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    /// <summary>
    /// The directory name used under the user's configuration root.
    /// </summary>
    public const string FolderName = "Enigma.GitClient";

    private readonly string _root;

    /// <summary>
    /// Initialises a new instance using the operating system's per-user configuration location.
    /// </summary>
    public AppPaths()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
            FolderName))
    {
    }

    /// <summary>
    /// Initialises a new instance rooted at an explicit directory. Tests use this so they never
    /// touch the developer's own configuration.
    /// </summary>
    /// <param name="root">The directory to use.</param>
    public AppPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
    }

    /// <inheritdoc />
    public string ConfigurationDirectory
    {
        get
        {
            EnsureDirectory();
            return _root;
        }
    }

    /// <inheritdoc />
    public string GetConfigurationFile(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Path.Combine(ConfigurationDirectory, fileName);
    }

    [UnsupportedOSPlatform("browser")]
    private void EnsureDirectory()
    {
        if (Directory.Exists(_root))
        {
            return;
        }

        DirectoryInfo directory = Directory.CreateDirectory(_root);

        if (!OperatingSystem.IsWindows())
        {
            // The configuration directory will hold encrypted access tokens, so it is created
            // private to the user from the start rather than tightened later.
            directory.UnixFileMode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        }
    }
}
