using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Diff;

/// <summary>
/// The unit tests parse payloads captured from git. These run the parser against a **live** git, so
/// the captured payloads cannot silently drift away from what the installed git actually emits.
/// </summary>
public sealed class UnifiedDiffParserAgainstRealGitTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private TemporaryRepository _repository = null!;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _repository = await _workspace.InitRepositoryAsync();

        _repository.WriteFile("plain.txt", "one\ntwo\nthree\n");
        _repository.WriteFile("a file with spaces.txt", "keep\n");
        _repository.WriteFile("old-name.txt", "renamed line 1\nline 2\nline 3\nline 4\n");
        _repository.WriteFile("gone.txt", "delete me\n");
        _repository.WriteFile("rapport-écrit.txt", "accentué\n");
        await File.WriteAllBytesAsync(
            _repository.GetPath("blob.bin"),
            [0x00, 0x01, 0x02, 0x03],
            TestContext.Current.CancellationToken);
        await _repository.CommitAllAsync("base");

        _repository.WriteFile("plain.txt", "one\ntwo modified\nthree\nfour\n");
        await _repository.GitAsync("mv", "old-name.txt", "new-name.txt");
        _repository.WriteFile("new-name.txt", "renamed line 1\nline 2 edited\nline 3\nline 4\n");
        _repository.DeleteFile("gone.txt");
        _repository.WriteFile("added.txt", "added file\nsecond line");
        _repository.WriteFile("rapport-écrit.txt", "accentué et modifié\n");
        await File.WriteAllBytesAsync(
            _repository.GetPath("blob.bin"),
            [0x00, 0x01, 0x02, 0xFF],
            TestContext.Current.CancellationToken);
        await _repository.GitAsync("add", "--all");
    }

    public async ValueTask DisposeAsync() => await _workspace.DisposeAsync();

    private async Task<PatchSet> ReadStagedPatchAsync()
    {
        string patch = await _repository.GitAsync(
            "-c",
            "core.quotePath=false",
            "diff",
            "--cached",
            "--find-renames",
            "--find-copies");

        return UnifiedDiffParser.Parse(patch);
    }

    [Fact]
    public async Task Parse_ReadsALivePatchFromGit()
    {
        PatchSet patch = await ReadStagedPatchAsync();

        Assert.Contains(patch.Files, file => file.DisplayPath == "plain.txt");
        Assert.Contains(patch.Files, file => file.DisplayPath == "added.txt");
        Assert.Contains(patch.Files, file => file.DisplayPath == "gone.txt");
        Assert.Contains(patch.Files, file => file.DisplayPath == "new-name.txt");
        Assert.Contains(patch.Files, file => file.DisplayPath == "blob.bin");
        Assert.Contains(patch.Files, file => file.DisplayPath == "rapport-écrit.txt");
    }

    [Fact]
    public async Task Parse_ClassifiesEveryChangeKindTheSameWayGitDoes()
    {
        PatchSet patch = await ReadStagedPatchAsync();

        Assert.Equal(FileChangeKind.Modified, Find(patch, "plain.txt").ChangeKind);
        Assert.Equal(FileChangeKind.Added, Find(patch, "added.txt").ChangeKind);
        Assert.Equal(FileChangeKind.Deleted, Find(patch, "gone.txt").ChangeKind);
        Assert.Equal(FileChangeKind.Renamed, Find(patch, "new-name.txt").ChangeKind);
        Assert.True(Find(patch, "blob.bin").IsBinary);
    }

    [Fact]
    public async Task Parse_ReadsTheRenameSourceFromLiveOutput()
    {
        FilePatch renamed = Find(await ReadStagedPatchAsync(), "new-name.txt");

        Assert.Equal("old-name.txt", renamed.OldPath);
        Assert.Equal("new-name.txt", renamed.NewPath);
        Assert.NotNull(renamed.SimilarityIndex);
    }

    [Fact]
    public async Task Parse_ReadsNonAsciiPathsAndContentFromLiveOutput()
    {
        FilePatch file = Find(await ReadStagedPatchAsync(), "rapport-écrit.txt");
        DiffHunk hunk = Assert.Single(file.Hunks);

        Assert.Equal("accentué", hunk.Lines.Single(line => line.Kind == DiffLineKind.Removed).Text);
        Assert.Equal("accentué et modifié", hunk.Lines.Single(line => line.Kind == DiffLineKind.Added).Text);
    }

    [Fact]
    public async Task Parse_ReadsTheMissingTrailingNewlineMarkerFromLiveOutput()
    {
        FilePatch file = Find(await ReadStagedPatchAsync(), "added.txt");

        Assert.Contains(
            file.Hunks.SelectMany(hunk => hunk.Lines),
            line => line.Kind == DiffLineKind.NoNewline);
    }

    [Fact]
    public async Task Parse_LineCountsMatchWhatGitReportsInNumstat()
    {
        PatchSet patch = await ReadStagedPatchAsync();

        // -z is the only unambiguous form: a rename is written as an empty path field followed by
        // two separate records for the old and the new path, instead of the "dir/{old => new}"
        // shorthand the human-readable output uses.
        string numstat = await _repository.GitAsync("diff", "--cached", "--numstat", "-z", "--find-renames");
        string[] records = numstat.Split('\0');

        int index = 0;
        int compared = 0;

        while (index < records.Length)
        {
            string record = records[index];
            index++;

            if (record.Length == 0)
            {
                continue;
            }

            string[] fields = record.Split('\t');
            if (fields.Length < 3)
            {
                continue;
            }

            string path = fields[2];
            if (path.Length == 0 && index + 1 < records.Length)
            {
                // A rename: the next two records are the old and the new path.
                index++;
                path = records[index];
                index++;
            }

            if (fields[0] == "-")
            {
                // git writes "-" for a binary file, which has no line counts to compare.
                continue;
            }

            int added = int.Parse(fields[0], System.Globalization.CultureInfo.InvariantCulture);
            int removed = int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);

            FilePatch file = patch.Files.Single(candidate =>
                candidate.DisplayPath == path || candidate.OldPath == path);

            Assert.Equal(added, file.AddedLines);
            Assert.Equal(removed, file.RemovedLines);
            compared++;
        }

        // Guards the loop itself: a parsing slip that skipped every record would otherwise pass.
        Assert.True(compared >= 4, $"expected at least four comparable files, compared {compared}");
    }

    [Fact]
    public async Task Parse_ReadsAPureModeChangeFromLiveOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("File modes are not tracked the same way on Windows.");
            return;
        }

        await _repository.GitAsync("update-index", "--chmod=+x", "a file with spaces.txt");

        FilePatch file = Find(await ReadStagedPatchAsync(), "a file with spaces.txt");

        Assert.Equal(FileChangeKind.ModeChanged, file.ChangeKind);
        Assert.Equal("100644", file.OldMode);
        Assert.Equal("100755", file.NewMode);
        Assert.Empty(file.Hunks);
    }

    [Fact]
    public async Task Parse_ReadsACombinedMergeDiffFromLiveOutput()
    {
        await _repository.CommitAllAsync("first change");

        await _repository.GitAsync("checkout", "-b", "side", "HEAD~1");
        _repository.WriteFile("plain.txt", "ONE\ntwo\nthree\n");
        await _repository.CommitAllAsync("side change");

        await _repository.GitAsync("checkout", "main");

        // The merge conflicts on purpose, then is resolved, so git has a real combined diff to show.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.GitAsync("merge", "side"));
        _repository.WriteFile("plain.txt", "ONE\ntwo modified\nthree\nfour\n");
        await _repository.GitAsync("add", "plain.txt");
        await _repository.GitAsync("commit", "--no-edit");

        string combined = await _repository.GitAsync("show", "--format=", "-p", "-c", "HEAD");
        PatchSet patch = UnifiedDiffParser.Parse(combined);

        FilePatch file = Assert.Single(patch.Files);
        Assert.True(file.IsCombined);
        Assert.Equal("plain.txt", file.DisplayPath);
        Assert.NotEmpty(file.Hunks);
    }

    [Fact]
    public async Task Parse_ReadsASectionHeadingFromLiveOutput()
    {
        _repository.WriteFile(
            "code.cs",
            "public class Example\n{\n    public void First()\n    {\n        int a = 1;\n        int b = 2;\n"
            + "        int c = 3;\n        int d = 4;\n        int e = 5;\n        int f = 6;\n    }\n}\n");
        await _repository.CommitAllAsync("add code");

        _repository.WriteFile(
            "code.cs",
            "public class Example\n{\n    public void First()\n    {\n        int a = 1;\n        int b = 2;\n"
            + "        int c = 3;\n        int d = 4;\n        int e = 55;\n        int f = 6;\n    }\n}\n");
        await _repository.GitAsync("add", "code.cs");

        FilePatch file = Find(await ReadStagedPatchAsync(), "code.cs");
        DiffHunk hunk = Assert.Single(file.Hunks);

        Assert.Equal("public class Example", hunk.SectionHeading);
    }

    [Fact]
    public async Task Parse_ComputesAUsefulWordDiffOnLiveOutput()
    {
        FilePatch file = Find(await ReadStagedPatchAsync(), "plain.txt");
        DiffHunk hunk = Assert.Single(file.Hunks);

        DiffLine added = hunk.Lines.First(line => line.Kind == DiffLineKind.Added && line.Text == "two modified");

        Assert.True(added.HasSegments);
        Assert.Contains(added.Segments, segment => segment.IsChanged);

        // The unchanged word must not be highlighted.
        DiffSegment unchanged = added.Segments.First(segment => !segment.IsChanged);
        Assert.Equal("two", added.Text.Substring(unchanged.Start, unchanged.Length));
    }

    private static FilePatch Find(PatchSet patch, string displayPath)
        => patch.Files.Single(file => file.DisplayPath == displayPath);
}
