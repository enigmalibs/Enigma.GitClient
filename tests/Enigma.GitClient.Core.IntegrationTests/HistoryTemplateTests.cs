using System.Threading.Tasks;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests;

/// <summary>
/// The shared history is copied, never shared: every test that reads it relies on getting a clean
/// repository of its own.
/// </summary>
public sealed class HistoryTemplateTests : IAsyncLifetime
{
    private readonly HistoryTemplate _template;
    private GitWorkspace _workspace = null!;

    public HistoryTemplateTests(HistoryTemplate template)
    {
        _template = template;
    }

    public ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _workspace.DisposeAsync();

    [Fact]
    public async Task ACopy_IsACleanRepositoryOnMainAtTheLastCommit()
    {
        HistoryFixture copy = _template.CopyInto(_workspace);

        Assert.Equal(copy.ShaE, await copy.Repository.ResolveAsync("HEAD"));
        Assert.Equal("main", await copy.Repository.GitLineAsync("symbolic-ref", "--short", "HEAD"));
        Assert.Equal(string.Empty, await copy.Repository.GitAsync("status", "--porcelain"));
    }

    [Fact]
    public async Task ACopyATestChanges_LeavesTheNextCopyAsItWas()
    {
        HistoryFixture changed = _template.CopyInto(_workspace, "changed");
        await changed.Repository.CommitFileAsync("extra.txt", "extra\n", "A test's own commit");

        HistoryFixture next = _template.CopyInto(_workspace, "next");

        Assert.Equal(next.ShaE, await next.Repository.ResolveAsync("HEAD"));
        Assert.Equal(string.Empty, await next.Repository.GitAsync("status", "--porcelain"));
    }
}
