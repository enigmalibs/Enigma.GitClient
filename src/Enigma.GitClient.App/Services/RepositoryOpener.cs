using System;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// Opens a repository by path: git's own discovery, then the repository context, then the recent
/// list.
/// </summary>
/// <remarks>
/// Two places open a repository from a path — the repositories page and the command line — and they
/// must do the same three things in the same order. What they do with a failure differs, which is
/// why this reports one rather than showing it.
/// </remarks>
public interface IRepositoryOpener
{
    /// <summary>
    /// Discovers the repository containing a path and makes it the one the application is looking at.
    /// </summary>
    /// <param name="path">A path inside the repository.</param>
    /// <param name="cancellationToken">Cancels the discovery.</param>
    /// <returns>What discovery found; when nothing was found, nothing was changed.</returns>
    Task<RepositoryDiscoveryResult> OpenAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IRepositoryOpener"/>.
/// </summary>
public sealed class RepositoryOpener : IRepositoryOpener
{
    private readonly IRepositoryService _repositories;
    private readonly IRepositoryContext _context;
    private readonly IRecentRepositoryStore _recent;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositories">Finds the repository a path belongs to.</param>
    /// <param name="context">Receives the repository once it is found.</param>
    /// <param name="recent">Remembers that it was opened.</param>
    public RepositoryOpener(IRepositoryService repositories, IRepositoryContext context, IRecentRepositoryStore recent)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(recent);

        _repositories = repositories;
        _context = context;
        _recent = recent;
    }

    /// <inheritdoc />
    public async Task<RepositoryDiscoveryResult> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        RepositoryDiscoveryResult discovery = await _repositories.OpenAsync(path, cancellationToken).ConfigureAwait(true);

        if (discovery.IsFound)
        {
            RepositoryHandle repository = discovery.Repository!;

            await _context.OpenAsync(repository, cancellationToken).ConfigureAwait(true);
            await _recent.TouchAsync(repository.WorkTreePath, repository.Name, cancellationToken).ConfigureAwait(true);
        }

        return discovery;
    }
}
