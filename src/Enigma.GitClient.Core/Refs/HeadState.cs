namespace Enigma.GitClient.Core.Refs;

/// <summary>
/// A multi-step operation the repository is in the middle of. The UI surfaces this as a banner so
/// the user is never surprised by a repository that is half-way through something.
/// </summary>
public enum RepositoryOperation
{
    /// <summary>Nothing is in progress.</summary>
    None,

    /// <summary>A merge stopped, normally on conflicts.</summary>
    Merge,

    /// <summary>A cherry-pick stopped.</summary>
    CherryPick,

    /// <summary>A revert stopped.</summary>
    Revert,

    /// <summary>A bisect session is running.</summary>
    Bisect,

    /// <summary>
    /// A rebase left behind by another tool. Enigma.GitClient never starts one; it detects the
    /// state only so it can warn, and so it does not misread the repository.
    /// </summary>
    Rebase,

    /// <summary>An <c>am</c> (apply mailbox) session is running.</summary>
    ApplyMailbox,
}

/// <summary>
/// Where HEAD is and what the repository is in the middle of.
/// </summary>
/// <param name="IsUnborn">
/// Whether HEAD names a branch that does not exist yet, which is the state of a freshly initialised
/// repository.
/// </param>
/// <param name="IsDetached">Whether HEAD points straight at a commit rather than at a branch.</param>
/// <param name="BranchName">
/// The short name of the branch HEAD points at. Set even when unborn, because that is the branch the
/// first commit will create.
/// </param>
/// <param name="Sha">The commit HEAD resolves to, empty when unborn.</param>
/// <param name="Operation">The multi-step operation in progress, if any.</param>
public sealed record HeadState(
    bool IsUnborn,
    bool IsDetached,
    string? BranchName,
    string Sha,
    RepositoryOperation Operation)
{
    /// <summary>
    /// Gets a value indicating whether a multi-step operation is in progress.
    /// </summary>
    public bool HasOperationInProgress => Operation != RepositoryOperation.None;

    /// <summary>
    /// Gets the label the title bar shows for the current position.
    /// </summary>
    public string DisplayName
        => IsDetached
            ? Sha.Length >= 7 ? $"detached at {Sha.Substring(0, 7)}" : "detached"
            : BranchName ?? "HEAD";

    /// <summary>
    /// Creates the state of a repository that has no commits yet.
    /// </summary>
    /// <param name="branchName">The branch the first commit will create.</param>
    /// <returns>The state.</returns>
    public static HeadState Unborn(string? branchName)
        => new(IsUnborn: true, IsDetached: false, branchName, string.Empty, RepositoryOperation.None);
}
