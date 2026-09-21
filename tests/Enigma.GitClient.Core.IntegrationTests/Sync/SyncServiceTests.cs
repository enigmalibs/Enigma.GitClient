using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Sync;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Sync;

/// <summary>
/// Drives fetch, pull, push and remote management against a second directory standing in as the
/// remote.
/// </summary>
/// <remarks>
/// A local path gives real push, fetch, prune and lease semantics with no network and no flakiness.
/// Mocking git would prove nothing here: the behaviour worth testing — what a rejected push looks
/// like, what a lease refuses — belongs entirely to git.
/// </remarks>
public sealed class SyncServiceTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _origin = null!;
    private TemporaryRepository _local = null!;
    private RepositoryHandle _handle = null!;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);

        _origin = await _workspace.InitRepositoryAsync("origin", bare: true);
        _local = await _workspace.InitRepositoryAsync("local");

        _local.WriteFile("README.md", "# one\n");
        await _local.CommitAllAsync("Add the readme");

        await _local.GitAsync("remote", "add", "origin", _origin.Path);
        await _local.GitAsync("push", "--set-upstream", "origin", "main");

        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_local.Path, TestContext.Current.CancellationToken);

        _handle = discovery.Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private ISyncService Sync => _host.GetRequiredService<ISyncService>();

    private IRemoteService Remotes => _host.GetRequiredService<IRemoteService>();

    private Task<RefCollection> RefsAsync()
        => _host.GetRequiredService<IRefReader>().GetRefsAsync(_handle, TestContext.Current.CancellationToken);

    /// <summary>
    /// Builds a second clone of the same remote, so one side can move while the other is unaware.
    /// </summary>
    private async Task<TemporaryRepository> BuildSecondCloneAsync()
    {
        string path = System.IO.Path.Combine(_workspace.RootPath, "other");
        System.IO.Directory.CreateDirectory(path);

        await GitCli.RunAsync(_workspace.RootPath, _workspace.Environment, ["clone", _origin.Path, path]);

        return new TemporaryRepository(_workspace, path, isBare: false);
    }

    // ---------------------------------------------------------------- remotes

    [Fact]
    public async Task ListAsync_ReadsTheRemoteThatIsThere()
    {
        GitRemote remote = Assert.Single(await Remotes.ListAsync(_handle, TestContext.Current.CancellationToken));

        Assert.Equal("origin", remote.Name);
        Assert.Equal(_origin.Path, remote.FetchUrl);
    }

    [Fact]
    public async Task AddAsync_AddsARemoteAndRenameAndRemoveTakeItBack()
    {
        TemporaryRepository second = await _workspace.InitRepositoryAsync("mirror", bare: true);

        await Remotes.AddAsync(_handle, "mirror", second.Path, TestContext.Current.CancellationToken);

        Assert.Contains(
            await Remotes.ListAsync(_handle, TestContext.Current.CancellationToken),
            remote => remote.Name == "mirror");

        await Remotes.RenameAsync(_handle, "mirror", "backup", TestContext.Current.CancellationToken);

        IReadOnlyList<GitRemote> renamed = await Remotes.ListAsync(_handle, TestContext.Current.CancellationToken);

        Assert.Contains(renamed, remote => remote.Name == "backup");
        Assert.DoesNotContain(renamed, remote => remote.Name == "mirror");

        await Remotes.RemoveAsync(_handle, "backup", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            await Remotes.ListAsync(_handle, TestContext.Current.CancellationToken),
            remote => remote.Name == "backup");
    }

    [Fact]
    public async Task AddAsync_RefusesAUrlThatCannotBeOne()
    {
        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Remotes.AddAsync(_handle, "broken", "not a url at all", TestContext.Current.CancellationToken));

        Assert.NotEqual(string.Empty, refusal.Message);
    }

    [Fact]
    public async Task AddAsync_RefusesANameGitWouldNot()
    {
        await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Remotes.AddAsync(_handle, "bad name", _origin.Path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetUrlAsync_ChangesWhereARemotePointsAndCanSplitPushFromFetch()
    {
        TemporaryRepository other = await _workspace.InitRepositoryAsync("elsewhere", bare: true);

        await Remotes.SetUrlAsync(_handle, "origin", other.Path, cancellationToken: TestContext.Current.CancellationToken);

        GitRemote remote = Assert.Single(await Remotes.ListAsync(_handle, TestContext.Current.CancellationToken));

        Assert.Equal(other.Path, remote.FetchUrl);
        Assert.Equal(other.Path, remote.PushUrl);

        await Remotes.SetUrlAsync(
            _handle,
            "origin",
            other.Path,
            _origin.Path,
            TestContext.Current.CancellationToken);

        GitRemote split = Assert.Single(await Remotes.ListAsync(_handle, TestContext.Current.CancellationToken));

        Assert.Equal(other.Path, split.FetchUrl);
        Assert.Equal(_origin.Path, split.PushUrl);
        Assert.True(split.HasSeparatePushUrl);
    }

    // ---------------------------------------------------------------- fetch

    [Fact]
    public async Task FetchAsync_BringsCommitsThatArrivedElsewhere()
    {
        TemporaryRepository other = await BuildSecondCloneAsync();

        other.WriteFile("src/theirs.txt", "from the other clone\n");
        await other.CommitAllAsync("Work done elsewhere");
        await other.GitAsync("push", "origin", "main");

        await Sync.FetchAsync(_handle, cancellationToken: TestContext.Current.CancellationToken);

        GitBranch tracking = (await RefsAsync()).RemoteBranches.Single(branch => branch.ShortName == "origin/main");

        Assert.Equal("Work done elsewhere", tracking.TipSubject);

        // Fetching does not move the local branch; that is what pull is for.
        Assert.Equal("Add the readme", (await RefsAsync()).LocalBranches.Single(b => b.ShortName == "main").TipSubject);
    }

    [Fact]
    public async Task FetchAsync_ReportsItsProgress()
    {
        TemporaryRepository other = await BuildSecondCloneAsync();

        other.WriteFile("src/theirs.txt", "from the other clone\n");
        await other.CommitAllAsync("Work done elsewhere");
        await other.GitAsync("push", "origin", "main");

        // A plain IProgress, not a Progress<T>: this test is about *when* the reports arrive, and a
        // Progress<T> built here would capture no synchronisation context and queue every callback to
        // the thread pool — which is exactly the defect that made this test flaky, moved into the
        // test. The list is locked because the reports arrive on the reader's thread.
        RecordingProgress progress = new();

        await Sync.FetchAsync(_handle, "origin", progress: progress, cancellationToken: TestContext.Current.CancellationToken);

        // Read the instant the fetch completes. This is the assertion the defect broke: the reports
        // were queued to the thread pool, so at this moment the list was often still empty.
        SyncProgress[] reports = progress.Reports;

        // A local transfer is fast, but git still narrates it; an empty transcript would mean the
        // progress plumbing is not connected at all.
        Assert.NotEmpty(reports);

        // And in the order git wrote them: within a stage a percentage only ever grows, so an overlay
        // never goes backwards. Queued callbacks have no order at all.
        foreach (IGrouping<SyncStage, SyncProgress> stage in reports.GroupBy(report => report.Stage))
        {
            int[] percentages = [.. stage.Where(report => report.Percent is not null).Select(report => report.Percent!.Value)];

            Assert.Equal([.. percentages.Order()], percentages);
        }
    }

    /// <summary>
    /// Collects progress reports on whatever thread reports them, in order.
    /// </summary>
    private sealed class RecordingProgress : IProgress<SyncProgress>
    {
        private readonly List<SyncProgress> _reports = [];

        public SyncProgress[] Reports
        {
            get
            {
                lock (_reports)
                {
                    return [.. _reports];
                }
            }
        }

        public void Report(SyncProgress value)
        {
            lock (_reports)
            {
                _reports.Add(value);
            }
        }
    }

    [Fact]
    public async Task FetchAsync_PruningDropsATrackingBranchTheRemoteNoLongerHas()
    {
        TemporaryRepository other = await BuildSecondCloneAsync();

        await other.GitAsync("checkout", "-b", "temporary");
        await other.CommitFileAsync("src/temp.txt", "temp\n", "Temporary work");
        await other.GitAsync("push", "origin", "temporary");

        await Sync.FetchAsync(_handle, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains((await RefsAsync()).RemoteBranches, branch => branch.ShortName == "origin/temporary");

        await other.GitAsync("push", "origin", "--delete", "temporary");
        await Sync.FetchAsync(_handle, prune: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain((await RefsAsync()).RemoteBranches, branch => branch.ShortName == "origin/temporary");
    }

    [Fact]
    public async Task FetchAsync_KeepsAStaleTrackingBranchWhenPruningIsOff()
    {
        TemporaryRepository other = await BuildSecondCloneAsync();

        await other.GitAsync("checkout", "-b", "temporary");
        await other.CommitFileAsync("src/temp.txt", "temp\n", "Temporary work");
        await other.GitAsync("push", "origin", "temporary");

        await Sync.FetchAsync(_handle, cancellationToken: TestContext.Current.CancellationToken);
        await other.GitAsync("push", "origin", "--delete", "temporary");

        await Sync.FetchAsync(_handle, prune: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains((await RefsAsync()).RemoteBranches, branch => branch.ShortName == "origin/temporary");
    }

    // ---------------------------------------------------------------- pull

    [Fact]
    public async Task PullAsync_FastForwardsOntoWhatArrived()
    {
        TemporaryRepository other = await BuildSecondCloneAsync();

        other.WriteFile("src/theirs.txt", "from the other clone\n");
        await other.CommitAllAsync("Work done elsewhere");
        await other.GitAsync("push", "origin", "main");

        await Sync.PullAsync(_handle, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Work done elsewhere", await _local.GitLineAsync("log", "-1", "--format=%s"));
        Assert.True(System.IO.File.Exists(_local.GetPath("src/theirs.txt")));
    }

    [Fact]
    public async Task PullAsync_MergesWhenBothSidesMoved()
    {
        TemporaryRepository other = await BuildSecondCloneAsync();

        other.WriteFile("src/theirs.txt", "from the other clone\n");
        await other.CommitAllAsync("Work done elsewhere");
        await other.GitAsync("push", "origin", "main");

        _local.WriteFile("src/ours.txt", "from here\n");
        await _local.CommitAllAsync("Work done here");

        await Sync.PullAsync(_handle, cancellationToken: TestContext.Current.CancellationToken);

        // A merge commit, with both sides' files present.
        Assert.Equal(2, (await _local.GitLinesAsync("rev-list", "--parents", "-1", "HEAD"))[0].Split(' ').Length - 1);
        Assert.True(System.IO.File.Exists(_local.GetPath("src/theirs.txt")));
        Assert.True(System.IO.File.Exists(_local.GetPath("src/ours.txt")));
    }

    [Fact]
    public async Task PullAsync_RefusesToMergeWhenAskedForFastForwardOnly()
    {
        TemporaryRepository other = await BuildSecondCloneAsync();

        other.WriteFile("src/theirs.txt", "from the other clone\n");
        await other.CommitAllAsync("Work done elsewhere");
        await other.GitAsync("push", "origin", "main");

        _local.WriteFile("src/ours.txt", "from here\n");
        await _local.CommitAllAsync("Work done here");

        await Assert.ThrowsAsync<SyncException>(() => Sync.PullAsync(
            _handle,
            strategy: PullStrategy.FastForwardOnly,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Work done here", await _local.GitLineAsync("log", "-1", "--format=%s"));
    }

    [Fact]
    public async Task PullAsync_ReportsAConflictAsOneRatherThanAsAGenericFailure()
    {
        TemporaryRepository other = await BuildSecondCloneAsync();

        other.WriteFile("README.md", "# theirs\n");
        await other.CommitAllAsync("Their version of the readme");
        await other.GitAsync("push", "origin", "main");

        _local.WriteFile("README.md", "# ours\n");
        await _local.CommitAllAsync("Our version of the readme");

        SyncException failure = await Assert.ThrowsAsync<SyncException>(
            () => Sync.PullAsync(_handle, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(SyncFailureKind.MergeConflict, failure.Failure.Kind);
        Assert.True(failure.Failure.IsRecoverableLocally);
    }

    // ---------------------------------------------------------------- push

    [Fact]
    public async Task PushAsync_PublishesABranchAndSetsItsUpstream()
    {
        await _local.GitAsync("checkout", "-b", "topic");
        await _local.CommitFileAsync("src/topic.txt", "topic\n", "Work on the topic branch");

        await Sync.PushAsync(
            _handle,
            new PushRequest { Remote = "origin", Branch = "topic", SetUpstream = true },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(
            "topic",
            await _origin.GitLinesAsync("for-each-ref", "--format=%(refname:short)", "refs/heads/"));

        Assert.Equal(
            "origin/topic",
            (await RefsAsync()).LocalBranches.Single(branch => branch.ShortName == "topic").UpstreamShortName);
    }

    [Fact]
    public async Task PushAsync_IsRejectedWhenTheRemoteHasCommitsItWouldDrop()
    {
        TemporaryRepository other = await BuildSecondCloneAsync();

        other.WriteFile("src/theirs.txt", "from the other clone\n");
        await other.CommitAllAsync("Work done elsewhere");
        await other.GitAsync("push", "origin", "main");

        _local.WriteFile("src/ours.txt", "from here\n");
        await _local.CommitAllAsync("Work done here");

        SyncException failure = await Assert.ThrowsAsync<SyncException>(() => Sync.PushAsync(
            _handle,
            new PushRequest { Remote = "origin", Branch = "main" },
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(SyncFailureKind.NonFastForward, failure.Failure.Kind);
        Assert.Contains("Pull first", failure.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PushAsync_ForceWithLeaseSucceedsWhileTheLeaseHolds()
    {
        _local.WriteFile("README.md", "# rewritten\n");
        await _local.GitAsync("add", "--all");
        await _local.GitAsync("commit", "--amend", "-m", "Say it better");

        await Sync.PushAsync(
            _handle,
            new PushRequest { Remote = "origin", Branch = "main", ForceWithLease = true },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Say it better", await _origin.GitLineAsync("log", "-1", "--format=%s", "main"));
    }

    [Fact]
    public async Task PushAsync_ForceWithLeaseRefusesOnceTheRemoteHasMoved()
    {
        TemporaryRepository other = await BuildSecondCloneAsync();

        // Someone else pushes; this clone never fetches, so its idea of the remote is stale.
        other.WriteFile("src/theirs.txt", "from the other clone\n");
        await other.CommitAllAsync("Work done elsewhere");
        await other.GitAsync("push", "origin", "main");

        _local.WriteFile("README.md", "# rewritten\n");
        await _local.GitAsync("add", "--all");
        await _local.GitAsync("commit", "--amend", "-m", "Say it better");

        SyncException failure = await Assert.ThrowsAsync<SyncException>(() => Sync.PushAsync(
            _handle,
            new PushRequest { Remote = "origin", Branch = "main", ForceWithLease = true },
            cancellationToken: TestContext.Current.CancellationToken));

        // This is the case a bare --force would have destroyed.
        Assert.Equal(SyncFailureKind.StaleLease, failure.Failure.Kind);
        Assert.Equal("Work done elsewhere", await _origin.GitLineAsync("log", "-1", "--format=%s", "main"));
    }

    [Fact]
    public async Task PushAsync_DeletesABranchOnTheRemote()
    {
        await _local.GitAsync("checkout", "-b", "topic");
        await _local.CommitFileAsync("src/topic.txt", "topic\n", "Work on the topic branch");
        await _local.GitAsync("push", "origin", "topic");

        await Sync.PushAsync(
            _handle,
            new PushRequest { Remote = "origin", Branch = "topic", Delete = true },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            "topic",
            await _origin.GitLinesAsync("for-each-ref", "--format=%(refname:short)", "refs/heads/"));
    }

    [Fact]
    public async Task PushAsync_RefusesADeleteThatNamesNoBranch()
    {
        SyncException failure = await Assert.ThrowsAsync<SyncException>(() => Sync.PushAsync(
            _handle,
            new PushRequest { Remote = "origin", Delete = true },
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Name the branch", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PushAsync_CarriesTagsWhenAskedTo()
    {
        await _local.CommitFileAsync("src/release.txt", "release\n", "Prepare the release");
        await _local.GitAsync("tag", "-a", "v1.0.0", "-m", "First release");

        await Sync.PushAsync(
            _handle,
            new PushRequest { Remote = "origin", Branch = "main", PushTags = true },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(
            "v1.0.0",
            await _origin.GitLinesAsync("for-each-ref", "--format=%(refname:short)", "refs/tags/"));
    }

    [Fact]
    public async Task PushAsync_ReportsAnUnknownRemoteRatherThanFailingSilently()
    {
        SyncException failure = await Assert.ThrowsAsync<SyncException>(() => Sync.PushAsync(
            _handle,
            new PushRequest { Remote = "nowhere", Branch = "main" },
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.NotEqual(string.Empty, failure.Failure.Message);
        Assert.NotEqual(string.Empty, failure.Failure.Detail);
    }
}
