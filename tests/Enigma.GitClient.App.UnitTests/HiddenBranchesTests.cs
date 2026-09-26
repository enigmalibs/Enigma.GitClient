using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// The open repository's hidden branches: which repository they are read for, and when the change is
/// announced.
/// </summary>
public sealed class HiddenBranchesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "enigma-hidden-service-" + Guid.NewGuid().ToString("N"));
    private readonly FakeRefReader _reader = new();
    private readonly RepositoryContext _context;
    private readonly HiddenBranchStore _store;

    public HiddenBranchesTests()
    {
        _context = new RepositoryContext(_reader, NullLogger<RepositoryContext>.Instance);
        _store = new HiddenBranchStore(new AppPaths(_root), NullLogger<HiddenBranchStore>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static RepositoryHandle Handle(string name)
        => OperatingSystem.IsWindows()
            ? new RepositoryHandle($@"C:\src\{name}", $@"C:\src\{name}\.git")
            : new RepositoryHandle($"/src/{name}", $"/src/{name}/.git");

    private static GitBranch Branch(string name)
        => new($"refs/heads/{name}", "aaa", false, null, BranchTracking.None, GitSignature.Empty, DateTimeOffset.UnixEpoch, name);

    private HiddenBranches Build(IHiddenBranchStore? store = null)
        => new(_context, store ?? _store, NullLogger<HiddenBranches>.Instance);

    private async Task OpenAsync(string name, params string[] branches)
    {
        _reader.Refs = new RefCollection([.. Array.ConvertAll(branches, Branch)], [], [], []);
        await _context.OpenAsync(Handle(name), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void WithNoRepository_NothingIsHidden_AndNothingCanBe()
    {
        HiddenBranches hidden = Build();
        int changes = 0;
        hidden.Changed += (_, _) => changes++;

        hidden.SetHidden("refs/heads/topic", true);

        Assert.Empty(hidden.Hidden);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task TheOpenRepositorysSet_IsReadTheFirstTimeItIsAskedFor()
    {
        _store.SetHidden(Handle("one").WorkTreePath, "refs/heads/topic", hidden: true);
        await OpenAsync("one", "main", "topic");

        HiddenBranches hidden = Build();

        Assert.True(hidden.IsHidden("refs/heads/topic"));
        Assert.False(hidden.IsHidden("refs/heads/main"));
    }

    [Fact]
    public async Task AnotherRepository_BringsItsOwnSet_AndClosingEmptiesIt()
    {
        _store.SetHidden(Handle("one").WorkTreePath, "refs/heads/topic", hidden: true);
        _store.SetHidden(Handle("two").WorkTreePath, "refs/heads/feature", hidden: true);

        HiddenBranches hidden = Build();

        await OpenAsync("one", "main", "topic");
        Assert.Equal(["refs/heads/topic"], hidden.Hidden);

        await OpenAsync("two", "main", "feature");
        Assert.Equal(["refs/heads/feature"], hidden.Hidden);

        _context.Close();
        Assert.Empty(hidden.Hidden);
    }

    [Fact]
    public async Task HidingAndShowing_IsRemembered_AndAnnouncedOncePerRealChange()
    {
        await OpenAsync("one", "main", "topic");
        HiddenBranches hidden = Build();
        int changes = 0;
        hidden.Changed += (_, _) => changes++;

        hidden.SetHidden("refs/heads/topic", true);
        hidden.SetHidden("refs/heads/topic", true);

        Assert.Equal(1, changes);
        Assert.Equal(["refs/heads/topic"], _store.Get(Handle("one").WorkTreePath));

        hidden.SetHidden("refs/heads/topic", false);

        Assert.Equal(2, changes);
        Assert.Empty(_store.Get(Handle("one").WorkTreePath));
    }

    [Fact]
    public async Task ShowAll_ShowsEveryBranchAgain()
    {
        await OpenAsync("one", "main", "topic", "spike");
        HiddenBranches hidden = Build();
        hidden.SetHidden("refs/heads/topic", true);
        hidden.SetHidden("refs/heads/spike", true);

        int changes = 0;
        hidden.Changed += (_, _) => changes++;

        hidden.ShowAll();
        hidden.ShowAll();

        Assert.Empty(hidden.Hidden);
        Assert.Empty(_store.Get(Handle("one").WorkTreePath));
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task ABranchDeletedSinceItWasHidden_IsForgottenAtTheNextChange()
    {
        _store.SetHidden(Handle("one").WorkTreePath, "refs/heads/deleted", hidden: true);
        await OpenAsync("one", "main", "topic");
        HiddenBranches hidden = Build();

        hidden.SetHidden("refs/heads/topic", true);

        Assert.Equal(["refs/heads/topic"], hidden.Hidden);
    }

    [Fact]
    public async Task AStoreThatFails_ReadsAsNothingHidden_AndTheSessionStillHides()
    {
        await OpenAsync("one", "main", "topic");
        HiddenBranches hidden = Build(new FailingStore());

        Assert.Empty(hidden.Hidden);

        hidden.SetHidden("refs/heads/topic", true);
        Assert.True(hidden.IsHidden("refs/heads/topic"));

        hidden.ShowAll();
        Assert.Empty(hidden.Hidden);
    }

    private sealed class FailingStore : IHiddenBranchStore
    {
        public IReadOnlySet<string> Get(string workTreePath) => throw new IOException("disk gone");

        public IReadOnlySet<string> SetHidden(string workTreePath, string refName, bool hidden, IReadOnlyCollection<string>? existingRefs = null)
            => throw new IOException("disk gone");

        public void ShowAll(string workTreePath) => throw new IOException("disk gone");
    }
}
