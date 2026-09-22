using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.Navigation;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Navigation;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.Views;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives the real shell — the window, its ViewModel and the navigation rail — on the headless
/// platform.
/// </summary>
/// <remarks>
/// The container's <see cref="IRefReader"/> is replaced with <see cref="FakeRefReader"/>: the shell
/// tests are about the shell, not about git, and a reader that answers synchronously lets a test
/// block the UI thread without deadlocking on a continuation that needs it.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class MainWindowShellTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public MainWindowShellTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        App.ConfigureServices(services);

        services.RemoveAll<IRefReader>();
        services.AddSingleton<IRefReader, FakeRefReader>();

        return services.BuildServiceProvider();
    }

    private static void Layout(Window window)
    {
        window.Measure(new Size(1400, 900));
        window.Arrange(new Rect(0, 0, 1400, 900));
        window.UpdateLayout();
    }

    private static RepositoryHandle Handle(string name)
        => OperatingSystem.IsWindows()
            ? new RepositoryHandle($@"C:\src\{name}", $@"C:\src\{name}\.git")
            : new RepositoryHandle($"/src/{name}", $"/src/{name}/.git");

    [Fact]
    public void MainWindow_BuildsWithItsViewModelAndLaysOut()
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();

            MainWindow window = provider.GetRequiredService<MainWindow>();
            window.DataContext = provider.GetRequiredService<MainWindowViewModel>();

            Layout(window);

            Assert.True(window.IsMeasureValid);
            Assert.NotNull(window.HostDialog);
            Assert.NotNull(window.HostOverlay);
            Assert.NotNull(window.HostInfoBar);
        });
    }

    [Fact]
    public void MainWindow_PlacesTheThreeOverlayHostsLastSoTheyDrawOverEverything()
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();

            MainWindow window = provider.GetRequiredService<MainWindow>();
            window.DataContext = provider.GetRequiredService<MainWindowViewModel>();

            Panel root = Assert.IsType<Panel>(window.Content);

            Assert.Same(window.HostDialog, root.Children[^3]);
            Assert.Same(window.HostOverlay, root.Children[^2]);
            Assert.Same(window.HostInfoBar, root.Children[^1]);
        });
    }

    [Fact]
    public void Shell_BuildsTheNavigationRailWithEveryPage()
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            MainWindowViewModel viewModel = provider.GetRequiredService<MainWindowViewModel>();

            // Guards the cycle that would otherwise build the rail twice: a page ViewModel depends
            // on IShellNavigation, so navigating from that service's constructor resolves it again.
            Assert.Equal(5, viewModel.Navigation.Items.Count);

            // No Repositories: choosing one is the start window's business. Tags sit directly under
            // Branches: they are the other half of what the branches page used to be.
            Assert.Equal(
                ["History", "Changes", "Branches", "Tags", "Remotes"],
                viewModel.Navigation.Items.Select(item => item.Header));

            Assert.Equal(
                ["Integrations", "Settings"],
                viewModel.Navigation.FooterItems.Select(item => item.Header));

            Assert.All(
                viewModel.Navigation.Items.Concat(viewModel.Navigation.FooterItems),
                item =>
                {
                    Assert.NotNull(item.IconData);
                    Assert.NotNull(item.PageType);
                    Assert.NotNull(item.PageViewModelType);
                });
        });
    }

    [Fact]
    public void Shell_StartsOnTheHistoryPage()
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();

            IShellNavigation shell = provider.GetRequiredService<IShellNavigation>();
            shell.Start();

            Assert.Equal(ShellPage.History, shell.Current);
            Assert.Equal("History", shell.Service.SelectedItem?.Header);
        });
    }

    [Fact]
    public void Shell_NavigatesBetweenPages()
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            IShellNavigation shell = provider.GetRequiredService<IShellNavigation>();

            shell.GoTo(ShellPage.History);

            Assert.Equal(ShellPage.History, shell.Current);
            Assert.Equal("History", shell.Service.SelectedItem?.Header);

            shell.GoTo(ShellPage.Settings);

            Assert.Equal("Settings", shell.Service.SelectedItem?.Header);
        });
    }

    [Fact]
    public void Shell_ResolvesEveryPageThroughTheContainerFactory()
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            MainWindowViewModel viewModel = provider.GetRequiredService<MainWindowViewModel>();
            INavigationService navigation = viewModel.Navigation;

            foreach (NavigationItem item in navigation.Items.Concat(navigation.FooterItems))
            {
                Control page = navigation.PageFactory(item);

                Assert.IsType(item.PageType, page);
                Assert.IsType(item.PageViewModelType, page.DataContext);
            }
        });
    }

    [Fact]
    public void Shell_TitleAndStripFollowTheOpenRepository()
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            MainWindowViewModel viewModel = provider.GetRequiredService<MainWindowViewModel>();
            IRepositoryContext context = provider.GetRequiredService<IRepositoryContext>();

            Assert.Equal("Enigma.GitClient", viewModel.WindowTitle);
            Assert.Equal("No repository open", viewModel.RepositoryName);
            Assert.False(viewModel.RefreshCommand.CanExecute(null));

            // The fake reader answers synchronously, so this completes without ever yielding.
            context.OpenAsync(Handle("my-repo")).GetAwaiter().GetResult();

            Assert.Equal("my-repo", viewModel.RepositoryName);
            Assert.Equal("my-repo — Enigma.GitClient", viewModel.WindowTitle);
            Assert.True(viewModel.RefreshCommand.CanExecute(null));

            context.Close();

            Assert.Equal("No repository open", viewModel.RepositoryName);
            Assert.False(viewModel.RefreshCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Shell_ShowsTheHeadStateAndTheOperationBanner()
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            MainWindowViewModel viewModel = provider.GetRequiredService<MainWindowViewModel>();
            IRepositoryContext context = provider.GetRequiredService<IRepositoryContext>();
            FakeRefReader reader = (FakeRefReader)provider.GetRequiredService<IRefReader>();

            reader.Head = new HeadState(false, true, null, "abcdef1234567890", RepositoryOperation.Merge);
            context.OpenAsync(Handle("my-repo")).GetAwaiter().GetResult();

            Assert.True(viewModel.IsHeadDetached);
            Assert.True(viewModel.HasOperationInProgress);
            Assert.Contains("merge", viewModel.OperationDescription, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("detached at abcdef1", viewModel.HeadDisplayName);
        });
    }

    [Fact]
    public void Shell_WarnsAboutARebaseLeftBehindByAnotherToolWithoutOfferingOne()
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            MainWindowViewModel viewModel = provider.GetRequiredService<MainWindowViewModel>();
            IRepositoryContext context = provider.GetRequiredService<IRepositoryContext>();
            FakeRefReader reader = (FakeRefReader)provider.GetRequiredService<IRefReader>();

            reader.Head = new HeadState(false, false, "main", "abc", RepositoryOperation.Rebase);
            context.OpenAsync(Handle("my-repo")).GetAwaiter().GetResult();

            Assert.True(viewModel.HasOperationInProgress);
            Assert.Contains("never rebases", viewModel.OperationDescription, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ToggleThemeCommand_SwitchesTheVariantForTheWholeApplication()
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            MainWindowViewModel viewModel = provider.GetRequiredService<MainWindowViewModel>();

            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

            try
            {
                application.RequestedThemeVariant = ThemeVariant.Dark;
                viewModel.ToggleThemeCommand.Execute(null);
                Assert.Equal(ThemeVariant.Light, application.RequestedThemeVariant);

                viewModel.ToggleThemeCommand.Execute(null);
                Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void MainWindow_ShowsTheRepositoryStripAndNoOperationBanner()
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();

            MainWindow window = provider.GetRequiredService<MainWindow>();
            window.DataContext = provider.GetRequiredService<MainWindowViewModel>();
            Layout(window);

            string[] texts = [.. window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(block => block.Text ?? string.Empty)];

            Assert.Contains("No repository open", texts);

            // The warning banner binds to HasOperationInProgress and must start hidden.
            Assert.DoesNotContain(texts, text => text.Contains("in progress", StringComparison.Ordinal));
        });
    }
}
