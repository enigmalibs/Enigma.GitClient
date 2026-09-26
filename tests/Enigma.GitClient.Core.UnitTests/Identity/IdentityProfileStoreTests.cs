using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Identity;

/// <summary>
/// The identity profiles file: what it keeps, in which order, and what it does with one it cannot
/// read.
/// </summary>
public sealed class IdentityProfileStoreTests : IDisposable
{
    private static readonly GitIdentity Work = new("Ada Lovelace", "ada@work.example");
    private static readonly GitIdentity Home = new("Ada", "ada@home.example");

    private readonly string _root;

    public IdentityProfileStoreTests()
        => _root = Path.Combine(Path.GetTempPath(), "enigma-profiles-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string File_ => Path.Combine(_root, IdentityProfileStore.FileName);

    private IdentityProfileStore Build() => new(new AppPaths(_root), NullLogger<IdentityProfileStore>.Instance);

    [Fact]
    public async Task NoFileMeansNoProfiles()
    {
        using IdentityProfileStore store = Build();

        Assert.Empty(await store.GetAllAsync(TestContext.Current.CancellationToken));
        Assert.False(File.Exists(File_));
    }

    [Fact]
    public async Task ProfilesSurviveANewStoreInTheOrderTheyWereAdded()
    {
        using (IdentityProfileStore store = Build())
        {
            await store.SaveAsync(IdentityProfile.Create("Work", Work), TestContext.Current.CancellationToken);
            await store.SaveAsync(IdentityProfile.Create("Home", Home), TestContext.Current.CancellationToken);
        }

        using IdentityProfileStore reopened = Build();
        IReadOnlyList<IdentityProfile> profiles = await reopened.GetAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Work", "Home"], profiles.Select(profile => profile.Label));
        Assert.Equal(Work, profiles[0].Identity);
        Assert.Equal(Home, profiles[1].Identity);
    }

    [Fact]
    public async Task SavingAnExistingProfileReplacesItWhereItStands()
    {
        using IdentityProfileStore store = Build();

        IdentityProfile work = await store.SaveAsync(IdentityProfile.Create("Work", Work), TestContext.Current.CancellationToken);
        await store.SaveAsync(IdentityProfile.Create("Home", Home), TestContext.Current.CancellationToken);

        await store.SaveAsync(work.With("Office", new GitIdentity("Ada L.", "ada@office.example")), TestContext.Current.CancellationToken);

        IReadOnlyList<IdentityProfile> profiles = await store.GetAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Office", "Home"], profiles.Select(profile => profile.Label));
        Assert.Equal(work.Id, profiles[0].Id);
        Assert.Equal(new GitIdentity("Ada L.", "ada@office.example"), profiles[0].Identity);
    }

    [Fact]
    public async Task SavingTrimsWhatIsStored()
    {
        using IdentityProfileStore store = Build();

        IdentityProfile stored = await store.SaveAsync(
            new IdentityProfile("abc", "  Work ", " Ada Lovelace ", " ada@work.example "),
            TestContext.Current.CancellationToken);

        Assert.Equal(new IdentityProfile("abc", "Work", "Ada Lovelace", "ada@work.example"), stored);
        Assert.Equal(stored, Assert.Single(await store.GetAllAsync(TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData("", "Ada", "ada@example.com")]
    [InlineData("Work", "", "ada@example.com")]
    [InlineData("Work", "Ada", "not an address")]
    public async Task AnUnusableProfileIsRefusedAndNothingIsWritten(string label, string name, string email)
    {
        using IdentityProfileStore store = Build();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync(new IdentityProfile("abc", label, name, email), TestContext.Current.CancellationToken));

        Assert.False(File.Exists(File_));
    }

    [Fact]
    public async Task AProfileWithoutAnIdentifierIsRefused()
    {
        using IdentityProfileStore store = Build();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync(new IdentityProfile(" ", "Work", "Ada", "ada@example.com"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemovingTakesOnlyThatProfile()
    {
        using IdentityProfileStore store = Build();

        IdentityProfile work = await store.SaveAsync(IdentityProfile.Create("Work", Work), TestContext.Current.CancellationToken);
        await store.SaveAsync(IdentityProfile.Create("Home", Home), TestContext.Current.CancellationToken);

        Assert.True(await store.RemoveAsync(work.Id, TestContext.Current.CancellationToken));
        Assert.False(await store.RemoveAsync(work.Id, TestContext.Current.CancellationToken));

        Assert.Equal("Home", Assert.Single(await store.GetAllAsync(TestContext.Current.CancellationToken)).Label);
    }

    [Fact]
    public async Task TheFileIsVersionedReadableJson()
    {
        using IdentityProfileStore store = Build();
        IdentityProfile work = await store.SaveAsync(IdentityProfile.Create("Work", Work), TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(File_, TestContext.Current.CancellationToken));
        JsonElement root = document.RootElement;

        Assert.Equal(IdentityProfileStore.CurrentVersion, root.GetProperty("version").GetInt32());

        JsonElement profile = Assert.Single(root.GetProperty("profiles").EnumerateArray());
        Assert.Equal(work.Id, profile.GetProperty("id").GetString());
        Assert.Equal("Work", profile.GetProperty("label").GetString());
        Assert.Equal("Ada Lovelace", profile.GetProperty("name").GetString());
        Assert.Equal("ada@work.example", profile.GetProperty("email").GetString());

        // The derived identity is not written a second time.
        Assert.False(profile.TryGetProperty("identity", out _));
    }

    [Fact]
    public async Task AnUnreadableFileIsMovedAsideAndNotOverwritten()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(File_, "{ this is not json", TestContext.Current.CancellationToken);

        using IdentityProfileStore store = Build();

        Assert.Empty(await store.GetAllAsync(TestContext.Current.CancellationToken));

        Assert.NotNull(store.BackupPath);
        Assert.Equal("{ this is not json", await File.ReadAllTextAsync(store.BackupPath!, TestContext.Current.CancellationToken));
        Assert.StartsWith("identity-profiles.corrupt-", Path.GetFileName(store.BackupPath), StringComparison.Ordinal);

        // The next save starts a fresh file beside the one kept.
        await store.SaveAsync(IdentityProfile.Create("Work", Work), TestContext.Current.CancellationToken);

        Assert.True(File.Exists(store.BackupPath));
        Assert.Single(await store.GetAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AHandEditedEntryWithoutAnIdentifierIsSkippedAndTheRestRead()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(
            File_,
            """
            {
              "version": 1,
              "profiles": [
                { "label": "No id", "name": "X", "email": "x@example.com" },
                { "id": "kept", "label": "Work", "name": "Ada", "email": "ada@example.com" },
                null
              ]
            }
            """,
            TestContext.Current.CancellationToken);

        using IdentityProfileStore store = Build();

        IdentityProfile profile = Assert.Single(await store.GetAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal("kept", profile.Id);
    }

    [Fact]
    public async Task TwoStoresOnOneFileDoNotLoseEachOthersProfiles()
    {
        // Two instances of the application, each with its own store.
        using IdentityProfileStore first = Build();
        using IdentityProfileStore second = Build();

        await first.SaveAsync(IdentityProfile.Create("Work", Work), TestContext.Current.CancellationToken);
        await second.SaveAsync(IdentityProfile.Create("Home", Home), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Work", "Home"],
            (await first.GetAllAsync(TestContext.Current.CancellationToken)).Select(profile => profile.Label));
    }
}
