using Avalonia;
using Avalonia.Styling;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The theme switch on the start window's toolbar: it switches the whole application, and the choice
/// is the theme preference, which the Settings page shows and the next start opens on.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ThemeSwitcherTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public ThemeSwitcherTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void Toggle_SwitchesTheVariantForTheWholeApplication()
    {
        _fixture.Run(() =>
        {
            // Over a throwaway configuration directory: the switch now writes the preference, and a
            // test must never write the developer's own.
            using TestServices services = TestServices.Build();
            IThemeSwitcher switcher = services.Get<IThemeSwitcher>();

            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

            try
            {
                application.RequestedThemeVariant = ThemeVariant.Dark;
                switcher.Toggle();
                Assert.Equal(ThemeVariant.Light, application.RequestedThemeVariant);

                switcher.Toggle();
                Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void Toggle_RecordsTheThemeItSwitchedToAsThePreference()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            IThemeSwitcher switcher = services.Get<IThemeSwitcher>();
            ISettingsService settings = services.Get<ISettingsService>();

            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

            try
            {
                Assert.Equal(ThemePreference.System, settings.Current.Theme);

                application.RequestedThemeVariant = ThemeVariant.Dark;
                switcher.Toggle();

                Assert.Equal(ThemePreference.Light, settings.Current.Theme);

                // The settings page reads the same store, so it says so too.
                Assert.Equal(ThemePreference.Light, services.Get<SettingsPageViewModel>().Theme);

                switcher.Toggle();

                Assert.Equal(ThemePreference.Dark, settings.Current.Theme);
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void Toggle_IsStillTheThemeAfterARestart()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IThemeSwitcher switcher = services.Get<IThemeSwitcher>();

            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

            try
            {
                application.RequestedThemeVariant = ThemeVariant.Dark;
                switcher.Toggle();

                await services.Get<ISettingsService>().FlushAsync(TestContext.Current.CancellationToken);
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }

            // The next start reads the same file afresh.
            using SettingsService restarted = new(
                services.Get<IAppPaths>(),
                NullLogger<SettingsService>.Instance);

            AppSettings read = await restarted.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(ThemePreference.Light, read.Theme);
        });
    }
}
