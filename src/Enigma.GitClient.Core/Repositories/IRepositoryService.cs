using System;
using System.Threading;
using System.Threading.Tasks;

namespace Enigma.GitClient.Core.Repositories;

/// <summary>
/// Creates and opens repositories.
/// </summary>
public interface IRepositoryService
{
    /// <summary>
    /// Opens the repository containing a path.
    /// </summary>
    /// <param name="path">A directory or file inside the repository.</param>
    /// <param name="cancellationToken">Cancels the discovery.</param>
    /// <returns>The discovery result.</returns>
    Task<RepositoryDiscoveryResult> OpenAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a repository in a directory, creating the directory if it does not exist.
    /// </summary>
    /// <param name="path">Where to create the repository.</param>
    /// <param name="initialBranch">The name of the branch the first commit will create.</param>
    /// <param name="cancellationToken">Cancels the initialisation.</param>
    /// <returns>The new repository.</returns>
    /// <exception cref="Git.GitCommandException">git refused to initialise the repository.</exception>
    Task<RepositoryHandle> InitAsync(
        string path,
        string initialBranch = "main",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clones a repository, reporting progress as git makes it.
    /// </summary>
    /// <param name="request">What to clone and where.</param>
    /// <param name="progress">Receives each progress report; may be <see langword="null"/>.</param>
    /// <param name="cancellationToken">
    /// Cancels the clone. The partially written directory is removed, so a cancelled clone leaves
    /// nothing behind.
    /// </param>
    /// <returns>The cloned repository.</returns>
    /// <exception cref="ArgumentException">The request's URL is not one git can clone from.</exception>
    /// <exception cref="Git.GitCommandException">git refused to clone.</exception>
    Task<RepositoryHandle> CloneAsync(
        CloneRequest request,
        IProgress<CloneProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a clone URL without starting anything.
    /// </summary>
    /// <param name="url">What the user typed.</param>
    /// <returns>The validation result.</returns>
    RemoteUrlValidation ValidateCloneUrl(string? url);
}
