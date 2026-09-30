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

/// <summary>
/// Each profile's list of repositories: its order, which is the user's, and the file every instance
/// shares.
/// </summary>
public sealed class RepositoryListStoreTests : IDisposable
{
    private const string Work = "work";
    private const string Home = "home";

    private readonly string _root;
    private readonly RepositoryListStore _store;

    public RepositoryListStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "enigma-lists-" + Guid.NewGuid().ToString("N"));
        _store = Build();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private RepositoryListStore Build() => new(new AppPaths(_root), NullLogger<RepositoryListStore>.Instance);

    private string File(string name) => Path.Combine(_root, name);

    private string ListFile => Path.Combine(_root, RepositoryListStore.FileName);

    private static IEnumerable<string> Names(IEnumerable<ListedRepository> entries) => entries.Select(entry => entry.Name);

    [Fact]
    public async Task GetAsync_ReturnsNothingBeforeAnythingIsStored()
        => Assert.Empty(await _store.GetAsync(Work, TestContext.Current.CancellationToken));

    [Fact]
    public async Task AddAsync_RemembersARepository()
    {
        IReadOnlyList<ListedRepository> entries = await _store.AddAsync(
            Work,
            File("my-repo"),
            "my-repo",
            TestContext.Current.CancellationToken);

        ListedRepository entry = Assert.Single(entries);
        Assert.Equal("my-repo", entry.Name);
        Assert.Equal(Path.GetFullPath(File("my-repo")), entry.Path);
    }

    [Fact]
    public async Task AddAsync_PutsANewRepositoryAtTheEnd()
    {
        await _store.AddAsync(Work, File("one"), "one", TestContext.Current.CancellationToken);
        await _store.AddAsync(Work, File("two"), "two", TestContext.Current.CancellationToken);
        IReadOnlyList<ListedRepository> entries =
            await _store.AddAsync(Work, File("three"), "three", TestContext.Current.CancellationToken);

        Assert.Equal(["one", "two", "three"], Names(entries));
    }

    [Fact]
    public async Task AddAsync_LeavesAListedRepositoryWhereItIsAndTakesItsNewName()
    {
        await _store.AddAsync(Work, File("one"), "one", TestContext.Current.CancellationToken);
        await _store.AddAsync(Work, File("two"), "two", TestContext.Current.CancellationToken);

        // Opening the first one again, from a path written with a trailing separator.
        IReadOnlyList<ListedRepository> entries = await _store.AddAsync(
            Work,
            File("one") + Path.DirectorySeparatorChar,
            "renamed",
            TestContext.Current.CancellationToken);

        Assert.Equal(["renamed", "two"], Names(entries));
    }

    [Fact]
    public async Task AddAsync_NamesAnUnnamedRepositoryAfterItsDirectory()
    {
        IReadOnlyList<ListedRepository> entries =
            await _store.AddAsync(Work, File("folder"), " ", TestContext.Current.CancellationToken);

        Assert.Equal("folder", Assert.Single(entries).Name);
    }

    [Fact]
    public async Task ALongListKeepsEveryEntry()
    {
        for (int index = 0; index < 40; index++)
        {
            string name = $"repo-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            await _store.AddAsync(Work, File(name), name, TestContext.Current.CancellationToken);
        }

        IReadOnlyList<ListedRepository> entries = await _store.GetAsync(Work, TestContext.Current.CancellationToken);

        Assert.Equal(40, entries.Count);
        Assert.Equal("repo-0", entries[0].Name);
        Assert.Equal("repo-39", entries[^1].Name);
    }

    [Fact]
    public async Task RemoveAsync_ForgetsAnEntryAndKeepsTheOrderOfTheRest()
    {
        await _store.AddAsync(Work, File("one"), "one", TestContext.Current.CancellationToken);
        await _store.AddAsync(Work, File("two"), "two", TestContext.Current.CancellationToken);
        await _store.AddAsync(Work, File("three"), "three", TestContext.Current.CancellationToken);

        IReadOnlyList<ListedRepository> entries =
            await _store.RemoveAsync(Work, File("two"), TestContext.Current.CancellationToken);

        Assert.Equal(["one", "three"], Names(entries));
    }

    [Fact]
    public async Task EachProfileHasAListOfItsOwn()
    {
        await _store.AddAsync(Work, File("work-repo"), "work-repo", TestContext.Current.CancellationToken);
        await _store.AddAsync(Home, File("home-repo"), "home-repo", TestContext.Current.CancellationToken);

        // One repository may be on two lists; forgetting it from one leaves the other.
        await _store.AddAsync(Home, File("work-repo"), "work-repo", TestContext.Current.CancellationToken);
        await _store.RemoveAsync(Work, File("work-repo"), TestContext.Current.CancellationToken);

        Assert.Empty(await _store.GetAsync(Work, TestContext.Current.CancellationToken));
        Assert.Equal(
            ["home-repo", "work-repo"],
            Names(await _store.GetAsync(Home, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task RemoveProfileAsync_ForgetsThatProfilesListOnly()
    {
        await _store.AddAsync(Work, File("work-repo"), "work-repo", TestContext.Current.CancellationToken);
        await _store.AddAsync(Home, File("home-repo"), "home-repo", TestContext.Current.CancellationToken);

        await _store.RemoveProfileAsync(Work, TestContext.Current.CancellationToken);

        Assert.Empty(await _store.GetAsync(Work, TestContext.Current.CancellationToken));
        Assert.Single(await _store.GetAsync(Home, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(
            "\"work\"",
            await System.IO.File.ReadAllTextAsync(ListFile, TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveProfileAsync_OfAProfileWithNoListWritesNothing()
    {
        await _store.RemoveProfileAsync(Work, TestContext.Current.CancellationToken);

        Assert.False(System.IO.File.Exists(ListFile));
    }

    [Fact]
    public async Task TheListsSurviveARestart()
    {
        await _store.AddAsync(Work, File("persisted"), "persisted", TestContext.Current.CancellationToken);

        Assert.Equal(
            "persisted",
            Assert.Single(await Build().GetAsync(Work, TestContext.Current.CancellationToken)).Name);
    }

    [Fact]
    public async Task TheOldRecentListIsNotReadAndIsLeftAlone()
    {
        // What 4.x wrote: one list, belonging to nobody.
        Directory.CreateDirectory(_root);
        string old = Path.Combine(_root, "recent-repositories.json");
        const string content =
            """
            {
              "version": 1,
              "repositories": [
                { "path": "/src/old", "name": "old", "lastOpenedUtc": "2026-01-01T00:00:00+00:00", "isPinned": true }
              ]
            }
            """;
        await System.IO.File.WriteAllTextAsync(old, content, TestContext.Current.CancellationToken);

        Assert.Empty(await _store.GetAsync(Work, TestContext.Current.CancellationToken));

        await _store.AddAsync(Work, File("new"), "new", TestContext.Current.CancellationToken);

        Assert.Equal(content, await System.IO.File.ReadAllTextAsync(old, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ACorruptFileIsBackedUpAndTheListsStartEmpty()
    {
        await _store.AddAsync(Work, File("one"), "one", TestContext.Current.CancellationToken);

        await System.IO.File.WriteAllTextAsync(ListFile, "{ this is not json", TestContext.Current.CancellationToken);

        Assert.Empty(await Build().GetAsync(Work, TestContext.Current.CancellationToken));
        Assert.True(System.IO.File.Exists(ListFile + ".corrupt"), "the unreadable file must be kept, not deleted");
    }

    [Fact]
    public async Task AFileFromANewerVersionIsStillRead()
    {
        Directory.CreateDirectory(_root);
        await System.IO.File.WriteAllTextAsync(
            ListFile,
            """
            {
              "version": 99,
              "profiles": {
                "work": [ { "path": "/src/from-the-future", "name": "from-the-future", "colour": "teal" } ]
              }
            }
            """,
            TestContext.Current.CancellationToken);

        ListedRepository entry = Assert.Single(await _store.GetAsync(Work, TestContext.Current.CancellationToken));

        Assert.Equal("from-the-future", entry.Name);
    }

    [Fact]
    public async Task AnEntryWithNoPathIsIgnored()
    {
        Directory.CreateDirectory(_root);
        await System.IO.File.WriteAllTextAsync(
            ListFile,
            """
            {
              "version": 1,
              "profiles": {
                "work": [
                  { "path": "", "name": "broken" },
                  null,
                  { "path": "/src/fine", "name": "fine" }
                ],
                "home": null
              }
            }
            """,
            TestContext.Current.CancellationToken);

        Assert.Equal("fine", Assert.Single(await _store.GetAsync(Work, TestContext.Current.CancellationToken)).Name);
        Assert.Empty(await _store.GetAsync(Home, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Exists_ReportsWhetherThePathIsStillThere()
    {
        string real = Path.Combine(_root, "present");
        Directory.CreateDirectory(real);

        await _store.AddAsync(Work, real, "present", TestContext.Current.CancellationToken);
        await _store.AddAsync(Work, File("absent"), "absent", TestContext.Current.CancellationToken);

        IReadOnlyList<ListedRepository> entries = await _store.GetAsync(Work, TestContext.Current.CancellationToken);

        Assert.True(entries.Single(entry => entry.Name == "present").Exists);
        Assert.False(entries.Single(entry => entry.Name == "absent").Exists);
    }

    [Fact]
    public async Task TheFileIsVersionedAndKeyedByProfile()
    {
        await _store.AddAsync(Work, File("one"), "one", TestContext.Current.CancellationToken);

        string json = await System.IO.File.ReadAllTextAsync(ListFile, TestContext.Current.CancellationToken);

        Assert.Contains("\"version\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"work\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("exists", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConcurrentWritesDoNotLoseEntries()
    {
        Task[] writes =
        [
            .. Enumerable.Range(0, 10).Select(index => _store.AddAsync(
                index % 2 == 0 ? Work : Home,
                File($"repo-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}"),
                $"repo-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                TestContext.Current.CancellationToken)),
        ];

        await Task.WhenAll(writes);

        Assert.Equal(5, (await _store.GetAsync(Work, TestContext.Current.CancellationToken)).Count);
        Assert.Equal(5, (await _store.GetAsync(Home, TestContext.Current.CancellationToken)).Count);
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

    [Fact]
    public async Task TwoStoresOverTheSameDirectory_SeeEachOthersWrites()
    {
        // Two running instances of the application share this file.
        RepositoryListStore other = Build();

        await _store.AddAsync(Work, File("one"), "one", TestContext.Current.CancellationToken);
        await other.AddAsync(Work, File("two"), "two", TestContext.Current.CancellationToken);
        await other.AddAsync(Home, File("three"), "three", TestContext.Current.CancellationToken);

        Assert.Equal(["one", "two"], Names(await _store.GetAsync(Work, TestContext.Current.CancellationToken)));
        Assert.Equal(["three"], Names(await _store.GetAsync(Home, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Writing_LeavesOnlyTheDocumentBehind()
    {
        await _store.AddAsync(Work, File("one"), "one", TestContext.Current.CancellationToken);
        await _store.RemoveAsync(Work, File("one"), TestContext.Current.CancellationToken);

        Assert.Equal(
            [RepositoryListStore.FileName],
            Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task AProfileIdentifierIsRequired()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.GetAsync(" ", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.AddAsync(string.Empty, File("one"), "one", TestContext.Current.CancellationToken));
    }
}
