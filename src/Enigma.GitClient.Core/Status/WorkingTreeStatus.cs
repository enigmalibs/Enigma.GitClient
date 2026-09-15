using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Files;

namespace Enigma.GitClient.Core.Status;

/// <summary>
/// What a submodule entry's own state is, as <c>--porcelain=v2</c> reports it in its four-character
/// submodule field.
/// </summary>
/// <param name="IsSubmodule">Whether the entry is a submodule at all.</param>
/// <param name="CommitChanged">Whether the recorded commit differs from the one checked out.</param>
/// <param name="HasModifiedTracked">Whether the submodule has modified tracked files.</param>
/// <param name="HasUntracked">Whether the submodule has untracked files.</param>
public readonly record struct SubmoduleState(
    bool IsSubmodule,
    bool CommitChanged,
    bool HasModifiedTracked,
    bool HasUntracked)
{
    /// <summary>
    /// An entry that is not a submodule.
    /// </summary>
    public static readonly SubmoduleState None = default;

    /// <summary>
    /// Gets a value indicating whether the submodule's own work tree is dirty.
    /// </summary>
    public bool IsDirty => HasModifiedTracked || HasUntracked;

    /// <summary>
    /// Parses the four-character field: <c>N...</c> for a plain file, <c>S&lt;c&gt;&lt;m&gt;&lt;u&gt;</c>
    /// for a submodule.
    /// </summary>
    /// <param name="field">The field as git wrote it.</param>
    /// <returns>The state.</returns>
    public static SubmoduleState Parse(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.Length < 4 || field[0] != 'S'
            ? None
            : new SubmoduleState(true, field[1] == 'C', field[2] == 'M', field[3] == 'U');
    }
}

/// <summary>
/// Where HEAD is, according to <c>git status</c>'s own branch headers.
/// </summary>
/// <param name="Oid">The commit HEAD resolves to, or <c>(initial)</c> before the first commit.</param>
/// <param name="Head">The branch name, or <c>(detached)</c>.</param>
/// <param name="Upstream">The upstream branch, empty when there is none.</param>
/// <param name="Ahead">How many commits the branch has that its upstream does not.</param>
/// <param name="Behind">How many commits the upstream has that the branch does not.</param>
public sealed record WorkingTreeBranch(
    string Oid,
    string Head,
    string Upstream,
    int Ahead,
    int Behind)
{
    /// <summary>
    /// What git reports for a repository with no commits yet.
    /// </summary>
    public const string InitialOid = "(initial)";

    /// <summary>
    /// What git reports when HEAD is not on a branch.
    /// </summary>
    public const string DetachedHead = "(detached)";

    /// <summary>
    /// The state of a repository whose status has not been read.
    /// </summary>
    public static readonly WorkingTreeBranch Unknown = new(string.Empty, string.Empty, string.Empty, 0, 0);

    /// <summary>Gets a value indicating whether HEAD is not on a branch.</summary>
    public bool IsDetached => string.Equals(Head, DetachedHead, StringComparison.Ordinal);

    /// <summary>Gets a value indicating whether the repository has no commits yet.</summary>
    public bool IsUnborn => string.Equals(Oid, InitialOid, StringComparison.Ordinal);

    /// <summary>Gets a value indicating whether the branch tracks an upstream.</summary>
    public bool HasUpstream => Upstream.Length > 0;
}

/// <summary>
/// Everything <c>git status</c> reported: where HEAD is, and every file that differs from it.
/// </summary>
/// <param name="Branch">Where HEAD is.</param>
/// <param name="Staged">The changes that are in the index and would be committed.</param>
/// <param name="Unstaged">The changes that are in the work tree only.</param>
/// <param name="Untracked">Files git has never been told about.</param>
/// <param name="Conflicted">Files with unresolved merge conflicts.</param>
/// <param name="Ignored">Files an ignore rule covers, present only when they were asked for.</param>
public sealed record WorkingTreeStatus(
    WorkingTreeBranch Branch,
    IReadOnlyList<ChangedFile> Staged,
    IReadOnlyList<ChangedFile> Unstaged,
    IReadOnlyList<ChangedFile> Untracked,
    IReadOnlyList<ChangedFile> Conflicted,
    IReadOnlyList<ChangedFile> Ignored)
{
    /// <summary>
    /// The status of a repository that has not been read.
    /// </summary>
    public static readonly WorkingTreeStatus Empty =
        new(WorkingTreeBranch.Unknown, [], [], [], [], []);

    /// <summary>
    /// Gets a value indicating whether there is nothing to commit and nothing in the way.
    /// </summary>
    /// <remarks>
    /// Ignored files deliberately do not count: they are on disk on purpose and a working tree full
    /// of build output is still a clean one.
    /// </remarks>
    public bool IsClean
        => Staged.Count == 0 && Unstaged.Count == 0 && Untracked.Count == 0 && Conflicted.Count == 0;

    /// <summary>
    /// Gets a value indicating whether a merge or a similar operation left conflicts behind.
    /// </summary>
    public bool HasConflicts => Conflicted.Count > 0;

    /// <summary>
    /// Gets how many entries the status holds, ignored files excluded.
    /// </summary>
    public int Count => Staged.Count + Unstaged.Count + Untracked.Count + Conflicted.Count;

    /// <summary>
    /// Enumerates everything that is not staged: the work-tree changes, the untracked files and the
    /// conflicts, which is what the "unstaged" half of the changes page shows.
    /// </summary>
    /// <returns>The entries, work-tree changes first.</returns>
    public IEnumerable<ChangedFile> NotStaged()
    {
        foreach (ChangedFile file in Conflicted)
        {
            yield return file;
        }

        foreach (ChangedFile file in Unstaged)
        {
            yield return file;
        }

        foreach (ChangedFile file in Untracked)
        {
            yield return file;
        }
    }
}
