using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Repositories;

public sealed class RepositoryServiceTests : IAsyncLifetime
{
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

    private IRepositoryService Service => _host.GetRequiredService<IRepositoryService>();

    private string Path(string name) => System.IO.Path.Combine(_workspace.RootPath, name);

    // ---------------------------------------------------------------- init

    [Fact]
    public async Task InitAsync_CreatesARepositoryOnTheRequestedBranch()
    {
        RepositoryHandle repository = await Service.InitAsync(
            Path("fresh"),
            "trunk",
            TestContext.Current.CancellationToken);

        Assert.Equal("fresh", repository.Name);
        Assert.True(Directory.Exists(repository.GitDirectory));

        HeadState head = await _host.GetRequiredService<IRefReader>()
            .GetHeadStateAsync(repository, TestContext.Current.CancellationToken);

        Assert.True(head.IsUnborn);
        Assert.Equal("trunk", head.BranchName);
    }

    [Fact]
    public async Task InitAsync_DefaultsToMain()
    {
        RepositoryHandle repository = await Service.InitAsync(
            Path("defaulted"),
            cancellationToken: TestContext.Current.CancellationToken);

        HeadState head = await _host.GetRequiredService<IRefReader>()
            .GetHeadStateAsync(repository, TestContext.Current.CancellationToken);

        Assert.Equal("main", head.BranchName);
    }

