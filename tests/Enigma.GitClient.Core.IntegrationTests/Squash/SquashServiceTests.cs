using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Squash;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Squash;

/// <summary>
/// Drives the squash service against a real repository shaped like the phased workflow that
/// motivated it — a base, then phases committed on their own branches and merged back with
/// <c>--no-ff</c> — and checks the one promise that matters: one new commit with the tip's files on a
/// new branch, and nothing that existed changed.
/// </summary>
public sealed class SquashServiceTests : IAsyncLifetime
{
    private const string Title = "Squash the feature";

    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _repository = null!;
    private RepositoryHandle _handle = null!;

    private string _rootSha = string.Empty;
    private string _baseSha = string.Empty;
    private string _tipSha = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _repository = await _workspace.InitRepositoryAsync("squash");

        _rootSha = await _repository.CommitInitialAsync();
        _baseSha = await _repository.CommitFileAsync("docs/plan.md", "plan\n", "Plan the feature");

        await _repository.GitAsync("checkout", "-b", "feature");

        for (int phase = 1; phase <= 3; phase++)
        {
            await _repository.GitAsync("checkout", "-b", $"phase{phase}");
            await _repository.CommitFileAsync($"docs/phase{phase}.md", $"phase {phase}\n", $"Add phase {phase}");
            await _repository.GitAsync("checkout", "feature");
            await _repository.GitAsync("merge", "--no-ff", $"phase{phase}", "-m", $"Merge phase{phase} into feature");
        }

