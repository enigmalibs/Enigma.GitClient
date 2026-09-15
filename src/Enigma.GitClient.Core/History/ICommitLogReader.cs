using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.History;

/// <summary>
/// Reads commits out of a repository.
/// </summary>
public interface ICommitLogReader
{
    /// <summary>
    /// Reads one page of history.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="query">Which commits to read, and how many.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The page. A repository with no commits yet returns <see cref="CommitLogPage.Empty"/> rather
    /// than throwing.
    /// </returns>
    Task<CommitLogPage> GetPageAsync(
        RepositoryHandle repository,
        CommitLogQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single commit.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="revision">Any revision expression that resolves to a commit.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The commit, or <see langword="null"/> when the revision does not resolve.</returns>
    Task<GitCommit?> GetCommitAsync(
        RepositoryHandle repository,
        string revision,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the commits a query would return, ignoring its paging.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="query">The query whose selectors and filters are counted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The number of matching commits.</returns>
    Task<int> CountAsync(
        RepositoryHandle repository,
        CommitLogQuery query,
        CancellationToken cancellationToken = default);
}