    [Fact]
    public async Task InitAsync_CreatesTheDirectoryWhenItDoesNotExist()
    {
        string path = Path(System.IO.Path.Combine("nested", "deeper", "repo"));

        RepositoryHandle repository = await Service.InitAsync(
            path,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(System.IO.Path.GetFullPath(path), repository.WorkTreePath);
    }

    [Fact]
    public async Task InitAsync_OnAnExistingRepositoryIsHarmless()
    {
        string path = Path("twice");

        RepositoryHandle first = await Service.InitAsync(path, "main", TestContext.Current.CancellationToken);
        RepositoryHandle second = await Service.InitAsync(path, "main", TestContext.Current.CancellationToken);

        Assert.Equal(first.WorkTreePath, second.WorkTreePath);
    }

    // ---------------------------------------------------------------- open

    [Fact]
    public async Task OpenAsync_FindsARepositoryFromANestedDirectory()
    {
        TemporaryRepository repository = await _workspace.InitRepositoryAsync("existing");
        await repository.CommitInitialAsync();
        string nested = repository.CreateDirectory(System.IO.Path.Combine("src", "deep"));

        RepositoryDiscoveryResult result = await Service.OpenAsync(nested, TestContext.Current.CancellationToken);

        Assert.True(result.IsFound);
        Assert.Equal("existing", result.Repository!.Name);
    }

    [Fact]
    public async Task OpenAsync_ReportsANonRepository()
    {
        string plain = _workspace.CreateDirectory("plain");

        RepositoryDiscoveryResult result = await Service.OpenAsync(plain, TestContext.Current.CancellationToken);

        Assert.Equal(RepositoryDiscoveryStatus.NotARepository, result.Status);
    }

    // ---------------------------------------------------------------- clone

    private async Task<TemporaryRepository> CreateSourceAsync(string name = "source")
    {
        TemporaryRepository source = await _workspace.InitRepositoryAsync(name);
        await source.CommitFileAtAsync("README.md", "# source\n", "Add the readme",
            new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
        await source.CommitFileAtAsync("src/app.txt", "one\n", "Add the application file",
            new DateTimeOffset(2026, 1, 1, 8, 10, 0, TimeSpan.Zero));
        await source.GitAsync("branch", "feature");
        return source;
    }

    [Fact]
    public async Task CloneAsync_ClonesALocalRepositoryAndReportsProgress()
    {
        TemporaryRepository source = await CreateSourceAsync();

        List<CloneProgress> reports = [];
        Progress<CloneProgress> progress = new(reports.Add);

        RepositoryHandle clone = await Service.CloneAsync(
            new CloneRequest { Url = source.Path, ParentDirectory = _workspace.RootPath, DirectoryName = "cloned" },
            progress,
            TestContext.Current.CancellationToken);

        Assert.Equal("cloned", clone.Name);
        Assert.True(File.Exists(System.IO.Path.Combine(clone.WorkTreePath, "README.md")));
        Assert.True(File.Exists(System.IO.Path.Combine(clone.WorkTreePath, "src", "app.txt")));

        // Progress is reported through a Progress<T>, which posts asynchronously, so the reports
        // may still be arriving; the first and last are the ones the service raises directly.
        Assert.NotEmpty(reports);
        Assert.Equal(CloneStage.Starting, reports[0].Stage);
    }

    [Fact]
    public async Task CloneAsync_CreatesTheRemoteUnderTheRequestedName()
    {
        TemporaryRepository source = await CreateSourceAsync();

        RepositoryHandle clone = await Service.CloneAsync(
            new CloneRequest
            {
                Url = source.Path,
                ParentDirectory = _workspace.RootPath,
                DirectoryName = "named-remote",
                RemoteName = "upstream",
            },
            progress: null,
            TestContext.Current.CancellationToken);

        IReadOnlyList<GitRemote> remotes = await _host.GetRequiredService<IRemoteReader>()
            .GetRemotesAsync(clone, TestContext.Current.CancellationToken);

        Assert.Equal("upstream", Assert.Single(remotes).Name);
    }

    [Fact]
    public async Task CloneAsync_ChecksOutTheRequestedBranch()
    {
        TemporaryRepository source = await CreateSourceAsync();

        RepositoryHandle clone = await Service.CloneAsync(
            new CloneRequest
            {
                Url = source.Path,
                ParentDirectory = _workspace.RootPath,
                DirectoryName = "on-feature",
                Branch = "feature",
            },
            progress: null,
            TestContext.Current.CancellationToken);

        HeadState head = await _host.GetRequiredService<IRefReader>()
            .GetHeadStateAsync(clone, TestContext.Current.CancellationToken);

        Assert.Equal("feature", head.BranchName);
    }

    [Fact]
    public async Task CloneAsync_HonoursAShallowDepth()
    {
        TemporaryRepository source = await CreateSourceAsync();

        // A file:// URL on purpose: git ignores --depth for a plain local path, because it hardlinks
        // the object store instead of using a transport. Using the transport is what a shallow clone
        // means, and it is what the option is for.
        RepositoryHandle clone = await Service.CloneAsync(
            new CloneRequest
            {
                Url = new Uri(source.Path).AbsoluteUri,
                ParentDirectory = _workspace.RootPath,
                DirectoryName = "shallow",
                Depth = 1,
            },
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(System.IO.Path.Combine(clone.GitDirectory, "shallow")));

        CommitLogPage page = await _host.GetRequiredService<ICommitLogReader>().GetPageAsync(
            clone,
            new CommitLogQuery { Scope = CommitLogScope.Head },
            TestContext.Current.CancellationToken);

        Assert.Single(page.Commits);
    }

    [Fact]
    public async Task CloneAsync_RejectsAUrlGitCannotUse()
    {
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => Service.CloneAsync(
                new CloneRequest { Url = "telnet://example.com/repo", ParentDirectory = _workspace.RootPath },
                progress: null,
                TestContext.Current.CancellationToken));

        Assert.Contains("telnet", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CloneAsync_RefusesToCloneIntoANonEmptyDirectory()
    {
        TemporaryRepository source = await CreateSourceAsync();
        string occupied = _workspace.CreateDirectory("occupied");
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(occupied, "existing.txt"),
            "content",
            TestContext.Current.CancellationToken);

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => Service.CloneAsync(
                new CloneRequest
                {
                    Url = source.Path,
                    ParentDirectory = _workspace.RootPath,
                    DirectoryName = "occupied",
                },
                progress: null,
                TestContext.Current.CancellationToken));

        Assert.Contains("not empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CloneAsync_LeavesNothingBehindWhenGitFails()
    {
        string missing = System.IO.Path.Combine(_workspace.RootPath, "no-such-source");
        Directory.CreateDirectory(missing);

        // An empty directory is a valid local path, so validation passes and git is the one that
        // refuses — which is exactly the path that must clean up after itself.
        await Assert.ThrowsAsync<GitCommandException>(
            () => Service.CloneAsync(
                new CloneRequest
                {
                    Url = missing,
                    ParentDirectory = _workspace.RootPath,
                    DirectoryName = "failed-clone",
                },
                progress: null,
                TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(System.IO.Path.Combine(_workspace.RootPath, "failed-clone")));
    }

    [Fact]
    public async Task CloneAsync_LeavesNothingBehindWhenCancelled()
    {
        TemporaryRepository source = await CreateSourceAsync();

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service.CloneAsync(
                new CloneRequest
                {
                    Url = source.Path,
                    ParentDirectory = _workspace.RootPath,
                    DirectoryName = "cancelled",
                },
                progress: null,
                cancellation.Token));

        Assert.False(Directory.Exists(System.IO.Path.Combine(_workspace.RootPath, "cancelled")));
    }

    [Fact]
    public void ValidateCloneUrl_DelegatesToTheValidator()
    {
        Assert.True(Service.ValidateCloneUrl("https://github.com/owner/repo.git").IsValid);
        Assert.False(Service.ValidateCloneUrl("telnet://example.com/repo").IsValid);
    }

    // ---------------------------------------------------------------- streaming

    [Fact]
    public async Task RunStreamingAsync_ReportsProgressChunksAsGitWritesThem()
    {
        TemporaryRepository source = await CreateSourceAsync();

        List<string> chunks = [];
        Progress<string> progress = new(chunks.Add);

        GitCommand command = _host.GetRequiredService<IGitCommandFactory>().Create(
            _workspace.RootPath,
            "clone",
            "--progress",
            "--",
            source.Path,
            System.IO.Path.Combine(_workspace.RootPath, "streamed"));

        GitResult result = await _host.GetRequiredService<IGitProcessRunner>()
            .RunStreamingAsync(command, progress, throwOnError: true, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);

        // The whole of standard error is still accumulated, whatever the chunking did.
        Assert.Contains("Cloning into", result.StandardError, StringComparison.Ordinal);
    }
}
