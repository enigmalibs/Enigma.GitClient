using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Worktrees;

namespace Enigma.GitClient.Desktop.UnitTests.Infrastructure;

/// <summary>
/// An <see cref="IWorktreeService"/> whose list answers immediately from memory, as
/// <see cref="FakeRefReader"/> does the references — and whose writes, when it wraps a real service, go
/// to git.
/// </summary>
/// <remarks>
/// The repository context lists the worktrees on every refresh. With the fake reader, a list from a real
/// git process would be the one read that does not complete synchronously, and a test that blocks the UI
/// thread would wait on it forever.
/// </remarks>
public sealed class FakeWorktreeService : IWorktreeService
{
    private readonly IWorktreeService? _inner;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="inner">
    /// The real service creating, counting and removing go to; <see langword="null"/> refuses them.
    /// </param>
    public FakeWorktreeService(IWorktreeService? inner = null) => _inner = inner;

    /// <summary>
    /// Gets or sets the working trees every list returns.
    /// </summary>
    public IReadOnlyList<GitWorktree> Worktrees { get; set; } = [];

    /// <summary>
    /// Gets or sets what every list fails with instead, or <see langword="null"/> for a list that works.
    /// </summary>
    public Exception? ListFailure { get; set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<GitWorktree>> ListAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
        => ListFailure is { } failure
            ? Task.FromException<IReadOnlyList<GitWorktree>>(failure)
            : Task.FromResult(Worktrees);

    /// <inheritdoc />
    public Task<int> CountChangesAsync(GitWorktree worktree, CancellationToken cancellationToken = default)
        => Inner.CountChangesAsync(worktree, cancellationToken);

    /// <inheritdoc />
    public Task RemoveAsync(
        RepositoryHandle repository,
        string worktreePath,
        bool force = false,
        CancellationToken cancellationToken = default)
        => Inner.RemoveAsync(repository, worktreePath, force, cancellationToken);

    /// <inheritdoc />
    public Task AddAsync(
        RepositoryHandle repository,
        string path,
        string branch,
        string startPoint,
        CancellationToken cancellationToken = default)
        => Inner.AddAsync(repository, path, branch, startPoint, cancellationToken);

    private IWorktreeService Inner
        => _inner ?? throw new InvalidOperationException("This fake has no real worktree service to write with.");
}
