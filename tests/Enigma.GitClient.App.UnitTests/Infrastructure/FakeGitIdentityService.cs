using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.App.UnitTests.Infrastructure;

/// <summary>
/// An <see cref="IGitIdentityService"/> that keeps the identities in memory.
/// </summary>
/// <remarks>
/// The real service writes the global git configuration of whoever runs the tests. An App test that
/// saved an identity through it would change the developer's own name and email for every commit
/// they make afterwards, so <see cref="TestServices"/> always installs this instead.
/// </remarks>
public sealed class FakeGitIdentityService : IGitIdentityService
{
    private readonly Dictionary<string, GitIdentity> _local = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the global identity.</summary>
    public GitIdentity Global { get; set; } = GitIdentity.Empty;

    /// <summary>
    /// Gets or sets a failure every call throws, for a test driving the page's error path.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>Gets how many times the global identity has been written.</summary>
    public int GlobalWrites { get; private set; }

    /// <summary>Gets the repositories whose identity was written or removed, oldest first.</summary>
    public List<string> LocalWrites { get; } = [];

    /// <summary>
    /// Reads a repository's own identity as stored here.
    /// </summary>
    /// <param name="workTreePath">The repository's work tree.</param>
    /// <returns>The identity, or <see cref="GitIdentity.Empty"/>.</returns>
    public GitIdentity LocalOf(string workTreePath)
        => _local.TryGetValue(workTreePath, out GitIdentity? identity) ? identity : GitIdentity.Empty;

    /// <summary>
    /// Sets a repository's own identity as if a terminal had.
    /// </summary>
    /// <param name="workTreePath">The repository's work tree.</param>
    /// <param name="identity">The identity.</param>
    public void SetLocalDirectly(string workTreePath, GitIdentity identity) => _local[workTreePath] = identity;

    /// <inheritdoc />
    public Task<GitIdentity> GetGlobalAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        return Task.FromResult(Global);
    }

    /// <inheritdoc />
    public Task SetGlobalAsync(GitIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ThrowIfFailing();
        Validate(identity);

        Global = identity.Normalised();
        GlobalWrites++;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<GitIdentity> GetLocalAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ThrowIfFailing();

        return Task.FromResult(LocalOf(repository.WorkTreePath));
    }

    /// <inheritdoc />
    public Task SetLocalAsync(
        RepositoryHandle repository,
        GitIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(identity);
        ThrowIfFailing();
        Validate(identity);

        _local[repository.WorkTreePath] = identity.Normalised();
        LocalWrites.Add(repository.WorkTreePath);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveLocalAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ThrowIfFailing();

        _local.Remove(repository.WorkTreePath);
        LocalWrites.Add(repository.WorkTreePath);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the failure git reports when another process holds the configuration's lock.
    /// </summary>
    /// <returns>The exception.</returns>
    public static GitCommandException LockFailure()
        => new(
            new GitCommand(".", ["config", "--global", "user.name", "x"], environment: null, standardInput: null),
            4,
            "error: could not lock config file /home/user/.gitconfig: File exists",
            string.Empty);

    private static void Validate(GitIdentity identity)
    {
        if (GitIdentityRules.Validate(identity.Normalised()) is { } problem)
        {
            throw new ArgumentException(problem, nameof(identity));
        }
    }

    private void ThrowIfFailing()
    {
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
