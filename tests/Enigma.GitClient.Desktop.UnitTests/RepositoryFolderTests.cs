using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The repository window's two ways out to the desktop: the repository's folder in the file manager,
/// and a terminal in it. The desktop itself is the recording double: nothing is launched.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RepositoryFolderTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public RepositoryFolderTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A repository with no commit yet: something real to open, with no object git marks read-only.
    /// </summary>
    private static Task<RepositoryHandle> InitAsync(TestServices services, string name)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        return services.Get<IRepositoryService>().InitAsync(Path.Combine(root, name), "main");
    }

    /// <summary>
    /// Lets the history's first read — started when the repository opened — finish, so git is no
    /// longer running in the folder the test deletes once it is over.
    /// </summary>
    private static async Task LetTheHistoryFinishAsync(TestServices services)
    {
        HistoryPageViewModel history = services.Get<HistoryPageViewModel>();

        for (int attempt = 0; attempt < 500 && history.IsBusy; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    [Fact]
    public void TheToolbar_OpensTheRepositorysFolderAndATerminalInIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            MainWindowViewModel window = services.Get<MainWindowViewModel>();

            // Nothing to open before a repository is.
            Assert.False(window.OpenFolderCommand.CanExecute(null));
            Assert.False(window.OpenTerminalCommand.CanExecute(null));

            RepositoryHandle repository = await InitAsync(services, "folder");
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            Assert.True(window.OpenFolderCommand.CanExecute(null));
            Assert.True(window.OpenTerminalCommand.CanExecute(null));

            await window.OpenFolderCommand.ExecuteAsync(null);
            await window.OpenTerminalCommand.ExecuteAsync(null);

            Assert.Equal([repository.WorkTreePath], services.Interop.Opened);
            Assert.Equal([repository.WorkTreePath], services.Interop.Terminals);
            Assert.DoesNotContain(services.InfoBar.Shown, note => note.Severity == InfoBarSeverity.Warning);

            await LetTheHistoryFinishAsync(services);
            services.Get<IRepositoryContext>().Close();

            Assert.False(window.OpenFolderCommand.CanExecute(null));
            Assert.False(window.OpenTerminalCommand.CanExecute(null));
        });
    }

    [Fact]
    public void ALaunchThatFails_SaysSo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            MainWindowViewModel window = services.Get<MainWindowViewModel>();
            RepositoryHandle repository = await InitAsync(services, "nothing-opens");
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            services.Interop.OpensPaths = false;
            services.Interop.OpensTerminals = false;

            await window.OpenFolderCommand.ExecuteAsync(null);
            await window.OpenTerminalCommand.ExecuteAsync(null);

            RecordedNotification folder = services.InfoBar.Shown.Single(note => note.Title == "The folder did not open");
            RecordedNotification terminal = services.InfoBar.Shown.Single(note => note.Title == "No terminal started");

            Assert.Equal(InfoBarSeverity.Warning, folder.Severity);
            Assert.Equal(InfoBarSeverity.Warning, terminal.Severity);
            Assert.Contains(repository.WorkTreePath, folder.Message, StringComparison.Ordinal);
            Assert.Contains(repository.WorkTreePath, terminal.Message, StringComparison.Ordinal);

            await LetTheHistoryFinishAsync(services);
        });
    }

    [Fact]
    public void TheRepositoryStrip_CarriesBothButtons_WithTheirIconsAndNames()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            MainWindowViewModel viewModel = services.Get<MainWindowViewModel>();
            RepositoryHandle repository = await InitAsync(services, "strip");
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            MainWindow window = services.Get<MainWindow>();
            window.DataContext = viewModel;
            window.Measure(new Size(1400, 900));
            window.Arrange(new Rect(0, 0, 1400, 900));
            window.UpdateLayout();

            (string Name, string Automation, PhosphorIcon Icon, object Command)[] expected =
            [
                ("OpenFolder", "Open the repository's folder", PhosphorIcon.FolderOpen, viewModel.OpenFolderCommand),
                ("OpenTerminal", "Open a terminal in the repository's folder", PhosphorIcon.TerminalWindow, viewModel.OpenTerminalCommand),
            ];

            foreach ((string name, string automation, PhosphorIcon icon, object command) in expected)
            {
                Button button = window.GetVisualDescendants().OfType<Button>().Single(candidate => candidate.Name == name);

                Assert.True(button.IsVisible);
                Assert.Same(command, button.Command);
                Assert.Equal(automation, AutomationProperties.GetName(button));
                Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(button) as string));
                Assert.Equal(icon, Assert.IsType<Icon>(button.Content).Kind);
            }

            await LetTheHistoryFinishAsync(services);
        });
    }

    // ---------------------------------------------------------------- which terminal

    private static string[] Arguments(ProcessStartInfo startInfo) => [.. startInfo.ArgumentList];

    [Fact]
    public void OnWindows_WindowsTerminalIsTried_ThenTheCommandPrompt()
    {
        const string directory = @"C:\src\a repository";

        IReadOnlyList<ProcessStartInfo> candidates = SystemInterop.TerminalCandidates(DesktopPlatform.Windows, directory, terminal: null);

        Assert.Equal(["wt.exe", "cmd.exe"], candidates.Select(candidate => candidate.FileName));

        // The directory is one argument, spaces and all: nothing is parsed by a shell.
        Assert.Equal(["-d", directory], Arguments(candidates[0]));
        Assert.False(candidates[0].UseShellExecute);

        // The command prompt through the shell, so it gets a console window of its own.
        Assert.True(candidates[1].UseShellExecute);
        Assert.Equal(directory, candidates[1].WorkingDirectory);
    }

    [Fact]
    public void OnMacOS_TerminalIsOpenedOnTheDirectory()
    {
        const string directory = "/Users/ada/a repository";

        ProcessStartInfo candidate = Assert.Single(SystemInterop.TerminalCandidates(DesktopPlatform.MacOS, directory, terminal: null));

        Assert.Equal("open", candidate.FileName);
        Assert.Equal(["-a", "Terminal", directory], Arguments(candidate));
    }

    [Fact]
    public void OnLinux_TheTerminalTheUserNamedComesFirst_ThenTheUsualOnes()
    {
        const string directory = "/home/ada/a repository";

        IReadOnlyList<ProcessStartInfo> candidates = SystemInterop.TerminalCandidates(DesktopPlatform.Linux, directory, terminal: "kitty");

        Assert.Equal(
            ["kitty", "x-terminal-emulator", "gnome-terminal", "konsole", "xfce4-terminal", "xterm"],
            candidates.Select(candidate => candidate.FileName));

        // Every one starts in the directory, and is told it where it takes an option for it.
        Assert.All(candidates, candidate =>
        {
            Assert.Equal(directory, candidate.WorkingDirectory);
            Assert.False(candidate.UseShellExecute);
        });

        Assert.Equal([$"--working-directory={directory}"], Arguments(candidates[2]));
        Assert.Equal(["--workdir", directory], Arguments(candidates[3]));
        Assert.Equal([$"--working-directory={directory}"], Arguments(candidates[4]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OnLinux_WithoutATerminalNamed_TheUsualOnesAreTried(string? terminal)
    {
        IReadOnlyList<ProcessStartInfo> candidates = SystemInterop.TerminalCandidates(DesktopPlatform.Linux, "/src/x", terminal);

        Assert.Equal("x-terminal-emulator", candidates[0].FileName);
        Assert.Equal(5, candidates.Count);
    }
}
