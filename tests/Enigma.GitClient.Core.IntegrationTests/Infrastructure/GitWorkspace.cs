using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Enigma.GitClient.Core.IntegrationTests.Infrastructure;

/// <summary>
/// A throwaway directory holding one or more real git repositories plus an isolated git
/// configuration, so a test never reads the developer's own global config and never leaves
/// anything behind.
/// </summary>
public sealed class GitWorkspace : IAsyncDisposable
{
    private readonly Dictionary<string, string> _environment;

    private GitWorkspace(string rootPath, Dictionary<string, string> environment)
    {
        RootPath = rootPath;
        _environment = environment;
    }

    /// <summary>
    /// Gets the workspace root directory.
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Gets the environment every git invocation in this workspace must run with.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment => _environment;

    /// <summary>
    /// Creates a new isolated workspace.
    /// </summary>
    /// <returns>The workspace.</returns>
    public static GitWorkspace Create()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "enigma-gitclient-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        string home = Path.Combine(root, "home");
        Directory.CreateDirectory(home);

        // An empty file rather than /dev/null: git reads it happily on both platforms and a test
        // can append to it when it needs a specific global setting.
        string globalConfig = Path.Combine(home, ".gitconfig");
        File.WriteAllText(globalConfig, string.Empty);

        string systemConfig = Path.Combine(home, "system.gitconfig");
        File.WriteAllText(systemConfig, string.Empty);

        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["HOME"] = home,
            ["USERPROFILE"] = home,
            ["XDG_CONFIG_HOME"] = Path.Combine(home, ".config"),
            ["GIT_CONFIG_GLOBAL"] = globalConfig,
            ["GIT_CONFIG_SYSTEM"] = systemConfig,
            ["GIT_AUTHOR_NAME"] = "Enigma Test",
            ["GIT_AUTHOR_EMAIL"] = "test@enigma.invalid",
            ["GIT_COMMITTER_NAME"] = "Enigma Test",
            ["GIT_COMMITTER_EMAIL"] = "test@enigma.invalid",
            ["GIT_TERMINAL_PROMPT"] = "0",
        };

        return new GitWorkspace(root, environment);
    }

    /// <summary>
    /// Creates a repository inside the workspace.
    /// </summary>
    /// <param name="name">The directory name of the repository.</param>
    /// <param name="bare">Whether the repository is bare.</param>
    /// <param name="initialBranch">The name of the initial branch.</param>
    /// <returns>The initialised repository.</returns>
    public async Task<TemporaryRepository> InitRepositoryAsync(
        string name = "repo",
        bool bare = false,
        string initialBranch = "main")
    {
        string path = Path.Combine(RootPath, name);
        Directory.CreateDirectory(path);

        List<string> arguments = ["init"];
        if (bare)
        {
            arguments.Add("--bare");
        }

        arguments.Add(".");

        await GitCli.RunAsync(path, Environment, arguments);

        // "git init -b" only exists from 2.28; setting the symbolic ref works on every version and
        // keeps the fixture honest about the minimum the product supports.
        await GitCli.RunAsync(path, Environment, ["symbolic-ref", "HEAD", $"refs/heads/{initialBranch}"]);

        return new TemporaryRepository(this, path, bare);
    }

    /// <summary>
    /// Creates an empty, non-repository directory inside the workspace.
    /// </summary>
    /// <param name="name">The directory name.</param>
    /// <returns>The absolute path of the directory.</returns>
    public string CreateDirectory(string name)
    {
        string path = Path.Combine(RootPath, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        DirectoryCleanup.Delete(RootPath);
        return ValueTask.CompletedTask;
    }
}
