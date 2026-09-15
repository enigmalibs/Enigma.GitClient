using System.Threading;
using System.Threading.Tasks;

namespace Enigma.GitClient.Core.Repositories;

/// <summary>
/// Finds the repository containing a path.
/// </summary>
public interface IRepositoryLocator
{
    /// <summary>
    /// Walks up from a path to the root of the repository containing it.
    /// </summary>
    /// <param name="path">A directory inside the repository, or the repository root itself.</param>
    /// <param name="cancellationToken">Cancels the discovery.</param>
    /// <returns>The discovery result.</returns>
    Task<RepositoryDiscoveryResult> DiscoverAsync(string path, CancellationToken cancellationToken = default);
}
