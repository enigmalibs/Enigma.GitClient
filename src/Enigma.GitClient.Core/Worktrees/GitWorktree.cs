namespace Enigma.GitClient.Core.Worktrees;

/// <summary>
/// One working tree of a repository, as <c>git worktree list</c> reports it: the main one, or a linked
/// worktree added beside it.
/// </summary>
/// <remarks>
/// A branch can be checked out in one working tree at a time, which is why git refuses to delete, check
/// out or force-move a branch another working tree has checked out. This record is what lets the
/// application say so before git does.
/// </remarks>
public sealed record GitWorktree
{
    /// <summary>
    /// Gets the working tree's folder, as a full path with the platform's separators and no trailing
    /// one.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the commit the working tree has checked out, or an empty string for a bare entry.
    /// </summary>
    public string HeadSha { get; init; } = string.Empty;

    /// <summary>
    /// Gets the branch the working tree has checked out, without <c>refs/heads/</c>, or
    /// <see langword="null"/> when it is detached or bare.
    /// </summary>
    public string? Branch { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is the main working tree — the first one git lists, which
    /// holds the repository's <c>.git</c> folder and can never be removed.
    /// </summary>
    public bool IsMain { get; init; }

    /// <summary>
    /// Gets a value indicating whether the entry is a bare repository, with no files checked out.
    /// </summary>
    public bool IsBare { get; init; }

    /// <summary>
    /// Gets a value indicating whether the working tree is on a detached HEAD.
    /// </summary>
    public bool IsDetached { get; init; }

    /// <summary>
    /// Gets a value indicating whether the worktree is locked against pruning and removal
    /// (<c>git worktree lock</c>).
    /// </summary>
    public bool IsLocked { get; init; }

    /// <summary>
    /// Gets the reason the worktree was locked with, or an empty string when none was given — or when
    /// git is older than 2.31, which prints none.
    /// </summary>
    public string LockReason { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the working tree's folder is gone: git still records it, until
    /// it is pruned or removed.
    /// </summary>
    public bool IsMissing { get; init; }
}
