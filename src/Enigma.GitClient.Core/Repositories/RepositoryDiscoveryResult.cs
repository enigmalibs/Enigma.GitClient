namespace Enigma.GitClient.Core.Repositories;

/// <summary>
/// The outcome of looking for a repository at a path.
/// </summary>
/// <param name="Status">What was found.</param>
/// <param name="Repository">The repository, when one was found and is usable.</param>
/// <param name="Message">A user-facing explanation, empty on success.</param>
public sealed record RepositoryDiscoveryResult(
    RepositoryDiscoveryStatus Status,
    RepositoryHandle? Repository,
    string Message)
{
    /// <summary>
    /// Gets a value indicating whether a usable repository was found.
    /// </summary>
    public bool IsFound => Status == RepositoryDiscoveryStatus.Found;

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="repository">The repository that was found.</param>
    /// <returns>The result.</returns>
    public static RepositoryDiscoveryResult Found(RepositoryHandle repository)
        => new(RepositoryDiscoveryStatus.Found, repository, string.Empty);

    /// <summary>
    /// Creates a "not a repository" result.
    /// </summary>
    /// <param name="path">The path that was probed.</param>
    /// <returns>The result.</returns>
    public static RepositoryDiscoveryResult NotARepository(string path)
        => new(
            RepositoryDiscoveryStatus.NotARepository,
            Repository: null,
            $"'{path}' is not inside a git repository.");

    /// <summary>
    /// Creates a "bare repository" result.
    /// </summary>
    /// <param name="path">The path that was probed.</param>
    /// <returns>The result.</returns>
    public static RepositoryDiscoveryResult Bare(string path)
        => new(
            RepositoryDiscoveryStatus.BareRepository,
            Repository: null,
            $"'{path}' is a bare repository. Enigma.GitClient works on repositories with a work tree.");
}

/// <summary>
/// What a repository discovery found.
/// </summary>
public enum RepositoryDiscoveryStatus
{
    /// <summary>A usable, non-bare repository was found.</summary>
    Found,

    /// <summary>The path is not inside a git repository.</summary>
    NotARepository,

    /// <summary>The path is inside a bare repository, which has no work tree to show.</summary>
    BareRepository,
}
