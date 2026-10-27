using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Identity;

/// <summary>
/// Covers what the bulk service decides for each repository — write, leave, skip, report — with git
/// replaced by fakes; the integration suite runs it against real repositories.
/// </summary>
public sealed class BulkLocalIdentityServiceTests
{
    private static readonly GitIdentity Work = new("Ada Lovelace", "ada@work.example");

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "bulk-identity");

    private readonly FakeLocator _locator = new();
    private readonly FakeIdentity _identity = new();

    private BulkLocalIdentityService Service => new(_locator, _identity);

    private static string At(string name) => Path.Combine(Root, name);

    [Fact]
    public async Task ARepositoryWithoutTheIdentity_IsWritten()
    {
        _locator.Repository(At("one"));

        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Service.SetLocalAsync([At("one")], Work, TestContext.Current.CancellationToken);

        Assert.Equal(new BulkLocalIdentityOutcome(At("one"), BulkLocalIdentityResult.Written), Assert.Single(outcomes));
        Assert.Equal(Work, _identity.Local[At("one")]);
    }

    [Fact]
    public async Task ARepositoryWithAnotherIdentity_GetsThisOne()
    {
        _locator.Repository(At("one"));
        _identity.Local[At("one")] = new GitIdentity("Someone Else", "someone@else.example");

        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Service.SetLocalAsync([At("one")], Work, TestContext.Current.CancellationToken);

        Assert.Equal(BulkLocalIdentityResult.Written, Assert.Single(outcomes).Result);
        Assert.Equal(Work, _identity.Local[At("one")]);
    }

    [Fact]
    public async Task ARepositoryThatAlreadySetsIt_IsNotWrittenAgain()
    {
        _locator.Repository(At("one"));

        // Emails compare regardless of case, and both values trimmed, as git writes them.
        _identity.Local[At("one")] = new GitIdentity(" Ada Lovelace ", "ADA@work.example");

        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Service.SetLocalAsync([At("one")], Work, TestContext.Current.CancellationToken);

        Assert.Equal(BulkLocalIdentityResult.AlreadySet, Assert.Single(outcomes).Result);
        Assert.Empty(_identity.Writes);
    }

    [Fact]
    public async Task TwoWorktreesSharingOneConfiguration_AreWrittenOnce()
    {
        _locator.Repository(At("main"));
        _locator.Repository(At("main.worktree.topic"));
        _identity.SharedConfiguration(At("main"), At("main.worktree.topic"));

        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Service.SetLocalAsync(
            [At("main"), At("main.worktree.topic")],
            Work,
            TestContext.Current.CancellationToken);

        Assert.Equal([BulkLocalIdentityResult.Written, BulkLocalIdentityResult.AlreadySet], outcomes.Select(outcome => outcome.Result));
        Assert.Single(_identity.Writes);
    }

    [Fact]
    public async Task APathThatIsNoRepository_IsSkipped()
    {
        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Service.SetLocalAsync([At("gone")], Work, TestContext.Current.CancellationToken);

        Assert.Equal(new BulkLocalIdentityOutcome(At("gone"), BulkLocalIdentityResult.NotARepository), Assert.Single(outcomes));
        Assert.Empty(_identity.Writes);
    }

    [Fact]
    public async Task ABareRepository_IsSkipped()
    {
        _locator.Bare(At("bare.git"));

        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Service.SetLocalAsync([At("bare.git")], Work, TestContext.Current.CancellationToken);

        Assert.Equal(BulkLocalIdentityResult.NotARepository, Assert.Single(outcomes).Result);
        Assert.Empty(_identity.Writes);
    }

    [Fact]
    public async Task AFolderInsideAnotherRepository_IsSkippedAndThatRepositoryLeftAlone()
    {
        // A listed repository whose .git was deleted: git now finds the repository around it.
        _locator.Repository(At("outer"));
        _locator.Inside(At(Path.Combine("outer", "inner")), At("outer"));

        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Service.SetLocalAsync(
            [At(Path.Combine("outer", "inner"))],
            Work,
            TestContext.Current.CancellationToken);

        Assert.Equal(BulkLocalIdentityResult.NotARepository, Assert.Single(outcomes).Result);
        Assert.Empty(_identity.Writes);
    }

    [Fact]
    public async Task APathWithATrailingSeparator_IsStillItsRepository()
    {
        _locator.Repository(At("one"));

        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Service.SetLocalAsync(
            [At("one") + Path.DirectorySeparatorChar],
            Work,
            TestContext.Current.CancellationToken);

        Assert.Equal(BulkLocalIdentityResult.Written, Assert.Single(outcomes).Result);
    }

    [Fact]
    public async Task ABlankPath_IsSkipped()
    {
        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Service.SetLocalAsync(["  "], Work, TestContext.Current.CancellationToken);

        Assert.Equal(BulkLocalIdentityResult.NotARepository, Assert.Single(outcomes).Result);
        Assert.Empty(_locator.Probed);
    }

    [Fact]
    public async Task AFailureInTheMiddle_IsReportedAndTheOthersAreStillWritten()
    {
        _locator.Repository(At("one"));
        _locator.Repository(At("locked"));
        _locator.Repository(At("three"));
        GitCommandException failure = LockFailure();
        _identity.FailWritesOf[At("locked")] = failure;

        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Service.SetLocalAsync(
            [At("one"), At("locked"), At("three")],
            Work,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new BulkLocalIdentityOutcome(At("one"), BulkLocalIdentityResult.Written),
                new BulkLocalIdentityOutcome(At("locked"), BulkLocalIdentityResult.Failed, failure),
                new BulkLocalIdentityOutcome(At("three"), BulkLocalIdentityResult.Written),
            ],
            outcomes);
        Assert.Equal(Work, _identity.Local[At("three")]);
    }

    [Fact]
    public async Task AFileSystemFailure_IsReportedLikeGits()
    {
        _locator.Repository(At("one"));
        _identity.FailWritesOf[At("one")] = new UnauthorizedAccessException("Access to the path is denied.");

        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Service.SetLocalAsync([At("one")], Work, TestContext.Current.CancellationToken);

        BulkLocalIdentityOutcome outcome = Assert.Single(outcomes);
        Assert.Equal(BulkLocalIdentityResult.Failed, outcome.Result);
        Assert.IsType<UnauthorizedAccessException>(outcome.Failure);
    }

    [Fact]
    public async Task GitNotFound_StopsEverything()
    {
        _locator.Repository(At("one"));
        _locator.Repository(At("two"));
        _identity.FailWritesOf[At("one")] = new GitNotFoundException("git was not found.");

        await Assert.ThrowsAsync<GitNotFoundException>(
            () => Service.SetLocalAsync([At("one"), At("two")], Work, TestContext.Current.CancellationToken));

        Assert.False(_identity.Local.ContainsKey(At("two")));
    }

    [Theory]
    [InlineData("", "ada@work.example")]
    [InlineData("Ada Lovelace", "")]
    [InlineData("Ada <Lovelace>", "ada@work.example")]
    [InlineData("Ada Lovelace", "not-an-email")]
    public async Task AnInvalidIdentity_IsRefusedBeforeAnyRepositoryIsRead(string name, string email)
    {
        _locator.Repository(At("one"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => Service.SetLocalAsync([At("one")], new GitIdentity(name, email), TestContext.Current.CancellationToken));

        Assert.Empty(_locator.Probed);
        Assert.Empty(_identity.Writes);
    }

    [Fact]
    public async Task TheIdentityIsWrittenTrimmed()
    {
        _locator.Repository(At("one"));

        await Service.SetLocalAsync([At("one")], new GitIdentity("  Ada Lovelace ", " ada@work.example "), TestContext.Current.CancellationToken);

        Assert.Equal(Work, _identity.Local[At("one")]);
    }

    [Fact]
    public async Task OutcomesKeepTheOrderTheyWereAskedIn()
    {
        _locator.Repository(At("b"));
        _locator.Repository(At("a"));

        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Service.SetLocalAsync(
            [At("b"), At("missing"), At("a")],
            Work,
            TestContext.Current.CancellationToken);

        Assert.Equal([At("b"), At("missing"), At("a")], outcomes.Select(outcome => outcome.Path));
        Assert.Equal([At("b"), At("a")], _identity.Writes);
    }

    [Fact]
    public async Task ACancelledRun_StopsBeforeTheNextRepository()
    {
        _locator.Repository(At("one"));
        _locator.Repository(At("two"));
        using CancellationTokenSource cancellation = new();
        _identity.AfterWrite = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service.SetLocalAsync([At("one"), At("two")], Work, cancellation.Token));

        Assert.Equal([At("one")], _identity.Writes);
    }

    [Fact]
    public async Task NoPaths_WriteNothing()
    {
        IReadOnlyList<BulkLocalIdentityOutcome> outcomes = await Service.SetLocalAsync([], Work, TestContext.Current.CancellationToken);

        Assert.Empty(outcomes);
    }

    private static GitCommandException LockFailure()
        => new(
            new GitCommand(".", ["config", "--local", "user.name", "x"], environment: null, standardInput: null),
            4,
            "error: could not lock config file .git/config: File exists",
            string.Empty);

    /// <summary>
    /// Answers discoveries from a table of paths: a repository, a bare one, or a folder inside one.
    /// </summary>
    private sealed class FakeLocator : IRepositoryLocator
    {
        private readonly Dictionary<string, RepositoryDiscoveryResult> _results = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Probed { get; } = [];

        public void Repository(string workTree)
            => _results[workTree] = RepositoryDiscoveryResult.Found(Handle(workTree));

        public void Bare(string path) => _results[path] = RepositoryDiscoveryResult.Bare(path);

        public void Inside(string folder, string workTree)
            => _results[folder] = RepositoryDiscoveryResult.Found(Handle(workTree));

        public Task<RepositoryDiscoveryResult> DiscoverAsync(string path, CancellationToken cancellationToken = default)
        {
            Probed.Add(path);

            string key = Path.TrimEndingDirectorySeparator(path);

            return Task.FromResult(
                _results.TryGetValue(key, out RepositoryDiscoveryResult? result)
                    ? result
                    : RepositoryDiscoveryResult.NotARepository(path));
        }

        private static RepositoryHandle Handle(string workTree) => new(workTree, Path.Combine(workTree, ".git"));
    }

    /// <summary>
    /// Keeps each repository's own identity in memory, by work tree — or by the configuration two
    /// worktrees share.
    /// </summary>
    private sealed class FakeIdentity : IGitIdentityService
    {
        private readonly Dictionary<string, string> _configurationOf = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, GitIdentity> Local { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Exception> FailWritesOf { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Writes { get; } = [];

        public Action? AfterWrite { get; set; }

        public void SharedConfiguration(string main, string worktree) => _configurationOf[worktree] = main;

        public Task<GitIdentity> GetLocalAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
            => Task.FromResult(Local.TryGetValue(ConfigurationOf(repository), out GitIdentity? identity) ? identity : GitIdentity.Empty);

        public Task SetLocalAsync(RepositoryHandle repository, GitIdentity identity, CancellationToken cancellationToken = default)
        {
            if (FailWritesOf.TryGetValue(repository.WorkTreePath, out Exception? failure))
            {
                throw failure;
            }

            Local[ConfigurationOf(repository)] = identity;
            Writes.Add(repository.WorkTreePath);
            AfterWrite?.Invoke();

            return Task.CompletedTask;
        }

        public Task<GitIdentity> GetGlobalAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The bulk service never reads the global identity.");

        public Task SetGlobalAsync(GitIdentity identity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The bulk service never writes the global identity.");

        public Task<GitIdentity> GetEffectiveAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The bulk service reads only what a repository sets itself.");

        public Task RemoveLocalAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The bulk service never removes an identity.");

        private string ConfigurationOf(RepositoryHandle repository)
            => _configurationOf.TryGetValue(repository.WorkTreePath, out string? main) ? main : repository.WorkTreePath;
    }
}
