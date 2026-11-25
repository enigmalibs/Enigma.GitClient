using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Submodules;

/// <summary>
/// Reads a repository's submodules and puts them on the commits the repository expects.
/// </summary>
/// <remarks>
/// <para>
/// A submodule is a repository inside another one. The outer repository does not store its files: it
/// records one commit for the submodule's folder — the <em>expected</em> commit — and git checks that
/// commit out in the folder, on no branch, when it updates the submodule. Nothing moves the folder by
/// itself: after a pull that changes the expected commit, the folder stays where it was until it is
/// updated.
/// </para>
/// <para>
/// Every update here keeps one rule: it never moves a submodule with uncommitted changes, or one on a
/// commit no branch, tag or remote branch contains — that commit would be lost from sight. Such a
/// submodule is skipped and named, with the reason.
/// </para>
/// </remarks>
public interface ISubmoduleService
{
    /// <summary>
    /// Lists a repository's submodules, and theirs.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The submodules, each followed by the ones nested in it, in git's order; empty at once — no git
    /// runs — when the work tree has no <c>.gitmodules</c>.
    /// </returns>
    /// <exception cref="GitCommandException">
    /// git could not read the submodules — for instance a folder recorded as a submodule that
    /// <c>.gitmodules</c> does not declare.
    /// </exception>
    Task<IReadOnlyList<GitSubmodule>> ListAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads submodules and puts them on the commits the repository expects, and the ones nested in
    /// them on the commits they expect.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="paths">
    /// The submodules to update, by <see cref="GitSubmodule.Path"/> — nested ones included — or
    /// <see langword="null"/> for every submodule.
    /// </param>
    /// <param name="progress">Receives git's progress, submodule by submodule.</param>
    /// <param name="cancellationToken">Cancels the update; what is done stays done.</param>
    /// <returns>What was updated, skipped and why, and what git could not update.</returns>
    /// <remarks>
    /// <c>git submodule update --init --checkout</c>, one submodule at a time: <c>--checkout</c> overrides
    /// a <c>submodule.&lt;name&gt;.update</c> setting, so nothing is ever rebased or merged, and the
    /// submodule ends on the expected commit with no branch checked out. A submodule is not moved when
    /// it has uncommitted changes or is on a commit no branch contains (<see cref="SubmoduleSkip"/>);
    /// the ones nested in it are still looked at, against the commit it stays on.
    /// </remarks>
    Task<SubmoduleUpdateResult> UpdateAsync(
        RepositoryHandle repository,
        IReadOnlyCollection<string>? paths = null,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts on their expected commits the submodules an operation moved the repository's expectation
    /// for — and only those, plus the ones not downloaded yet.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="oldSha">
    /// The commit HEAD was on before the operation, or <see langword="null"/> when there was none — every
    /// submodule then counts as moved.
    /// </param>
    /// <param name="newSha">The commit HEAD is on after it.</param>
    /// <param name="progress">Receives git's progress, submodule by submodule.</param>
    /// <param name="cancellationToken">Cancels the update; what is done stays done.</param>
    /// <returns>What was updated, skipped and why, and what git could not update.</returns>
    /// <remarks>
    /// The submodules whose recorded commit differs between the two commits
    /// (<see cref="ChangedPinsAsync"/>), and the ones <c>.gitmodules</c> declares that are not
    /// downloaded, go through <see cref="UpdateAsync"/>'s rule. A submodule the operation did not touch —
    /// one the user moved on purpose — stays where it is. Inside a submodule that moves, the same rule
    /// applies to the ones nested in it, between the commit it leaves and the one it goes to. Nothing
    /// runs without a <c>.gitmodules</c>.
    /// </remarks>
    Task<SubmoduleUpdateResult> FollowAsync(
        RepositoryHandle repository,
        string? oldSha,
        string newSha,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the commit a repository's HEAD is on — what <see cref="FollowAsync"/> compares.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The commit's full name, or <see langword="null"/> before the first commit.</returns>
    Task<string?> ReadHeadAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Says what a change of a submodule's recorded commit does: which way it goes, and the commits it
    /// brings in or takes back.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="path">The submodule's path.</param>
    /// <param name="oldSha">The commit recorded before, or <see langword="null"/> for a new submodule.</param>
    /// <param name="newSha">The commit recorded after, or <see langword="null"/> for a removed one.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The change; <see cref="PinDirection.Unknown"/>, with why, when the submodule is not downloaded or
    /// lacks one of the commits. Nothing is fetched.
    /// </returns>
    Task<PinChange> DescribePinChangeAsync(
        RepositoryHandle repository,
        string path,
        string? oldSha,
        string? newSha,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one commit of a submodule: its title, author and date.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="path">The submodule's path.</param>
    /// <param name="sha">The commit.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The commit, or <see langword="null"/> when the submodule is not downloaded or lacks it.</returns>
    Task<PinCommit?> DescribeCommitAsync(RepositoryHandle repository, string path, string sha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the submodules whose recorded commit the index changes against HEAD — what a commit would
    /// record.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The staged pin changes, in git's order; empty at once without <c>.gitmodules</c>.</returns>
    Task<IReadOnlyList<StagedPin>> ReadStagedPinsAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Says what discarding the changes of some paths would do to the submodules among them.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="paths">The paths to be discarded; the ones that are not submodules HEAD records are left out.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>For each submodule among them, the commit it goes back to and what stops it or would be lost.</returns>
    Task<IReadOnlyList<SubmoduleDiscard>> PlanDiscardAsync(
        RepositoryHandle repository,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts submodules back on the commit the index records, once a discard has put the index back on
    /// HEAD's — even off a commit no branch contains, which the discard's question named.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="paths">The submodules.</param>
    /// <param name="progress">Receives git's progress, submodule by submodule.</param>
    /// <param name="cancellationToken">Cancels the moves; what is done stays done.</param>
    /// <returns>What was moved, what was left — never one with uncommitted changes — and what failed.</returns>
    Task<SubmoduleUpdateResult> PutBackAsync(
        RepositoryHandle repository,
        IReadOnlyCollection<string> paths,
        IProgress<SubmoduleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the repository a repository is a submodule of.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The superproject's work tree as a full path with the platform's separators, or
    /// <see langword="null"/> when the repository is not a submodule.
    /// </returns>
    /// <remarks><c>git rev-parse --show-superproject-working-tree</c>, which prints nothing for a repository that is not one.</remarks>
    Task<string?> ReadSuperprojectAsync(RepositoryHandle repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the submodules whose expected commit differs between two commits of the repository.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="oldSha">
    /// The first commit, or <see langword="null"/> or empty for none — every submodule of
    /// <paramref name="newSha"/> then counts.
    /// </param>
    /// <param name="newSha">The second commit.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The paths whose recorded commit was added, removed or changed, in git's order; the repository's
    /// own submodules only — a nested one moves with the submodule it is in.
    /// </returns>
    Task<IReadOnlyList<string>> ChangedPinsAsync(
        RepositoryHandle repository,
        string? oldSha,
        string newSha,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells whether a branch, a tag or a remote branch of a submodule contains a commit.
    /// </summary>
    /// <param name="submodule">The submodule, downloaded.</param>
    /// <param name="sha">The commit, as a full object name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// Whether one does; <see langword="false"/> means moving the submodule off the commit would leave
    /// it where nothing finds it again.
    /// </returns>
    Task<bool> IsOnAnyRefAsync(GitSubmodule submodule, string sha, CancellationToken cancellationToken = default);
}