        _tipSha = await _repository.ResolveAsync("HEAD");

        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_repository.Path, TestContext.Current.CancellationToken);

        _handle = discovery.Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private ISquashService Squash => _host.GetRequiredService<ISquashService>();

    private Task<SquashPreview> PreviewAsync(string baseSha, string tipSha)
        => Squash.PreviewAsync(_handle, baseSha, tipSha, TestContext.Current.CancellationToken);

    private Task<SquashOutcome> SquashAsync(
        string branch,
        bool checkout = false,
        string? baseSha = null,
        string? tipSha = null,
        string message = $"{Title}\n\n- Add phase 1")
        => Squash.SquashAsync(
            _handle,
            new SquashRequest(baseSha ?? _baseSha, tipSha ?? _tipSha, branch, message, checkout),
            TestContext.Current.CancellationToken);

    private Task<IReadOnlyList<string>> RefsAsync()
        => _repository.GitLinesAsync("for-each-ref", "--format=%(refname) %(objectname)");

    private async Task<bool> BranchExistsAsync(string name)
        => (await _repository.GitLinesAsync("branch", "--list", name)).Count > 0;

    [Fact]
    public async Task Preview_DescribesThePhasedRange()
    {
        SquashPreview preview = await PreviewAsync(_baseSha, _tipSha);

        Assert.Equal(_baseSha, preview.BaseSha);
        Assert.Equal(_tipSha, preview.TipSha);
        Assert.Equal(6, preview.CommitCount);
        Assert.Equal(3, preview.MergeCount);
        Assert.Equal(["Add phase 1", "Add phase 2", "Add phase 3"], preview.Subjects);
        Assert.Empty(preview.OutsideMerges);
        Assert.Equal(0, preview.OutsideCommitCount);
        Assert.False(preview.ChangesNothing);
    }

    [Fact]
    public async Task Squash_RecordsOneCommitWithTheTipsFilesOnTheBase()
    {
        SquashOutcome outcome = await SquashAsync("feature-squashed");

        Assert.Equal("feature-squashed", outcome.BranchName);
        Assert.False(outcome.CheckedOut);
        Assert.Equal(outcome.CommitSha, await _repository.ResolveAsync("refs/heads/feature-squashed"));

        // Its only parent is the base, its files are the tip's byte for byte, and it is the one
        // commit the new branch has above the base.
        Assert.Equal($"{outcome.CommitSha} {_baseSha}", await _repository.GitLineAsync("rev-list", "--parents", "-n", "1", outcome.CommitSha));
        Assert.Equal(string.Empty, await _repository.GitAsync("diff", _tipSha, outcome.CommitSha));
        Assert.Equal("1", await _repository.GitLineAsync("rev-list", "--count", $"{_baseSha}..feature-squashed"));
        Assert.Equal(Title, await _repository.GitLineAsync("log", "-1", "--format=%s", outcome.CommitSha));
    }

    [Fact]
    public async Task Squash_ChangesNoExistingRefNorTheIndexNorTheWorkTree()
    {
        await _repository.GitAsync("tag", "v1", "feature~1");
        await _repository.GitAsync("config", "branch.feature.remote", "origin");
        await _repository.GitAsync("config", "branch.feature.merge", "refs/heads/feature");

        _repository.WriteFile("docs/staged.md", "staged\n");
        await _repository.GitAsync("add", "docs/staged.md");
        _repository.WriteFile("docs/phase1.md", "edited\n");
        _repository.WriteFile("loose.txt", "untracked\n");

        IReadOnlyList<string> refsBefore = await RefsAsync();
        string configBefore = await _repository.GitAsync("config", "--local", "--list");
        string statusBefore = await _repository.GitAsync("status", "--porcelain");
        string stagedBefore = await _repository.GitAsync("diff", "--cached");
        string unstagedBefore = await _repository.GitAsync("diff");

        SquashOutcome outcome = await SquashAsync("feature-squashed");

        List<string> expectedRefs = [.. refsBefore, $"refs/heads/feature-squashed {outcome.CommitSha}"];
        expectedRefs.Sort(System.StringComparer.Ordinal);
        List<string> refsAfter = [.. await RefsAsync()];
        refsAfter.Sort(System.StringComparer.Ordinal);

        Assert.Equal(expectedRefs, refsAfter);
        Assert.Equal(configBefore, await _repository.GitAsync("config", "--local", "--list"));
        Assert.Equal("feature", await _repository.GitLineAsync("symbolic-ref", "--short", "HEAD"));
        Assert.Equal(statusBefore, await _repository.GitAsync("status", "--porcelain"));
        Assert.Equal(stagedBefore, await _repository.GitAsync("diff", "--cached"));
        Assert.Equal(unstagedBefore, await _repository.GitAsync("diff"));
    }

    [Fact]
    public async Task Squash_SwitchesWithUncommittedChangesWhenHeadIsAtTheTip()
    {
        _repository.WriteFile("docs/staged.md", "staged\n");
        await _repository.GitAsync("add", "docs/staged.md");
        _repository.WriteFile("docs/phase2.md", "edited\n");
        _repository.WriteFile("loose.txt", "untracked\n");
        string statusBefore = await _repository.GitAsync("status", "--porcelain");

        SquashOutcome outcome = await SquashAsync("feature-squashed", checkout: true);

        Assert.True(outcome.CheckedOut);
        Assert.Equal("feature-squashed", await _repository.GitLineAsync("symbolic-ref", "--short", "HEAD"));
        Assert.Equal(outcome.CommitSha, await _repository.ResolveAsync("HEAD"));
        Assert.Equal(statusBefore, await _repository.GitAsync("status", "--porcelain"));
        Assert.Equal(_tipSha, await _repository.ResolveAsync("feature"));
    }

    [Fact]
    public async Task Squash_WhoseSwitchIsBlockedCreatesNothing()
    {
        await _repository.GitAsync("checkout", "main");
        _repository.WriteFile("docs/phase1.md", "mine\n");
        IReadOnlyList<string> refsBefore = await RefsAsync();

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => SquashAsync("feature-squashed", checkout: true));

        Assert.Contains("docs/phase1.md", refusal.Message, System.StringComparison.Ordinal);
        Assert.Contains("nothing was created", refusal.Message, System.StringComparison.Ordinal);
        Assert.False(await BranchExistsAsync("feature-squashed"));
        Assert.Equal(refsBefore, await RefsAsync());
        Assert.Equal("main", await _repository.GitLineAsync("symbolic-ref", "--short", "HEAD"));
        Assert.Equal("mine\n", File.ReadAllText(_repository.GetPath("docs/phase1.md")));
    }

    [Fact]
    public async Task Preview_FlagsAMergeThatBringsInABranchFromOutside()
    {
        await _repository.GitAsync("checkout", "-b", "develop", _rootSha);
        await _repository.CommitFileAsync("src/develop.txt", "develop\n", "Work on develop");
        await _repository.GitAsync("checkout", "feature");
        await _repository.GitAsync("merge", "--no-ff", "develop", "-m", "Merge develop into feature");
        string tip = await _repository.ResolveAsync("HEAD");

        SquashPreview preview = await PreviewAsync(_baseSha, tip);

        SquashOutsideMerge outside = Assert.Single(preview.OutsideMerges);
        Assert.Equal(tip, outside.Sha);
        Assert.Equal("Merge develop into feature", outside.Subject);
        Assert.Equal(1, preview.OutsideCommitCount);
        Assert.Equal(7, preview.CommitCount);
        Assert.Equal(4, preview.MergeCount);

        // The squash keeps what the merge brought in: the files are the tip's.
        SquashOutcome outcome = await SquashAsync("feature-squashed", tipSha: tip);

        Assert.Equal(string.Empty, await _repository.GitAsync("diff", tip, outcome.CommitSha));
    }

    [Fact]
    public async Task Squash_FromATipInTheMiddleEndsTheNewBranchThere()
    {
        string middle = await _repository.ResolveAsync("feature~1");

        SquashOutcome outcome = await SquashAsync("middle", tipSha: middle);

        Assert.Equal(string.Empty, await _repository.GitAsync("diff", middle, outcome.CommitSha));
        Assert.Equal(_baseSha, await _repository.ResolveAsync($"{outcome.CommitSha}^"));
        Assert.Equal(outcome.CommitSha, await _repository.ResolveAsync("middle"));
        Assert.Equal(_tipSha, await _repository.ResolveAsync("feature"));
    }

    [Fact]
    public async Task Squash_OfARangeThatChangesNothingRecordsTheBasesFiles()
    {
        await _repository.GitAsync("checkout", "-b", "undo", _baseSha);
        await _repository.CommitFileAsync("docs/plan.md", "changed\n", "Change the plan");
        await _repository.GitAsync("revert", "--no-edit", "HEAD");
        string tip = await _repository.ResolveAsync("HEAD");

        SquashPreview preview = await PreviewAsync(_baseSha, tip);

        Assert.True(preview.ChangesNothing);

        SquashOutcome outcome = await SquashAsync("undo-squashed", tipSha: tip);

        Assert.Equal(await _repository.ResolveAsync($"{_baseSha}^{{tree}}"), await _repository.ResolveAsync($"{outcome.CommitSha}^{{tree}}"));
    }

    [Fact]
    public async Task Squash_RecordsTheMessageAsTypedAndTheConfiguredAuthor()
    {
        SquashOutcome outcome = await SquashAsync("feature-squashed", message: "Ajouté l'été  \r\n\r\n- déjà vu\r\n");

        Assert.Equal("Ajouté l'été\n\n- déjà vu", await _repository.GitLineAsync("log", "-1", "--format=%B", outcome.CommitSha));
        Assert.Equal("Enigma Test <test@enigma.invalid>", await _repository.GitLineAsync("log", "-1", "--format=%an <%ae>", outcome.CommitSha));
    }

    [Fact]
    public async Task Squash_RefusesABaseOnAMergedSideBranch()
    {
        string sideCommit = await _repository.ResolveAsync("phase1");
        IReadOnlyList<string> refsBefore = await RefsAsync();

        await Assert.ThrowsAsync<GitOperationRefusedException>(() => PreviewAsync(sideCommit, _tipSha));
        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => SquashAsync("feature-squashed", baseSha: sideCommit));

        Assert.Contains("main line", refusal.Message, System.StringComparison.Ordinal);
        Assert.Equal(refsBefore, await RefsAsync());
    }

    [Fact]
    public async Task Squash_RefusesABaseThatIsNoAncestor()
    {
        await _repository.GitAsync("checkout", "-b", "elsewhere", _rootSha);
        string elsewhere = await _repository.CommitFileAsync("src/elsewhere.txt", "elsewhere\n", "Work elsewhere");
        await _repository.GitAsync("checkout", "feature");
        IReadOnlyList<string> refsBefore = await RefsAsync();

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => SquashAsync("feature-squashed", baseSha: elsewhere));

        Assert.Contains("does not descend", refusal.Message, System.StringComparison.Ordinal);
        Assert.Equal(refsBefore, await RefsAsync());
    }

    [Fact]
    public async Task Squash_RefusesTheBaseAsTheTip()
    {
        await Assert.ThrowsAsync<GitOperationRefusedException>(() => PreviewAsync(_tipSha, _tipSha));
        await Assert.ThrowsAsync<GitOperationRefusedException>(() => SquashAsync("feature-squashed", baseSha: _tipSha));

        Assert.False(await BranchExistsAsync("feature-squashed"));
    }

    [Fact]
    public async Task Squash_RefusesATakenBranchName()
    {
        await _repository.GitAsync("branch", "taken", _rootSha);
        IReadOnlyList<string> refsBefore = await RefsAsync();

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => SquashAsync("taken"));

        Assert.Contains("already exists", refusal.Message, System.StringComparison.Ordinal);
        Assert.Equal(refsBefore, await RefsAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n- Add phase 1")]
    [InlineData("   \n- Add phase 1")]
    public async Task Squash_RefusesABlankTitle(string message)
    {
        await Assert.ThrowsAsync<GitOperationRefusedException>(() => SquashAsync("feature-squashed", message: message));

        Assert.False(await BranchExistsAsync("feature-squashed"));
    }

    [Theory]
    [InlineData("bad..name")]
    [InlineData("has space")]
    [InlineData("-dash")]
    public async Task Squash_RefusesAnInvalidBranchName(string name)
    {
        IReadOnlyList<string> refsBefore = await RefsAsync();

        await Assert.ThrowsAsync<GitOperationRefusedException>(() => SquashAsync(name));

        Assert.Equal(refsBefore, await RefsAsync());
    }
}
