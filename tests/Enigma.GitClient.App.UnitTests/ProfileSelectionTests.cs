using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Whose list of repositories the start window shows: the default profile when there is no other,
/// the one last chosen while it exists, the first one otherwise.
/// </summary>
public sealed class ProfileSelectionTests : IDisposable
{
    private readonly string _root;
    private readonly IdentityProfileStore _profiles;
    private readonly SettingsService _settings;

    public ProfileSelectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "enigma-selection-" + Guid.NewGuid().ToString("N"));
        _profiles = new IdentityProfileStore(new AppPaths(_root), NullLogger<IdentityProfileStore>.Instance);
        _settings = new SettingsService(new AppPaths(_root), NullLogger<SettingsService>.Instance, TimeSpan.Zero);
    }

    public void Dispose()
    {
        _profiles.Dispose();
        _settings.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ProfileSelection Build(IIdentityProfileStore? profiles = null)
        => new(profiles ?? _profiles, _settings, NullLogger<ProfileSelection>.Instance);

    [Fact]
    public async Task WithoutProfiles_TheDefaultOneIsCreatedOnceAndSelected()
    {
        ProfileSelection selection = Build();

        ProfileChoice first = await selection.LoadAsync(TestContext.Current.CancellationToken);
        ProfileChoice second = await selection.LoadAsync(TestContext.Current.CancellationToken);

        IdentityProfile created = Assert.Single(first.Profiles);
        Assert.Equal(IdentityProfile.CreateDefault(), created);
        Assert.Same(created, first.Selected);
        Assert.Single(second.Profiles);
        Assert.Single(await _profiles.GetAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoInstancesCreatingTheDefaultProfileLeaveOne()
    {
        // Two instances started together on a machine without profiles.
        using IdentityProfileStore other = new(new AppPaths(_root), NullLogger<IdentityProfileStore>.Instance);

        await Build().LoadAsync(TestContext.Current.CancellationToken);
        await other.SaveAsync(IdentityProfile.CreateDefault(), TestContext.Current.CancellationToken);

        Assert.Single(await _profiles.GetAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistingProfilesAreKept_AndTheFirstIsSelectedUntilOneIsChosen()
    {
        IdentityProfile work = await _profiles.SaveAsync(
            IdentityProfile.Create("Work", new GitIdentity("Ada", "ada@work.example")),
            TestContext.Current.CancellationToken);
        await _profiles.SaveAsync(IdentityProfile.Create("Home", GitIdentity.Empty), TestContext.Current.CancellationToken);

        ProfileChoice choice = await Build().LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Work", "Home"], [.. Labels(choice.Profiles)]);
        Assert.Equal(work, choice.Selected);
    }

    [Fact]
    public async Task TheChosenProfileIsRemembered()
    {
        await _profiles.SaveAsync(IdentityProfile.Create("Work", GitIdentity.Empty), TestContext.Current.CancellationToken);
        IdentityProfile home = await _profiles.SaveAsync(
            IdentityProfile.Create("Home", GitIdentity.Empty),
            TestContext.Current.CancellationToken);

        Build().Select(home.Id);

        Assert.Equal(home.Id, _settings.Current.SelectedProfileId);
        Assert.Equal(home, (await Build().LoadAsync(TestContext.Current.CancellationToken)).Selected);
    }

    [Fact]
    public async Task AChosenProfileThatIsGone_FallsBackToTheFirst()
    {
        IdentityProfile work = await _profiles.SaveAsync(
            IdentityProfile.Create("Work", GitIdentity.Empty),
            TestContext.Current.CancellationToken);
        IdentityProfile home = await _profiles.SaveAsync(
            IdentityProfile.Create("Home", GitIdentity.Empty),
            TestContext.Current.CancellationToken);

        ProfileSelection selection = Build();
        selection.Select(home.Id);
        await _profiles.RemoveAsync(home.Id, TestContext.Current.CancellationToken);

        Assert.Equal(work, (await selection.LoadAsync(TestContext.Current.CancellationToken)).Selected);
    }

    [Fact]
    public async Task AProfilesFileThatCannotBeRead_LetsTheDefaultProfileStandIn()
    {
        ProfileChoice choice = await Build(new UnreadableProfiles()).LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(IdentityProfile.DefaultId, Assert.Single(choice.Profiles).Id);
        Assert.Equal(IdentityProfile.DefaultId, choice.Selected.Id);
    }

    [Fact]
    public void SelectingTheSameProfileAgainChangesNothing()
    {
        ProfileSelection selection = Build();
        int changes = 0;
        _settings.Changed += (_, _) => changes++;

        selection.Select("work");
        selection.Select("work");

        Assert.Equal(1, changes);
        Assert.Throws<ArgumentException>(() => selection.Select(" "));
    }

    private static IEnumerable<string> Labels(IEnumerable<IdentityProfile> profiles)
    {
        foreach (IdentityProfile profile in profiles)
        {
            yield return profile.Label;
        }
    }

    /// <summary>
    /// A profiles file the user may not read.
    /// </summary>
    private sealed class UnreadableProfiles : IIdentityProfileStore
    {
        public Task<IReadOnlyList<IdentityProfile>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new UnauthorizedAccessException("Access to the profiles is denied.");

        public Task<IReadOnlyList<IdentityProfile>> EnsureAnyAsync(CancellationToken cancellationToken = default)
            => throw new UnauthorizedAccessException("Access to the profiles is denied.");

        public Task<IdentityProfile> SaveAsync(IdentityProfile profile, CancellationToken cancellationToken = default)
            => throw new IOException("The disk is full.");

        public Task<bool> RemoveAsync(string profileId, CancellationToken cancellationToken = default)
            => throw new IOException("The disk is full.");
    }
}
