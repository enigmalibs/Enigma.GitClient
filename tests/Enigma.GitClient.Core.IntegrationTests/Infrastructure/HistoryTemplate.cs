using System.Threading.Tasks;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Xunit;

[assembly: AssemblyFixture<HistoryTemplate>]

namespace Enigma.GitClient.Core.IntegrationTests.Infrastructure;

/// <summary>
/// The <see cref="HistoryFixture"/> history, built once for the whole run and copied into each test's
/// workspace.
/// </summary>
/// <remarks>
/// Building the history takes some thirty git starts; copying it takes none. The history is the same
/// on every build — explicit dates and a fixed identity give the same SHAs — so a copy is as good as a
/// rebuild, and each test still gets a repository of its own to change.
/// </remarks>
public sealed class HistoryTemplate : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private HistoryFixture _history = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _history = await HistoryFixture.CreateAsync(_workspace);
    }

    /// <summary>
    /// Copies the history into a test's workspace.
    /// </summary>
    /// <param name="workspace">The test's workspace.</param>
    /// <param name="name">The repository directory name.</param>
    /// <returns>The test's own copy of the fixture.</returns>
    public HistoryFixture CopyInto(GitWorkspace workspace, string name = "history")
        => _history.CopyInto(workspace, name);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_workspace is not null)
        {
            await _workspace.DisposeAsync();
        }
    }
}
