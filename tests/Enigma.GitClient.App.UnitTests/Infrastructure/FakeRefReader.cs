using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.App.UnitTests.Infrastructure;

/// <summary>
/// An <see cref="IRefReader"/> that answers immediately from memory.
/// </summary>
/// <remarks>
/// Two reasons, both deliberate. It keeps shell tests away from a real git process, and — because
/// every method returns an already-completed task — it lets a test that must block the UI thread do
/// so without deadlocking: an <c>await</c> on a completed task continues synchronously rather than
/// posting back to a dispatcher that is not pumping.
/// </remarks>
public sealed class FakeRefReader : IRefReader
{
    /// <summary>
    /// Gets or sets the references every read returns.
    /// </summary>
    public RefCollection Refs { get; set; } = RefCollection.Empty;

    /// <summary>
    /// Gets or sets the HEAD state every read returns.
    /// </summary>
    public HeadState Head { get; set; } = HeadState.Unborn("main");

    /// <summary>
    /// Gets how many times the reference state has been read.
    /// </summary>
    public int ReadCount { get; private set; }

    /// <inheritdoc />
    public Task<RefCollection> GetRefsAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Refs);

    /// <inheritdoc />
    public Task<HeadState> GetHeadStateAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Head);

    /// <inheritdoc />
    public Task<RepositoryRefState> GetStateAsync(
        RepositoryHandle repository,
        CancellationToken cancellationToken = default)
    {
        ReadCount++;
        return Task.FromResult(new RepositoryRefState(Refs, Head, RefDecorationIndex.Build(Refs, Head)));
    }
}
