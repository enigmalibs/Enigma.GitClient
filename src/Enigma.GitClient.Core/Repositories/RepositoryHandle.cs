using System;
using System.IO;

namespace Enigma.GitClient.Core.Repositories;

/// <summary>
/// An opened, non-bare git repository: where its work tree lives, where its git directory lives,
/// and a display name for the UI.
/// </summary>
public sealed class RepositoryHandle : IEquatable<RepositoryHandle>
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="workTreePath">The absolute path of the work-tree root.</param>
    /// <param name="gitDirectory">The absolute path of the git directory.</param>
    public RepositoryHandle(string workTreePath, string gitDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workTreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitDirectory);

        WorkTreePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workTreePath));
        GitDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gitDirectory));
        Name = Path.GetFileName(WorkTreePath) is { Length: > 0 } name ? name : WorkTreePath;
    }

    /// <summary>
    /// Gets the absolute path of the work-tree root.
    /// </summary>
    public string WorkTreePath { get; }

    /// <summary>
    /// Gets the absolute path of the git directory. For a worktree or a submodule this is not
    /// simply <c>.git</c> beneath the work tree.
    /// </summary>
    public string GitDirectory { get; }

    /// <summary>
    /// Gets the repository's display name — the last segment of its work-tree path.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Builds an absolute path to a file inside the git directory, for example <c>MERGE_HEAD</c>.
    /// </summary>
    /// <param name="relativePath">The path relative to the git directory.</param>
    /// <returns>The absolute path.</returns>
    public string GetGitPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return Path.Combine(GitDirectory, relativePath);
    }

    /// <inheritdoc />
    public bool Equals(RepositoryHandle? other)
        => other is not null &&
           string.Equals(WorkTreePath, other.WorkTreePath, PathComparison) &&
           string.Equals(GitDirectory, other.GitDirectory, PathComparison);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RepositoryHandle);

    /// <inheritdoc />
    public override int GetHashCode()
        => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase.GetHashCode(WorkTreePath)
            : StringComparer.Ordinal.GetHashCode(WorkTreePath);

    /// <inheritdoc />
    public override string ToString() => WorkTreePath;

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
