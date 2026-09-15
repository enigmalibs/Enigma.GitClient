using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Refs;

public sealed class RefReaderTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private HistoryFixture _fixture = null!;
    private RepositoryHandle _handle = null!;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _fixture = await HistoryFixture.CreateAsync(_workspace);

        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_fixture.Repository.Path, TestContext.Current.CancellationToken);

        _handle = discovery.Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private IRefReader Reader => _host.GetRequiredService<IRefReader>();

    private Task<RefCollection> ReadRefsAsync() => Reader.GetRefsAsync(_handle, TestContext.Current.CancellationToken);

    private Task<HeadState> ReadHeadAsync() => Reader.GetHeadStateAsync(_handle, TestContext.Current.CancellationToken);

    [Fact]
    public async Task GetRefsAsync_ReadsEveryLocalBranchWithTheCurrentOneFirst()
    {
        RefCollection refs = await ReadRefsAsync();

        Assert.Equal(["main", "feature", "topic"], refs.LocalBranches.Select(branch => branch.ShortName));
        Assert.True(refs.LocalBranches[0].IsCurrent);
        Assert.Equal("main", refs.CurrentBranch?.ShortName);
        Assert.All(refs.LocalBranches.Skip(1), branch => Assert.False(branch.IsCurrent));
    }

    [Fact]
    public async Task GetRefsAsync_ReadsTheBranchTipMetadata()
    {
        RefCollection refs = await ReadRefsAsync();
        GitBranch topic = refs.LocalBranches.Single(branch => branch.ShortName == "topic");

        Assert.Equal(_fixture.ShaC, topic.TargetSha);
        Assert.Equal("Work on the topic branch", topic.TipSubject);
        Assert.Equal("Enigma Test", topic.TipAuthor.Name);
        Assert.Equal("test@enigma.invalid", topic.TipAuthor.Email);
        Assert.Equal(
            new DateTimeOffset(2026, 1, 1, 8, 20, 0, TimeSpan.Zero),
            topic.TipDate.ToUniversalTime());
    }

    [Fact]
    public async Task GetRefsAsync_ReadsBothKindsOfTag()
    {
        RefCollection refs = await ReadRefsAsync();

        GitTag lightweight = refs.Tags.Single(tag => tag.ShortName == "v0.9");
        GitTag annotated = refs.Tags.Single(tag => tag.ShortName == "v1.0");

        Assert.False(lightweight.IsAnnotated);
        Assert.Equal(_fixture.ShaA, lightweight.TargetSha);
        Assert.Null(lightweight.Tagger);
        Assert.Equal(string.Empty, lightweight.Message);

        Assert.True(annotated.IsAnnotated);
        Assert.Equal(_fixture.ShaB, annotated.TargetSha);
        Assert.NotNull(annotated.TagObjectSha);
        Assert.NotEqual(annotated.TargetSha, annotated.TagObjectSha);
        Assert.Equal("Enigma Test", annotated.Tagger!.Name);
        Assert.Equal("First release", annotated.Message);
    }

    [Fact]
    public async Task GetRefsAsync_ReadsTheStashRef()
    {
        _fixture.Repository.WriteFile("src/app.txt", "uncommitted change\n");
        await _fixture.Repository.GitAsync("stash", "push", "-m", "Work in progress");

        RefCollection refs = await ReadRefsAsync();

        Assert.NotNull(refs.Stash);
        Assert.Equal(GitRefKind.Stash, refs.Stash!.Kind);
    }

    [Fact]
    public async Task GetRefsAsync_ReadsRemoteTrackingBranchesAndAheadBehind()
    {
        TemporaryRepository origin = await _workspace.InitRepositoryAsync("origin.git", bare: true);
        await _fixture.Repository.GitAsync("remote", "add", "origin", origin.Path);
        await _fixture.Repository.GitAsync("push", "-u", "origin", "main");

        // Two local commits ahead, and one commit on the remote the local branch has not seen.
        await _fixture.Repository.CommitFileAtAsync("ahead1.txt", "1\n", "Ahead one",
            new DateTimeOffset(2026, 1, 2, 8, 0, 0, TimeSpan.Zero));
        await _fixture.Repository.CommitFileAtAsync("ahead2.txt", "2\n", "Ahead two",
            new DateTimeOffset(2026, 1, 2, 8, 10, 0, TimeSpan.Zero));

        RefCollection refs = await ReadRefsAsync();
        GitBranch main = refs.LocalBranches.Single(branch => branch.ShortName == "main");

        Assert.Equal("origin/main", main.UpstreamShortName);
        Assert.Equal(2, main.Tracking.Ahead);
        Assert.Equal(0, main.Tracking.Behind);
        Assert.False(main.Tracking.IsUpstreamGone);

        GitBranch remote = Assert.Single(refs.RemoteBranches);
        Assert.Equal("origin/main", remote.ShortName);
        Assert.Equal("origin", remote.RemoteName);
        Assert.Equal("main", remote.NameWithoutRemote);
        Assert.True(remote.IsRemote);
    }

    [Fact]
    public async Task GetRefsAsync_ReportsAnUpstreamThatNoLongerExists()
    {
        TemporaryRepository origin = await _workspace.InitRepositoryAsync("origin.git", bare: true);
        await _fixture.Repository.GitAsync("remote", "add", "origin", origin.Path);
        await _fixture.Repository.GitAsync("push", "-u", "origin", "topic");
        await _fixture.Repository.GitAsync("push", "origin", "--delete", "topic");
        await _fixture.Repository.GitAsync("fetch", "--prune", "origin");

        RefCollection refs = await ReadRefsAsync();
        GitBranch topic = refs.LocalBranches.Single(branch => branch.ShortName == "topic");

        Assert.True(topic.Tracking.IsUpstreamGone);
        Assert.False(topic.Tracking.IsSynchronised);
    }

    [Fact]
    public async Task GetRefsAsync_DropsTheRemotesHeadPointer()
    {
        TemporaryRepository origin = await _workspace.InitRepositoryAsync("origin.git", bare: true);
        await _fixture.Repository.GitAsync("remote", "add", "origin", origin.Path);
        await _fixture.Repository.GitAsync("push", "origin", "main");
        await _fixture.Repository.GitAsync("remote", "set-head", "origin", "main");

        RefCollection refs = await ReadRefsAsync();

        Assert.DoesNotContain(refs.RemoteBranches, branch => branch.ShortName.EndsWith("/HEAD", StringComparison.Ordinal));
        Assert.Contains(refs.RemoteBranches, branch => branch.ShortName == "origin/main");
    }

    [Fact]
    public async Task GetRefsAsync_ReadsNothingFromARepositoryWithNoCommits()
    {
        TemporaryRepository unborn = await _workspace.InitRepositoryAsync("unborn");
        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(unborn.Path, TestContext.Current.CancellationToken);

        RefCollection refs = await Reader.GetRefsAsync(discovery.Repository!, TestContext.Current.CancellationToken);

        Assert.Empty(refs.LocalBranches);
        Assert.Empty(refs.Tags);
        Assert.Null(refs.CurrentBranch);
    }

    [Fact]
    public async Task GetHeadStateAsync_ReadsACheckedOutBranch()
    {
        HeadState head = await ReadHeadAsync();

        Assert.False(head.IsUnborn);
        Assert.False(head.IsDetached);
        Assert.Equal("main", head.BranchName);
        Assert.Equal(_fixture.ShaE, head.Sha);
        Assert.Equal(RepositoryOperation.None, head.Operation);
        Assert.Equal("main", head.DisplayName);
    }

    [Fact]
    public async Task GetHeadStateAsync_ReadsADetachedHead()
    {
        await _fixture.Repository.GitAsync("checkout", "--detach", _fixture.ShaB);

        HeadState head = await ReadHeadAsync();

        Assert.True(head.IsDetached);
        Assert.False(head.IsUnborn);
        Assert.Null(head.BranchName);
        Assert.Equal(_fixture.ShaB, head.Sha);
        Assert.StartsWith("detached at", head.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetHeadStateAsync_ReadsAnUnbornHeadAndKeepsItsBranchName()
    {
        TemporaryRepository unborn = await _workspace.InitRepositoryAsync("unborn", initialBranch: "trunk");
        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(unborn.Path, TestContext.Current.CancellationToken);

        HeadState head = await Reader.GetHeadStateAsync(discovery.Repository!, TestContext.Current.CancellationToken);

        Assert.True(head.IsUnborn);
        Assert.False(head.IsDetached);
        Assert.Equal("trunk", head.BranchName);
        Assert.Equal(string.Empty, head.Sha);
    }

    [Fact]
    public async Task GetHeadStateAsync_ReportsAMergeInProgress()
    {
        await _fixture.Repository.GitAsync("checkout", "-b", "conflicting", _fixture.ShaB);
        _fixture.Repository.WriteFile("src/app.txt", "conflicting content\n");
        await _fixture.Repository.CommitAllAtAsync("Conflicting change",
            new DateTimeOffset(2026, 1, 3, 8, 0, 0, TimeSpan.Zero));

        await _fixture.Repository.GitAsync("checkout", "main");

        // The merge is expected to fail with conflicts, which is exactly the state under test.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _fixture.Repository.GitAsync("merge", "conflicting"));

        HeadState head = await ReadHeadAsync();

        Assert.Equal(RepositoryOperation.Merge, head.Operation);
        Assert.True(head.HasOperationInProgress);
    }

    [Fact]
    public void DetectOperation_ReadsEachMarkerFile()
    {
        string gitDirectory = _handle.GitDirectory;

        Assert.Equal(RepositoryOperation.None, RefReader.DetectOperation(_handle));

        File.WriteAllText(Path.Combine(gitDirectory, "REVERT_HEAD"), "sha\n");
        Assert.Equal(RepositoryOperation.Revert, RefReader.DetectOperation(_handle));
        File.Delete(Path.Combine(gitDirectory, "REVERT_HEAD"));

        File.WriteAllText(Path.Combine(gitDirectory, "CHERRY_PICK_HEAD"), "sha\n");
        Assert.Equal(RepositoryOperation.CherryPick, RefReader.DetectOperation(_handle));
        File.Delete(Path.Combine(gitDirectory, "CHERRY_PICK_HEAD"));

        File.WriteAllText(Path.Combine(gitDirectory, "BISECT_LOG"), "log\n");
        Assert.Equal(RepositoryOperation.Bisect, RefReader.DetectOperation(_handle));
        File.Delete(Path.Combine(gitDirectory, "BISECT_LOG"));

        // A rebase left behind by another tool is detected but never started by this client.
        Directory.CreateDirectory(Path.Combine(gitDirectory, "rebase-merge"));
        Assert.Equal(RepositoryOperation.Rebase, RefReader.DetectOperation(_handle));
        Directory.Delete(Path.Combine(gitDirectory, "rebase-merge"), recursive: true);

        Directory.CreateDirectory(Path.Combine(gitDirectory, "rebase-apply"));
        File.WriteAllText(Path.Combine(gitDirectory, "rebase-apply", "applying"), string.Empty);
        Assert.Equal(RepositoryOperation.ApplyMailbox, RefReader.DetectOperation(_handle));
        Directory.Delete(Path.Combine(gitDirectory, "rebase-apply"), recursive: true);
    }

    [Fact]
    public async Task GetStateAsync_BuildsTheDecorationIndexOverEveryRef()
    {
        // A second tag on the feature tip, so one commit carries both a branch and a tag.
        await _fixture.Repository.GitAsync("tag", "feature-mark", _fixture.ShaF);

        RepositoryRefState state = await Reader.GetStateAsync(_handle, TestContext.Current.CancellationToken);

        Assert.Equal("main", state.Head.BranchName);

        // The annotated tag decorates the commit it points at, not the tag object.
        Assert.Contains(state.Decorations.GetRefs(_fixture.ShaB), reference => reference.ShortName == "v1.0");

        // The lightweight tag decorates the root commit.
        Assert.Contains(state.Decorations.GetRefs(_fixture.ShaA), reference => reference.ShortName == "v0.9");

        // The checked-out branch sorts first and is reported as HEAD.
        IReadOnlyList<GitRef> onE = state.Decorations.GetRefs(_fixture.ShaE);
        Assert.Equal("main", onE[0].ShortName);
        Assert.True(state.Decorations.IsHead(_fixture.ShaE));
        Assert.False(state.Decorations.IsHead(_fixture.ShaA));

        // A branch sorts before a tag on the same commit.
        IReadOnlyList<GitRef> onF = state.Decorations.GetRefs(_fixture.ShaF);
        Assert.Equal(["feature", "feature-mark"], onF.Select(reference => reference.ShortName));

        Assert.Equal(_fixture.ShaC, Assert.Single(state.Decorations.GetRefs(_fixture.ShaC)).TargetSha);
    }

    [Fact]
    public async Task GetRemotesAsync_ReadsTheConfiguredRemotes()
    {
        TemporaryRepository origin = await _workspace.InitRepositoryAsync("origin.git", bare: true);
        await _fixture.Repository.GitAsync("remote", "add", "origin", origin.Path);
        await _fixture.Repository.GitAsync("remote", "add", "upstream", "https://example.com/upstream.git");

        IReadOnlyList<GitRemote> remotes = await _host.GetRequiredService<IRemoteReader>()
            .GetRemotesAsync(_handle, TestContext.Current.CancellationToken);

        Assert.Equal(2, remotes.Count);
        Assert.Equal("origin", remotes[0].Name);
        Assert.Equal(origin.Path, remotes[0].FetchUrl);
        Assert.Equal("upstream", remotes[1].Name);
        Assert.Equal("example.com", remotes[1].Host);
    }

    [Fact]
    public async Task GetRemotesAsync_ReturnsNothingWhenThereAreNoRemotes()
        => Assert.Empty(await _host.GetRequiredService<IRemoteReader>()
            .GetRemotesAsync(_handle, TestContext.Current.CancellationToken));
}
