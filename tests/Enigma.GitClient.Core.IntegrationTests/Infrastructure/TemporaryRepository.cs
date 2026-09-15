using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Infrastructure;

/// <summary>
/// A real git repository living inside a <see cref="GitWorkspace"/>, with the helpers fixtures
/// need to build a history worth asserting against.
/// </summary>
public sealed class TemporaryRepository
{
    internal TemporaryRepository(GitWorkspace workspace, string path, bool isBare)
    {
        Workspace = workspace;
        Path = path;
        IsBare = isBare;
    }

    /// <summary>
    /// Gets the workspace this repository belongs to.
    /// </summary>
    public GitWorkspace Workspace { get; }

    /// <summary>
    /// Gets the absolute path of the repository (its work tree, or the git directory when bare).
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets a value indicating whether the repository is bare.
    /// </summary>
    public bool IsBare { get; }

    /// <summary>
    /// Runs a git command in the repository and returns its standard output.
    /// </summary>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>Standard output.</returns>
    public Task<string> GitAsync(params string[] arguments)
        => GitCli.RunAsync(Path, Workspace.Environment, arguments);

    /// <summary>
    /// Runs a git command and returns its output with the trailing newline removed.
    /// </summary>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>The trimmed standard output.</returns>
    public async Task<string> GitLineAsync(params string[] arguments)
        => (await GitAsync(arguments)).TrimEnd('\n', '\r');

    /// <summary>
    /// Writes a file inside the work tree, creating any missing directories.
    /// </summary>
    /// <param name="relativePath">The path relative to the work tree.</param>
    /// <param name="content">The file's content.</param>
    public void WriteFile(string relativePath, string content)
    {
        string full = System.IO.Path.Combine(Path, relativePath);
        string? directory = System.IO.Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(full, content);
    }

    /// <summary>
    /// Deletes a file from the work tree.
    /// </summary>
    /// <param name="relativePath">The path relative to the work tree.</param>
    public void DeleteFile(string relativePath)
        => File.Delete(System.IO.Path.Combine(Path, relativePath));

    /// <summary>
    /// Stages everything and commits it.
    /// </summary>
    /// <param name="message">The commit message.</param>
    /// <returns>The new commit's full SHA.</returns>
    public async Task<string> CommitAllAsync(string message)
    {
        await GitAsync("add", "--all");
        await GitAsync("commit", "-m", message);
        return await GitLineAsync("rev-parse", "HEAD");
    }

    /// <summary>
    /// Writes a file, stages it and commits it in one step.
    /// </summary>
    /// <param name="relativePath">The path relative to the work tree.</param>
    /// <param name="content">The file's content.</param>
    /// <param name="message">The commit message.</param>
    /// <returns>The new commit's full SHA.</returns>
    public async Task<string> CommitFileAsync(string relativePath, string content, string message)
    {
        WriteFile(relativePath, content);
        return await CommitAllAsync(message);
    }

    /// <summary>
    /// Creates an initial commit so the repository has a HEAD.
    /// </summary>
    /// <param name="message">The commit message.</param>
    /// <returns>The new commit's full SHA.</returns>
    public Task<string> CommitInitialAsync(string message = "Initial commit")
        => CommitFileAsync("README.md", "# fixture\n", message);

    /// <summary>
    /// Reads the full SHA a revision resolves to.
    /// </summary>
    /// <param name="revision">Any revision expression.</param>
    /// <returns>The full SHA.</returns>
    public Task<string> ResolveAsync(string revision) => GitLineAsync("rev-parse", revision);

    /// <summary>
    /// Builds an absolute path inside the work tree.
    /// </summary>
    /// <param name="relativePath">The path relative to the work tree.</param>
    /// <returns>The absolute path.</returns>
    public string GetPath(string relativePath) => System.IO.Path.Combine(Path, relativePath);

    /// <summary>
    /// Creates a directory inside the work tree.
    /// </summary>
    /// <param name="relativePath">The path relative to the work tree.</param>
    /// <returns>The absolute path of the created directory.</returns>
    public string CreateDirectory(string relativePath)
    {
        string full = GetPath(relativePath);
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>
    /// Runs a git command and returns its output split into lines.
    /// </summary>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>The non-empty output lines.</returns>
    public async Task<IReadOnlyList<string>> GitLinesAsync(params string[] arguments)
        => (await GitAsync(arguments)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Waits long enough for the next commit to carry a distinct timestamp, for tests that assert
    /// ordering by date rather than by topology.
    /// </summary>
    /// <returns>A task that completes after the delay.</returns>
    public static Task DelayForDistinctTimestampAsync()
        => Task.Delay(TimeSpan.FromMilliseconds(1100), TestContext.Current.CancellationToken);
}
