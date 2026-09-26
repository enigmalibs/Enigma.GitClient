using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.App.Navigation;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// The start window, the repository window, and the hand-over between them — with real windows on
/// the headless platform.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class AppWindowsTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public AppWindowsTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The test container, with the real window coordinator put back in place of the recording one.
    /// </summary>
    private static TestServices Build(bool useRealRefReader = false)
        => TestServices.Build(useRealRefReader, services =>
        {
            services.RemoveAll<IAppWindows>();
            services.AddSingleton<IAppWindows, AppWindows>();
        });

    private static void CloseWindow(IAppWindows windows) => windows.CurrentWindow?.Close();

    [Fact]
    public void ShowStart_ShowsTheStartWindowOnTheRepositoriesPage()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            IAppWindows windows = services.Get<IAppWindows>();

            try
            {
                windows.ShowStart();

                StartWindow window = Assert.IsType<StartWindow>(windows.CurrentWindow);
                StartWindowViewModel viewModel = Assert.IsType<StartWindowViewModel>(window.DataContext);
                await viewModel.InitialiseAsync();

                Assert.Equal(AppWindowKind.Start, windows.Current);
                Assert.True(window.IsVisible);
                Assert.Equal(StartPage.Repositories, viewModel.StartNavigation.Current);
                Assert.IsType<RepositoriesPageView>(viewModel.Navigation.CurrentPage);
            }
            finally
            {
                CloseWindow(windows);
            }
        });
    }

    [Fact]
    public void StartWindow_OffersTheRepositoriesTheIdentityTheIntegrationsAndTheSettingsOnly()
    {
        _fixture.Run(() =>
        {
            using TestServices services = Build();
            StartWindowViewModel viewModel = services.Get<StartWindowViewModel>();

            Assert.Equal(["Repositories"], viewModel.Navigation.Items.Select(item => item.Header));
            Assert.Equal(["Identity", "Integrations", "Settings"], viewModel.Navigation.FooterItems.Select(item => item.Header));
        });
    }

    [Fact]
    public void ShowRepository_ReplacesTheStartWindowAndClosesIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            IAppWindows windows = services.Get<IAppWindows>();

            try
            {
                windows.ShowStart();
                Window start = windows.CurrentWindow!;

                windows.ShowRepository();

                MainWindow repository = Assert.IsType<MainWindow>(windows.CurrentWindow);
                await ((MainWindowViewModel)repository.DataContext!).InitialiseAsync();

                Assert.Equal(AppWindowKind.Repository, windows.Current);
                Assert.True(repository.IsVisible);
                Assert.False(start.IsVisible);
                Assert.Null(start.DataContext);
                Assert.Equal(ShellPage.History, services.Get<IShellNavigation>().Current);
            }
            finally
            {
                CloseWindow(windows);
            }
        });
    }

    [Fact]
    public void GoingBackAndForth_BuildsFreshWindowsAndKeepsThePages()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            IAppWindows windows = services.Get<IAppWindows>();

            try
            {
                windows.ShowStart();
                await ((StartWindowViewModel)windows.CurrentWindow!.DataContext!).InitialiseAsync();
                Control repositoriesPage = services.Get<StartWindowViewModel>().Navigation.CurrentPage!;

                windows.ShowRepository();
                Window firstRepositoryWindow = windows.CurrentWindow!;
                await ((MainWindowViewModel)firstRepositoryWindow.DataContext!).InitialiseAsync();
                Control historyPage = services.Get<MainWindowViewModel>().Navigation.CurrentPage!;

                // The same pages, in new windows: a page is freed by the window it leaves, or it
                // could not be put into the next one.
                windows.ShowStart();
                Assert.Same(repositoriesPage, services.Get<StartWindowViewModel>().Navigation.CurrentPage);

                windows.ShowRepository();
                Assert.NotSame(firstRepositoryWindow, windows.CurrentWindow);
                Assert.Same(historyPage, services.Get<MainWindowViewModel>().Navigation.CurrentPage);
                Assert.Same(windows.CurrentWindow, TopLevel.GetTopLevel(historyPage));
            }
            finally
            {
                CloseWindow(windows);
            }
        });
    }

    [Fact]
    public void ShowRepository_WhileItIsAlreadyShown_KeepsTheSameWindow()
    {
        _fixture.Run(() =>
        {
            using TestServices services = Build();
            IAppWindows windows = services.Get<IAppWindows>();

            try
            {
                windows.ShowRepository();
                Window first = windows.CurrentWindow!;

                // A clone started from the repository window's integrations page asks again.
                windows.ShowRepository();

                Assert.Same(first, windows.CurrentWindow);
                Assert.True(first.IsVisible);
            }
            finally
            {
                CloseWindow(windows);
            }
        });
    }

    [Fact]
    public void StartAsync_WithARepositoryPath_OpensStraightIntoIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build(useRealRefReader: true);
            IAppWindows windows = services.Get<IAppWindows>();

            string root = Path.Combine(services.ConfigurationRoot, "workspace");
            Directory.CreateDirectory(root);
            RepositoryHandle repository = await services.Get<IRepositoryService>()
                .InitAsync(Path.Combine(root, "from-the-command-line"), "main");

            try
            {
                await windows.StartAsync(repository.WorkTreePath);

                Assert.Equal(AppWindowKind.Repository, windows.Current);
                Assert.Equal("from-the-command-line", services.Get<IRepositoryContext>().Repository?.Name);
                Assert.Empty(services.InfoBar.Shown);
            }
            finally
            {
                CloseWindow(windows);
            }
        });
    }

    [Fact]
    public void StartAsync_WithAPathThatIsNoRepository_OpensTheStartWindowAndSaysSo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            IAppWindows windows = services.Get<IAppWindows>();

            string notARepository = Path.Combine(services.ConfigurationRoot, "just-a-folder");
            Directory.CreateDirectory(notARepository);

            try
            {
                await windows.StartAsync(notARepository);

                Assert.Equal(AppWindowKind.Start, windows.Current);
                Assert.False(services.Get<IRepositoryContext>().IsRepositoryOpen);

                RecordedNotification notice = Assert.Single(services.InfoBar.Shown);
                Assert.Equal(InfoBarSeverity.Warning, notice.Severity);
                Assert.Contains(notARepository, notice.Message, StringComparison.Ordinal);
            }
            finally
            {
                CloseWindow(windows);
            }
        });
    }

    [Fact]
    public void StartAsync_WithNoPath_OpensTheStartWindow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            IAppWindows windows = services.Get<IAppWindows>();

            try
            {
                await windows.StartAsync(null);

                Assert.Equal(AppWindowKind.Start, windows.Current);
                Assert.Empty(services.InfoBar.Shown);
            }
            finally
            {
                CloseWindow(windows);
            }
        });
    }

    [Fact]
    public void CloseRepository_ClosesItAndAsksForTheStartWindow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            IRepositoryContext context = services.Get<IRepositoryContext>();
            MainWindowViewModel viewModel = services.Get<MainWindowViewModel>();

            await context.OpenAsync(OperatingSystem.IsWindows()
                ? new RepositoryHandle(@"C:\src\closing", @"C:\src\closing\.git")
                : new RepositoryHandle("/src/closing", "/src/closing/.git"));

            viewModel.CloseRepositoryCommand.Execute(null);

            Assert.False(context.IsRepositoryOpen);
            Assert.Equal([AppWindowKind.Start], services.Windows.Requested);
        });
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "  " }, null)]
    [InlineData(new[] { "/src/repo" }, "/src/repo")]
    [InlineData(new[] { "/src/repo", "--ignored" }, "/src/repo")]
    public void FirstArgument_IsTheRepositoryToOpen(string[]? args, string? expected)
        => Assert.Equal(expected, App.FirstArgument(args));
}
