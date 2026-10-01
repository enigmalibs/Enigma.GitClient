using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Diagnostics;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Enigma.GitClient.Desktop.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The About dialog: what it says, how it is shown, and where it is opened from.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class AboutDialogTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public AboutDialogTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void TheViewModel_DescribesTheRunningBuild()
    {
        AboutViewModel about = new();

        Assert.Equal("Enigma Git Client", about.DisplayName);
        Assert.Equal(ProductInformation.Version, about.Version);
        Assert.Equal(ProductInformation.BuildSha, about.BuildSha);
        Assert.Equal(ProductInformation.Copyright, about.Copyright);
        Assert.Same(AboutViewModel.DefaultCredits, about.Credits);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("9a1b2c3", true)]
    public void TheBuildLine_IsThereOnlyWithARevision(string? sha, bool shown)
        => Assert.Equal(shown, new AboutViewModel("1.0.0", sha, "Copyright", []).HasBuildSha);

    [Theory]
    [InlineData("", false)]
    [InlineData("Copyright © 2026 Josué Clément", true)]
    public void TheCopyrightLine_IsThereOnlyWithANotice(string copyright, bool shown)
        => Assert.Equal(shown, new AboutViewModel("1.0.0", null, copyright, []).HasCopyright);

    [Fact]
    public void TheCredits_NameWhatTheApplicationShipsAndUnderWhichLicence()
    {
        Assert.Equal(
            ["Avalonia", "SkiaSharp", "HarfBuzzSharp", "ANGLE", "CommunityToolkit.Mvvm", "Microsoft.Extensions", "BouncyCastle", "Enigma libraries", "Phosphor Icons", "Inter"],
            AboutViewModel.DefaultCredits.Select(credit => credit.Name));

        Assert.Equal("SIL Open Font License 1.1", AboutViewModel.DefaultCredits.Single(credit => credit.Name == "Inter").License);
        Assert.Equal("BSD-3-Clause", AboutViewModel.DefaultCredits.Single(credit => credit.Name == "ANGLE").License);
        Assert.All(AboutViewModel.DefaultCredits.Where(credit => credit.Name is not ("Inter" or "ANGLE")), credit => Assert.Equal("MIT", credit.License));
    }

    [Fact]
    public void TheService_ShowsTheAboutViewWithOneButtonThatOnlyCloses()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            await services.Get<IAboutDialogService>().ShowAsync();

            ContentDialog dialog = Assert.Single(services.Dialogs.Shown);
            Assert.Equal("About", dialog.Title);
            Assert.NotNull(dialog.IconData);
            Assert.Equal("Close", dialog.CloseButtonText);
            Assert.Null(dialog.PrimaryButtonText);
            Assert.Null(dialog.SecondaryButtonText);

            AboutView view = Assert.IsType<AboutView>(dialog.Content);
            Assert.IsType<AboutViewModel>(view.DataContext);
        });
    }

    [Fact]
    public void TheRepositoryToolbar_OpensIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            MainWindowViewModel window = services.Get<MainWindowViewModel>();

            Assert.True(window.OpenAboutCommand.CanExecute(null));
            await window.OpenAboutCommand.ExecuteAsync(null);

            Assert.Equal("About", Assert.Single(services.Dialogs.Shown).Title);
        });
    }

    [Fact]
    public void TheSettingsPage_OpensIt_SoTheStartWindowCanToo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            SettingsPageViewModel settings = services.Get<SettingsPageViewModel>();

            await settings.OpenAboutCommand.ExecuteAsync(null);

            Assert.Equal("About", Assert.Single(services.Dialogs.Shown).Title);
        });
    }

    [Fact]
    public void TheStartWindowsHome_OpensIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            RepositoriesPageViewModel repositories = services.Get<RepositoriesPageViewModel>();

            Assert.True(repositories.OpenAboutCommand.CanExecute(null));
            await repositories.OpenAboutCommand.ExecuteAsync(null);

            ContentDialog dialog = Assert.Single(services.Dialogs.Shown);
            Assert.Equal("About", dialog.Title);
            Assert.IsType<AboutView>(dialog.Content);
        });
    }

    [Fact]
    public void TheStartWindowsHome_HasTheAboutButtonAtTheEndOfItsHeader()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
            await model.ReloadListAsync();

            RepositoriesPageView page = services.Get<RepositoriesPageView>();
            page.DataContext = model;

            Window window = new() { Content = page, Width = 1100, Height = 720 };
            window.Show();

            try
            {
                window.UpdateLayout();

                Button about = page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "OpenAbout");
                Button create = page.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.GetValue(AutomationProperties.NameProperty), "Create a repository"));

                Assert.Equal("About Enigma Git Client", about.GetValue(AutomationProperties.NameProperty));

                // Last in the header, after the repository actions.
                Point aboutAt = about.TranslatePoint(default, page) ?? default;
                Point createAt = create.TranslatePoint(default, page) ?? default;
                Assert.True(aboutAt.X > createAt.X, "the About button is not after Create");

                // A real click, so what is tested is the binding and not only the command.
                Point middle = about.TranslatePoint(new Point(about.Bounds.Width / 2, about.Bounds.Height / 2), window)
                    ?? throw new InvalidOperationException("The About button is not in the window.");
                window.MouseDown(middle, MouseButton.Left);
                window.MouseUp(middle, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("About", Assert.Single(services.Dialogs.Shown).Title);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheView_ShowsTheProductItsBuildAndItsCredits()
    {
        _fixture.Run(() =>
        {
            AboutView view = new() { DataContext = new AboutViewModel("1.0.0", "9a1b2c3", "Copyright © 2026 Josué Clément", AboutViewModel.DefaultCredits) };
            Window window = new() { Content = view, Width = 480, Height = 640 };
            window.Show();

            try
            {
                string[] texts = [.. view.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text ?? string.Empty)];

                Assert.Equal(380, view.Width);
                Assert.Contains("Enigma Git Client", texts);
                Assert.Contains("1.0.0", texts);
                Assert.Contains("9a1b2c3", texts);
                Assert.Contains("Copyright © 2026 Josué Clément", texts);
                Assert.Contains("BUILT WITH", texts);

                foreach (CreditEntry credit in AboutViewModel.DefaultCredits)
                {
                    Assert.Contains(credit.Name, texts);
                }

                Image icon = Assert.Single(view.GetLogicalDescendants().OfType<Image>());
                Assert.NotNull(icon.Source);
            }
            finally
            {
                window.Content = null;
                window.Close();
            }
        });
    }

    [Fact]
    public void TheView_HidesTheBuildLineOfABuildWithNoRevision()
    {
        _fixture.Run(() =>
        {
            AboutView view = new() { DataContext = new AboutViewModel("1.0.0", null, string.Empty, []) };
            Window window = new() { Content = view };
            window.Show();

            try
            {
                StackPanel buildLine = view.GetLogicalDescendants().OfType<StackPanel>().Single(panel => panel.Name == "BuildLine");
                TextBlock copyright = view.GetLogicalDescendants().OfType<TextBlock>().Single(block => block.Name == "Copyright");

                Assert.False(buildLine.IsVisible);
                Assert.False(copyright.IsVisible);
            }
            finally
            {
                window.Content = null;
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(typeof(MainWindow))]
    [InlineData(typeof(StartWindow))]
    public void InEitherWindow_TheDialogShowsEverythingWithoutScrolling(Type windowType)
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(configure: collection =>
            {
                collection.RemoveAll<IContentDialogService>();
                collection.AddSingleton<IContentDialogService, ContentDialogService>();
            });

            Window window = (Window)services.Provider.GetRequiredService(windowType);
            ContentDialog host = ((IHostWindow)window).DialogHost;
            services.Get<IContentDialogService>().RegisterHost(host);
            window.Show();

            try
            {
                Task showing = services.Get<IAboutDialogService>().ShowAsync();

                Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                // The content row of the library's card scrolls once the card reaches its maximum
                // height; an About box whose credits need scrolling is one nobody reads to the end.
                ScrollViewer content = host.GetVisualDescendants().OfType<ScrollViewer>().First();
                Assert.True(
                    content.Extent.Height <= content.Viewport.Height + 0.5,
                    $"the About dialog scrolls: {content.Extent.Height} of content in {content.Viewport.Height}");

                await host.HideAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await showing.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(typeof(MainWindow))]
    [InlineData(typeof(StartWindow))]
    public void InEitherWindow_TheDialogShowsTheAboutViewItself(Type windowType)
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(configure: collection =>
            {
                collection.RemoveAll<IContentDialogService>();
                collection.AddSingleton<IContentDialogService, ContentDialogService>();
            });

            Window window = (Window)services.Provider.GetRequiredService(windowType);
            ContentDialog host = ((IHostWindow)window).DialogHost;
            services.Get<IContentDialogService>().RegisterHost(host);
            window.Show();

            try
            {
                Task showing = services.Get<IAboutDialogService>().ShowAsync();

                Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                // The view is in the card, drawing its own text — not a line naming its type, which is
                // what a content template made of every view in 4.1.0.
                AboutView view = Assert.IsType<AboutView>(host.Content);
                Visual[] shown = [.. host.GetVisualDescendants()];
                TextBlock[] texts = [.. shown.OfType<TextBlock>()];

                Assert.Contains(view, shown);
                Assert.Contains(texts, text => text.Text == "Enigma Git Client");
                Assert.DoesNotContain(texts, text => text.Text == view.ToString());

                await host.HideAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await showing.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheView_IsDrawn()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            AboutView view = new() { DataContext = new AboutViewModel("1.0.0", "9a1b2c3", "Copyright © 2026 Josué Clément", AboutViewModel.DefaultCredits) };
            Window window = new()
            {
                Content = new Border { Padding = new Thickness(24), Child = view },
                SizeToContent = SizeToContent.WidthAndHeight,
                Background = (IBrush?)application.FindResource(ThemeVariant.Dark, "EnigmaSurfaceHighBrush"),
            };
            window.Show();

            try
            {
                string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "about.png");

                int colours = 0;

                for (int attempt = 0; attempt < 20 && colours < 8; attempt++)
                {
                    Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                    window.UpdateLayout();
                    window.InvalidateVisual();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                    Dispatcher.UIThread.RunJobs();

                    using Bitmap frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("The About view produced no rendered frame.");

                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    colours = SnapshotColours.Count(path);
                }

                Assert.True(colours >= 8, "the About view drew nothing");
            }
            finally
            {
                window.Content = null;
                window.Close();
                application.RequestedThemeVariant = original;
            }
        });
    }
}
