using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.ViewModels.Panels;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Sync;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// The settings page, and the preferences it changes taking effect where they are used.
/// </summary>
/// <remarks>
/// A settings page that writes a file nobody reads is the classic way this feature fails, so most
/// of these tests change something on the page and then look at the thing it is supposed to govern.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class SettingsPageTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public SettingsPageTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- the page and the store

    [Fact]
    public void ThePageShowsWhatIsStoredAndWritesBackToIt()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            ISettingsService store = services.Get<ISettingsService>();
            SettingsPageViewModel page = services.Get<SettingsPageViewModel>();

            Assert.Equal(AppSettings.Defaults.Theme, page.Theme);
            Assert.Equal(AppSettings.Defaults.TabWidth, page.TabWidth);

            page.Theme = ThemePreference.Dark;
            page.TabWidth = 8;
            page.Pull = PullStrategy.FastForwardOnly;

            Assert.Equal(ThemePreference.Dark, store.Current.Theme);
            Assert.Equal(8, store.Current.TabWidth);
            Assert.Equal(PullStrategy.FastForwardOnly, store.Current.Pull);
        });
    }

    [Fact]
    public void ThePageFollowsAChangeMadeSomewhereElse()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            ISettingsService store = services.Get<ISettingsService>();
            SettingsPageViewModel page = services.Get<SettingsPageViewModel>();

            List<string> raised = [];
            page.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

            store.Update(current => current with { Theme = ThemePreference.Light });

            Assert.Equal(ThemePreference.Light, page.Theme);
            Assert.Contains(nameof(SettingsPageViewModel.Theme), raised);
        });
    }

    [Fact]
    public void AValueOutsideItsRangeIsClampedRatherThanStored()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            SettingsPageViewModel page = services.Get<SettingsPageViewModel>();

            page.TabWidth = 900;
            page.HistoryPageSize = 1;

            Assert.Equal(16, page.TabWidth);
            Assert.Equal(50, page.HistoryPageSize);
        });
    }

    [Fact]
    public void TheChoicesOfferedAreTheOnesTheProductHas()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            SettingsPageViewModel page = services.Get<SettingsPageViewModel>();

            Assert.Equal(3, page.Themes.Count);
            Assert.Equal("Follow the system", SettingsPageViewModel.Describe(ThemePreference.System));

            // Two pull strategies, and neither of them is a rebase.
            Assert.Equal([PullStrategy.Merge, PullStrategy.FastForwardOnly], page.PullStrategies);
            Assert.DoesNotContain(
                "rebase",
                SettingsPageViewModel.Describe(PullStrategy.Merge),
                StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void TheAboutCardSaysWhatThisBuildIsAndWhatItDoesNotDo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            SettingsPageViewModel page = services.Get<SettingsPageViewModel>();
            await page.OnAppearingAsync();

            Assert.Equal("Enigma.GitClient", page.ProductName);
            Assert.False(string.IsNullOrWhiteSpace(page.ProductVersion));
            Assert.False(string.IsNullOrWhiteSpace(page.AvaloniaVersion));
            Assert.Equal("MIT", page.Licence);

            // The two permanent exclusions, where a user can read them.
            Assert.Contains("never rebases", page.ScopeStatement, StringComparison.Ordinal);
            Assert.Contains("issues or pull requests", page.ScopeStatement, StringComparison.Ordinal);

            // And which git it actually found, rather than which one it hoped for.
            Assert.Contains("git", page.GitDescription, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ResettingAsksFirst()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            SettingsPageViewModel page = services.Get<SettingsPageViewModel>();
            page.Theme = ThemePreference.Dark;

            services.Dialogs.Script(DialogResult.Close, DialogResult.Primary);

            await page.ResetCommand.ExecuteAsync(null);

            Assert.Equal(ThemePreference.Dark, page.Theme);
            Assert.Equal("Reset every preference", services.Dialogs.Last!.Title);

            await page.ResetCommand.ExecuteAsync(null);

            Assert.Equal(AppSettings.Defaults, services.Get<ISettingsService>().Current);
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Preferences reset");
        });
    }

    [Fact]
    public void APreferenceSurvivesARestart()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            SettingsPageViewModel page = services.Get<SettingsPageViewModel>();
            page.Theme = ThemePreference.Light;
            page.GraphRowHeight = 34;

            await services.Get<ISettingsService>().FlushAsync();

            // A second store over the same configuration directory is what a restart looks like.
            AppPaths paths = new(services.ConfigurationRoot);
            using SettingsService reopened = new(
                paths,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsService>.Instance);

            AppSettings stored = await reopened.LoadAsync();

            Assert.Equal(ThemePreference.Light, stored.Theme);
            Assert.Equal(34, stored.GraphRowHeight);
        });
    }

    // ---------------------------------------------------------------- taking effect live

    [Fact]
    public void ChangingTheThemeRepaintsTheApplication()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

            try
            {
                App.ApplyTheme(ThemePreference.Light);
                Assert.Equal(ThemeVariant.Light, application.RequestedThemeVariant);

                App.ApplyTheme(ThemePreference.Dark);
                Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);

                // "Follow the system" is Avalonia's Default rather than a third variant of our own.
                App.ApplyTheme(ThemePreference.System);
                Assert.Equal(ThemeVariant.Default, application.RequestedThemeVariant);
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void ChangingTheFileViewMovesAPanelThatIsAlreadyOpen()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            ChangedFilesPanelViewModel panel = new(services.Interop, services.Get<ISettingsService>());

            Assert.Equal(ChangedFilesViewMode.Tree, panel.ViewMode);

            services.Get<SettingsPageViewModel>().FilesView = FilesView.List;

            Assert.Equal(ChangedFilesViewMode.List, panel.ViewMode);

            services.Get<SettingsPageViewModel>().FilesAutoExpandLimit = 12;

            Assert.Equal(12, panel.AutoExpandLimit);
        });
    }

    [Fact]
    public void ChangingTheDiffPreferencesMovesAViewerThatIsAlreadyOpen()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            DiffViewerViewModel viewer = services.Get<DiffViewerViewModel>();

            Assert.Equal(DiffViewMode.SideBySide, viewer.ViewMode);
            Assert.Equal(4, viewer.Render.TabWidth);

            SettingsPageViewModel page = services.Get<SettingsPageViewModel>();

            page.DiffView = DiffView.Unified;
            page.TabWidth = 2;
            page.ShowWhitespace = true;
            page.WrapLines = true;

            Assert.Equal(DiffViewMode.Unified, viewer.ViewMode);
            Assert.Equal(2, viewer.Render.TabWidth);
            Assert.True(viewer.Render.ShowWhitespace);
            Assert.True(viewer.Render.WrapLines);
        });
    }

    [Fact]
    public void ANewViewerOpensOnTheStoredPreferences()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            // The stored shape, not the default one: a viewer opened after the preference moved
            // must open on what was stored.
            services.Get<SettingsPageViewModel>().DiffView = DiffView.Unified;

            // Transient: the next diff pane the application opens is a new instance.
            DiffViewerViewModel viewer = services.Get<DiffViewerViewModel>();

            Assert.Equal(DiffViewMode.Unified, viewer.ViewMode);
        });
    }

    [Fact]
    public void ChangingTheGraphMetricsMovesTheHistoryPage()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();

            Assert.Equal(16, history.LaneWidth);
            Assert.Equal(36, history.RowHeight);

            SettingsPageViewModel page = services.Get<SettingsPageViewModel>();

            page.GraphLaneWidth = 24;
            page.GraphRowHeight = 34;

            Assert.Equal(24, history.LaneWidth);
            Assert.Equal(34, history.RowHeight);

            // The graph column is measured from the lane width, so it moves with it.
            Assert.True(history.GraphColumnWidth >= 24 + (HistoryPageViewModel.LanePadding * 2));
        });
    }

    [Fact]
    public void ChangingTheHistoryPreferencesReachesTheHistoryPage()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            SettingsPageViewModel page = services.Get<SettingsPageViewModel>();

            page.HistoryPageSize = 250;
            page.FirstParentOnly = true;

            Assert.Equal(250, history.PageSize);
            Assert.True(history.FirstParentOnly);
        });
    }

    [Fact]
    public void ThePullStrategyIsTheOneTheUserChose()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            services.Get<SettingsPageViewModel>().Pull = PullStrategy.FastForwardOnly;

            // What the sync operation will hand to git, without running one.
            Assert.Equal(PullStrategy.FastForwardOnly, services.Get<ISettingsService>().Current.Pull);

            List<string> arguments = [.. SyncService.BuildPullArguments(null, null, PullStrategy.FastForwardOnly)];

            Assert.Contains("--ff-only", arguments);

            // And --no-rebase whatever the choice: this client never rebases.
            Assert.Contains("--no-rebase", arguments);
            Assert.Contains("--no-rebase", SyncService.BuildPullArguments(null, null, PullStrategy.Merge));
        });
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void ThePageActuallyPaints()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            SettingsPageViewModel page = services.Get<SettingsPageViewModel>();
            await page.OnAppearingAsync();

            SettingsPageView view = services.Get<SettingsPageView>();
            view.DataContext = page;

            Window window = new() { Width = 1280, Height = 900, Content = view };

            string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, "settings-page-dark.png");

            window.Show();

            int colours = 0;

            try
            {
                for (int attempt = 0; attempt < 20 && colours < 8; attempt++)
                {
                    Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                    window.Measure(new Size(window.Width, window.Height));
                    window.Arrange(new Rect(0, 0, window.Width, window.Height));
                    window.UpdateLayout();
                    window.InvalidateVisual();

                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                    Dispatcher.UIThread.RunJobs();

                    using Bitmap frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("The window produced no rendered frame.");

                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    colours = CountColours(path);
                }

                // Every group is on the page, and the one-line promise is on it too.
                List<string> texts =
                [
                    .. view.GetVisualDescendants()
                        .OfType<TextBlock>()
                        .Select(block => block.Text ?? string.Empty),
                ];

                Assert.Contains(texts, text => text == "Theme");
                Assert.Contains(texts, text => text == "History and graph");
                Assert.Contains(texts, text => text == "Diff");
                Assert.Contains(texts, text => text == "Git");
                Assert.Contains(texts, text => text == "About");
                Assert.Contains(texts, text => text.Contains("never rebases", StringComparison.Ordinal));
            }
            finally
            {
                window.Content = null;
                window.Close();
            }

            Assert.True(colours >= 8, $"the frame holds only {colours.ToString(System.Globalization.CultureInfo.InvariantCulture)} distinct colours");
        });
    }

    private static unsafe int CountColours(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using WriteableBitmap writeable = WriteableBitmap.Decode(stream);
        using ILockedFramebuffer buffer = writeable.Lock();

        HashSet<int> colours = [];

        byte* pixels = (byte*)buffer.Address;

        for (int y = 0; y < buffer.Size.Height; y++)
        {
            byte* row = pixels + (y * buffer.RowBytes);

            for (int x = 0; x < buffer.Size.Width; x++)
            {
                colours.Add(((row[(x * 4) + 2] >> 4) << 8) | ((row[(x * 4) + 1] >> 4) << 4) | (row[(x * 4) + 0] >> 4));
            }
        }

        return colours.Count;
    }
}
