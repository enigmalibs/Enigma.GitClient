using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

public sealed class HiddenBranchStoreTests : IDisposable
{
    private readonly string _root;

    public HiddenBranchStoreTests()
        => _root = Path.Combine(Path.GetTempPath(), "enigma-hidden-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string FilePath => Path.Combine(_root, HiddenBranchStore.FileName);

    private string Repository(string name) => Path.Combine(_root, "work", name);

    private HiddenBranchStore Build() => new(new AppPaths(_root), NullLogger<HiddenBranchStore>.Instance);

    [Fact]
    public void Get_IsEmptyBeforeAnythingIsHidden()
        => Assert.Empty(Build().Get(Repository("one")));

    [Fact]
    public void AHiddenBranch_IsRememberedAcrossStores()
    {
        Build().SetHidden(Repository("one"), "refs/heads/topic", hidden: true);
        Build().SetHidden(Repository("one"), "refs/remotes/origin/topic", hidden: true);

        Assert.Equal(["refs/heads/topic", "refs/remotes/origin/topic"], Build().Get(Repository("one")).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EachRepository_HasItsOwnSet()
    {
        HiddenBranchStore store = Build();

        store.SetHidden(Repository("one"), "refs/heads/topic", hidden: true);
        store.SetHidden(Repository("two"), "refs/heads/feature", hidden: true);

        Assert.Equal(["refs/heads/topic"], store.Get(Repository("one")));
        Assert.Equal(["refs/heads/feature"], store.Get(Repository("two")));
    }

    [Fact]
    public void TheSameDirectory_IsTheSameRepository_WithOrWithoutATrailingSeparator()
    {
        HiddenBranchStore store = Build();

        store.SetHidden(Repository("one") + Path.DirectorySeparatorChar, "refs/heads/topic", hidden: true);

        Assert.Equal(["refs/heads/topic"], store.Get(Repository("one")));
    }

    [Fact]
    public void ShowingABranchAgain_ForgetsIt_AndTheRepositoryWithItsLastOne()
    {
        HiddenBranchStore store = Build();

        store.SetHidden(Repository("one"), "refs/heads/topic", hidden: true);
        IReadOnlySet<string> after = store.SetHidden(Repository("one"), "refs/heads/topic", hidden: false);

        Assert.Empty(after);
        Assert.Empty(store.Get(Repository("one")));
        Assert.DoesNotContain(Repository("one"), File.ReadAllText(FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void ShowAll_ForgetsOneRepositoryOnly()
    {
        HiddenBranchStore store = Build();

        store.SetHidden(Repository("one"), "refs/heads/topic", hidden: true);
        store.SetHidden(Repository("one"), "refs/heads/spike", hidden: true);
        store.SetHidden(Repository("two"), "refs/heads/feature", hidden: true);

        store.ShowAll(Repository("one"));

        Assert.Empty(store.Get(Repository("one")));
        Assert.Equal(["refs/heads/feature"], store.Get(Repository("two")));
    }

    [Fact]
    public void ABranchThatNoLongerExists_IsDroppedWhenTheSetIsWritten()
    {
        HiddenBranchStore store = Build();

        store.SetHidden(Repository("one"), "refs/heads/deleted", hidden: true);
        IReadOnlySet<string> after = store.SetHidden(
            Repository("one"),
            "refs/heads/topic",
            hidden: true,
            existingRefs: ["refs/heads/main", "refs/heads/topic"]);

        Assert.Equal(["refs/heads/topic"], after);
        Assert.Equal(["refs/heads/topic"], store.Get(Repository("one")));
    }

    [Fact]
    public void WithoutTheRepositorysRefs_NothingIsPruned()
    {
        HiddenBranchStore store = Build();

        store.SetHidden(Repository("one"), "refs/heads/spike", hidden: true);
        store.SetHidden(Repository("one"), "refs/heads/topic", hidden: true, existingRefs: null);

        Assert.Equal(2, store.Get(Repository("one")).Count);
    }

    [Theory]
    [InlineData("topic")]
    [InlineData("refs/")]
    [InlineData("refs/heads/*")]
    [InlineData("")]
    public void SetHidden_RefusesWhatIsNotAFullRefName(string refName)
        => Assert.Throws<ArgumentException>(() => Build().SetHidden(Repository("one"), refName, hidden: true));

    [Fact]
    public void TwoStores_WritingInTurn_KeepEachOthersWork()
    {
        // Two instances of the application: each reads the file again before it writes.
        HiddenBranchStore first = Build();
        HiddenBranchStore second = Build();

        first.SetHidden(Repository("one"), "refs/heads/topic", hidden: true);
        second.SetHidden(Repository("two"), "refs/heads/feature", hidden: true);
        first.SetHidden(Repository("one"), "refs/heads/spike", hidden: true);

        HiddenBranchStore reader = Build();
        Assert.Equal(2, reader.Get(Repository("one")).Count);
        Assert.Equal(["refs/heads/feature"], reader.Get(Repository("two")));
    }

    [Fact]
    public void TheFile_IsReadableAndNamesItsVersion()
    {
        Build().SetHidden(Repository("one"), "refs/heads/topic", hidden: true);

        string json = File.ReadAllText(FilePath);

        Assert.Contains("\"version\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"refs/heads/topic\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AHandEditedEntry_ThatIsNoRefName_HidesNothing()
    {
        Directory.CreateDirectory(_root);
        string key = System.Text.Json.JsonSerializer.Serialize(Repository("one"));
        File.WriteAllText(FilePath, $$"""{ "version": 1, "repositories": { {{key}}: ["refs/heads/topic", "refs/heads/*", "topic", null] } }""");

        Assert.Equal(["refs/heads/topic"], Build().Get(Repository("one")));
    }

    [Fact]
    public void ACorruptFile_IsMovedAside_AndReadAsNothingHidden()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, "{ this is not json");

        HiddenBranchStore store = Build();

        Assert.Empty(store.Get(Repository("one")));
        Assert.NotNull(store.BackupPath);
        Assert.True(File.Exists(store.BackupPath));
        Assert.False(File.Exists(FilePath));

        // And the next write starts a new file instead of overwriting what the user had.
        store.SetHidden(Repository("one"), "refs/heads/topic", hidden: true);
        Assert.Equal("{ this is not json", File.ReadAllText(store.BackupPath!));
    }
}
