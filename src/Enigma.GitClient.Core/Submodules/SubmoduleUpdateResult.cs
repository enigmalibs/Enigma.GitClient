using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Sync;

namespace Enigma.GitClient.Core.Submodules;

/// <summary>
/// Why an update left a submodule where it was.
/// </summary>
public enum SubmoduleSkipReason
{
    /// <summary>Files git tracks in it are modified; moving it could lose or tangle them.</summary>
    UncommittedChanges,

    /// <summary>
    /// It is on a commit no branch, tag or remote branch contains: moving it would leave that commit
    /// where nothing finds it again.
    /// </summary>
    CommitOnNoBranch,

    /// <summary>A merge left two commits for it; the conflict is resolved first.</summary>
    Conflicted,

    /// <summary>The path asked for is not a submodule <c>.gitmodules</c> declares.</summary>
    NotASubmodule,
}

/// <summary>
/// A submodule an update left where it was, and why.
/// </summary>
/// <param name="Path">The submodule's path, as <see cref="GitSubmodule.Path"/>.</param>
/// <param name="Reason">Why it was left.</param>
/// <param name="CheckedOutSha">The commit it stays on, or <see langword="null"/> when it has none.</param>
public sealed record SubmoduleSkip(string Path, SubmoduleSkipReason Reason, string? CheckedOutSha = null)
{
    /// <summary>
    /// Gets the sentence that says so, to put in front of a user.
    /// </summary>
    public string Message
        => Reason switch
        {
            SubmoduleSkipReason.UncommittedChanges => $"{Path} has uncommitted changes.",
            SubmoduleSkipReason.CommitOnNoBranch =>
                $"{Path} is on {Short(CheckedOutSha)}, a commit no branch or tag contains; moving it would leave that commit behind.",
            SubmoduleSkipReason.Conflicted => $"{Path} has a merge conflict to resolve first.",
            SubmoduleSkipReason.NotASubmodule => $"{Path} is not a submodule listed in .gitmodules.",
            _ => $"{Path} was left as it is.",
        };

    private static string Short(string? sha)
        => sha is { Length: > 7 } ? sha.Substring(0, 7) : sha ?? "a commit";
}

/// <summary>
/// A submodule git could not update.
/// </summary>
/// <param name="Path">The submodule's path, as <see cref="GitSubmodule.Path"/>.</param>
/// <param name="Message">What git said, with any credential redacted.</param>
public sealed record SubmoduleFailure(string Path, string Message);

/// <summary>
/// What an update did with each submodule it looked at.
/// </summary>
/// <param name="Updated">The paths of the submodules it downloaded or moved, in the order it did.</param>
/// <param name="Skipped">The submodules it left where they were on purpose.</param>
/// <param name="Failed">The submodules git could not update.</param>
public sealed record SubmoduleUpdateResult(
    IReadOnlyList<string> Updated,
    IReadOnlyList<SubmoduleSkip> Skipped,
    IReadOnlyList<SubmoduleFailure> Failed)
{
    /// <summary>
    /// The result of an update that had nothing to do.
    /// </summary>
    public static readonly SubmoduleUpdateResult Nothing = new([], [], []);

    /// <summary>
    /// Gets a value indicating whether a submodule was skipped or failed — something to tell the user.
    /// </summary>
    public bool HasProblems => Skipped.Count > 0 || Failed.Count > 0;
}

/// <summary>
/// One progress report from a submodule update.
/// </summary>
/// <param name="Path">The submodule being updated.</param>
/// <param name="Step">What git said about it, parsed like a fetch's progress.</param>
public sealed record SubmoduleUpdateProgress(string Path, SyncProgress Step)
{
    /// <summary>
    /// Gets the sentence to put in front of a user.
    /// </summary>
    public string Description
        => Step.Stage is SyncStage.Starting or SyncStage.Other
            ? $"Updating submodule {Path}"
            : $"Updating submodule {Path} — {Step.Description}";

    /// <summary>
    /// Builds the report sent before git starts on a submodule.
    /// </summary>
    /// <param name="path">The submodule.</param>
    /// <returns>The report.</returns>
    public static SubmoduleUpdateProgress Starting(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return new SubmoduleUpdateProgress(path, SyncProgress.Starting);
    }
}
