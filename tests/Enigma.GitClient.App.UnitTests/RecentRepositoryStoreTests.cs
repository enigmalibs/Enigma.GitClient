using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

public sealed class RecentRepositoryStoreTests : IDisposable
{
    private readonly string _root;
    private readonly RecentRepositoryStore _store;

    public RecentRepositoryStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "enigma-recent-" + Guid.NewGuid().ToString("N"));
        _store = new RecentRepositoryStore(new AppPaths(_root), NullLogger<RecentRepositoryStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string File(string name) => Path.Combine(_root, name);

    [Fact]
    public async Task GetAllAsync_ReturnsNothingBeforeAnythingIsStored()
        => Assert.Empty(await _store.GetAllAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task TouchAsync_RemembersARepository()
    {
        IReadOnlyList<RecentRepository> entries = await _store.TouchAsync(
            File("my-repo"),
            "my-repo",
            TestContext.Current.CancellationToken);

        RecentRepository entry = Assert.Single(entries);
        Assert.Equal("my-repo", entry.Name);
        Assert.Equal(Path.GetFullPath(File("my-repo")), entry.Path);
        Assert.False(entry.IsPinned);
    }

    [Fact]
    public async Task TouchAsync_MovesAnExistingEntryToTheTopWithoutDuplicatingIt()
    {
        await _store.TouchAsync(File("one"), "one", TestContext.Current.CancellationToken);
        await _store.TouchAsync(File("two"), "two", TestContext.Current.CancellationToken);
        IReadOnlyList<RecentRepository> entries =
            await _store.TouchAsync(File("one"), "one", TestContext.Current.CancellationToken);

        Assert.Equal(2, entries.Count);
        Assert.Equal("one", entries[0].Name);
    }

    [Fact]
    public async Task TouchAsync_KeepsAnEntryPinnedWhenItIsReopened()
    {
        await _store.TouchAsync(File("pinned"), "pinned", TestContext.Current.CancellationToken);
        await _store.SetPinnedAsync(File("pinned"), true, TestContext.Current.CancellationToken);

        IReadOnlyList<RecentRepository> entries =
            await _store.TouchAsync(File("pinned"), "pinned", TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(entries).IsPinned);
    }

    [Fact]
    public async Task PinnedEntriesSortAboveTheRest()
    {
        await _store.TouchAsync(File("old"), "old", TestContext.Current.CancellationToken);
        await _store.TouchAsync(File("new"), "new", TestContext.Current.CancellationToken);
        IReadOnlyList<RecentRepository> entries =
            await _store.SetPinnedAsync(File("old"), true, TestContext.Current.CancellationToken);

        Assert.Equal("old", entries[0].Name);
        Assert.True(entries[0].IsPinned);
    }

    [Fact]
    public async Task RemoveAsync_ForgetsAnEntry()
    {
        await _store.TouchAsync(File("one"), "one", TestContext.Current.CancellationToken);
        await _store.TouchAsync(File("two"), "two", TestContext.Current.CancellationToken);

        IReadOnlyList<RecentRepository> entries =
            await _store.RemoveAsync(File("one"), TestContext.Current.CancellationToken);

        Assert.Equal("two", Assert.Single(entries).Name);
    }

    [Fact]
    public async Task TheListIsCappedAndTheOldestUnpinnedEntriesAreDropped()
    {
        for (int index = 0; index < RecentRepositoryStore.Capacity + 5; index++)
        {
            await _store.TouchAsync(
                File($"repo-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}"),
                $"repo-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                TestContext.Current.CancellationToken);
        }

        IReadOnlyList<RecentRepository> entries = await _store.GetAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RecentRepositoryStore.Capacity, entries.Count);
        Assert.Equal("repo-24", entries[0].Name);
        Assert.DoesNotContain(entries, entry => entry.Name == "repo-0");
    }

    [Fact]
    public async Task APinnedEntryIsNeverDroppedByTheCap()
    {
        await _store.TouchAsync(File("keeper"), "keeper", TestContext.Current.CancellationToken);
        await _store.SetPinnedAsync(File("keeper"), true, TestContext.Current.CancellationToken);

        for (int index = 0; index < RecentRepositoryStore.Capacity + 5; index++)
        {
            await _store.TouchAsync(
                File($"repo-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}"),
                $"repo-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                TestContext.Current.CancellationToken);
        }

        IReadOnlyList<RecentRepository> entries = await _store.GetAllAsync(TestContext.Current.CancellationToken);

        Assert.Contains(entries, entry => entry.Name == "keeper" && entry.IsPinned);
        Assert.Equal(RecentRepositoryStore.Capacity, entries.Count);
    }

    [Fact]
    public async Task TheListSurvivesARestart()
    {
        await _store.TouchAsync(File("persisted"), "persisted", TestContext.Current.CancellationToken);

        RecentRepositoryStore reopened = new(new AppPaths(_root), NullLogger<RecentRepositoryStore>.Instance);

        Assert.Equal("persisted", Assert.Single(await reopened.GetAllAsync(TestContext.Current.CancellationToken)).Name);
    }

    [Fact]
    public async Task ACorruptFileIsBackedUpAndTheListStartsEmpty()
    {
        await _store.TouchAsync(File("one"), "one", TestContext.Current.CancellationToken);

        string path = Path.Combine(_root, RecentRepositoryStore.FileName);
        await System.IO.File.WriteAllTextAsync(path, "{ this is not json", TestContext.Current.CancellationToken);

        RecentRepositoryStore reopened = new(new AppPaths(_root), NullLogger<RecentRepositoryStore>.Instance);

        Assert.Empty(await reopened.GetAllAsync(TestContext.Current.CancellationToken));
        Assert.True(System.IO.File.Exists(path + ".corrupt"), "the unreadable file must be kept, not deleted");
    }

    [Fact]
    public async Task AFileFromANewerVersionIsStillRead()
    {
        string path = Path.Combine(_root, RecentRepositoryStore.FileName);
        Directory.CreateDirectory(_root);
        await System.IO.File.WriteAllTextAsync(
            path,
            """
            {
              "version": 99,
              "repositories": [
                { "path": "/src/from-the-future", "name": "from-the-future", "lastOpenedUtc": "2030-01-01T00:00:00+00:00", "isPinned": false }
              ]
            }
            """,
            TestContext.Current.CancellationToken);

        RecentRepository entry = Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken));

        Assert.Equal("from-the-future", entry.Name);
    }

    [Fact]
    public async Task AnEntryWithNoPathIsIgnored()
    {
        string path = Path.Combine(_root, RecentRepositoryStore.FileName);
        Directory.CreateDirectory(_root);
        await System.IO.File.WriteAllTextAsync(
            path,
            """
            {
              "version": 1,
              "repositories": [
                { "path": "", "name": "broken", "lastOpenedUtc": "2026-01-01T00:00:00+00:00", "isPinned": false },
                { "path": "/src/fine", "name": "fine", "lastOpenedUtc": "2026-01-01T00:00:00+00:00", "isPinned": false }
              ]
            }
            """,
            TestContext.Current.CancellationToken);

        Assert.Equal("fine", Assert.Single(await _store.GetAllAsync(TestContext.Current.CancellationToken)).Name);
    }

    [Fact]
    public async Task Exists_ReportsWhetherThePathIsStillThere()
    {
        string real = Path.Combine(_root, "present");
        Directory.CreateDirectory(real);

        await _store.TouchAsync(real, "present", TestContext.Current.CancellationToken);
        await _store.TouchAsync(File("absent"), "absent", TestContext.Current.CancellationToken);

        IReadOnlyList<RecentRepository> entries = await _store.GetAllAsync(TestContext.Current.CancellationToken);

        Assert.True(entries.Single(entry => entry.Name == "present").Exists);
        Assert.False(entries.Single(entry => entry.Name == "absent").Exists);
    }

    [Fact]
    public async Task TheFileIsWrittenWithASchemaVersion()
    {
        await _store.TouchAsync(File("one"), "one", TestContext.Current.CancellationToken);

        string json = await System.IO.File.ReadAllTextAsync(
            Path.Combine(_root, RecentRepositoryStore.FileName),
            TestContext.Current.CancellationToken);

        Assert.Contains("\"version\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentWritesDoNotLoseEntries()
    {
        Task[] writes =
        [
            .. Enumerable.Range(0, 10).Select(index => _store.TouchAsync(
                File($"repo-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}"),
                $"repo-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                TestContext.Current.CancellationToken)),
        ];

        await Task.WhenAll(writes);

        Assert.Equal(10, (await _store.GetAllAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public void AppPaths_CreatesTheDirectoryPrivateToTheUser()
    {
        AppPaths paths = new(Path.Combine(_root, "configuration"));

        string directory = paths.ConfigurationDirectory;

        Assert.True(Directory.Exists(directory));

        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode mode = System.IO.File.GetUnixFileMode(directory);

            Assert.Equal(UnixFileMode.None, mode & UnixFileMode.GroupRead);
            Assert.Equal(UnixFileMode.None, mode & UnixFileMode.OtherRead);
        }
    }

    [Fact]
    public void AppPaths_BuildsFilePathsUnderTheDirectory()
    {
        AppPaths paths = new(Path.Combine(_root, "configuration"));

        Assert.Equal(
            Path.Combine(paths.ConfigurationDirectory, "settings.json"),
            paths.GetConfigurationFile("settings.json"));
    }
}
