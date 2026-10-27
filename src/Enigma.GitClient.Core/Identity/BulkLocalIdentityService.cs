using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Core.Identity;

/// <summary>
/// What became of one repository when an identity was given to several.
/// </summary>
public enum BulkLocalIdentityResult
{
    /// <summary>The identity was written into the repository's own configuration.</summary>
    Written,

    /// <summary>The repository already set that identity itself; nothing was written.</summary>
    AlreadySet,

    /// <summary>
    /// The path is not the work tree of a repository — gone, bare, or a folder inside another
    /// repository; nothing was written.
    /// </summary>
    NotARepository,

    /// <summary>git or the file system refused for this repository; see the outcome's failure.</summary>
    Failed,
}

/// <summary>
/// What became of one repository when an identity was given to several.
/// </summary>
/// <param name="Path">The path, as it was given.</param>
/// <param name="Result">What happened to it.</param>
/// <param name="Failure">Why it failed, for <see cref="BulkLocalIdentityResult.Failed"/>; otherwise <see langword="null"/>.</param>
public sealed record BulkLocalIdentityOutcome(string Path, BulkLocalIdentityResult Result, Exception? Failure = null);

/// <summary>
/// Gives several repositories the same identity of their own, the way
/// <see cref="IGitIdentityService.SetLocalAsync"/> gives it to one.
/// </summary>
public interface IBulkLocalIdentityService
{
    /// <summary>
    /// Writes an identity into the own configuration of every repository whose work tree is listed.
    /// </summary>
    /// <param name="workTreePaths">The repositories' work trees, in the order they are written.</param>
    /// <param name="identity">The identity; both values are required and are trimmed.</param>
    /// <param name="cancellationToken">Cancels before the next repository.</param>
    /// <returns>One outcome per path, in the order given.</returns>
    /// <exception cref="ArgumentException">The identity is incomplete or invalid; nothing was written.</exception>
    /// <exception cref="GitNotFoundException">git cannot be run; every repository would fail alike.</exception>
    /// <remarks>
    /// A repository that refuses is reported and the next one is tried. A repository that already sets
    /// the identity is not written again — two worktrees sharing one configuration are written once.
    /// </remarks>
    Task<IReadOnlyList<BulkLocalIdentityOutcome>> SetLocalAsync(
        IReadOnlyList<string> workTreePaths,
        GitIdentity identity,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IBulkLocalIdentityService"/>: discovers each repository, reads its own identity,
/// and writes the new one where it differs.
/// </summary>
public sealed class BulkLocalIdentityService : IBulkLocalIdentityService
{
    private readonly IRepositoryLocator _locator;
    private readonly IGitIdentityService _identity;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="locator">Finds the repository at each path.</param>
    /// <param name="identity">Reads and writes each repository's identity.</param>
    public BulkLocalIdentityService(IRepositoryLocator locator, IGitIdentityService identity)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(identity);

        _locator = locator;
        _identity = identity;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BulkLocalIdentityOutcome>> SetLocalAsync(
        IReadOnlyList<string> workTreePaths,
        GitIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workTreePaths);
        ArgumentNullException.ThrowIfNull(identity);

        GitIdentity trimmed = identity.Normalised();

        // Checked once, before any repository: otherwise every one of them would fail alike.
        if (GitIdentityRules.Validate(trimmed) is { } problem)
        {
            throw new ArgumentException(problem, nameof(identity));
        }

        List<BulkLocalIdentityOutcome> outcomes = new(workTreePaths.Count);

        foreach (string path in workTreePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            outcomes.Add(await SetOneAsync(path, trimmed, cancellationToken).ConfigureAwait(false));
        }

        return outcomes;
    }

    private async Task<BulkLocalIdentityOutcome> SetOneAsync(
        string path,
        GitIdentity identity,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new BulkLocalIdentityOutcome(path, BulkLocalIdentityResult.NotARepository);
        }

        try
        {
            RepositoryDiscoveryResult discovery = await _locator.DiscoverAsync(path, cancellationToken)
                .ConfigureAwait(false);

            // A listed folder whose .git has gone still discovers the repository around it, which is
            // not the one the list means.
            if (discovery.Repository is not { } repository || !IsWorkTreeOf(repository, path))
            {
                return new BulkLocalIdentityOutcome(path, BulkLocalIdentityResult.NotARepository);
            }

            GitIdentity own = await _identity.GetLocalAsync(repository, cancellationToken).ConfigureAwait(false);

            if (own.IsSameAs(identity))
            {
                return new BulkLocalIdentityOutcome(path, BulkLocalIdentityResult.AlreadySet);
            }

            await _identity.SetLocalAsync(repository, identity, cancellationToken).ConfigureAwait(false);

            return new BulkLocalIdentityOutcome(path, BulkLocalIdentityResult.Written);
        }
        catch (Exception exception) when (exception is GitCommandException or IOException or UnauthorizedAccessException)
        {
            return new BulkLocalIdentityOutcome(path, BulkLocalIdentityResult.Failed, exception);
        }
    }

    private static bool IsWorkTreeOf(RepositoryHandle repository, string path)
        => string.Equals(
            repository.WorkTreePath,
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
            PathComparison);

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
