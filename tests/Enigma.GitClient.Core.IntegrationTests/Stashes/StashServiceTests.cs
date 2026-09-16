using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Stashes;
using Enigma.GitClient.Core.Status;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Stashes;

/// <summary>
/// Drives the stash against a real repository. Everything here exists so that switching context
/// never costs work, so what is asserted is mostly that the work came back.
/// </summary>
public sealed class StashServiceTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _repository = null!;
    private RepositoryHandle _handle = null!;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _repository = await _workspace.InitRepositoryAsync("stashes");

        _repository.WriteFile("README.md", "# one\n");
        _repository.WriteFile("src/app.txt", "one\ntwo\n");
        await _repository.CommitAllAsync("Add the initial files");

        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_repository.Path, TestContext.Current.CancellationToken);

        _handle = discovery.Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private IStashService Stashes => _host.GetRequiredService<IStashService>();

    private IStatusService Status => _host.GetRequiredService<IStatusService>();

    private Task<IReadOnlyList<StashEntry>> ListAsync()
        => Stashes.ListAsync(_handle, TestContext.Current.CancellationToken);

    private Task<WorkingTreeStatus> StatusAsync()
        => Status.GetStatusAsync(_handle, false, TestContext.Current.CancellationToken);

    // ---------------------------------------------------------------- pushing

    [Fact]
    public async Task PushAsync_PutsTheWorkAsideAndLeavesACleanTree()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo edited\n");

        Assert.True(await Stashes.PushAsync(
            _handle,
            "half a thought",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.True((await StatusAsync()).IsClean);
        Assert.Equal("one\ntwo\n", File.ReadAllText(_repository.GetPath("src/app.txt")));

        StashEntry entry = Assert.Single(await ListAsync());

        Assert.Equal("half a thought", entry.Message);
        Assert.Equal("main", entry.Branch);
        Assert.Equal(7, entry.ShortSha.Length);
    }

    [Fact]
    public async Task PushAsync_TakesUntrackedFilesWithIt()
    {
        _repository.WriteFile("src/brand-new.txt", "never committed\n");

        Assert.True(await Stashes.PushAsync(_handle, cancellationToken: TestContext.Current.CancellationToken));

        // Without --include-untracked the file would still be sitting there, and "your work is
        // stashed" would be a half-truth.
        Assert.False(File.Exists(_repository.GetPath("src/brand-new.txt")));
        Assert.True((await StatusAsync()).IsClean);
    }

    [Fact]
    public async Task PushAsync_CanLeaveUntrackedFilesAlone()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo edited\n");
        _repository.WriteFile("src/brand-new.txt", "never committed\n");

        await Stashes.PushAsync(
            _handle,
            includeUntracked: false,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(File.Exists(_repository.GetPath("src/brand-new.txt")));
        Assert.Equal("one\ntwo\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
    }

    [Fact]
    public async Task PushAsync_CanKeepTheIndex()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo staged\n");
        await _repository.GitAsync("add", "src/app.txt");
        _repository.WriteFile("README.md", "# edited\n");

        await Stashes.PushAsync(
            _handle,
            keepIndex: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // What was staged stays staged, which is the point of --keep-index: stash the rest and
        // commit what you had ready.
        Assert.Equal("src/app.txt", Assert.Single((await StatusAsync()).Staged).Path);
    }

    [Fact]
    public async Task PushAsync_CanStashOnlySomePaths()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo edited\n");
        _repository.WriteFile("README.md", "# edited\n");

        await Stashes.PushAsync(
            _handle,
            "just the readme",
            paths: ["README.md"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("# one\n", File.ReadAllText(_repository.GetPath("README.md")));
        Assert.Equal("one\ntwo edited\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
    }

    [Fact]
    public async Task PushAsync_SaysSoWhenThereWasNothingToStash()
        => Assert.False(await Stashes.PushAsync(_handle, cancellationToken: TestContext.Current.CancellationToken));

    // ---------------------------------------------------------------- listing

    [Fact]
    public async Task ListAsync_ReportsTheEntriesNewestFirst()
    {
        _repository.WriteFile("src/app.txt", "first\n");
        await Stashes.PushAsync(_handle, "the first one", cancellationToken: TestContext.Current.CancellationToken);

        _repository.WriteFile("src/app.txt", "second\n");
        await Stashes.PushAsync(_handle, "the second one", cancellationToken: TestContext.Current.CancellationToken);

        IReadOnlyList<StashEntry> entries = await ListAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal("the second one", entries[0].Message);
        Assert.Equal("the first one", entries[1].Message);
        Assert.Equal([0, 1], entries.Select(entry => entry.Index));
    }

    [Fact]
    public async Task ListAsync_KeepsAMessageWithColonsInIt()
    {
        _repository.WriteFile("src/app.txt", "edited\n");

        await Stashes.PushAsync(
            _handle,
            "refactor: split the parser: properly",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("refactor: split the parser: properly", Assert.Single(await ListAsync()).Message);
    }

    [Fact]
    public async Task ListAsync_IsEmptyWhenNothingIsStashed()
        => Assert.Empty(await ListAsync());

    // ---------------------------------------------------------------- getting it back

    [Fact]
    public async Task ApplyAsync_BringsTheWorkBackAndKeepsTheEntry()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo edited\n");
        await Stashes.PushAsync(_handle, "work", cancellationToken: TestContext.Current.CancellationToken);

        await Stashes.ApplyAsync(_handle, 0, TestContext.Current.CancellationToken);

        Assert.Equal("one\ntwo edited\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
        Assert.Single(await ListAsync());
    }

    [Fact]
    public async Task PopAsync_BringsTheWorkBackAndRemovesTheEntry()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo edited\n");
        await Stashes.PushAsync(_handle, "work", cancellationToken: TestContext.Current.CancellationToken);

        await Stashes.PopAsync(_handle, 0, TestContext.Current.CancellationToken);

        Assert.Equal("one\ntwo edited\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task DropAsync_RemovesTheEntryItWasGivenAndNoOther()
    {
        _repository.WriteFile("src/app.txt", "first\n");
        await Stashes.PushAsync(_handle, "the first one", cancellationToken: TestContext.Current.CancellationToken);

        _repository.WriteFile("src/app.txt", "second\n");
        await Stashes.PushAsync(_handle, "the second one", cancellationToken: TestContext.Current.CancellationToken);

        // Index 1 is the older one; dropping by position is exactly where an off-by-one loses work.
        await Stashes.DropAsync(_handle, 1, TestContext.Current.CancellationToken);

        StashEntry remaining = Assert.Single(await ListAsync());

        Assert.Equal("the second one", remaining.Message);
    }

    [Fact]
    public async Task PopAsync_LeavesTheConflictVisibleWhenTheWorkNoLongerApplies()
    {
        _repository.WriteFile("src/app.txt", "one\nstashed version\n");
        await Stashes.PushAsync(_handle, "work", cancellationToken: TestContext.Current.CancellationToken);

        // The file moves on underneath the stash.
        _repository.WriteFile("src/app.txt", "one\ncommitted version\n");
        await _repository.CommitAllAsync("Change the same line");

        await Assert.ThrowsAsync<GitCommandException>(
            () => Stashes.PopAsync(_handle, 0, TestContext.Current.CancellationToken));

        WorkingTreeStatus status = await StatusAsync();

        Assert.True(status.HasConflicts);
        Assert.Equal("src/app.txt", Assert.Single(status.Conflicted).Path);

        // A failed pop keeps the entry: the work is still recoverable.
        Assert.Single(await ListAsync());
    }

    // ---------------------------------------------------------------- reading one

    [Fact]
    public async Task ShowAsync_ReturnsAPatchTheViewerCanRender()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo edited\n");
        await Stashes.PushAsync(_handle, "work", cancellationToken: TestContext.Current.CancellationToken);

        PatchSet patch = await Stashes.ShowAsync(
            _handle,
            0,
            cancellationToken: TestContext.Current.CancellationToken);

        FilePatch file = Assert.Single(patch.Files);

        Assert.Equal("src/app.txt", file.DisplayPath);
        Assert.NotEmpty(file.Hunks);
        Assert.Equal(1, file.AddedLines);
        Assert.Equal(1, file.RemovedLines);
    }

    [Fact]
    public async Task ShowAsync_IncludesTheUntrackedFilesTheEntryTook()
    {
        _repository.WriteFile("src/brand-new.txt", "never committed\n");
        await Stashes.PushAsync(_handle, "work", cancellationToken: TestContext.Current.CancellationToken);

        PatchSet patch = await Stashes.ShowAsync(
            _handle,
            0,
            cancellationToken: TestContext.Current.CancellationToken);

        // The untracked files live in a third parent of the stash commit; without asking for them
        // the entry looks empty, which is the opposite of the truth.
        Assert.Contains(patch.Files, file => file.DisplayPath == "src/brand-new.txt");
    }

    // ---------------------------------------------------------------- branching out

    [Fact]
    public async Task BranchAsync_MakesABranchWithTheWorkOnIt()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo edited\n");
        await Stashes.PushAsync(_handle, "work", cancellationToken: TestContext.Current.CancellationToken);

        await Stashes.BranchAsync(_handle, 0, "rescued", TestContext.Current.CancellationToken);

        RefCollection refs = await _host.GetRequiredService<IRefReader>()
            .GetRefsAsync(_handle, TestContext.Current.CancellationToken);

        Assert.Contains(refs.LocalBranches, branch => branch.ShortName == "rescued");
        Assert.Equal("one\ntwo edited\n", File.ReadAllText(_repository.GetPath("src/app.txt")));

        // git drops the entry once it has been applied cleanly on the new branch.
        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task BranchAsync_RefusesANameGitWouldNot()
    {
        _repository.WriteFile("src/app.txt", "edited\n");
        await Stashes.PushAsync(_handle, "work", cancellationToken: TestContext.Current.CancellationToken);

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Stashes.BranchAsync(_handle, 0, "bad..name", TestContext.Current.CancellationToken));

        Assert.Contains("\"..\"", refusal.Message, StringComparison.Ordinal);
        Assert.Single(await ListAsync());
    }

    // ---------------------------------------------------------------- the graph

    [Fact]
    public async Task TheStashAppearsAmongTheRepositorysReferences()
    {
        _repository.WriteFile("src/app.txt", "edited\n");
        await Stashes.PushAsync(_handle, "work", cancellationToken: TestContext.Current.CancellationToken);

        RefCollection refs = await _host.GetRequiredService<IRefReader>()
            .GetRefsAsync(_handle, TestContext.Current.CancellationToken);

        Assert.NotNull(refs.Stash);
        Assert.Equal(GitRefKind.Stash, refs.Stash!.Kind);
    }
}
