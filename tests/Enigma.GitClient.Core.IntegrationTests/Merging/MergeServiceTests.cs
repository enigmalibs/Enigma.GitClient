using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Merging;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Status;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Merging;

/// <summary>
/// Drives merging and the conflict model against a real repository.
/// </summary>
/// <remarks>
/// Rebase is forbidden product-wide, which makes merge the integration operation this client has.
/// Every outcome it can report is produced here by a real repository rather than asserted from a
/// captured string, because the thing worth pinning is that the classification matches what git
/// actually did.
/// </remarks>
public sealed class MergeServiceTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _repository = null!;
    private RepositoryHandle _handle = null!;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _repository = await _workspace.InitRepositoryAsync("merges");

        _repository.WriteFile("README.md", "# one\n");
        _repository.WriteFile("src/app.txt", "one\ntwo\nthree\n");
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

    private IMergeService Merges => _host.GetRequiredService<IMergeService>();

    private IConflictService Conflicts => _host.GetRequiredService<IConflictService>();

    private Task<WorkingTreeStatus> StatusAsync()
        => _host.GetRequiredService<IStatusService>()
            .GetStatusAsync(_handle, false, TestContext.Current.CancellationToken);

    private Task<MergeOutcome> MergeAsync(string source, FastForwardMode mode = FastForwardMode.WhenPossible)
        => Merges.MergeAsync(
            _handle,
            new MergeRequest { Source = source, FastForward = mode },
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Builds a branch that is simply ahead of main.
    /// </summary>
    private async Task BuildAheadBranchAsync()
    {
        await _repository.GitAsync("checkout", "-b", "ahead");
        await _repository.CommitFileAsync("src/new.txt", "added on the branch\n", "Work on the branch");
        await _repository.GitAsync("checkout", "main");
    }

    /// <summary>
    /// Builds two branches that changed different files, so they merge cleanly.
    /// </summary>
    private async Task BuildDivergedBranchAsync()
    {
        await _repository.GitAsync("checkout", "-b", "theirs");
        await _repository.CommitFileAsync("src/theirs.txt", "from them\n", "Their work");
        await _repository.GitAsync("checkout", "main");
        await _repository.CommitFileAsync("src/ours.txt", "from us\n", "Our work");
    }

    /// <summary>
    /// Builds two branches that changed the same line of the same file.
    /// </summary>
    private async Task BuildConflictingBranchAsync()
    {
        await _repository.GitAsync("checkout", "-b", "theirs");
        _repository.WriteFile("src/app.txt", "one\ntheir version\nthree\n");
        await _repository.CommitAllAsync("Their version");

        await _repository.GitAsync("checkout", "main");
        _repository.WriteFile("src/app.txt", "one\nour version\nthree\n");
        await _repository.CommitAllAsync("Our version");
    }

    // ---------------------------------------------------------------- outcomes

    [Fact]
    public async Task MergeAsync_ReportsAlreadyUpToDate()
    {
        await _repository.GitAsync("branch", "behind", "HEAD");

        MergeOutcome outcome = await MergeAsync("behind");

        Assert.Equal(MergeResultKind.AlreadyUpToDate, outcome.Kind);
        Assert.False(outcome.ChangedAnything);
    }

    [Fact]
    public async Task MergeAsync_FastForwardsWhenItCan()
    {
        await BuildAheadBranchAsync();

        MergeOutcome outcome = await MergeAsync("ahead");

        Assert.Equal(MergeResultKind.FastForward, outcome.Kind);
        Assert.True(outcome.ChangedAnything);

        // A fast-forward records nothing of its own, so the history is still one line.
        Assert.Equal("Work on the branch", await _repository.GitLineAsync("log", "-1", "--format=%s"));
        Assert.Single((await _repository.GitLinesAsync("rev-list", "--parents", "-1", "HEAD"))[0].Split(' ')[1..]);
    }

    [Fact]
    public async Task MergeAsync_CanBeMadeToRecordAMergeCommitAnyway()
    {
        await BuildAheadBranchAsync();

        MergeOutcome outcome = await MergeAsync("ahead", FastForwardMode.Never);

        Assert.Equal(MergeResultKind.Merged, outcome.Kind);
        Assert.Equal(2, (await _repository.GitLinesAsync("rev-list", "--parents", "-1", "HEAD"))[0].Split(' ')[1..].Length);
    }

    [Fact]
    public async Task MergeAsync_RecordsAMergeWhenBothSidesMoved()
    {
        await BuildDivergedBranchAsync();

        MergeOutcome outcome = await MergeAsync("theirs");

        Assert.Equal(MergeResultKind.Merged, outcome.Kind);
        Assert.True(File.Exists(_repository.GetPath("src/theirs.txt")));
        Assert.True(File.Exists(_repository.GetPath("src/ours.txt")));
    }

    [Fact]
    public async Task MergeAsync_CanCarryItsOwnMessage()
    {
        await BuildDivergedBranchAsync();

        await Merges.MergeAsync(
            _handle,
            new MergeRequest { Source = "theirs", Message = "Bring the two halves together" },
            TestContext.Current.CancellationToken);

        Assert.Equal("Bring the two halves together", await _repository.GitLineAsync("log", "-1", "--format=%s"));
    }

    [Fact]
    public async Task MergeAsync_RefusesAnythingButAFastForwardWhenAsked()
    {
        await BuildDivergedBranchAsync();

        MergeOutcome outcome = await MergeAsync("theirs", FastForwardMode.Only);

        Assert.Equal(MergeResultKind.Failed, outcome.Kind);
        Assert.Contains("diverged", outcome.Message, StringComparison.Ordinal);
        Assert.False(await Merges.IsMergeInProgressAsync(_handle, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MergeAsync_CanStageTheChangesWithoutRecordingAMerge()
    {
        await BuildDivergedBranchAsync();

        MergeOutcome outcome = await Merges.MergeAsync(
            _handle,
            new MergeRequest { Source = "theirs", Squash = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(MergeResultKind.Squashed, outcome.Kind);
        Assert.Contains((await StatusAsync()).Staged, file => file.Path == "src/theirs.txt");

        // A squash has no second parent — that is the entire difference.
        Assert.Single((await _repository.GitLinesAsync("rev-list", "--parents", "-1", "HEAD"))[0].Split(' ')[1..]);
    }

    [Fact]
    public async Task MergeAsync_CanStopBeforeCommitting()
    {
        await BuildDivergedBranchAsync();

        MergeOutcome outcome = await Merges.MergeAsync(
            _handle,
            new MergeRequest { Source = "theirs", NoCommit = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(MergeResultKind.Stopped, outcome.Kind);
        Assert.True(await Merges.IsMergeInProgressAsync(_handle, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MergeAsync_ReportsWhenUncommittedWorkIsInTheWay()
    {
        await BuildDivergedBranchAsync();
        _repository.WriteFile("src/theirs.txt", "in the way\n");

        MergeOutcome outcome = await MergeAsync("theirs");

        Assert.Equal(MergeResultKind.Failed, outcome.Kind);
        Assert.Contains("stash", outcome.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- conflicts

    [Fact]
    public async Task MergeAsync_ReportsAConflictWithTheFilesToResolve()
    {
        await BuildConflictingBranchAsync();

        MergeOutcome outcome = await MergeAsync("theirs");

        Assert.Equal(MergeResultKind.Conflicted, outcome.Kind);
        Assert.True(outcome.NeedsResolution);
        Assert.Equal(["src/app.txt"], outcome.ConflictedPaths);
        Assert.True(await Merges.IsMergeInProgressAsync(_handle, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AbortAsync_PutsEverythingBackExactlyAsItWas()
    {
        await BuildConflictingBranchAsync();

        string before = await _repository.ResolveAsync("HEAD");
        string content = File.ReadAllText(_repository.GetPath("src/app.txt"));

        await MergeAsync("theirs");
        await Merges.AbortAsync(_handle, TestContext.Current.CancellationToken);

        Assert.Equal(before, await _repository.ResolveAsync("HEAD"));
        Assert.Equal(content, File.ReadAllText(_repository.GetPath("src/app.txt")));
        Assert.True((await StatusAsync()).IsClean);
        Assert.False(await Merges.IsMergeInProgressAsync(_handle, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ContinueAsync_RecordsTheMergeOnceTheConflictIsResolved()
    {
        await BuildConflictingBranchAsync();
        await MergeAsync("theirs");

        _repository.WriteFile("src/app.txt", "one\nthe resolved version\nthree\n");
        await _repository.GitAsync("add", "src/app.txt");

        string sha = await Merges.ContinueAsync(
            _handle,
            "Resolve the two versions",
            TestContext.Current.CancellationToken);

        Assert.Equal(await _repository.ResolveAsync("HEAD"), sha);
        Assert.Equal("Resolve the two versions", await _repository.GitLineAsync("log", "-1", "--format=%s"));

        // Two parents: the merge really happened.
        Assert.Equal(2, (await _repository.GitLinesAsync("rev-list", "--parents", "-1", "HEAD"))[0].Split(' ')[1..].Length);
        Assert.False(await Merges.IsMergeInProgressAsync(_handle, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ContinueAsync_RefusesWhileAnythingIsStillConflicted()
    {
        await BuildConflictingBranchAsync();
        await MergeAsync("theirs");

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Merges.ContinueAsync(_handle, null, TestContext.Current.CancellationToken));

        Assert.Contains("still have conflicts", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMergeHeadsAsync_NamesBothSides()
    {
        await BuildConflictingBranchAsync();

        string theirs = await _repository.ResolveAsync("theirs");
        string ours = await _repository.ResolveAsync("HEAD");

        await MergeAsync("theirs");

        MergeHeads heads = await Merges.GetMergeHeadsAsync(_handle, TestContext.Current.CancellationToken);

        Assert.True(heads.Exists);
        Assert.Equal(ours, heads.Ours);
        Assert.Equal(theirs, heads.Theirs);
        Assert.Equal("main", heads.OursName);
        Assert.Equal("theirs", heads.TheirsName);
    }

    [Fact]
    public async Task GetMergeHeadsAsync_SaysThereIsNoMergeWhenThereIsNot()
        => Assert.False((await Merges.GetMergeHeadsAsync(_handle, TestContext.Current.CancellationToken)).Exists);

    // ---------------------------------------------------------------- the conflict model

    [Fact]
    public async Task GetConflictsAsync_ClassifiesBothSidesChangingAFile()
    {
        await BuildConflictingBranchAsync();
        await MergeAsync("theirs");

        ConflictFile file = Assert.Single(
            await Conflicts.GetConflictsAsync(_handle, TestContext.Current.CancellationToken));

        Assert.Equal("src/app.txt", file.Path);
        Assert.Equal(ConflictKind.BothModified, file.Kind);
        Assert.Equal("UU", file.State);
        Assert.False(file.IsBinary);
        Assert.True(file.HasThreeWayContent);
        Assert.Contains("Both sides changed", file.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConflictsAsync_ClassifiesBothSidesAddingTheSameFile()
    {
        await _repository.GitAsync("checkout", "-b", "theirs");
        await _repository.CommitFileAsync("src/new.txt", "their new file\n", "Their new file");

        await _repository.GitAsync("checkout", "main");
        await _repository.CommitFileAsync("src/new.txt", "our new file\n", "Our new file");

        await MergeAsync("theirs");

        ConflictFile file = Assert.Single(
            await Conflicts.GetConflictsAsync(_handle, TestContext.Current.CancellationToken));

        Assert.Equal(ConflictKind.BothAdded, file.Kind);

        // There is no base to merge against, so this is a choice between two whole files.
        Assert.False(file.HasThreeWayContent);
    }

    [Fact]
    public async Task GetConflictsAsync_ClassifiesADeleteAgainstAChange()
    {
        await _repository.GitAsync("checkout", "-b", "theirs");
        _repository.DeleteFile("src/app.txt");
        await _repository.CommitAllAsync("They deleted it");

        await _repository.GitAsync("checkout", "main");
        _repository.WriteFile("src/app.txt", "one\nchanged by us\nthree\n");
        await _repository.CommitAllAsync("We changed it");

        await MergeAsync("theirs");

        ConflictFile file = Assert.Single(
            await Conflicts.GetConflictsAsync(_handle, TestContext.Current.CancellationToken));

        Assert.Equal(ConflictKind.DeletedByThem, file.Kind);
        Assert.Contains("deleted this file", file.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConflictsAsync_ClassifiesAChangeAgainstADelete()
    {
        await _repository.GitAsync("checkout", "-b", "theirs");
        _repository.WriteFile("src/app.txt", "one\nchanged by them\nthree\n");
        await _repository.CommitAllAsync("They changed it");

        await _repository.GitAsync("checkout", "main");
        _repository.DeleteFile("src/app.txt");
        await _repository.CommitAllAsync("We deleted it");

        await MergeAsync("theirs");

        Assert.Equal(
            ConflictKind.DeletedByUs,
            Assert.Single(await Conflicts.GetConflictsAsync(_handle, TestContext.Current.CancellationToken)).Kind);
    }

    [Theory]
    [InlineData("UU", ConflictKind.BothModified)]
    [InlineData("AA", ConflictKind.BothAdded)]
    [InlineData("AU", ConflictKind.AddedByUs)]
    [InlineData("UA", ConflictKind.AddedByThem)]
    [InlineData("DU", ConflictKind.DeletedByUs)]
    [InlineData("UD", ConflictKind.DeletedByThem)]
    [InlineData("DD", ConflictKind.BothDeleted)]
    [InlineData("??", ConflictKind.Unknown)]
    public void ToKind_MapsEveryStateGitReports(string state, ConflictKind expected)
        => Assert.Equal(expected, ConflictService.ToKind(state));

    // ---------------------------------------------------------------- the three sides

    [Fact]
    public async Task GetSidesAsync_ReadsAllThreeVersionsFromTheIndex()
    {
        await BuildConflictingBranchAsync();
        await MergeAsync("theirs");

        ConflictSides sides = await Conflicts.GetSidesAsync(
            _handle,
            "src/app.txt",
            TestContext.Current.CancellationToken);

        Assert.True(sides.HasAllThree);
        Assert.Equal("one\ntwo\nthree\n", sides.Base);
        Assert.Equal("one\nour version\nthree\n", sides.Ours);
        Assert.Equal("one\ntheir version\nthree\n", sides.Theirs);
        Assert.False(sides.IsBinary);
    }

    [Fact]
    public async Task GetSidesAsync_ReportsAMissingBaseForAnAddAgainstAnAdd()
    {
        await _repository.GitAsync("checkout", "-b", "theirs");
        await _repository.CommitFileAsync("src/new.txt", "their new file\n", "Their new file");

        await _repository.GitAsync("checkout", "main");
        await _repository.CommitFileAsync("src/new.txt", "our new file\n", "Our new file");

        await MergeAsync("theirs");

        ConflictSides sides = await Conflicts.GetSidesAsync(
            _handle,
            "src/new.txt",
            TestContext.Current.CancellationToken);

        // Neither side started from anything, which is why this cannot be merged line by line.
        Assert.Null(sides.Base);
        Assert.Equal("our new file\n", sides.Ours);
        Assert.Equal("their new file\n", sides.Theirs);
        Assert.False(sides.HasAllThree);
    }

    [Fact]
    public async Task GetSidesAsync_ReportsAMissingSideForADeleteAgainstAChange()
    {
        await _repository.GitAsync("checkout", "-b", "theirs");
        _repository.DeleteFile("src/app.txt");
        await _repository.CommitAllAsync("They deleted it");

        await _repository.GitAsync("checkout", "main");
        _repository.WriteFile("src/app.txt", "one\nchanged by us\nthree\n");
        await _repository.CommitAllAsync("We changed it");

        await MergeAsync("theirs");

        ConflictSides sides = await Conflicts.GetSidesAsync(
            _handle,
            "src/app.txt",
            TestContext.Current.CancellationToken);

        Assert.NotNull(sides.Base);
        Assert.True(sides.HasOurs);
        Assert.False(sides.HasTheirs);
    }

    [Fact]
    public async Task GetSidesAsync_KeepsNonAsciiContentExactly()
    {
        await _repository.GitAsync("checkout", "-b", "theirs");
        _repository.WriteFile("src/accents.txt", "première ligne\nleur version — accentuée\n");
        await _repository.CommitAllAsync("Their accented version");

        await _repository.GitAsync("checkout", "main");
        _repository.WriteFile("src/accents.txt", "première ligne\nnotre version — accentuée\n");
        await _repository.CommitAllAsync("Our accented version");

        await MergeAsync("theirs");

        ConflictSides sides = await Conflicts.GetSidesAsync(
            _handle,
            "src/accents.txt",
            TestContext.Current.CancellationToken);

        // The bytes have to come back exactly as they went in, or a preview of the resolution is not
        // a preview of what will be written.
        Assert.Equal("première ligne\nnotre version — accentuée\n", sides.Ours);
        Assert.Equal("première ligne\nleur version — accentuée\n", sides.Theirs);
    }

    [Fact]
    public async Task GetSidesAsync_SaysSoForABinaryConflict()
    {
        byte[] ours = [0x00, 0x01, 0x02, 0x03];
        byte[] theirs = [0x00, 0x09, 0x08, 0x07];

        await _repository.GitAsync("checkout", "-b", "theirs");
        await File.WriteAllBytesAsync(_repository.GetPath("blob.bin"), theirs, TestContext.Current.CancellationToken);
        await _repository.CommitAllAsync("Their blob");

        await _repository.GitAsync("checkout", "main");
        await File.WriteAllBytesAsync(_repository.GetPath("blob.bin"), ours, TestContext.Current.CancellationToken);
        await _repository.CommitAllAsync("Our blob");

        await MergeAsync("theirs");

        ConflictSides sides = await Conflicts.GetSidesAsync(
            _handle,
            "blob.bin",
            TestContext.Current.CancellationToken);

        Assert.True(sides.IsBinary);
        Assert.Null(sides.Ours);
        Assert.Null(sides.Theirs);

        ConflictFile file = Assert.Single(
            await Conflicts.GetConflictsAsync(_handle, TestContext.Current.CancellationToken));

        Assert.True(file.IsBinary);
        Assert.False(file.HasThreeWayContent);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(new byte[] { 65, 66, 67 }, false)]
    [InlineData(new byte[] { 65, 0, 67 }, true)]
    public void LooksBinary_AgreesWithWhatGitCallsBinary(byte[]? content, bool expected)
        => Assert.Equal(expected, ConflictService.LooksBinary(content));

    // ---------------------------------------------------------------- the argument vector

    [Fact]
    public void BuildArguments_SaysWhatItWasAskedFor()
    {
        Assert.Contains("--no-ff", MergeService.BuildArguments(
            new MergeRequest { Source = "topic", FastForward = FastForwardMode.Never }));

        Assert.Contains("--ff-only", MergeService.BuildArguments(
            new MergeRequest { Source = "topic", FastForward = FastForwardMode.Only }));

        List<string> plain = MergeService.BuildArguments(new MergeRequest { Source = "topic" });

        Assert.Equal(["merge", "topic"], plain);
    }

    [Fact]
    public void BuildArguments_NeverCombinesSquashWithNoCommit()
    {
        List<string> arguments = MergeService.BuildArguments(
            new MergeRequest { Source = "topic", Squash = true, NoCommit = true });

        // A squash already stops before committing; passing both makes git complain about an
        // option the user never chose.
        Assert.Contains("--squash", arguments);
        Assert.DoesNotContain("--no-commit", arguments);
    }
}
