using System;
using Enigma.GitClient.Core.Worktrees;

namespace Enigma.GitClient.Desktop.Formatting;

/// <summary>
/// Says where a branch is checked out, in the same words wherever the application says it: the badge
/// on a branch's row, the tree on its history pill, and the refusals of a delete or a checkout git
/// would refuse.
/// </summary>
public static class WorktreeText
{
    /// <summary>
    /// Names a working tree by what it is and where it is.
    /// </summary>
    /// <param name="worktree">The working tree.</param>
    /// <returns><c>the worktree at &lt;path&gt;</c>, or <c>the main working tree at &lt;path&gt;</c>.</returns>
    public static string Location(GitWorktree worktree)
    {
        ArgumentNullException.ThrowIfNull(worktree);

        return worktree.IsMain
            ? $"the main working tree at {worktree.Path}"
            : $"the worktree at {worktree.Path}";
    }

    /// <summary>
    /// Says where a branch checked out in another working tree is, as a tooltip does.
    /// </summary>
    /// <param name="worktree">The working tree that has the branch checked out.</param>
    /// <returns>
    /// <c>Checked out in the worktree at &lt;path&gt;</c>, followed by <c>(its folder is missing)</c> when
    /// the folder is gone.
    /// </returns>
    public static string CheckedOutTip(GitWorktree worktree)
    {
        ArgumentNullException.ThrowIfNull(worktree);

        return $"Checked out in {Location(worktree)}" + (worktree.IsMissing ? " (its folder is missing)" : string.Empty);
    }
}
