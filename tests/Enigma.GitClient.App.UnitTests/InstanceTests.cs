using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Several instances side by side: how another one is started, and the two places that ask for one.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class InstanceTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public InstanceTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void BuildStartInfo_RunsTheExecutableAgainWithThePathAsOneArgument()
    {
        ProcessStartInfo start = InstanceLauncher.BuildStartInfo(
            "/opt/enigma/Enigma.GitClient.App",
            "/opt/enigma/Enigma.GitClient.App.dll",
            "/src/a repo; rm -rf ~")!;

        Assert.Equal("/opt/enigma/Enigma.GitClient.App", start.FileName);
        Assert.Equal(["/src/a repo; rm -rf ~"], start.ArgumentList);
        Assert.False(start.UseShellExecute);
    }

    [Fact]
    public void BuildStartInfo_WithNoRepository_StartsOnTheStartWindow()
    {
        ProcessStartInfo start = InstanceLauncher.BuildStartInfo("/opt/enigma/Enigma.GitClient.App", null, null)!;

        Assert.Empty(start.ArgumentList);
    }

    [Fact]
    public void BuildStartInfo_UnderTheDotnetHost_HandsItTheAssemblyAgain()
    {
        // The host's own path shape: a path is only split on the separators of the platform it runs on.
        string host = OperatingSystem.IsWindows()
            ? @"C:\Program Files\dotnet\dotnet.exe"
            : "/usr/share/dotnet/dotnet";

        ProcessStartInfo start = InstanceLauncher.BuildStartInfo(host, "/opt/enigma/Enigma.GitClient.App.dll", "/src/repo")!;

        Assert.Equal(host, start.FileName);
        Assert.Equal(["/opt/enigma/Enigma.GitClient.App.dll", "/src/repo"], start.ArgumentList);
    }

    [Theory]
    [InlineData(null, "/x.dll")]
    [InlineData("", "/x.dll")]
    [InlineData("/usr/bin/dotnet", null)]
    public void BuildStartInfo_WithNothingToStart_ReturnsNothing(string? processPath, string? assembly)
        => Assert.Null(InstanceLauncher.BuildStartInfo(processPath, assembly, "/src/repo"));

    [Fact]
    public void OpenInNewWindow_StartsAnotherInstanceOnThatRepositoryAndLeavesThisWindowAlone()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();

            string path = Path.Combine(services.ConfigurationRoot, "other-repo");
            Directory.CreateDirectory(path);

            await page.OpenInNewWindowCommand.ExecuteAsync(new RecentRepository(path, "other-repo", DateTimeOffset.UtcNow));

            Assert.Equal([path], services.Launcher.Launched);
            Assert.Empty(services.Windows.Requested);
            Assert.False(services.Get<IRepositoryContext>().IsRepositoryOpen);
        });
    }

    [Fact]
    public void OpenInNewWindow_OnARepositoryThatMoved_SaysSoAndStartsNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();

            string gone = Path.Combine(services.ConfigurationRoot, "gone");

            await page.OpenInNewWindowCommand.ExecuteAsync(new RecentRepository(gone, "gone", DateTimeOffset.UtcNow));

            Assert.Empty(services.Launcher.Launched);
            Assert.Equal(InfoBarSeverity.Warning, Assert.Single(services.InfoBar.Shown).Severity);
        });
    }

    [Fact]
    public void OpenInNewWindow_WhenTheInstanceCannotStart_SaysSo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Launcher.Succeeds = false;
            RepositoriesPageViewModel page = services.Get<RepositoriesPageViewModel>();

            string path = Path.Combine(services.ConfigurationRoot, "other-repo");
            Directory.CreateDirectory(path);

            await page.OpenInNewWindowCommand.ExecuteAsync(new RecentRepository(path, "other-repo", DateTimeOffset.UtcNow));

            Assert.Equal(InfoBarSeverity.Error, Assert.Single(services.InfoBar.Shown).Severity);
        });
    }

    [Fact]
    public void NewWindow_StartsAnotherInstanceOnItsStartWindow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            MainWindowViewModel shell = services.Get<MainWindowViewModel>();

            await shell.NewWindowCommand.ExecuteAsync(null);

            Assert.Equal([null], services.Launcher.Launched);
            Assert.Empty(services.InfoBar.Shown);
        });
    }

    [Fact]
    public void NewWindow_WhenTheInstanceCannotStart_SaysSo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            services.Launcher.Succeeds = false;

            await services.Get<MainWindowViewModel>().NewWindowCommand.ExecuteAsync(null);

            Assert.Equal("No new window", Assert.Single(services.InfoBar.Shown).Title);
        });
    }
}
