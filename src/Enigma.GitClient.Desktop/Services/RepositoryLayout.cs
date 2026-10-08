using System;
using System.IO;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Where a repository's files are, and what a change to each of them means for what the application
/// shows.
/// </summary>
/// <param name="WorkTree">The work tree's root.</param>
/// <param name="GitDirectory">
/// The git directory: <c>HEAD</c>, the index and the state of a merge. For a linked worktree,
/// <c>&lt;main&gt;/.git/worktrees/&lt;name&gt;</c>; for a submodule,
/// <c>&lt;superproject&gt;/.git/modules/&lt;name&gt;</c>.
/// </param>
/// <param name="CommonDirectory">
/// Where the references live — <c>refs/</c>, <c>packed-refs</c> and <c>config</c>: the git directory
/// itself, except in a linked worktree, where it is the main repository's.
/// </param>
/// <remarks>
/// Telling events apart is string work on paths alone, with no disk access, so every rule is tested on
/// its own. It is asked about every event the operating system reports, a build's thousands included,
/// and allocates nothing.
/// </remarks>
public sealed record RepositoryLayout(string WorkTree, string GitDirectory, string CommonDirectory)
{
    /// <summary>
    /// The files directly in a git directory whose change is a change of the reference state: where
    /// HEAD is, what is staged, a merge, cherry-pick or revert under way, the packed references, and
    /// the configuration that names the remotes and the upstreams.
    /// </summary>
    /// <remarks>
    /// <c>FETCH_HEAD</c> is deliberately not here: every fetch rewrites it, the automatic refresh's
    /// included, and what a fetch changes is in <c>refs/</c>.
    /// </remarks>
    private static readonly string[] ReferenceFiles =
        ["HEAD", "index", "packed-refs", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "ORIG_HEAD", "config"];

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Says what a change to a path means.
    /// </summary>
    /// <param name="path">The full path the operating system reported.</param>
    /// <returns>
    /// <see cref="RepositoryChanges.References"/> for a reference, HEAD, the index or the merge state;
    /// <see cref="RepositoryChanges.WorkingTree"/> for a file of the work tree; and
    /// <see cref="RepositoryChanges.None"/> for everything else — objects, logs, lock files, the git
    /// directory's own bookkeeping, and anything outside the repository.
    /// </returns>
    public RepositoryChanges Classify(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return RepositoryChanges.None;
        }

        // The git directory before the common one: a linked worktree's git directory is inside the
        // main repository's, and its HEAD is this worktree's own.
        if (TryRelativeTo(path, GitDirectory, out ReadOnlySpan<char> inGitDirectory))
        {
            return ClassifyGitPath(inGitDirectory, holdsReferences: GitDirectory.Equals(CommonDirectory, PathComparison));
        }

        if (TryRelativeTo(path, CommonDirectory, out ReadOnlySpan<char> inCommonDirectory))
        {
            return ClassifyGitPath(inCommonDirectory, holdsReferences: true);
        }

        if (TryRelativeTo(path, WorkTree, out ReadOnlySpan<char> inWorkTree))
        {
            // The git directory under the work tree, or a linked worktree's .git file: the git
            // directory's own rules decide about them, and a recursive watch of the work tree sees
            // them too.
            return FirstSegment(inWorkTree).Equals(".git", PathComparison)
                ? RepositoryChanges.None
                : RepositoryChanges.WorkingTree;
        }

        return RepositoryChanges.None;
    }

    private static RepositoryChanges ClassifyGitPath(ReadOnlySpan<char> relative, bool holdsReferences)
    {
        // git writes every file through a lock beside it and renames the lock into place: the rename
        // is the change, and the lock on its own is nothing.
        if (relative.EndsWith(".lock", PathComparison))
        {
            return RepositoryChanges.None;
        }

        ReadOnlySpan<char> first = FirstSegment(relative);

        if (first.Length == relative.Length)
        {
            foreach (string file in ReferenceFiles)
            {
                if (first.Equals(file, PathComparison))
                {
                    return RepositoryChanges.References;
                }
            }

            return RepositoryChanges.None;
        }

        return holdsReferences && first.Equals("refs", PathComparison)
            ? RepositoryChanges.References
            : RepositoryChanges.None;
    }

    /// <summary>
    /// Finds the path relative to a directory: empty for the directory itself, and nothing when the
    /// path is not inside it.
    /// </summary>
    private static bool TryRelativeTo(string path, string directory, out ReadOnlySpan<char> relative)
    {
        relative = default;

        if (directory.Length == 0 || !path.StartsWith(directory, PathComparison))
        {
            return false;
        }

        if (path.Length == directory.Length)
        {
            return true;
        }

        char next = path[directory.Length];

        if (next != Path.DirectorySeparatorChar && next != Path.AltDirectorySeparatorChar)
        {
            return false;
        }

        relative = path.AsSpan(directory.Length + 1);
        return true;
    }

    private static ReadOnlySpan<char> FirstSegment(ReadOnlySpan<char> relative)
    {
        int separator = relative.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return separator < 0 ? relative : relative[..separator];
    }
}
