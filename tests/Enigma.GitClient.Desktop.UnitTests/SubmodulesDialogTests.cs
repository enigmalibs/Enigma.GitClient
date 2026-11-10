using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Submodules;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The Submodules dialog: the toolbar button only for a repository with submodules; one line per
/// submodule, nested ones under theirs, with the expected commit and the state in words; what each
/// line's menu offers per state, a glyph on every item; and each action reaching the service.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class SubmodulesDialogTests
{
    private const string Expected = "0123456789abcdef0123456789abcdef01234567";
    private const string Other = "fedcba9876543210fedcba9876543210fedcba98";

    private readonly HeadlessAvaloniaFixture _fixture;

    public SubmodulesDialogTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static string Folder(string name) => OperatingSystem.IsWindows() ? $@"C:\src\{name}" : $"/src/{name}";

    private static RepositoryHandle HandleAt(string path) => new(path, Path.Combine(path, ".git"));

    private static GitSubmodule Submodule(
        string path,
        SubmoduleCheckoutState state,
        string? parent = null,
        bool dirty = false,
        string url = "")
        => new()
        {
            Name = path,
            Path = path,
            FullPath = Path.Combine(Folder("super"), path.Replace('/', Path.DirectorySeparatorChar)),
            Url = url,
            ExpectedSha = state == SubmoduleCheckoutState.Conflicted ? string.Empty : Expected,
            CheckedOutSha = state switch
            {
                SubmoduleCheckoutState.NotDownloaded => null,
                SubmoduleCheckoutState.UpToDate => Expected,
                _ => Other,
            },
            State = state,
            HasUncommittedChanges = dirty,
            ParentPath = parent,
        };

    private static readonly GitSubmodule Alpha = Submodule("libs/alpha", SubmoduleCheckoutState.UpToDate, url: "https://user:***@example.com/alpha.git");
    private static readonly GitSubmodule Leaf = Submodule("libs/alpha/vendor/leaf", SubmoduleCheckoutState.Behind, parent: "libs/alpha");
    private static readonly GitSubmodule Beta = Submodule("libs/beta lib", SubmoduleCheckoutState.NewCommits, dirty: true);
    private static readonly GitSubmodule Delta = Submodule("libs/delta", SubmoduleCheckoutState.NotDownloaded);
    private static readonly GitSubmodule Gamma = Submodule("libs/gamma", SubmoduleCheckoutState.Elsewhere);
    private static readonly GitSubmodule Torn = Submodule("libs/torn", SubmoduleCheckoutState.Conflicted);

    private static TestServices Build(FakeSubmoduleService fake)
        => TestServices.Build(configure: services =>
        {
            services.RemoveAll<ISubmoduleService>();
            services.AddSingleton<ISubmoduleService>(fake);
        });

    private static async Task<SubmodulesPageViewModel> ListAsync(TestServices services, FakeSubmoduleService fake, params GitSubmodule[] submodules)
    {
        fake.Submodules = submodules;
        await services.Get<IRepositoryContext>().OpenAsync(HandleAt(Folder("super")));

        SubmodulesPageViewModel page = services.Get<SubmodulesPageViewModel>();
        await page.OnAppearingAsync();

        return page;
    }

    private static SubmoduleRowViewModel Row(SubmodulesPageViewModel page, GitSubmodule submodule)
        => page.Rows.Single(row => row.Path == submodule.Path);

    // ---------------------------------------------------------------- the toolbar

    [Fact]
    public void TheToolbarButton_ShowsOnlyWhenTheRepositoryDeclaresSubmodules()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            string root = Path.Combine(services.ConfigurationRoot, "workspace");
            Directory.CreateDirectory(root);
            RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, "super"), "main");
            string folder = repository.WorkTreePath;

            IRepositoryContext context = services.Get<IRepositoryContext>();
            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.OnAppearingAsync();

            HistoryPageView view = services.Get<HistoryPageView>();
            view.DataContext = history;
            Window window = new() { Content = view, Width = 1600, Height = 700 };
            window.Show();

            try
            {
                await context.OpenAsync(repository);
                Render(window);

                Button button = view.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "OpenSubmodules");

                Assert.False(context.HasSubmodules);
                Assert.False(history.HasSubmodules);
                Assert.False(button.IsVisible);

                File.WriteAllText(Path.Combine(folder, ".gitmodules"), "[submodule \"libs/alpha\"]\n\tpath = libs/alpha\n\turl = ../alpha\n");
                await context.RefreshAsync();
                Render(window);

                Assert.True(context.HasSubmodules);
                Assert.True(history.HasSubmodules);
                Assert.True(button.IsVisible);
                Assert.Same(history.OpenSubmodulesCommand, button.Command);

                File.Delete(Path.Combine(folder, ".gitmodules"));
                await context.RefreshAsync();
                Render(window);

                Assert.False(button.IsVisible);

                // The history's own read ends before the folder is deleted.
                await WaitUntilAsync(() => !history.IsBusy);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- the rows

    [Fact]
    public void TheRows_FollowTheService_NestedOnesUnderTheirs_WithTheExpectedCommitAndTheState()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new();
            using TestServices services = Build(fake);
            SubmodulesPageViewModel page = await ListAsync(services, fake, Alpha, Leaf, Beta, Delta, Gamma, Torn);

            Assert.Equal("Submodules", page.Title);
            Assert.False(page.IsEmpty);
            Assert.Equal([Alpha.Path, Leaf.Path, Beta.Path, Delta.Path, Gamma.Path, Torn.Path], page.Rows.Select(row => row.Path));

            Assert.Equal("libs/alpha", Row(page, Alpha).DisplayPath);
            Assert.Equal(0, Row(page, Alpha).Depth);
            Assert.Equal("vendor/leaf", Row(page, Leaf).DisplayPath);
            Assert.Equal(1, Row(page, Leaf).Depth);
            Assert.Equal(SubmoduleRowViewModel.IndentPerLevel, Row(page, Leaf).Indent.Left);

            Assert.Equal("0123456", Row(page, Alpha).ExpectedShort);

            Assert.Equal("Up to date", Row(page, Alpha).State);
            Assert.Equal("Behind", Row(page, Leaf).State);
            Assert.Equal("New commits", Row(page, Beta).State);
            Assert.Equal("Not downloaded", Row(page, Delta).State);
            Assert.Equal("Elsewhere, on fedcba9", Row(page, Gamma).State);
            Assert.Equal("Conflicted", Row(page, Torn).State);

            Assert.True(Row(page, Leaf).IsAttention);
            Assert.True(Row(page, Delta).IsAttention);
            Assert.False(Row(page, Beta).IsAttention);
            Assert.False(Row(page, Gamma).IsAttention);

            Assert.True(Row(page, Beta).HasUncommittedChanges);
            Assert.False(Row(page, Alpha).HasUncommittedChanges);

            Assert.Equal($"libs/alpha{Environment.NewLine}https://user:***@example.com/alpha.git", Row(page, Alpha).Details);
            Assert.Equal("libs/delta", Row(page, Delta).Details);
        });
    }

    [Fact]
    public void WithNothingListed_OrAListGitRefused_ItSaysSo()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new();
            using TestServices services = Build(fake);
            SubmodulesPageViewModel page = await ListAsync(services, fake);

            Assert.True(page.IsEmpty);
            Assert.Equal("This repository has no submodules.", page.EmptyMessage);
            Assert.False(page.UpdateAllCommand.CanExecute(null));

            fake.ListFailure = new GitCommandException(
                new GitCommand(Folder("super"), ["ls-files"], environment: null, standardInput: null),
                128,
                "fatal: not a git repository",
                string.Empty);
            await page.ReloadAsync();

            Assert.True(page.IsEmpty);
            Assert.Contains("fatal: not a git repository", page.EmptyMessage, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TheList_FollowsARefreshWhileTheDialogIsShown()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new();
            using TestServices services = Build(fake);
            SubmodulesPageViewModel page = await ListAsync(services, fake, Delta);

            fake.Submodules = [Delta with { State = SubmoduleCheckoutState.UpToDate, CheckedOutSha = Expected }];
            await services.Get<IRepositoryContext>().RefreshAsync();
            await WaitUntilAsync(() => page.Rows.Single().State == "Up to date");

            // Closed, it reads nothing more.
            await page.OnDisappearingAsync();
            fake.Submodules = [];
            await services.Get<IRepositoryContext>().RefreshAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.Single(page.Rows);
        });
    }

    // ---------------------------------------------------------------- what the menu offers

    [Fact]
    public void TheMenusItems_AreEnabledPerState_WithWhyWhenGreyed()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new();
            using TestServices services = Build(fake);
            SubmodulesPageViewModel page = await ListAsync(services, fake, Alpha, Delta, Torn);

            SubmoduleRowViewModel alpha = Row(page, Alpha);
            Assert.True(alpha.OpenCommand.CanExecute(alpha));
            Assert.True(alpha.UpdateCommand.CanExecute(alpha));
            Assert.True(alpha.OpenFolderCommand.CanExecute(alpha));
            Assert.True(alpha.OpenTerminalCommand.CanExecute(alpha));
            Assert.True(alpha.CopyPathCommand.CanExecute(alpha));

            SubmoduleRowViewModel delta = Row(page, Delta);
            Assert.False(delta.OpenCommand.CanExecute(delta));
            Assert.False(delta.OpenTerminalCommand.CanExecute(delta));
            Assert.True(delta.UpdateCommand.CanExecute(delta));
            Assert.True(delta.OpenFolderCommand.CanExecute(delta));
            Assert.True(delta.CopyPathCommand.CanExecute(delta));
            Assert.Equal("Not downloaded yet — update it first", delta.OpenTip);

            SubmoduleRowViewModel torn = Row(page, Torn);
            Assert.False(torn.UpdateCommand.CanExecute(torn));
            Assert.Equal("Resolve its merge conflict first", torn.UpdateTip);
        });
    }

    [Fact]
    public void EveryItemOfALinesMenu_HasAGlyph()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new();
            using TestServices services = Build(fake);
            SubmodulesPageViewModel page = await ListAsync(services, fake, Alpha, Delta);

            SubmodulesPageView view = services.Get<SubmodulesPageView>();
            view.DataContext = page;
            Window window = new() { Content = view, Width = 1000, Height = 600 };
            window.Show();
            Render(window);

            try
            {
                ContextMenu[] menus = [.. view.GetVisualDescendants()
                    .OfType<Control>()
                    .Select(control => control.ContextMenu)
                    .OfType<ContextMenu>()
                    .Distinct()];

                Assert.Equal(2, menus.Length);

                foreach (ContextMenu menu in menus)
                {
                    MenuItem[] items = [.. menu.Items.OfType<MenuItem>()];

                    Assert.Equal(["Open in a new window", "Update", "Open folder", "Open a terminal", "Copy path"], items.Select(item => item.Header as string));
                    Assert.All(items, item => Assert.NotEmpty(Assert.IsAssignableFrom<Control>(item.Icon).GetSelfAndLogicalDescendants().OfType<Icon>()));
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- the actions

    [Fact]
    public void Update_AsksTheServiceForTheLinesPath_AndUpdateAll_ForEveryOne()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new() { UpdateResult = new SubmoduleUpdateResult(["libs/delta"], [], []) };
            using TestServices services = Build(fake);
            SubmodulesPageViewModel page = await ListAsync(services, fake, Alpha, Delta);

            SubmoduleRowViewModel delta = Row(page, Delta);
            await delta.UpdateCommand.ExecuteAsync(delta);

            Assert.Equal(["libs/delta"], Assert.Single(fake.Updates)!);
            Assert.False(services.Overlay.IsOpen);

            RecordedNotification done = services.InfoBar.Shown[^1];
            Assert.Equal("Submodules updated", done.Title);
            Assert.Equal(InfoBarSeverity.Success, done.Severity);

            await page.UpdateAllCommand.ExecuteAsync(null);

            Assert.Equal(2, fake.Updates.Count);
            Assert.Null(fake.Updates[1]);
        });
    }

    [Fact]
    public void AnUpdateThatLeftSubmodulesBehind_NamesEachWithItsReason()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new()
            {
                UpdateResult = new SubmoduleUpdateResult(
                    [],
                    [new SubmoduleSkip("libs/gamma", SubmoduleSkipReason.UncommittedChanges)],
                    [new SubmoduleFailure("libs/delta", "fatal: repository not found")]),
            };
            using TestServices services = Build(fake);
            SubmodulesPageViewModel page = await ListAsync(services, fake, Delta, Gamma);

            await page.UpdateAllCommand.ExecuteAsync(null);

            RecordedNotification warning = services.InfoBar.Shown[^1];
            Assert.Equal("2 submodules were not updated", warning.Title);
            Assert.Equal(InfoBarSeverity.Warning, warning.Severity);
            Assert.Contains("libs/gamma has uncommitted changes.", warning.Message, StringComparison.Ordinal);
            Assert.Contains("libs/delta could not be updated: fatal: repository not found", warning.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Open_Folder_Terminal_AndCopy_ReachTheSubmodulesFolder()
    {
        _fixture.RunAsync(async () =>
        {
            FakeSubmoduleService fake = new();
            using TestServices services = Build(fake);
            SubmodulesPageViewModel page = await ListAsync(services, fake, Alpha);
            SubmoduleRowViewModel alpha = Row(page, Alpha);

            await alpha.OpenCommand.ExecuteAsync(alpha);
            await alpha.OpenFolderCommand.ExecuteAsync(alpha);
            await alpha.OpenTerminalCommand.ExecuteAsync(alpha);
            await alpha.CopyPathCommand.ExecuteAsync(alpha);

            RecordingSystemInterop interop = (RecordingSystemInterop)services.Get<ISystemInterop>();

            Assert.Equal([Alpha.FullPath], ((RecordingInstanceLauncher)services.Get<IInstanceLauncher>()).Launched);
            Assert.Equal([Alpha.FullPath], interop.Opened);
            Assert.Equal([Alpha.FullPath], interop.Terminals);
            Assert.Equal([Alpha.FullPath], interop.Copied);
        });
    }

    // ---------------------------------------------------------------- helpers

    private static void Render(Window window)
    {
        window.UpdateLayout();

        for (int attempt = 0; attempt < 5; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int attempts = 300)
    {
        for (int attempt = 0; attempt < attempts && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "the condition never held");
    }
}
