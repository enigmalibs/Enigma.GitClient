namespace Enigma.GitClient.Core.Submodules;

/// <summary>
/// Where a submodule's checkout stands against the commit its repository expects — the commit the
/// repository's index records for the submodule's folder.
/// </summary>
public enum SubmoduleCheckoutState
{
    /// <summary>The submodule is on the expected commit.</summary>
    UpToDate,

    /// <summary>
    /// The submodule is on an older commit than the expected one — an ancestor of it, or a commit before
    /// one it has not fetched yet. This is the stale folder a later commit would record again.
    /// </summary>
    Behind,

    /// <summary>
    /// The submodule is on a newer commit — a descendant of the expected one: work in progress, not
    /// recorded in the repository yet.
    /// </summary>
    NewCommits,

    /// <summary>The submodule is on a commit on another line than the expected one.</summary>
    Elsewhere,

    /// <summary>The submodule's folder is empty: git has not downloaded it.</summary>
    NotDownloaded,

    /// <summary>A merge left two commits for the submodule; the repository expects none yet.</summary>
    Conflicted,
}

/// <summary>
/// One submodule of a repository, or of one of its submodules.
/// </summary>
public sealed record GitSubmodule
{
    /// <summary>
    /// Gets the submodule's name in <c>.gitmodules</c> — usually its path when it was added.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the submodule's folder relative to the repository the list was read from, with <c>/</c>
    /// separators: <c>libs/Common</c>, or <c>libs/Common/vendor/Json</c> for a nested one.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the submodule's folder as a full path, with the platform's separators.
    /// </summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// Gets the address <c>.gitmodules</c> gives for the submodule, as written there (it may be relative
    /// to the repository's own remote), with any credential in it redacted; empty when none is given.
    /// </summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// Gets the commit the repository expects the submodule on — the one its index records; empty for a
    /// <see cref="SubmoduleCheckoutState.Conflicted"/> submodule.
    /// </summary>
    public required string ExpectedSha { get; init; }

    /// <summary>
    /// Gets the commit the submodule has checked out, or <see langword="null"/> when it is not
    /// downloaded.
    /// </summary>
    public string? CheckedOutSha { get; init; }

    /// <summary>
    /// Gets where the checkout stands against the expected commit.
    /// </summary>
    public SubmoduleCheckoutState State { get; init; }

    /// <summary>
    /// Gets a value indicating whether files git tracks in the submodule are modified, staged or not.
    /// Its own submodules do not count: each is listed with its own state.
    /// </summary>
    public bool HasUncommittedChanges { get; init; }

    /// <summary>
    /// Gets a value indicating whether the submodule holds files git does not track and does not
    /// ignore. A checkout never deletes them, so they never stop an update.
    /// </summary>
    public bool HasUntrackedFiles { get; init; }

    /// <summary>
    /// Gets the <see cref="Path"/> of the submodule this one is nested in, or <see langword="null"/> for
    /// a submodule of the repository itself.
    /// </summary>
    public string? ParentPath { get; init; }

    /// <summary>
    /// Gets a value indicating whether the submodule's folder holds a checkout.
    /// </summary>
    public bool IsDownloaded => CheckedOutSha is not null;
}
