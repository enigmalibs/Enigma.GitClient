using System;
using System.Collections.Generic;
using System.Globalization;

namespace Enigma.GitClient.Desktop.ViewModels.Dialogs;

/// <summary>
/// What removing a worktree costs, said before it is removed: the folder that goes, the uncommitted work
/// in it, the windows still working there — and whether its branch goes too.
/// </summary>
/// <remarks>
/// The branch is offered ticked only when nothing would be lost by deleting it: an unmerged branch is
/// offered unticked, with the commits it holds named under the box, so keeping the work is what happens
/// unless the reader says otherwise.
/// </remarks>
public sealed class RemoveWorktreeDialogViewModel : ViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="path">The worktree's folder.</param>
    /// <param name="isMissing">Whether the folder is already gone.</param>
    /// <param name="changes">How many uncommitted changes are in it.</param>
    /// <param name="branch">The branch it has checked out, or <see langword="null"/> when it is detached.</param>
    /// <param name="unmergedCommits">How many commits the branch holds that the current branch does not.</param>
    /// <param name="unmergedSubjects">The first of those commits' subjects, newest first.</param>
    public RemoveWorktreeDialogViewModel(
        string path,
        bool isMissing,
        int changes,
        string? branch,
        int unmergedCommits,
        IReadOnlyList<string> unmergedSubjects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(unmergedSubjects);

        Path = path;
        IsMissing = isMissing;
        Changes = Math.Max(0, changes);
        Branch = branch is { Length: > 0 } ? branch : null;
        UnmergedCommits = Math.Max(Math.Max(0, unmergedCommits), unmergedSubjects.Count);

        List<string> listed = [.. unmergedSubjects];

        if (UnmergedCommits > unmergedSubjects.Count)
        {
            listed.Add($"…and {(UnmergedCommits - unmergedSubjects.Count).ToString(CultureInfo.CurrentCulture)} more");
        }

        UnmergedList = listed;

        // Nothing to lose: offered ticked. Commits only the branch has: offered unticked.
        DeleteBranch = HasBranch && !IsUnmerged;
    }

    /// <summary>Gets the worktree's folder.</summary>
    public string Path { get; }

    /// <summary>Gets a value indicating whether the folder is already gone.</summary>
    public bool IsMissing { get; }

    /// <summary>Gets how many uncommitted changes are in the worktree.</summary>
    public int Changes { get; }

    /// <summary>Gets the branch the worktree has checked out, or <see langword="null"/> when it is detached.</summary>
    public string? Branch { get; }

    /// <summary>Gets how many commits the branch holds that the current branch does not.</summary>
    public int UnmergedCommits { get; }

    /// <summary>Gets the question: which folder goes.</summary>
    public string Question
        => IsMissing
            ? $"Remove the worktree at {Path}? Its folder is already gone; this removes git's record of it."
            : $"Remove the worktree at {Path}? Its folder is deleted.";

    /// <summary>Gets a value indicating whether there is uncommitted work in the worktree.</summary>
    public bool HasChanges => Changes > 0;

    /// <summary>Gets what is said, in the warning colour, about the uncommitted work that would be lost.</summary>
    public string ChangesWarning
        => Changes == 1
            ? "1 uncommitted change in it will be lost."
            : $"{Changes.ToString(CultureInfo.CurrentCulture)} uncommitted changes in it will be lost.";

    /// <summary>Gets what is always said: another window may still be working in the folder.</summary>
    public string CloseWindowsNote => "Close any window working in it first.";

    /// <summary>Gets a value indicating whether there is a branch to offer deleting — none for a detached worktree.</summary>
    public bool HasBranch => Branch is not null;

    /// <summary>Gets what the box that deletes the branch too says.</summary>
    public string DeleteBranchLabel => $"Also delete the branch \"{Branch}\"";

    /// <summary>Gets or sets a value indicating whether the branch is deleted with the worktree.</summary>
    public bool DeleteBranch { get; set => SetProperty(ref field, value && HasBranch); }

    /// <summary>Gets a value indicating whether the branch holds commits the current branch does not.</summary>
    public bool IsUnmerged => HasBranch && UnmergedCommits > 0;

    /// <summary>Gets what is said under the box about the commits deleting the branch would lose.</summary>
    public string UnmergedNote
        => UnmergedCommits == 1
            ? "holds 1 commit the current branch does not; deleting it loses it"
            : $"holds {UnmergedCommits.ToString(CultureInfo.CurrentCulture)} commits the current branch does not; deleting it loses them";

    /// <summary>Gets the subjects of those commits, newest first, then how many more there are.</summary>
    public IReadOnlyList<string> UnmergedList { get; }

    /// <summary>Gets what the button that removes says: plainly, or "anyway" when work would be lost.</summary>
    public string ConfirmText => HasChanges ? "Remove anyway" : "Remove";
}
