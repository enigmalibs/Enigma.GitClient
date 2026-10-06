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
using Enigma.GitClient.Core.Identity;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The two ways out to the desktop — the repository's folder in the file manager, and a terminal in
/// it — from the repository window's toolbar and from every row of the home list (FEATURE-630E). The
/// desktop itself is the recording double: nothing is launched.
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

    // ---------------------------------------------------------------- the home list

    /// <summary>
    /// The home page listing one repository, whose folder exists or not.
    /// </summary>
    private static async Task<(RepositoriesPageViewModel Model, ListedRepository Entry)> ListOneAsync(TestServices services, bool exists)
    {
        string path = Path.Combine(services.ConfigurationRoot, "listed");

        if (exists)
        {
            Directory.CreateDirectory(path);
        }

        await services.Get<IRepositoryListStore>().AddAsync(IdentityProfile.DefaultId, path, "listed");

        RepositoriesPageViewModel model = services.Get<RepositoriesPageViewModel>();
        await model.OnAppearingAsync();

        return (model, Assert.Single(model.Repositories));
    }

    [Fact]
    public void TheHomeList_OpensARowsFolderAndATerminalInIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, ListedRepository entry) = await ListOneAsync(services, exists: true);

            await model.OpenFolderCommand.ExecuteAsync(entry);
            await model.OpenTerminalCommand.ExecuteAsync(entry);

            Assert.Equal([entry.Path], services.Interop.Opened);
            Assert.Equal([entry.Path], services.Interop.Terminals);
            Assert.DoesNotContain(services.InfoBar.Shown, note => note.Severity == InfoBarSeverity.Warning);
        });
    }

    [Fact]
    public void TheHomeList_SaysARepositoryHasMoved_AndLaunchesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, ListedRepository entry) = await ListOneAsync(services, exists: false);

            await model.OpenFolderCommand.ExecuteAsync(entry);
            await model.OpenTerminalCommand.ExecuteAsync(entry);

            Assert.Empty(services.Interop.Opened);
            Assert.Empty(services.Interop.Terminals);
            Assert.Equal(2, services.InfoBar.Shown.Count(note => note.Title == "That repository has moved"));
        });
    }

    [Fact]
    public void TheHomeList_SaysSoWhenALaunchFails()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, ListedRepository entry) = await ListOneAsync(services, exists: true);

            services.Interop.OpensPaths = false;
            services.Interop.OpensTerminals = false;

            await model.OpenFolderCommand.ExecuteAsync(entry);
            await model.OpenTerminalCommand.ExecuteAsync(entry);

            // The toolbar's own words for the same two failures.
            RecordedNotification folder = services.InfoBar.Shown.Single(note => note.Title == "The folder did not open");
            RecordedNotification terminal = services.InfoBar.Shown.Single(note => note.Title == "No terminal started");

            Assert.Equal(InfoBarSeverity.Warning, folder.Severity);
            Assert.Equal(InfoBarSeverity.Warning, terminal.Severity);
            Assert.Contains(entry.Path, folder.Message, StringComparison.Ordinal);
            Assert.Contains(entry.Path, terminal.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EveryRowOfTheHomeList_CarriesBothButtons_WithTheirIconsAndNames()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (RepositoriesPageViewModel model, ListedRepository entry) = await ListOneAsync(services, exists: true);

            RepositoriesPageView page = services.Get<RepositoriesPageView>();
            page.DataContext = model;

            Window window = new() { Content = page, Width = 1100, Height = 700 };
            window.Show();
            window.UpdateLayout();

            (string Automation, PhosphorIcon Icon, object Command)[] expected =
            [
                ("Open the repository's folder", PhosphorIcon.FolderOpen, model.OpenFolderCommand),
                ("Open a terminal in the repository's folder", PhosphorIcon.TerminalWindow, model.OpenTerminalCommand),
            ];

            foreach ((string automation, PhosphorIcon icon, object command) in expected)
            {
                Button button = page.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(candidate => AutomationProperties.GetName(candidate) == automation);

                Assert.True(button.IsVisible);
                Assert.Same(command, button.Command);
                Assert.Equal(entry, button.CommandParameter);
                Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(button) as string));
                Assert.Equal(icon, Assert.IsType<Icon>(button.Content).Kind);
            }

            window.Close();
        });
    }

    // ---------------------------------------------------------------- what counts as launched

    [Fact]
    public void AStartThatHandsTheRequestToARunningProcess_IsALaunch()
    {
        // What ShellExecute answers on Windows when Explorer, already running, opens the folder: no
        // process of its own, and the folder on screen. It used to be reported as "did not open"
        // (BUG-6EA3).
        ProcessStartInfo folder = new(@"C:\src\a repository") { UseShellExecute = true };

        Assert.True(SystemInterop.Launch(folder, _ => null));
    }

    [Fact]
    public void AStartThatStartsAProcess_IsALaunch()
    {
        ProcessStartInfo program = new("xdg-open") { UseShellExecute = false };

        Assert.True(SystemInterop.Launch(program, _ => Process.GetCurrentProcess()));
    }

    public static TheoryData<Exception> Refusals() => new()
    {
        new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified."),
        new InvalidOperationException("No file name was specified."),
        new PlatformNotSupportedException(),
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void AStartThatFails_IsNoLaunch(Exception refusal)
    {
        ProcessStartInfo folder = new("/nowhere") { UseShellExecute = true };

        Assert.False(SystemInterop.Launch(folder, _ => throw refusal));
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
