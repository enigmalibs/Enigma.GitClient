using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Controls;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels;
using Enigma.GitClient.Desktop.Views;
using Enigma.Icons.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The repository strip, the window's top bar: the open repository's name and icon, drawn larger than
/// a page's title, and where HEAD is, in the badge the history gives the checked-out branch.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RepositoryStripTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public RepositoryStripTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        App.ConfigureServices(services);

        services.RemoveAll<IRefReader>();
        services.AddSingleton<IRefReader, FakeRefReader>();

        return services.BuildServiceProvider();
    }

    private static RepositoryHandle Handle(string name)
        => OperatingSystem.IsWindows()
            ? new RepositoryHandle($@"C:\src\{name}", $@"C:\src\{name}\.git")
            : new RepositoryHandle($"/src/{name}", $"/src/{name}/.git");

    /// <summary>
    /// The window, on a repository whose HEAD is <paramref name="head"/>, laid out.
    /// </summary>
    private static MainWindow OpenOn(ServiceProvider provider, HeadState head)
    {
        ((FakeRefReader)provider.GetRequiredService<IRefReader>()).Head = head;

        // The fake reader answers synchronously, so this completes without ever yielding.
        provider.GetRequiredService<IRepositoryContext>().OpenAsync(Handle("my-repo")).GetAwaiter().GetResult();

        MainWindow window = provider.GetRequiredService<MainWindow>();
        window.DataContext = provider.GetRequiredService<MainWindowViewModel>();
        Layout(window);

        return window;
    }

    private static void Layout(Window window)
    {
        window.Measure(new Size(1400, 900));
        window.Arrange(new Rect(0, 0, 1400, 900));
        window.UpdateLayout();
    }

    private static T Named<T>(Visual root, string name)
        where T : Control
        => root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void WithVariant(ThemeVariant variant, Action work)
    {
        Application application = Application.Current!;
        ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

        try
        {
            application.RequestedThemeVariant = variant;
            work();
        }
        finally
        {
            application.RequestedThemeVariant = original;
        }
    }

    private static HeadState OnMain => new(false, false, "main", "abcdef1234567890", RepositoryOperation.None);

    [Fact]
    public void TheRepositorysNameAndIcon_AreLargerThanAPagesTitle()
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            MainWindow window = OpenOn(provider, OnMain);

            TextBlock title = Named<TextBlock>(window, "RepositoryTitle");
            Icon icon = Named<Icon>(window, "RepositoryIcon");

            Assert.Equal("my-repo", title.Text);
            Assert.Equal(18, title.FontSize);
            Assert.Equal(FontWeight.SemiBold, title.FontWeight);

            // Its own role on the icon scale, a step above a page's title icon.
            Assert.Contains("title", icon.Classes);
            Assert.Equal(24, icon.Size);

            // The tree a linked worktree adds after the name is the same size as the icon before it.
            Icon tree = Named<Border>(window, "LinkedWorktreeMark").GetVisualDescendants().OfType<Icon>().Single();
            Assert.Equal(24, tree.Size);

            window.Close();
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void TheBranchBadge_IsTheBadgeTheHistoryGivesTheCheckedOutBranch(string variant)
    {
        _fixture.Run(() => WithVariant(variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, () =>
        {
            using ServiceProvider provider = BuildProvider();
            MainWindow window = OpenOn(provider, OnMain);

            RefBadge strip = Named<RefBadge>(window, "HeadBadge");

            // As a history line declares it for the branch HEAD is on.
            RefBadge line = new() { Kind = GitRefKind.LocalBranch, Text = "main", IsCurrent = true };
            Window host = new() { Content = line, Width = 300, Height = 100 };
            Layout(host);

            Assert.True(strip.IsVisible);
            Assert.Equal("main", strip.Text);
            Assert.Contains("head", strip.Classes);

            // The light blue and the almost-white text, whichever theme is on.
            Application application = Application.Current!;
            Assert.True(application.TryFindResource("RefBadgeHeadBrush", application.ActualThemeVariant, out object? blue));
            Assert.Same(blue, strip.Background);
            Assert.Same(line.Background, strip.Background);
            Assert.Equal(
                Assert.IsAssignableFrom<ISolidColorBrush>(line.Foreground).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(strip.Foreground).Color);

            // And the same size: the same text, the same height.
            TextBlock Label(RefBadge badge) => badge.GetVisualDescendants().OfType<TextBlock>().Single();

            Assert.Equal(Label(line).FontSize, Label(strip).FontSize);
            Assert.Equal(line.Bounds.Height, strip.Bounds.Height);
            Assert.Equal(line.Bounds.Width, strip.Bounds.Width);

            host.Close();
            window.Close();
        }));
    }

    [Fact]
    public void ADetachedHead_HasNoBranchBadge_AndSaysWhereItIs()
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            MainWindow window = OpenOn(provider, new HeadState(false, true, null, "abcdef1234567890", RepositoryOperation.None));

            Border pill = Named<Border>(window, "DetachedHeadPill");

            Assert.False(Named<RefBadge>(window, "HeadBadge").IsVisible);
            Assert.True(pill.IsVisible);
            Assert.Equal("detached at abcdef1", pill.GetVisualDescendants().OfType<TextBlock>().Single().Text);

            window.Close();
        });
    }

    [Fact]
    public void IsOnBranch_FollowsTheRepositoryAndHead()
    {
        _fixture.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            MainWindowViewModel viewModel = provider.GetRequiredService<MainWindowViewModel>();
            IRepositoryContext context = provider.GetRequiredService<IRepositoryContext>();
            FakeRefReader reader = (FakeRefReader)provider.GetRequiredService<IRefReader>();

            Assert.False(viewModel.IsOnBranch);

            // A fresh repository's branch does not exist yet, and HEAD is still on it.
            reader.Head = HeadState.Unborn("main");
            context.OpenAsync(Handle("my-repo")).GetAwaiter().GetResult();
            Assert.True(viewModel.IsOnBranch);

            context.Close();
            Assert.False(viewModel.IsOnBranch);

            reader.Head = new HeadState(false, true, null, "abcdef1234567890", RepositoryOperation.None);
            context.OpenAsync(Handle("my-repo")).GetAwaiter().GetResult();
            Assert.False(viewModel.IsOnBranch);
        });
    }
}
