using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Identity;

/// <summary>
/// Gives several real repositories one identity, with a global configuration that is the workspace's
/// own throwaway file — never the developer's.
/// </summary>
public sealed class BulkLocalIdentityServiceTests : IAsyncLifetime
{
    private static readonly GitIdentity Work = new("Ada Lovelace", "ada@work.example");

    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;

    public ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private IBulkLocalIdentityService Bulk => _host.GetRequiredService<IBulkLocalIdentityService>();

    private string GlobalConfigFile => _workspace.Environment["GIT_CONFIG_GLOBAL"];

    [Fact]
    public async Task EveryListedRepository_EndsWithTheIdentityAsItsOwn()
    {
        TemporaryRepository none = await _workspace.InitRepositoryAsync("none");
        TemporaryRepository other = await _workspace.InitRepositoryAsync("other");
        TemporaryRepository same = await _workspace.InitRepositoryAsync("same");
        await other.GitAsync("config", "--local", "user.name", "Someone Else");
        await other.GitAsync("config", "--local", "user.email", "someone@else.example");
        await same.GitAsync("config", "--local", "user.name", "Ada Lovelace");
        await same.GitAsync("config", "--local", "user.email", "ADA@work.example");
        string sameConfig = await File.ReadAllTextAsync(Path.Combine(same.Path, ".git", "config"), TestContext.Current.CancellationToken);

        string missing = Path.Combine(_workspace.RootPath, "gone");
        string plain = _workspace.CreateDirectory("plain");
        string global = await File.ReadAllTextAsync(GlobalConfigFile, TestContext.Current.CancellationToken);

        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Bulk.SetLocalAsync(
            [none.Path, other.Path, same.Path, missing, plain],
            Work,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                BulkLocalIdentityResult.Written,
                BulkLocalIdentityResult.Written,
                BulkLocalIdentityResult.AlreadySet,
                BulkLocalIdentityResult.NotARepository,
                BulkLocalIdentityResult.NotARepository,
            ],
            outcomes.Select(outcome => outcome.Result));

        Assert.Equal("Ada Lovelace", await none.GitLineAsync("config", "--local", "user.name"));
        Assert.Equal("ada@work.example", await none.GitLineAsync("config", "--local", "user.email"));
        Assert.Equal("Ada Lovelace", await other.GitLineAsync("config", "--local", "user.name"));
        Assert.Equal("ada@work.example", await other.GitLineAsync("config", "--local", "user.email"));

        // The repository that already had it is not rewritten, its spelling of the email kept.
        Assert.Equal(sameConfig, await File.ReadAllTextAsync(Path.Combine(same.Path, ".git", "config"), TestContext.Current.CancellationToken));

        // Nothing but the repositories' own files: the global one is as it was, and nothing appeared
        // where the list pointed at no repository.
        Assert.Equal(global, await File.ReadAllTextAsync(GlobalConfigFile, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(missing));
        Assert.Empty(Directory.GetFileSystemEntries(plain));
    }

    [Fact]
    public async Task AFolderInsideARepository_IsSkippedAndTheRepositoryAroundItLeftAlone()
    {
        TemporaryRepository outer = await _workspace.InitRepositoryAsync("outer");
        string inner = outer.CreateDirectory("inner");
        string before = await File.ReadAllTextAsync(Path.Combine(outer.Path, ".git", "config"), TestContext.Current.CancellationToken);

        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Bulk.SetLocalAsync([inner], Work, TestContext.Current.CancellationToken);

        Assert.Equal(BulkLocalIdentityResult.NotARepository, Assert.Single(outcomes).Result);
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(outer.Path, ".git", "config"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ABareRepository_IsSkipped()
    {
        TemporaryRepository bare = await _workspace.InitRepositoryAsync("bare.git", bare: true);
        string before = await File.ReadAllTextAsync(Path.Combine(bare.Path, "config"), TestContext.Current.CancellationToken);

        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Bulk.SetLocalAsync([bare.Path], Work, TestContext.Current.CancellationToken);

        Assert.Equal(BulkLocalIdentityResult.NotARepository, Assert.Single(outcomes).Result);
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(bare.Path, "config"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AWorkTreePathWrittenWithATrailingSeparator_IsStillItsRepository()
    {
        TemporaryRepository repository = await _workspace.InitRepositoryAsync("trailing");

        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Bulk.SetLocalAsync(
            [repository.Path + Path.DirectorySeparatorChar],
            Work,
            TestContext.Current.CancellationToken);

        Assert.Equal(BulkLocalIdentityResult.Written, Assert.Single(outcomes).Result);
        Assert.Equal("ada@work.example", await repository.GitLineAsync("config", "--local", "user.email"));
    }
}
