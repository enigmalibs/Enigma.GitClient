using System.Threading;
using System.Threading.Tasks;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Reports what the host's git installation can do.
/// </summary>
public interface IGitEnvironment
{
    /// <summary>
    /// Reads the version of the git executable, caching the result.
    /// </summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The installed git version.</returns>
    /// <exception cref="GitNotFoundException">No usable git executable could be run.</exception>
    Task<GitVersion> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the installed git meets <see cref="GitVersion.Minimum"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The probe result, including the version when one could be read.</returns>
    Task<GitAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);
}
