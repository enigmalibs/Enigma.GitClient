using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Enigma.GitClient.Core.Worktrees;

/// <summary>
/// Where a new worktree goes: the folder proposed for it, and whether a folder can take one.
/// </summary>
/// <remarks>
/// A worktree goes <em>next to</em> the repository, never inside it: inside, its files would show up
/// as the repository's own untracked changes. Both the dialog, on every keystroke, and the service,
/// before git runs, judge a folder by <see cref="Validate"/>, so the reason a folder is refused is the
/// same sentence wherever it is given.
/// </remarks>
public static class WorktreePaths
{
    /// <summary>
    /// What joins the repository's folder name and the branch in a proposed folder:
    /// <c>Repo.worktree.topic</c>.
    /// </summary>
    public const string Infix = ".worktree.";

    /// <summary>
    /// The characters a branch name may hold that a folder name may not, on one platform or the
    /// other — and the separators, which would make the folder a path of several.
    /// </summary>
    private const string ReplacedCharacters = "/\\<>:\"|?*";

    /// <summary>
    /// Proposes the folder for a worktree on a branch: beside the repository, named after it and the
    /// branch.
    /// </summary>
    /// <param name="workTreePath">The repository's working tree.</param>
    /// <param name="branch">The branch the worktree is for.</param>
    /// <returns>
    /// <c>&lt;parent&gt;/&lt;folder&gt;.worktree.&lt;branch&gt;</c>, or an empty string for a repository
    /// at the root of a file system, which has nothing beside it.
    /// </returns>
    public static string DefaultFor(string workTreePath, string branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workTreePath);
        ArgumentNullException.ThrowIfNull(branch);

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workTreePath));
        string? parent = Path.GetDirectoryName(root);
        string name = Path.GetFileName(root);

        return parent is null || name.Length == 0
            ? string.Empty
            : Path.Combine(parent, name + Infix + ToFolderName(branch.Trim()));
    }

    /// <summary>
    /// Turns a branch name into one folder name: <c>feature/login</c> becomes <c>feature-login</c>.
    /// </summary>
    /// <param name="branch">The branch name.</param>
    /// <returns>The name with every separator, every character a file system refuses and every control
    /// character replaced by <c>-</c>.</returns>
    public static string ToFolderName(string branch)
    {
        ArgumentNullException.ThrowIfNull(branch);

        StringBuilder name = new(branch.Length);

        foreach (char character in branch)
        {
            name.Append(char.IsControl(character) || ReplacedCharacters.Contains(character, StringComparison.Ordinal)
                ? '-'
                : character);
        }

        return name.ToString();
    }

    /// <summary>
    /// Says why a folder cannot take a new worktree of a repository.
    /// </summary>
    /// <param name="workTreePath">The repository's working tree.</param>
    /// <param name="path">The folder proposed for the worktree.</param>
    /// <returns>
    /// The reason, empty when the folder can take it: a folder that does not exist yet, or an empty one
    /// — git accepts both.
    /// </returns>
    public static string Validate(string workTreePath, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workTreePath);

        if (string.IsNullOrWhiteSpace(path))
        {
            return "Choose the folder the worktree goes in.";
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return "The folder must be a full path, starting from the root of a drive.";
        }

        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workTreePath));

        if (string.Equals(full, root, PathComparison))
        {
            return "That is the repository's own folder. A worktree goes next to it, not in it.";
        }

        string inside = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;

        if (full.StartsWith(inside, PathComparison))
        {
            return "That folder is inside the repository, where the worktree would show up in its changes. Choose a folder next to it.";
        }

        if (File.Exists(full))
        {
            return $"'{full}' is a file.";
        }

        try
        {
            return Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()
                ? $"'{full}' already exists and is not empty."
                : string.Empty;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return $"'{full}' cannot be read.";
        }
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
