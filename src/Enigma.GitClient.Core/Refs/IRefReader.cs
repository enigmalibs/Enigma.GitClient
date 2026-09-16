using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Refs;

/// <summary>
/// Reads a repository's references and the position of HEAD.
/// </summary>
public interface IRefReader
{
    /// <summary>
    /// Reads every reference in one <c>for-each-ref</c> call.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The references, split by kind.</returns>
    Task<RefCollection> GetRefsAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads where HEAD is and what multi-step operation, if any, is in progress.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The HEAD state.</returns>
    Task<HeadState> GetHeadStateAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the references and HEAD together and builds the graph's decoration index.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The references, the HEAD state and the index over them.</returns>
    Task<RepositoryRefState> GetStateAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A repository's reference state at one moment.
/// </summary>
/// <param name="Refs">Every reference, split by kind.</param>
/// <param name="Head">Where HEAD is.</param>
/// <param name="Decorations">The commit-to-references index the graph renders from.</param>
public sealed record RepositoryRefState(
    RefCollection Refs,
    HeadState Head,
    RefDecorationIndex Decorations);

/// <summary>
/// Reads a repository's configured remotes.
/// </summary>
public interface IRemoteReader
{
    /// <summary>
    /// Reads every configured remote.
    /// </summary>
    /// <param name="repository">The repository to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The remotes, ordered by name.</returns>
    Task<IReadOnlyList<GitRemote>> GetRemotesAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default);
}
