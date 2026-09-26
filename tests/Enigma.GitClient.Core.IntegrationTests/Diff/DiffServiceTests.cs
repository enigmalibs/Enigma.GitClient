using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Diff;

/// <summary>
/// Drives the diff service against a repository built for the test, so what it reports is measured
/// against what git actually did.
/// </summary>
public sealed class DiffServiceTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _repository = null!;
    private RepositoryHandle _handle = null!;

    private string _rootSha = string.Empty;
    private string _changeSha = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _repository = await _workspace.InitRepositoryAsync("diffs");

        _repository.WriteFile("README.md", "# project\n");
        _repository.WriteFile("src/app.txt", "one\ntwo\nthree\n");
        _repository.WriteFile("src/old-name.txt", "line 1\nline 2\nline 3\nline 4\n");
        _repository.WriteFile("gone.txt", "delete me\n");
        _repository.WriteFile("a file with spaces.txt", "keep\n");
        _repository.WriteFile("rapport-écrit.txt", "accentué\n");
        await File.WriteAllBytesAsync(
            _repository.GetPath("blob.bin"),
            [0x00, 0x01, 0x02, 0x03],
            TestContext.Current.CancellationToken);

        _rootSha = await _repository.CommitAllAsync("Add the initial files");

        _repository.WriteFile("src/app.txt", "one\ntwo modified\nthree\nfour\n");
        await _repository.GitAsync("mv", "src/old-name.txt", "src/new-name.txt");
        _repository.WriteFile("src/new-name.txt", "line 1\nline 2 edited\nline 3\nline 4\n");
        _repository.DeleteFile("gone.txt");
        _repository.WriteFile("src/added.txt", "brand new\n");
        await File.WriteAllBytesAsync(
            _repository.GetPath("blob.bin"),
            [0x00, 0x01, 0x02, 0xFF],
            TestContext.Current.CancellationToken);

        _changeSha = await _repository.CommitAllAsync("Change several files at once");

        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_repository.Path, TestContext.Current.CancellationToken);

        _handle = discovery.Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private IDiffService Service => _host.GetRequiredService<IDiffService>();

    private Task<IReadOnlyList<ChangedFile>> FilesAsync(DiffTarget target)
        => Service.GetChangedFilesAsync(_handle, target, TestContext.Current.CancellationToken);

    // ---------------------------------------------------------------- changed files

    [Fact]
    public async Task GetChangedFilesAsync_ListsEveryKindOfChange()
    {
        IReadOnlyList<ChangedFile> files = await FilesAsync(DiffTarget.Commit(_changeSha));

        Assert.Equal(FileChangeKind.Modified, files.Single(file => file.Path == "src/app.txt").ChangeKind);
        Assert.Equal(FileChangeKind.Added, files.Single(file => file.Path == "src/added.txt").ChangeKind);
        Assert.Equal(FileChangeKind.Deleted, files.Single(file => file.Path == "gone.txt").ChangeKind);
        Assert.Equal(FileChangeKind.Renamed, files.Single(file => file.Path == "src/new-name.txt").ChangeKind);
        Assert.True(files.Single(file => file.Path == "blob.bin").IsBinary);
    }

    [Fact]
    public async Task GetChangedFilesAsync_ReportsTheRenameSource()
    {
        ChangedFile renamed = (await FilesAsync(DiffTarget.Commit(_changeSha)))
            .Single(file => file.Path == "src/new-name.txt");

        Assert.Equal("src/old-name.txt", renamed.OldPath);
        Assert.True(renamed.IsRenamed);
    }

    [Fact]
    public async Task GetChangedFilesAsync_ReportsLineCountsThatMatchGit()
    {
        ChangedFile app = (await FilesAsync(DiffTarget.Commit(_changeSha)))
            .Single(file => file.Path == "src/app.txt");

        Assert.Equal(2, app.AddedLines);
        Assert.Equal(1, app.RemovedLines);
        Assert.False(app.IsBinary);
    }

    [Fact]
    public async Task GetChangedFilesAsync_ListsAllFilesOfARootCommit()
    {
        IReadOnlyList<ChangedFile> files = await FilesAsync(DiffTarget.Commit(_rootSha));

        // Every file of the first commit is an addition; a diff against a parent that does not
        // exist would simply fail.
        Assert.All(files, file => Assert.Equal(FileChangeKind.Added, file.ChangeKind));
        Assert.Contains(files, file => file.Path == "README.md");
        Assert.Contains(files, file => file.Path == "a file with spaces.txt");
        Assert.Contains(files, file => file.Path == "rapport-écrit.txt");
    }

    [Fact]
    public async Task GetChangedFilesAsync_HandlesAPathWithSpacesAndNonAsciiCharacters()
    {
        _repository.WriteFile("a file with spaces.txt", "changed\n");
        _repository.WriteFile("rapport-écrit.txt", "accentué et modifié\n");
        string sha = await _repository.CommitAllAsync("Touch the awkward paths");

        IReadOnlyList<ChangedFile> files = await FilesAsync(DiffTarget.Commit(sha));

        Assert.Contains(files, file => file.Path == "a file with spaces.txt");
        Assert.Contains(files, file => file.Path == "rapport-écrit.txt");
    }

    [Fact]
    public async Task GetChangedFilesAsync_ReadsAMergeAgainstEitherParent()
    {
        await _repository.GitAsync("checkout", "-b", "side", _rootSha);
        _repository.WriteFile("src/side.txt", "side\n");
        await _repository.CommitAllAsync("Work on the side branch");

        await _repository.GitAsync("checkout", "main");
        await _repository.GitAsync("merge", "--no-ff", "side", "-m", "Merge the side branch");
        string mergeSha = await _repository.ResolveAsync("HEAD");

        IReadOnlyList<ChangedFile> againstFirst = await FilesAsync(DiffTarget.Commit(mergeSha));
        IReadOnlyList<ChangedFile> againstSecond = await FilesAsync(DiffTarget.Commit(mergeSha, parentIndex: 1));

        // Against the branch it was on, a merge brings in the side branch's file.
        Assert.Contains(againstFirst, file => file.Path == "src/side.txt");

        // Against the branch it merged, it brings in everything main had that side did not.
        Assert.Contains(againstSecond, file => file.Path == "src/app.txt");
        Assert.DoesNotContain(againstSecond, file => file.Path == "src/side.txt");
    }

    [Fact]
    public async Task GetChangedFilesAsync_ReadsARangeBetweenTwoRevisions()
    {
        IReadOnlyList<ChangedFile> files = await FilesAsync(DiffTarget.Range(_rootSha, _changeSha));

        Assert.Contains(files, file => file.Path == "src/added.txt");
        Assert.Contains(files, file => file.Path == "gone.txt");
    }

    [Fact]
    public async Task GetChangedFilesAsync_SeparatesStagedFromUnstagedWork()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo staged\nthree\nfour\n");
        await _repository.GitAsync("add", "src/app.txt");
        _repository.WriteFile("src/unstaged.txt", "not staged\n");
        await _repository.GitAsync("add", "--intent-to-add", "src/unstaged.txt");

        IReadOnlyList<ChangedFile> staged = await FilesAsync(DiffTarget.Staged());
        IReadOnlyList<ChangedFile> unstaged = await FilesAsync(DiffTarget.WorkingTree());
        IReadOnlyList<ChangedFile> everything = await FilesAsync(DiffTarget.Uncommitted());

        Assert.Contains(staged, file => file.Path == "src/app.txt");
        Assert.All(staged, file => Assert.Equal(FileStagingState.Staged, file.Staging));

        Assert.Contains(unstaged, file => file.Path == "src/unstaged.txt");
        Assert.All(unstaged, file => Assert.Equal(FileStagingState.Unstaged, file.Staging));

        Assert.Contains(everything, file => file.Path == "src/app.txt");
        Assert.Contains(everything, file => file.Path == "src/unstaged.txt");
    }

    [Fact]
    public async Task GetChangedFilesAsync_ReturnsNothingForAnUnchangedComparison()
        => Assert.Empty(await FilesAsync(DiffTarget.Range(_changeSha, _changeSha)));

    [Fact]
    public async Task GetChangedFilesAsync_HandlesAModeChange()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("File modes are not tracked the same way on Windows.");
            return;
        }

        // The mode has to change on disk: the fixture commits with "git add --all", which restages
        // README.md from the work tree and would undo a mode staged straight into the index.
        string readme = _repository.GetPath("README.md");
        File.SetUnixFileMode(
            readme,
            File.GetUnixFileMode(readme) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute);

        string sha = await _repository.CommitAllAsync("Make the readme executable");

        IReadOnlyList<ChangedFile> files = await FilesAsync(DiffTarget.Commit(sha));

        // git reports a pure mode change with the status letter M, which is what the panel shows.
        Assert.Equal(FileChangeKind.Modified, Assert.Single(files).ChangeKind);
    }

    // ---------------------------------------------------------------- patches

    [Fact]
    public async Task GetPatchAsync_ReadsTheDiffOfOneFile()
    {
        FilePatch? patch = await Service.GetPatchAsync(
            _handle,
            DiffTarget.Commit(_changeSha),
            "src/app.txt",
            options: null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(patch);
        Assert.Equal("src/app.txt", patch!.DisplayPath);
        Assert.Equal(2, patch.AddedLines);
        Assert.Equal(1, patch.RemovedLines);
        Assert.NotEmpty(patch.Hunks);
    }

    [Fact]
    public async Task GetPatchAsync_ReadsARenameFromItsChangedFileEntry()
    {
        ChangedFile renamed = (await FilesAsync(DiffTarget.Commit(_changeSha)))
            .Single(file => file.Path == "src/new-name.txt");

        FilePatch? patch = await Service.GetPatchAsync(
            _handle, DiffTarget.Commit(_changeSha), renamed, null, TestContext.Current.CancellationToken);

        Assert.NotNull(patch);
        Assert.Equal("src/old-name.txt", patch!.OldPath);
        Assert.Equal("src/new-name.txt", patch.NewPath);
        Assert.Equal(FileChangeKind.Renamed, patch.ChangeKind);

        // The edit that came with the rename is still in the patch, not just the move.
        Assert.NotEmpty(patch.Hunks);
    }

    [Fact]
    public async Task GetPatchAsync_ByTheNewPathAloneCannotSeeTheRename()
    {
        // git pairs a deletion with an addition to spot a rename, and it does that after the
        // pathspec has filtered the diff. Asking for the new path alone therefore leaves it looking
        // like a plain addition — which is why the panel passes the ChangedFile entry instead.
        FilePatch? patch = await Service.GetPatchAsync(
            _handle, DiffTarget.Commit(_changeSha), "src/new-name.txt", null, TestContext.Current.CancellationToken);

        Assert.NotNull(patch);
        Assert.Equal("src/new-name.txt", patch!.NewPath);
        Assert.Equal(FileChangeKind.Added, patch.ChangeKind);
    }

    [Fact]
    public async Task GetPatchAsync_ReportsABinaryFileWithoutHunks()
    {
        FilePatch? patch = await Service.GetPatchAsync(
            _handle, DiffTarget.Commit(_changeSha), "blob.bin", null, TestContext.Current.CancellationToken);

        Assert.NotNull(patch);
        Assert.True(patch!.IsBinary);
        Assert.Empty(patch.Hunks);
    }

    [Fact]
    public async Task GetPatchAsync_HonoursTheContextLineCount()
    {
        _repository.WriteFile("src/wide.txt", string.Join('\n', Enumerable.Range(1, 40).Select(n => $"line {n}")) + "\n");
        await _repository.CommitAllAsync("Add a wide file");

        _repository.WriteFile(
            "src/wide.txt",
            string.Join('\n', Enumerable.Range(1, 40).Select(n => n == 20 ? "line 20 changed" : $"line {n}")) + "\n");
        string sha = await _repository.CommitAllAsync("Change one line in the middle");

        FilePatch? narrow = await Service.GetPatchAsync(
            _handle, DiffTarget.Commit(sha), "src/wide.txt",
            new DiffOptions { ContextLines = 1 }, TestContext.Current.CancellationToken);

        FilePatch? wide = await Service.GetPatchAsync(
            _handle, DiffTarget.Commit(sha), "src/wide.txt",
            new DiffOptions { ContextLines = 10 }, TestContext.Current.CancellationToken);

        Assert.NotNull(narrow);
        Assert.NotNull(wide);
        Assert.True(
            wide!.Hunks[0].Lines.Count > narrow!.Hunks[0].Lines.Count,
            "more context must mean more lines");
    }

    [Fact]
    public async Task GetPatchAsync_CanIgnoreWhitespace()
    {
        _repository.WriteFile("src/spaces.txt", "value = 1\n");
        await _repository.CommitAllAsync("Add a file to reindent");

        _repository.WriteFile("src/spaces.txt", "    value   =   1\n");
        string sha = await _repository.CommitAllAsync("Reindent it");

        FilePatch? normal = await Service.GetPatchAsync(
            _handle, DiffTarget.Commit(sha), "src/spaces.txt", null, TestContext.Current.CancellationToken);

        FilePatch? ignoring = await Service.GetPatchAsync(
            _handle, DiffTarget.Commit(sha), "src/spaces.txt",
            new DiffOptions { IgnoreAllWhitespace = true }, TestContext.Current.CancellationToken);

        Assert.NotNull(normal);
        Assert.NotEmpty(normal!.Hunks);

        // With whitespace ignored there is nothing left to show.
        Assert.True(ignoring is null || ignoring.Hunks.Count == 0);
    }

    [Fact]
    public async Task GetPatchAsync_ReturnsNothingForAFileTheComparisonDoesNotTouch()
        => Assert.Null(await Service.GetPatchAsync(
            _handle, DiffTarget.Commit(_changeSha), "README.md", null, TestContext.Current.CancellationToken));

    [Fact]
    public async Task GetPatchAsync_ReadsTheWorkingTree()
    {
        _repository.WriteFile("src/app.txt", "one\ntwo modified\nthree\nfour\nfive uncommitted\n");

        FilePatch? patch = await Service.GetPatchAsync(
            _handle, DiffTarget.WorkingTree(), "src/app.txt", null, TestContext.Current.CancellationToken);

        Assert.NotNull(patch);
        Assert.Equal(1, patch!.AddedLines);
    }

    [Fact]
    public async Task GetPatchAsync_ComputesWordLevelSegments()
    {
        FilePatch? patch = await Service.GetPatchAsync(
            _handle, DiffTarget.Commit(_changeSha), "src/app.txt", null, TestContext.Current.CancellationToken);

        DiffLine added = patch!.Hunks
            .SelectMany(hunk => hunk.Lines)
            .First(line => line.Kind == DiffLineKind.Added && line.Text == "two modified");

        Assert.True(added.HasSegments);
        Assert.Contains(added.Segments, segment => segment.IsChanged);
    }
}
