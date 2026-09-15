using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.History;

public sealed class CommitLogReaderTests : IAsyncLifetime
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

    private ICommitLogReader Reader => _host.GetRequiredService<ICommitLogReader>();

    private Task<CommitLogPage> ReadAsync(CommitLogQuery query)
        => Reader.GetPageAsync(_handle, query, TestContext.Current.CancellationToken);

    [Fact]
    public async Task GetPageAsync_ReadsEveryCommitFromEveryRef()
    {
        CommitLogPage page = await ReadAsync(new CommitLogQuery());

        List<string> shas = [.. page.Commits.Select(commit => commit.Sha)];

        Assert.Contains(_fixture.ShaA, shas);
        Assert.Contains(_fixture.ShaB, shas);
        Assert.Contains(_fixture.ShaC, shas);
        Assert.Contains(_fixture.ShaD, shas);
        Assert.Contains(_fixture.ShaE, shas);
        Assert.Contains(_fixture.ShaF, shas);
        Assert.Equal(6, page.Commits.Count);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsNewestFirstInDateOrder()
    {
        CommitLogPage page = await ReadAsync(new CommitLogQuery { Ordering = CommitLogOrdering.Date });

        DateTimeOffset previous = DateTimeOffset.MaxValue;
        foreach (GitCommit commit in page.Commits)
        {
            Assert.True(commit.Committer.When <= previous, "commits must be returned newest first");
            previous = commit.Committer.When;
        }

        Assert.Equal(_fixture.ShaF, page.Commits[0].Sha);
    }

    [Fact]
    public async Task GetPageAsync_ReadsTheMergesTwoParentsInOrder()
    {
        CommitLogPage page = await ReadAsync(new CommitLogQuery());
        GitCommit merge = page.Commits.Single(commit => commit.Sha == _fixture.ShaD);

        Assert.True(merge.IsMerge);
        Assert.Equal([_fixture.ShaB, _fixture.ShaC], merge.ParentShas);
        Assert.Equal("Merge the topic branch", merge.Subject);
        Assert.Equal("It brings in the topic file.", merge.Body);
    }

    [Fact]
    public async Task GetPageAsync_ReadsTheRootCommit()
    {
        CommitLogPage page = await ReadAsync(new CommitLogQuery());
        GitCommit root = page.Commits.Single(commit => commit.Sha == _fixture.ShaA);

        Assert.True(root.IsRoot);
        Assert.Empty(root.ParentShas);
        Assert.Equal("Add the readme", root.Subject);
    }

    [Fact]
    public async Task GetPageAsync_ReadsTheRecordedTimestampsExactly()
    {
        CommitLogPage page = await ReadAsync(new CommitLogQuery());
        GitCommit root = page.Commits.Single(commit => commit.Sha == _fixture.ShaA);

        Assert.Equal(
            new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero),
            root.Author.When.ToUniversalTime());
    }

    [Fact]
    public async Task GetPageAsync_Pages()
    {
        CommitLogPage first = await ReadAsync(new CommitLogQuery { Take = 2 });

        Assert.Equal(2, first.Commits.Count);
        Assert.True(first.HasMore);
        Assert.Equal(0, first.Skip);

        CommitLogPage second = await ReadAsync(new CommitLogQuery { Take = 2 }.NextPage());

        Assert.Equal(2, second.Commits.Count);
        Assert.True(second.HasMore);
        Assert.Equal(2, second.Skip);

        CommitLogPage third = await ReadAsync(new CommitLogQuery { Take = 2, Skip = 4 });

        Assert.Equal(2, third.Commits.Count);
        Assert.False(third.HasMore);

        // The three pages together are the whole history, with nothing repeated.
        List<string> all =
        [
            .. first.Commits.Concat(second.Commits).Concat(third.Commits).Select(commit => commit.Sha),
        ];
        Assert.Equal(6, all.Distinct().Count());
    }

    [Fact]
    public async Task GetPageAsync_ReturnsNothingForAZeroSizedPage()
    {
        CommitLogPage page = await ReadAsync(new CommitLogQuery { Take = 0 });

        Assert.True(page.IsEmpty);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task GetPageAsync_HeadScopeExcludesUnmergedBranches()
    {
        CommitLogPage page = await ReadAsync(new CommitLogQuery { Scope = CommitLogScope.Head });

        List<string> shas = [.. page.Commits.Select(commit => commit.Sha)];

        Assert.DoesNotContain(_fixture.ShaF, shas);
        Assert.Contains(_fixture.ShaE, shas);
        Assert.Equal(5, page.Commits.Count);
    }

    [Fact]
    public async Task GetPageAsync_RevisionScopeWalksThatRevisionOnly()
    {
        CommitLogPage page = await ReadAsync(new CommitLogQuery
        {
            Scope = CommitLogScope.Revision,
            Revision = "topic",
        });

        List<string> shas = [.. page.Commits.Select(commit => commit.Sha)];

        Assert.Equal([_fixture.ShaC, _fixture.ShaB, _fixture.ShaA], shas);
    }

    [Fact]
    public async Task GetPageAsync_FirstParentOnlyHidesTheMergedInBranch()
    {
        CommitLogPage page = await ReadAsync(new CommitLogQuery
        {
            Scope = CommitLogScope.Head,
            FirstParentOnly = true,
        });

        List<string> shas = [.. page.Commits.Select(commit => commit.Sha)];

        Assert.Equal([_fixture.ShaE, _fixture.ShaD, _fixture.ShaB, _fixture.ShaA], shas);
        Assert.DoesNotContain(_fixture.ShaC, shas);
    }

    [Fact]
    public async Task GetPageAsync_FiltersByMessageWithoutTreatingItAsARegularExpression()
    {
        CommitLogPage page = await ReadAsync(new CommitLogQuery { MessageFilter = "topic branch" });

        Assert.Equal(2, page.Commits.Count);
        Assert.All(page.Commits, commit => Assert.Contains("topic branch", commit.Message, StringComparison.OrdinalIgnoreCase));

        CommitLogPage noRegex = await ReadAsync(new CommitLogQuery { MessageFilter = "topic.branch" });
        Assert.True(noRegex.IsEmpty);
    }

    [Fact]
    public async Task GetPageAsync_FiltersByMessageCaseInsensitively()
    {
        CommitLogPage page = await ReadAsync(new CommitLogQuery { MessageFilter = "ADD THE README" });

        Assert.Equal(_fixture.ShaA, Assert.Single(page.Commits).Sha);
    }

    [Fact]
    public async Task GetPageAsync_FiltersByAuthor()
    {
        CommitLogPage matching = await ReadAsync(new CommitLogQuery { AuthorFilter = "Enigma Test" });
        Assert.Equal(6, matching.Commits.Count);

        CommitLogPage missing = await ReadAsync(new CommitLogQuery { AuthorFilter = "Nobody At All" });
        Assert.True(missing.IsEmpty);
    }

    [Fact]
    public async Task GetPageAsync_FiltersByPath()
    {
        CommitLogPage page = await ReadAsync(new CommitLogQuery { PathFilters = ["src/feature.txt"] });

        Assert.Equal(_fixture.ShaF, Assert.Single(page.Commits).Sha);
    }

    [Fact]
    public async Task GetPageAsync_FiltersByDateRange()
    {
        CommitLogPage page = await ReadAsync(new CommitLogQuery
        {
            Since = new DateTimeOffset(2026, 1, 1, 8, 35, 0, TimeSpan.Zero),
        });

        List<string> shas = [.. page.Commits.Select(commit => commit.Sha)];

        Assert.Contains(_fixture.ShaE, shas);
        Assert.Contains(_fixture.ShaF, shas);
        Assert.DoesNotContain(_fixture.ShaA, shas);
    }

    [Fact]
    public async Task GetPageAsync_ReadsAMultiLineNonAsciiMessage()
    {
        const string subject = "Ajout du rapport financier";
        const string body = "Première ligne du corps.\n\nUne deuxième section, avec « guillemets ».";

        await _fixture.Repository.CommitFileAtAsync(
            "rapport.txt",
            "contenu\n",
            $"{subject}\n\n{body}",
            new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

        CommitLogPage page = await ReadAsync(new CommitLogQuery { Take = 1, Scope = CommitLogScope.Head });
        GitCommit commit = Assert.Single(page.Commits);

        Assert.Equal(subject, commit.Subject);
        Assert.Equal(body, commit.Body);
    }

    [Fact]
    public async Task GetPageAsync_ReadsAnOctopusMerge()
    {
        await _fixture.Repository.GitAsync("checkout", "-b", "octo-1", _fixture.ShaE);
        await _fixture.Repository.CommitFileAtAsync("octo1.txt", "1\n", "Octopus arm one",
            new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));

        await _fixture.Repository.GitAsync("checkout", "-b", "octo-2", _fixture.ShaE);
        await _fixture.Repository.CommitFileAtAsync("octo2.txt", "2\n", "Octopus arm two",
            new DateTimeOffset(2026, 1, 1, 10, 10, 0, TimeSpan.Zero));

        await _fixture.Repository.GitAsync("checkout", "main");
        await _fixture.Repository.GitAsync("merge", "--no-ff", "octo-1", "octo-2", "-m", "Octopus merge");

        string mergeSha = await _fixture.Repository.ResolveAsync("HEAD");
        GitCommit? merge = await Reader.GetCommitAsync(_handle, mergeSha, TestContext.Current.CancellationToken);

        Assert.NotNull(merge);
        Assert.True(merge!.IsMerge);
        Assert.Equal(3, merge.ParentShas.Count);
    }

    [Fact]
    public async Task GetCommitAsync_ResolvesEveryKindOfRevision()
    {
        GitCommit? bySha = await Reader.GetCommitAsync(_handle, _fixture.ShaB, TestContext.Current.CancellationToken);
        GitCommit? byShortSha = await Reader.GetCommitAsync(
            _handle,
            _fixture.ShaB.Substring(0, 7),
            TestContext.Current.CancellationToken);
        GitCommit? byBranch = await Reader.GetCommitAsync(_handle, "topic", TestContext.Current.CancellationToken);
        GitCommit? byTag = await Reader.GetCommitAsync(_handle, "v1.0", TestContext.Current.CancellationToken);

        Assert.Equal(_fixture.ShaB, bySha?.Sha);
        Assert.Equal(_fixture.ShaB, byShortSha?.Sha);
        Assert.Equal(_fixture.ShaC, byBranch?.Sha);
        Assert.Equal(_fixture.ShaB, byTag?.Sha);
    }

    [Fact]
    public async Task GetCommitAsync_ReturnsNullForAnUnknownRevision()
        => Assert.Null(await Reader.GetCommitAsync(_handle, "no-such-ref", TestContext.Current.CancellationToken));

    [Fact]
    public async Task CountAsync_CountsTheWholeHistoryAndAFilteredSubset()
    {
        Assert.Equal(6, await Reader.CountAsync(_handle, new CommitLogQuery(), TestContext.Current.CancellationToken));
        Assert.Equal(
            5,
            await Reader.CountAsync(
                _handle,
                new CommitLogQuery { Scope = CommitLogScope.Head },
                TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await Reader.CountAsync(
                _handle,
                new CommitLogQuery { MessageFilter = "readme" },
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CountAsync_IgnoresPagingSoTheTotalIsAlwaysTheTotal()
        => Assert.Equal(
            6,
            await Reader.CountAsync(
                _handle,
                new CommitLogQuery { Take = 2, Skip = 2 },
                TestContext.Current.CancellationToken));

    [Fact]
    public async Task GetPageAsync_ReturnsAnEmptyPageForARepositoryWithNoCommits()
    {
        TemporaryRepository unborn = await _workspace.InitRepositoryAsync("unborn");
        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(unborn.Path, TestContext.Current.CancellationToken);

        CommitLogPage page = await Reader.GetPageAsync(
            discovery.Repository!,
            new CommitLogQuery(),
            TestContext.Current.CancellationToken);

        Assert.True(page.IsEmpty);
        Assert.False(page.HasMore);
        Assert.Equal(0, await Reader.CountAsync(discovery.Repository!, new CommitLogQuery(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPageAsync_ReadsADetachedHead()
    {
        await _fixture.Repository.GitAsync("checkout", "--detach", _fixture.ShaB);

        CommitLogPage page = await ReadAsync(new CommitLogQuery { Scope = CommitLogScope.Head });

        Assert.Equal([_fixture.ShaB, _fixture.ShaA], page.Commits.Select(commit => commit.Sha));
    }

    [Fact]
    public async Task GetPageAsync_IsUnaffectedByARepositoryConfiguredToShowSignatures()
    {
        await _fixture.Repository.GitAsync("config", "log.showSignature", "true");

        CommitLogPage page = await ReadAsync(new CommitLogQuery());

        Assert.Equal(6, page.Commits.Count);
        Assert.All(page.Commits, commit => Assert.Equal(40, commit.Sha.Length));
    }

    [Fact]
    public async Task GetPageAsync_ThrowsForAGenuinelyInvalidQuery()
        => await Assert.ThrowsAsync<Core.Git.GitCommandException>(
            () => ReadAsync(new CommitLogQuery
            {
                Scope = CommitLogScope.Revision,
                Revision = "--not-a-real-option",
            }));
}
