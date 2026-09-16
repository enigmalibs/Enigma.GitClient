using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Checkout;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.IntegrationTests.Infrastructure;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Tags;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Tags;

/// <summary>
/// Drives the tag and checkout services against a real repository, asserting what git actually
/// recorded rather than what the command line looked like.
/// </summary>
public sealed class TagAndCheckoutServiceTests : IAsyncLifetime
{
    private GitWorkspace _workspace = null!;
    private CoreTestHost _host = null!;
    private TemporaryRepository _repository = null!;
    private RepositoryHandle _handle = null!;

    private string _firstSha = string.Empty;
    private string _secondSha = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _host = CoreTestHost.Create(_workspace);
        _repository = await _workspace.InitRepositoryAsync("tags");

        _firstSha = await _repository.CommitFileAsync("README.md", "# one\n", "Add the readme");
        _secondSha = await _repository.CommitFileAsync("src/app.txt", "one\n", "Add the application file");

        RepositoryDiscoveryResult discovery = await _host.GetRequiredService<IRepositoryLocator>()
            .DiscoverAsync(_repository.Path, TestContext.Current.CancellationToken);

        _handle = discovery.Repository!;
    }

    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await _workspace.DisposeAsync();
    }

    private ITagService Tags => _host.GetRequiredService<ITagService>();

    private ICheckoutService Checkout => _host.GetRequiredService<ICheckoutService>();

    private Task<RefCollection> RefsAsync()
        => _host.GetRequiredService<IRefReader>().GetRefsAsync(_handle, TestContext.Current.CancellationToken);

    private Task<HeadState> HeadAsync()
        => _host.GetRequiredService<IRefReader>().GetHeadStateAsync(_handle, TestContext.Current.CancellationToken);

    // ---------------------------------------------------------------- tags

    [Fact]
    public async Task CreateAsync_MakesALightweightTagWhenThereIsNoMessage()
    {
        await Tags.CreateAsync(_handle, "v1.0.0", cancellationToken: TestContext.Current.CancellationToken);

        GitTag tag = (await RefsAsync()).Tags.Single();

        Assert.Equal("v1.0.0", tag.ShortName);
        Assert.False(tag.IsAnnotated);
        Assert.Equal(_secondSha, tag.TargetSha);
    }

    [Fact]
    public async Task CreateAsync_MakesAnAnnotatedTagWhenThereIsOne()
    {
        await Tags.CreateAsync(
            _handle,
            "v2.0.0",
            message: "Second release\n\nWith a body that spans lines.\n",
            cancellationToken: TestContext.Current.CancellationToken);

        GitTag tag = (await RefsAsync()).Tags.Single();

        Assert.True(tag.IsAnnotated);
        Assert.NotNull(tag.TagObjectSha);
        Assert.NotNull(tag.Tagger);
        // The reference reader carries the subject, which is what a list shows.
        Assert.Equal("Second release", tag.Message);

        // The whole annotation, body and all, reads back through the service.
        string whole = await Tags.GetMessageAsync(_handle, "v2.0.0", TestContext.Current.CancellationToken);

        Assert.Contains("Second release", whole, StringComparison.Ordinal);
        Assert.Contains("spans lines", whole, StringComparison.Ordinal);

        // An annotated tag's target is still the commit, not the tag object.
        Assert.Equal(_secondSha, tag.TargetSha);
    }

    [Fact]
    public async Task CreateAsync_TagsAnyCommitNotJustHead()
    {
        await Tags.CreateAsync(_handle, "v0.1.0", _firstSha, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(_firstSha, (await RefsAsync()).Tags.Single().TargetSha);
    }

    [Fact]
    public async Task CreateAsync_RefusesANameThatIsTaken()
    {
        await Tags.CreateAsync(_handle, "v1.0.0", cancellationToken: TestContext.Current.CancellationToken);

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Tags.CreateAsync(_handle, "v1.0.0", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("already exists", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_MovesAnExistingTagWhenForced()
    {
        await Tags.CreateAsync(_handle, "latest", _firstSha, cancellationToken: TestContext.Current.CancellationToken);

        await Tags.CreateAsync(
            _handle,
            "latest",
            _secondSha,
            force: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(_secondSha, (await RefsAsync()).Tags.Single().TargetSha);
    }

    [Fact]
    public async Task CreateAsync_RefusesAnInvalidName()
    {
        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Tags.CreateAsync(_handle, "bad name", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("spaces", refusal.Message, StringComparison.Ordinal);
        Assert.Empty((await RefsAsync()).Tags);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheTag()
    {
        await Tags.CreateAsync(_handle, "v1.0.0", cancellationToken: TestContext.Current.CancellationToken);
        await Tags.DeleteAsync(_handle, "v1.0.0", TestContext.Current.CancellationToken);

        Assert.Empty((await RefsAsync()).Tags);
    }

    [Fact]
    public async Task ExistsAsync_KnowsWhatIsThere()
    {
        await Tags.CreateAsync(_handle, "v1.0.0", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await Tags.ExistsAsync(_handle, "v1.0.0", TestContext.Current.CancellationToken));
        Assert.False(await Tags.ExistsAsync(_handle, "v9.9.9", TestContext.Current.CancellationToken));

        // A lightweight tag has no annotation to read.
        Assert.Equal(string.Empty, await Tags.GetMessageAsync(_handle, "v1.0.0", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PushAsync_PublishesOneTagAndDeleteRemoteTakesItBack()
    {
        TemporaryRepository origin = await _workspace.InitRepositoryAsync("origin", bare: true);
        await _repository.GitAsync("remote", "add", "origin", origin.Path);
        await _repository.GitAsync("push", "origin", "main");

        await Tags.CreateAsync(_handle, "v1.0.0", cancellationToken: TestContext.Current.CancellationToken);
        await Tags.PushAsync(_handle, "origin", "v1.0.0", TestContext.Current.CancellationToken);

        Assert.Contains(
            "v1.0.0",
            await origin.GitLinesAsync("for-each-ref", "--format=%(refname:short)", "refs/tags/"));

        await Tags.DeleteRemoteAsync(_handle, "origin", "v1.0.0", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            "v1.0.0",
            await origin.GitLinesAsync("for-each-ref", "--format=%(refname:short)", "refs/tags/"));
    }

    // ---------------------------------------------------------------- checkout

    [Fact]
    public async Task CheckoutAsync_MovesOntoALocalBranchWithoutDetaching()
    {
        await _repository.GitAsync("branch", "topic", _firstSha);

        CheckoutResult result = await Checkout.CheckoutAsync(
            _handle,
            "topic",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsDetached);
        Assert.Equal("topic", result.Head.BranchName);
        Assert.Equal(_firstSha, result.Head.Sha);
    }

    [Fact]
    public async Task CheckoutAsync_DetachesOnATag()
    {
        await Tags.CreateAsync(_handle, "v0.1.0", _firstSha, cancellationToken: TestContext.Current.CancellationToken);

        CheckoutResult result = await Checkout.CheckoutAsync(
            _handle,
            "v0.1.0",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsDetached);
        Assert.Equal(_firstSha, result.Head.Sha);
    }

    [Fact]
    public async Task CheckoutAsync_DetachesOnARawCommit()
    {
        CheckoutResult result = await Checkout.CheckoutAsync(
            _handle,
            _firstSha,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsDetached);
        Assert.Equal(_firstSha, (await HeadAsync()).Sha);
    }

    [Fact]
    public async Task CheckoutAsync_DetachesOnARemoteBranch()
    {
        TemporaryRepository origin = await _workspace.InitRepositoryAsync("origin", bare: true);
        await _repository.GitAsync("remote", "add", "origin", origin.Path);
        await _repository.GitAsync("push", "origin", "main");
        await _repository.GitAsync("fetch", "origin");

        CheckoutResult result = await Checkout.CheckoutAsync(
            _handle,
            "origin/main",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsDetached);
    }

    [Fact]
    public async Task CheckoutAsync_CanDetachFromABranchOnPurpose()
    {
        await _repository.GitAsync("branch", "topic", _firstSha);

        CheckoutResult result = await Checkout.CheckoutAsync(
            _handle,
            "topic",
            new CheckoutOptions { Detach = true },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsDetached);
        Assert.Equal(_firstSha, result.Head.Sha);
    }

    [Theory]
    [InlineData("main", false)]
    [InlineData("v0.1.0", true)]
    public async Task WouldDetachAsync_AnswersBeforeAnythingHappens(string revision, bool expected)
    {
        await Tags.CreateAsync(_handle, "v0.1.0", _firstSha, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected, await Checkout.WouldDetachAsync(_handle, revision, TestContext.Current.CancellationToken));
    }

    // ---------------------------------------------------------------- a dirty work tree

    [Fact]
    public async Task CheckoutAsync_RefusesToTrampleLocalChanges()
    {
        await _repository.GitAsync("branch", "topic", _firstSha);
        _repository.WriteFile("src/app.txt", "edited but not committed\n");

        // "src/app.txt" does not exist on the first commit, so checking it out would have to delete
        // an edited file. git refuses, and so the client does too unless it is told otherwise.
        await Assert.ThrowsAsync<GitCommandException>(
            () => Checkout.CheckoutAsync(_handle, "topic", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("edited but not committed\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
    }

    [Fact]
    public async Task CheckoutAsync_CanStashTheChangesAndBringThemBack()
    {
        await _repository.GitAsync("branch", "topic", _firstSha);
        _repository.WriteFile("src/app.txt", "edited but not committed\n");

        CheckoutResult result = await Checkout.CheckoutAsync(
            _handle,
            "topic",
            new CheckoutOptions { Dirty = DirtyTreeResolution.Stash, StashMessage = "before checking out topic" },
            TestContext.Current.CancellationToken);

        Assert.True(result.Stashed);
        Assert.Equal("topic", result.Head.BranchName);

        // The work is on the stash, not gone: popping it brings the edit back.
        Assert.Contains("before checking out topic", await _repository.GitAsync("stash", "list"));

        await _repository.GitAsync("checkout", "main");
        await _repository.GitAsync("stash", "pop");

        Assert.Equal("edited but not committed\n", File.ReadAllText(_repository.GetPath("src/app.txt")));
    }

    [Fact]
    public async Task CheckoutAsync_CanDiscardTheChangesWhenToldTo()
    {
        await _repository.GitAsync("branch", "topic", _firstSha);
        _repository.WriteFile("src/app.txt", "edited but not committed\n");

        CheckoutResult result = await Checkout.CheckoutAsync(
            _handle,
            "topic",
            new CheckoutOptions { Dirty = DirtyTreeResolution.Discard },
            TestContext.Current.CancellationToken);

        Assert.True(result.Discarded);
        Assert.Equal("topic", result.Head.BranchName);
        Assert.False(File.Exists(_repository.GetPath("src/app.txt")));
    }

    [Fact]
    public async Task CheckoutAsync_RefusesOutrightWhenTheAnswerWasCancel()
    {
        await _repository.GitAsync("branch", "topic", _firstSha);

        GitOperationRefusedException refusal = await Assert.ThrowsAsync<GitOperationRefusedException>(
            () => Checkout.CheckoutAsync(
                _handle,
                "topic",
                new CheckoutOptions { Dirty = DirtyTreeResolution.Cancel },
                TestContext.Current.CancellationToken));

        Assert.Contains("cancelled", refusal.Message, StringComparison.Ordinal);
        Assert.Equal("main", (await HeadAsync()).BranchName);
    }

    [Fact]
    public async Task StashAsync_SaysSoWhenThereWasNothingToStash()
    {
        Assert.False(await Checkout.StashAsync(_handle, cancellationToken: TestContext.Current.CancellationToken));

        _repository.WriteFile("src/app.txt", "edited\n");

        Assert.True(await Checkout.StashAsync(_handle, "with something", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StashAsync_TakesUntrackedFilesToo()
    {
        _repository.WriteFile("src/brand-new.txt", "never committed\n");

        Assert.True(await Checkout.StashAsync(_handle, cancellationToken: TestContext.Current.CancellationToken));

        // Without --include-untracked the file would still be sitting there, and "your changes are
        // stashed" would be a half-truth.
        Assert.False(File.Exists(_repository.GetPath("src/brand-new.txt")));
    }

    [Fact]
    public async Task CheckoutAsync_ReportsWhereHeadEndedUpEveryTime()
    {
        await _repository.GitAsync("branch", "topic", _firstSha);

        List<string> visited = [];

        foreach (string revision in new[] { "topic", "main", _firstSha })
        {
            CheckoutResult result = await Checkout.CheckoutAsync(
                _handle,
                revision,
                cancellationToken: TestContext.Current.CancellationToken);

            visited.Add(result.Head.DisplayName);
        }

        Assert.Equal(3, visited.Count);
        Assert.Contains("topic", visited);
        Assert.Contains("main", visited);
    }
}
