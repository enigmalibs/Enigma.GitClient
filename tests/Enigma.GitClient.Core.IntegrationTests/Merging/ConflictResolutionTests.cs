using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Merging;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Status;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Merging;

/// <summary>
/// Resolves real conflicts in a real repository, through the same methods the UI will call.
/// </summary>
public sealed class ConflictResolutionTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _repository = null!;
    private RepositoryHandle _handle = null!;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _repository = await _workspace.InitRepositoryAsync("conflicts");

        _repository.WriteFile("src/app.txt", "one\ntwo\nthree\n");
        await _repository.CommitAllAsync("Add the file");

        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_repository.Path, TestContext.Current.CancellationToken);

        _handle = discovery.Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private IConflictService Conflicts => _host.GetRequiredService<IConflictService>();

    private IMergeService Merges => _host.GetRequiredService<IMergeService>();

    private Task<WorkingTreeStatus> StatusAsync()
        => _host.GetRequiredService<IStatusService>()
            .GetStatusAsync(_handle, false, TestContext.Current.CancellationToken);

    /// <summary>
    /// Leaves the repository mid-merge with one conflicted text file.
    /// </summary>
    private async Task ConflictAsync(string ours = "one\nour version\nthree\n", string theirs = "one\ntheir version\nthree\n")
    {
        await _repository.GitAsync("checkout", "-b", "theirs");
        _repository.WriteFile("src/app.txt", theirs);
        await _repository.CommitAllAsync("Their version");

        await _repository.GitAsync("checkout", "main");
        _repository.WriteFile("src/app.txt", ours);
        await _repository.CommitAllAsync("Our version");

        await Merges.MergeAsync(
            _handle,
            new MergeRequest { Source = "theirs" },
            TestContext.Current.CancellationToken);
    }

    // ---------------------------------------------------------------- building the document

    [Fact]
    public async Task GetDocumentAsync_BuildsTheRegionsFromGitsOwnMerge()
    {
        await ConflictAsync();

        ConflictDocument? document = await Conflicts.GetDocumentAsync(
            _handle,
            "src/app.txt",
            TestContext.Current.CancellationToken);

        Assert.NotNull(document);
        Assert.Equal(1, document!.ConflictCount);

        ConflictRegion region = document.Conflicts.Single();

        Assert.Equal(["our version\n"], region.OurLines);
        Assert.Equal(["two\n"], region.BaseLines);
        Assert.Equal(["their version\n"], region.TheirLines);
        Assert.False(document.IsFullyResolved);
    }

    [Fact]
    public async Task GetDocumentAsync_KeepsTheUnchangedTextAroundTheConflict()
    {
        await ConflictAsync();

        ConflictDocument document = (await Conflicts.GetDocumentAsync(
            _handle,
            "src/app.txt",
            TestContext.Current.CancellationToken))!;

        document.ResolveAll(ConflictResolution.Ours);

        // Exactly our side of the file, which is the strongest thing to be able to say about it.
        Assert.Equal("one\nour version\nthree\n", document.RenderPreview());
    }

    [Fact]
    public async Task GetDocumentAsync_SaysThereIsNothingToMergeForABinaryFile()
    {
        byte[] ours = [0x00, 0x01, 0x02];
        byte[] theirs = [0x00, 0x09, 0x08];

        await _repository.GitAsync("checkout", "-b", "theirs");
        await File.WriteAllBytesAsync(_repository.GetPath("blob.bin"), theirs, TestContext.Current.CancellationToken);
        await _repository.CommitAllAsync("Their blob");

        await _repository.GitAsync("checkout", "main");
        await File.WriteAllBytesAsync(_repository.GetPath("blob.bin"), ours, TestContext.Current.CancellationToken);
        await _repository.CommitAllAsync("Our blob");

        await Merges.MergeAsync(
            _handle,
            new MergeRequest { Source = "theirs" },
            TestContext.Current.CancellationToken);

        Assert.Null(await Conflicts.GetDocumentAsync(_handle, "blob.bin", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetDocumentAsync_SaysThereIsNothingToMergeWhenASideIsGone()
    {
        await _repository.GitAsync("checkout", "-b", "theirs");
        _repository.DeleteFile("src/app.txt");
        await _repository.CommitAllAsync("They deleted it");

        await _repository.GitAsync("checkout", "main");
        _repository.WriteFile("src/app.txt", "one\nchanged by us\nthree\n");
        await _repository.CommitAllAsync("We changed it");

        await Merges.MergeAsync(
            _handle,
            new MergeRequest { Source = "theirs" },
            TestContext.Current.CancellationToken);

        // A delete against a change is a choice between keeping the file and not having it.
        Assert.Null(await Conflicts.GetDocumentAsync(_handle, "src/app.txt", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetDocumentAsync_KeepsWindowsLineEndingsThroughTheWholeRoundTrip()
    {
        await ConflictAsync("one\r\nour version\r\nthree\r\n", "one\r\ntheir version\r\nthree\r\n");

        ConflictDocument document = (await Conflicts.GetDocumentAsync(
            _handle,
            "src/app.txt",
            TestContext.Current.CancellationToken))!;

        document.ResolveAll(ConflictResolution.Ours);

        Assert.Equal("\r\n", document.LineEnding);
        Assert.Equal("one\r\nour version\r\nthree\r\n", document.RenderPreview());
    }

    // ---------------------------------------------------------------- resolving

    [Fact]
    public async Task ResolveAsync_WritesThePreviewAndStagesIt()
    {
        await ConflictAsync();

        ConflictDocument document = (await Conflicts.GetDocumentAsync(
            _handle,
            "src/app.txt",
            TestContext.Current.CancellationToken))!;

        document.ResolveAll(ConflictResolution.OursThenTheirs);

        string preview = document.RenderPreview();

        await Conflicts.ResolveAsync(_handle, "src/app.txt", preview, TestContext.Current.CancellationToken);

        // What reached disk is what the preview said, byte for byte.
        Assert.Equal(preview, File.ReadAllText(_repository.GetPath("src/app.txt")));

        WorkingTreeStatus status = await StatusAsync();

        Assert.False(status.HasConflicts);
        Assert.Contains(status.Staged, file => file.Path == "src/app.txt");
    }

    [Fact]
    public async Task ResolveAsync_ThenContinueRecordsTheMergeWithBothParents()
    {
        await ConflictAsync();

        string ours = await _repository.ResolveAsync("HEAD");
        string theirs = await _repository.ResolveAsync("theirs");

        ConflictDocument document = (await Conflicts.GetDocumentAsync(
            _handle,
            "src/app.txt",
            TestContext.Current.CancellationToken))!;

        document.ResolveAll(ConflictResolution.Theirs);

        await Conflicts.ResolveAsync(
            _handle,
            "src/app.txt",
            document.RenderPreview(),
            TestContext.Current.CancellationToken);

        await Merges.ContinueAsync(_handle, "Resolve it", TestContext.Current.CancellationToken);

        string[] parents = (await _repository.GitLinesAsync("rev-list", "--parents", "-1", "HEAD"))[0].Split(' ')[1..];

        Assert.Equal(2, parents.Length);
        Assert.Contains(ours, parents);
        Assert.Contains(theirs, parents);

        // The tree holds what was chosen, not what either side had before.
        Assert.Equal("one\ntheir version\nthree\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
    }

    [Fact]
    public async Task ResolveWithAsync_TakesOneWholeSideOfATextConflict()
    {
        await ConflictAsync();

        await Conflicts.ResolveWithAsync(
            _handle,
            "src/app.txt",
            ConflictSide.Theirs,
            TestContext.Current.CancellationToken);

        Assert.Equal("one\ntheir version\nthree\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
        Assert.False((await StatusAsync()).HasConflicts);
    }

    [Fact]
    public async Task ResolveWithAsync_TakesOneWholeSideOfABinaryConflict()
    {
        byte[] ours = [0x00, 0x01, 0x02];
        byte[] theirs = [0x00, 0x09, 0x08];

        await _repository.GitAsync("checkout", "-b", "theirs");
        await File.WriteAllBytesAsync(_repository.GetPath("blob.bin"), theirs, TestContext.Current.CancellationToken);
        await _repository.CommitAllAsync("Their blob");

        await _repository.GitAsync("checkout", "main");
        await File.WriteAllBytesAsync(_repository.GetPath("blob.bin"), ours, TestContext.Current.CancellationToken);
        await _repository.CommitAllAsync("Our blob");

        await Merges.MergeAsync(
            _handle,
            new MergeRequest { Source = "theirs" },
            TestContext.Current.CancellationToken);

        await Conflicts.ResolveWithAsync(_handle, "blob.bin", ConflictSide.Ours, TestContext.Current.CancellationToken);

        Assert.Equal(ours, await File.ReadAllBytesAsync(_repository.GetPath("blob.bin"), TestContext.Current.CancellationToken));
        Assert.False((await StatusAsync()).HasConflicts);
    }

    [Fact]
    public async Task ResolveWithAsync_RemovesTheFileWhenTakingASideThatDeletedIt()
    {
        await _repository.GitAsync("checkout", "-b", "theirs");
        _repository.DeleteFile("src/app.txt");
        await _repository.CommitAllAsync("They deleted it");

        await _repository.GitAsync("checkout", "main");
        _repository.WriteFile("src/app.txt", "one\nchanged by us\nthree\n");
        await _repository.CommitAllAsync("We changed it");

        await Merges.MergeAsync(
            _handle,
            new MergeRequest { Source = "theirs" },
            TestContext.Current.CancellationToken);

        await Conflicts.ResolveWithAsync(
            _handle,
            "src/app.txt",
            ConflictSide.Theirs,
            TestContext.Current.CancellationToken);

        // Keeping "their deletion" means the file is gone, which is the honest reading of it.
        Assert.False(File.Exists(_repository.GetPath("src/app.txt")));
        Assert.False((await StatusAsync()).HasConflicts);
    }

    [Fact]
    public async Task MarkResolvedAsync_StagesAFileTheUserFixedByHand()
    {
        await ConflictAsync();

        _repository.WriteFile("src/app.txt", "one\nfixed by hand\nthree\n");

        await Conflicts.MarkResolvedAsync(_handle, "src/app.txt", TestContext.Current.CancellationToken);

        WorkingTreeStatus status = await StatusAsync();

        Assert.False(status.HasConflicts);
        Assert.Contains(status.Staged, file => file.Path == "src/app.txt");
    }

    [Fact]
    public async Task ResolveAllWithAsync_TakesTheSameSideOfEveryConflictAtOnce()
    {
        byte[] ours = [0x00, 0x01, 0x02];
        byte[] theirs = [0x00, 0x09, 0x08];

        await _repository.GitAsync("checkout", "-b", "theirs");
        _repository.WriteFile("src/app.txt", "one\ntheir version\nthree\n");
        _repository.WriteFile("src/other.txt", "theirs\n");
        await File.WriteAllBytesAsync(_repository.GetPath("blob.bin"), theirs, TestContext.Current.CancellationToken);
        await _repository.CommitAllAsync("Their version");

        await _repository.GitAsync("checkout", "main");
        _repository.WriteFile("src/app.txt", "one\nour version\nthree\n");
        _repository.WriteFile("src/other.txt", "ours\n");
        await File.WriteAllBytesAsync(_repository.GetPath("blob.bin"), ours, TestContext.Current.CancellationToken);
        await _repository.CommitAllAsync("Our version");

        await Merges.MergeAsync(
            _handle,
            new MergeRequest { Source = "theirs" },
            TestContext.Current.CancellationToken);

        IReadOnlyList<string> resolved = await Conflicts.ResolveAllWithAsync(
            _handle,
            ConflictSide.Ours,
            TestContext.Current.CancellationToken);

        // Text and binary conflicts alike: one call clears the whole list.
        Assert.Equal(3, resolved.Count);
        Assert.Contains("src/app.txt", resolved);
        Assert.Contains("blob.bin", resolved);

        Assert.Empty(await Conflicts.GetConflictsAsync(_handle, TestContext.Current.CancellationToken));
        Assert.Equal("one\nour version\nthree\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
        Assert.Equal("ours\n", File.ReadAllText(_repository.GetPath("src/other.txt")));
        Assert.Equal(ours, await File.ReadAllBytesAsync(_repository.GetPath("blob.bin"), TestContext.Current.CancellationToken));

        await Merges.ContinueAsync(_handle, null, TestContext.Current.CancellationToken);

        Assert.False(await Merges.IsMergeInProgressAsync(_handle, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolveAllWithAsync_TakesTheirSideOfEveryConflict()
    {
        await ConflictAsync();

        await Conflicts.ResolveAllWithAsync(_handle, ConflictSide.Theirs, TestContext.Current.CancellationToken);

        Assert.Equal("one\ntheir version\nthree\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
        Assert.False((await StatusAsync()).HasConflicts);
    }

    [Fact]
    public async Task ResolveAllWithAsync_HasNothingToDoWhenThereIsNoMerge()
    {
        Assert.Empty(await Conflicts.ResolveAllWithAsync(
            _handle,
            ConflictSide.Ours,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolvingEveryFileIsWhatLetsTheMergeBeCommitted()
    {
        await ConflictAsync();

        IReadOnlyList<ConflictFile> files = await Conflicts.GetConflictsAsync(
            _handle,
            TestContext.Current.CancellationToken);

        Assert.Single(files);

        foreach (ConflictFile file in files)
        {
            await Conflicts.ResolveWithAsync(_handle, file.Path, ConflictSide.Ours, TestContext.Current.CancellationToken);
        }

        Assert.Empty(await Conflicts.GetConflictsAsync(_handle, TestContext.Current.CancellationToken));

        string sha = await Merges.ContinueAsync(_handle, null, TestContext.Current.CancellationToken);

        Assert.Equal(await _repository.ResolveAsync("HEAD"), sha);
    }
}
