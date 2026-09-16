using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Configuration;

/// <summary>
/// The preferences file: what it remembers, what it does with one it cannot read, and how often it
/// writes.
/// </summary>
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _root;

    public SettingsServiceTests()
        => _root = Path.Combine(Path.GetTempPath(), "enigma-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string File_ => Path.Combine(_root, SettingsService.FileName);

    private SettingsService Build(TimeSpan? debounce = null)
        => new(new AppPaths(_root), NullLogger<SettingsService>.Instance, debounce ?? TimeSpan.Zero);

    // ---------------------------------------------------------------- defaults

    [Fact]
    public void EveryPreferenceHasADefault()
    {
        AppSettings defaults = AppSettings.Defaults;

        Assert.Equal(AppSettings.CurrentVersion, defaults.Version);
        Assert.Equal(ThemePreference.System, defaults.Theme);
        Assert.Equal(DateDisplay.Relative, defaults.DateDisplay);
        Assert.Equal(FilesView.Tree, defaults.FilesView);
        Assert.Equal(DiffView.Unified, defaults.DiffView);
        Assert.Equal(PullStrategy.Merge, defaults.Pull);
        Assert.Equal(3, defaults.DiffContextLines);
        Assert.Equal(4, defaults.TabWidth);
        Assert.Equal(36, defaults.GraphRowHeight);
        Assert.Equal(16, defaults.GraphLaneWidth);
        Assert.Equal(string.Empty, defaults.GitExecutablePath);
        Assert.False(defaults.ShowWhitespace);
        Assert.False(defaults.WrapLines);

        // And the defaults are already a valid set: normalising them changes nothing.
        Assert.Equal(defaults, defaults.Normalised());
    }

    [Fact]
    public async Task AFreshInstallRunsOnTheDefaults()
    {
        using SettingsService settings = Build();

        Assert.Equal(AppSettings.Defaults, await settings.LoadAsync(TestContext.Current.CancellationToken));
        Assert.False(System.IO.File.Exists(File_));
    }

    [Fact]
    public void ThePullStrategyCannotBeRebase()
    {
        // Not a test of an enum so much as a test of the product's one hard exclusion: if a rebase
        // option ever appears here, this is where it is noticed.
        Assert.Equal(["Merge", "FastForwardOnly"], Enum.GetNames<PullStrategy>());
    }

    // ---------------------------------------------------------------- round trip

    [Fact]
    public async Task APreferenceSurvivesARestart()
    {
        using (SettingsService settings = Build())
        {
            settings.Update(current => current with
            {
                Theme = ThemePreference.Light,
                DiffView = DiffView.SideBySide,
                DiffContextLines = 8,
                TabWidth = 2,
                FilesView = FilesView.List,
                HistoryPageSize = 750,
                FirstParentOnly = true,
                DateDisplay = DateDisplay.Absolute,
                GraphRowHeight = 32,
                GraphLaneWidth = 20,
                Pull = PullStrategy.FastForwardOnly,
                GitExecutablePath = "/opt/git/bin/git",
            });

            await settings.FlushAsync(TestContext.Current.CancellationToken);
        }

        using SettingsService reopened = Build();
        AppSettings stored = await reopened.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ThemePreference.Light, stored.Theme);
        Assert.Equal(DiffView.SideBySide, stored.DiffView);
        Assert.Equal(8, stored.DiffContextLines);
        Assert.Equal(2, stored.TabWidth);
        Assert.Equal(FilesView.List, stored.FilesView);
        Assert.Equal(750, stored.HistoryPageSize);
        Assert.True(stored.FirstParentOnly);
        Assert.Equal(DateDisplay.Absolute, stored.DateDisplay);
        Assert.Equal(32, stored.GraphRowHeight);
        Assert.Equal(20, stored.GraphLaneWidth);
        Assert.Equal(PullStrategy.FastForwardOnly, stored.Pull);
        Assert.Equal("/opt/git/bin/git", stored.GitExecutablePath);
    }

    [Fact]
    public async Task TheFileIsReadableByAPersonAndNamesItsVersion()
    {
        using SettingsService settings = Build();

        settings.Update(current => current with { Theme = ThemePreference.Dark });
        await settings.FlushAsync(TestContext.Current.CancellationToken);

        string json = System.IO.File.ReadAllText(File_);

        Assert.Contains("\"theme\": \"Dark\"", json, StringComparison.Ordinal);
        Assert.Contains(
            $"\"version\": {AppSettings.CurrentVersion.ToString(CultureInfo.InvariantCulture)}",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingSomethingRaisesTheEventWithTheNewSettings()
    {
        using SettingsService settings = Build();

        AppSettings? seen = null;
        settings.Changed += (_, e) => seen = e.Settings;

        settings.Update(current => current with { Theme = ThemePreference.Dark });

        Assert.Equal(ThemePreference.Dark, seen!.Theme);
        Assert.Equal(ThemePreference.Dark, settings.Current.Theme);
    }

    [Fact]
    public void SettingSomethingToWhatItAlreadyIsChangesNothing()
    {
        using SettingsService settings = Build();

        int raised = 0;
        settings.Changed += (_, _) => raised++;

        settings.Update(current => current with { Theme = ThemePreference.System });

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task ResettingPutsEverythingBack()
    {
        using SettingsService settings = Build();

        settings.Update(current => current with { Theme = ThemePreference.Dark, TabWidth = 8 });

        Assert.Equal(AppSettings.Defaults, await settings.ResetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(AppSettings.Defaults, settings.Current);
    }

    // ---------------------------------------------------------------- unreadable and future files

    [Fact]
    public async Task ACorruptFileIsKeptAndTheDefaultsLoad()
    {
        Directory.CreateDirectory(_root);
        System.IO.File.WriteAllText(File_, "{ this is not json");

        using SettingsService settings = Build();

        Assert.Equal(AppSettings.Defaults, await settings.LoadAsync(TestContext.Current.CancellationToken));

        // Kept rather than overwritten: it is the user's file, and it may be the only copy of
        // something they hand-edited.
        Assert.NotNull(settings.BackupPath);
        Assert.True(System.IO.File.Exists(settings.BackupPath));
        Assert.Contains("this is not json", System.IO.File.ReadAllText(settings.BackupPath!), StringComparison.Ordinal);
        Assert.False(System.IO.File.Exists(File_));
    }

    [Fact]
    public async Task AFileFromANewerBuildKeepsTheKeysThisOneUnderstands()
    {
        Directory.CreateDirectory(_root);

        System.IO.File.WriteAllText(File_, """
            {
              "version": 99,
              "theme": "Dark",
              "tabWidth": 8,
              "somethingFromTheFuture": { "nested": true }
            }
            """);

        using SettingsService settings = Build();
        AppSettings stored = await settings.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ThemePreference.Dark, stored.Theme);
        Assert.Equal(8, stored.TabWidth);

        // And nothing was moved aside: the file is readable, it is simply newer.
        Assert.Null(settings.BackupPath);
    }

    [Fact]
    public async Task AnOlderFileIsBroughtUpToTheCurrentVersion()
    {
        Directory.CreateDirectory(_root);
        System.IO.File.WriteAllText(File_, """{ "version": 0, "theme": "Light" }""");

        using SettingsService settings = Build();
        AppSettings stored = await settings.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppSettings.CurrentVersion, stored.Version);
        Assert.Equal(ThemePreference.Light, stored.Theme);
    }

    [Fact]
    public async Task AVersionOneRowHeightNobodyChangedTakesTheNewDefault()
    {
        Directory.CreateDirectory(_root);
        System.IO.File.WriteAllText(File_, """{ "version": 1, "graphRowHeight": 26, "tabWidth": 8 }""");

        using SettingsService settings = Build();
        AppSettings stored = await settings.LoadAsync(TestContext.Current.CancellationToken);

        // 26 was version 1's own default, so it is a number nobody chose.
        Assert.Equal(AppSettings.Defaults.GraphRowHeight, stored.GraphRowHeight);
        Assert.Equal(AppSettings.CurrentVersion, stored.Version);
        Assert.Equal(8, stored.TabWidth);
    }

    [Fact]
    public async Task AVersionOneRowHeightSomebodyChoseIsKept()
    {
        Directory.CreateDirectory(_root);
        System.IO.File.WriteAllText(File_, """{ "version": 1, "graphRowHeight": 30 }""");

        using SettingsService settings = Build();
        AppSettings stored = await settings.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(30, stored.GraphRowHeight);
        Assert.Equal(AppSettings.CurrentVersion, stored.Version);
    }

    [Fact]
    public async Task ACurrentVersionRowHeightIsNeverMigrated()
    {
        Directory.CreateDirectory(_root);
        System.IO.File.WriteAllText(File_, """{ "version": 2, "graphRowHeight": 26 }""");

        using SettingsService settings = Build();

        // At version 2, 26 is a preference like any other.
        Assert.Equal(
            AppSettings.LegacyGraphRowHeight,
            (await settings.LoadAsync(TestContext.Current.CancellationToken)).GraphRowHeight);
    }

    [Fact]
    public async Task AHandEditedFileIsClampedRatherThanRefused()
    {
        Directory.CreateDirectory(_root);

        System.IO.File.WriteAllText(File_, """
            {
              "version": 1,
              "historyPageSize": 0,
              "tabWidth": 900,
              "graphRowHeight": 4,
              "diffContextLines": -3,
              "gitExecutablePath": "  /usr/bin/git  "
            }
            """);

        using SettingsService settings = Build();
        AppSettings stored = await settings.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(50, stored.HistoryPageSize);
        Assert.Equal(16, stored.TabWidth);
        Assert.Equal(18, stored.GraphRowHeight);
        Assert.Equal(0, stored.DiffContextLines);
        Assert.Equal("/usr/bin/git", stored.GitExecutablePath);
    }

    [Fact]
    public async Task AnUnknownEnumValueFallsBackToItsDefault()
    {
        Directory.CreateDirectory(_root);
        System.IO.File.WriteAllText(File_, """{ "version": 1, "theme": "Dark", "pull": "Merge" }""");

        using SettingsService settings = Build();

        Assert.Equal(PullStrategy.Merge, (await settings.LoadAsync(TestContext.Current.CancellationToken)).Pull);

        // A value this build does not have is a broken file rather than a preference, so the whole
        // file goes to the backup and the defaults load.
        System.IO.File.WriteAllText(File_, """{ "version": 1, "pull": "Rebase" }""");

        using SettingsService second = Build();

        Assert.Equal(AppSettings.Defaults, await second.LoadAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(second.BackupPath);
    }

    // ---------------------------------------------------------------- writing

    [Fact]
    public async Task ABurstOfChangesWritesOnce()
    {
        using SettingsService settings = Build(TimeSpan.FromMilliseconds(200));

        for (int width = 1; width <= 8; width++)
        {
            settings.Update(current => current with { TabWidth = width });
        }

        await settings.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, settings.Writes);
        Assert.Equal(8, settings.Current.TabWidth);

        using SettingsService reopened = Build();

        Assert.Equal(8, (await reopened.LoadAsync(TestContext.Current.CancellationToken)).TabWidth);
    }

    [Fact]
    public async Task TheDebouncedWriteHappensOnItsOwn()
    {
        using SettingsService settings = Build(TimeSpan.FromMilliseconds(30));

        settings.Update(current => current with { TabWidth = 7 });

        for (int attempt = 0; attempt < 200 && settings.Writes == 0; attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        // No flush: the point is that nobody has to remember to call one.
        Assert.Equal(1, settings.Writes);
        Assert.Contains("\"tabWidth\": 7", System.IO.File.ReadAllText(File_), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FlushingWithNothingPendingWritesNothing()
    {
        using SettingsService settings = Build();

        await settings.LoadAsync(TestContext.Current.CancellationToken);
        await settings.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, settings.Writes);
    }

    [Fact]
    public async Task TheSettingsFileIsOnlyWrittenWithValuesTheApplicationCanUse()
    {
        using SettingsService settings = Build();

        settings.Update(current => current with { TabWidth = 999, HistoryPageSize = -1 });
        await settings.FlushAsync(TestContext.Current.CancellationToken);

        JsonDocument document = JsonDocument.Parse(System.IO.File.ReadAllText(File_));

        Assert.Equal(16, document.RootElement.GetProperty("tabWidth").GetInt32());
        Assert.Equal(50, document.RootElement.GetProperty("historyPageSize").GetInt32());

        document.Dispose();
    }
}
