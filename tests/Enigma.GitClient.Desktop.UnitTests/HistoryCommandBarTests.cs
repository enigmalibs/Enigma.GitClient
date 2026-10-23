using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
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
/// The History page's command bar, which took the repository strip's buttons (FEATURE-D977): the
/// commands in the middle, labelled, in the order asked for; search on its right, and nothing else — the
/// theme and About are on the Settings page (FEATURE-47AE); over the whole page, so neither the details
/// panel nor a diff takes it away.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryCommandBarTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryCommandBarTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The repository window, shown at a known size on a repository with no commit yet, on its History
    /// page.
    /// </summary>
    private static async Task<(MainWindow Window, MainWindowViewModel Shell, HistoryPageViewModel History, HistoryPageView Page)> ShowAsync(
        TestServices services,
        double width = 1920)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);
        RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, "bar"), "main");
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        MainWindowViewModel shell = services.Get<MainWindowViewModel>();
        MainWindow window = services.Get<MainWindow>();
        window.DataContext = shell;
        window.WindowState = WindowState.Normal;
        window.Width = width;
        window.Height = 900;
        window.Show();
        window.UpdateLayout();

        HistoryPageView page = window.GetVisualDescendants().OfType<HistoryPageView>().Single();

        return (window, shell, services.Get<HistoryPageViewModel>(), page);
    }

    /// <summary>
    /// Lets the history's first read finish, so git is no longer running in the folder the test
    /// deletes once it is over.
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

    private static T Named<T>(Visual root, string name)
        where T : Control
        => root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static TextBlock LabelOf(Button button)
        => button.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("commandlabel"));

    private static Icon IconOf(Button button) => button.GetVisualDescendants().OfType<Icon>().First();

    private static Rect BoundsIn(Visual control, Visual ancestor)
    {
        Point origin = control.TranslatePoint(default, ancestor)
            ?? throw new InvalidOperationException($"{control} is not under {ancestor}.");

        return new Rect(origin, control.Bounds.Size);
    }

    [Fact]
    public void TheCommands_AreInTheOrderAskedFor_InFourGroups()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (MainWindow window, _, _, HistoryPageView page) = await ShowAsync(services);

            try
            {
                string[] read = [.. Named<StackPanel>(page, "Commands").Children
                    .Select(child => child is Button button ? LabelOf(button).Text ?? string.Empty : "|")];

                Assert.Equal(
                    ["Fetch", "Pull", "Push", "Stash", "|", "Folder", "Terminal", "|",
                     "Branches", "Worktrees", "Tags", "Remotes", "|", "Refresh", "Close"],
                    read);

                await LetTheHistoryFinishAsync(services);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void EachCommand_IsTheWindowsOrTheHistorys_WithItsTipAndItsName()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (MainWindow window, MainWindowViewModel shell, HistoryPageViewModel history, HistoryPageView page) = await ShowAsync(services);

            try
            {
                (string Name, ICommand Command)[] expected =
                [
                    ("Fetch", shell.FetchCommand),
                    ("Pull", shell.PullCommand),
                    ("Push", shell.PushCommand),
                    ("StashChanges", history.StashCommand),
                    ("OpenFolder", shell.OpenFolderCommand),
                    ("OpenTerminal", shell.OpenTerminalCommand),
                    ("OpenBranches", history.OpenBranchesCommand),
                    ("OpenWorktrees", history.OpenWorktreesCommand),
                    ("OpenTags", history.OpenTagsCommand),
                    ("OpenRemotes", history.OpenRemotesCommand),
                    ("RefreshEverything", shell.RefreshCommand),
                    ("CloseRepository", shell.CloseRepositoryCommand),
                ];

                foreach ((string name, ICommand command) in expected)
                {
                    Button button = Named<Button>(page, name);

                    Assert.Same(command, button.Command);
                    Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(button) as string), name);
                    Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(button)), name);
                }

                await LetTheHistoryFinishAsync(services);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheCommands_AreAtTheBarsCentre_WithSearchOnItsRight()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (MainWindow window, _, _, HistoryPageView page) = await ShowAsync(services);

            try
            {
                Border bar = Named<Border>(page, "CommandBar");
                Rect commands = BoundsIn(Named<StackPanel>(page, "Commands"), bar);
                Rect search = BoundsIn(Named<TextBox>(page, "SearchBox"), bar);
                Button clear = Named<Button>(page, "ClearSearch");
                Rect clearAt = BoundsIn(clear, bar);

                Assert.Equal(bar.Bounds.Width / 2, commands.Center.X, 0.5);

                // The search block ends the bar: clearing the search is its last button.
                Assert.True(search.Left > commands.Right, "the search box is not right of the commands");
                Assert.True(clearAt.Left > search.Right, "clearing the search is not right of the box");
                Assert.Equal(bar.Bounds.Width - bar.Padding.Right, clearAt.Right, 1.0);

                // At the size it always had: a toolbar button, not a command.
                Assert.Equal(18, IconOf(clear).Size);
                Assert.DoesNotContain("command", clear.Classes);

                await LetTheHistoryFinishAsync(services);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheBar_HasNeitherTheThemeSwitchNorAbout()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (MainWindow window, _, _, HistoryPageView page) = await ShowAsync(services);

            try
            {
                Border bar = Named<Border>(page, "CommandBar");

                // Both are on the Settings page: the theme preference, and its About button.
                Assert.DoesNotContain(bar.GetVisualDescendants().OfType<Button>(), button => button.Name is "SwitchTheme" or "OpenAbout");
                Assert.DoesNotContain(bar.GetVisualDescendants().OfType<Icon>(), icon => icon.Kind is PhosphorIcon.Palette or PhosphorIcon.Info);

                await LetTheHistoryFinishAsync(services);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ACommandsIcon_IsTheCommandSize_AndItsNameIsUnderIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (MainWindow window, _, _, HistoryPageView page) = await ShowAsync(services);

            try
            {
                Button[] buttons = [.. Named<StackPanel>(page, "Commands").Children.OfType<Button>()];
                Assert.Equal(12, buttons.Length);

                foreach (Button button in buttons)
                {
                    Icon icon = IconOf(button);
                    TextBlock label = LabelOf(button);
                    Rect iconAt = BoundsIn(icon, button);
                    Rect labelAt = BoundsIn(label, button);

                    Assert.Contains("command", icon.Classes);
                    Assert.Equal(24, icon.Size);
                    Assert.True(label.IsEffectivelyVisible, $"{label.Text}'s name is not shown");
                    Assert.Equal(11, label.FontSize);
                    Assert.True(labelAt.Top >= iconAt.Bottom, $"{label.Text}'s name is not under its icon");
                    Assert.Equal(iconAt.Center.X, labelAt.Center.X, 1.0);
                }

                // Bigger than the toolbar buttons they were.
                Assert.All(buttons, button => Assert.True(button.Bounds.Height > 30));

                await LetTheHistoryFinishAsync(services);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OnANarrowWindow_TheCommandsGiveUpTheirNames_AndTheSearchStaysOnTheBar()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();

            // 1920 at 150 %: too narrow for the named commands beside the search box.
            (MainWindow window, _, _, HistoryPageView page) = await ShowAsync(services, width: 1280);

            try
            {
                Border bar = Named<Border>(page, "CommandBar");
                Button clear = Named<Button>(page, "ClearSearch");
                Button[] buttons = [.. Named<StackPanel>(page, "Commands").Children.OfType<Button>()];

                Assert.True(bar.GetVisualDescendants().OfType<Controls.CommandBarPanel>().Single().IsCompact);
                Assert.All(buttons, button => Assert.False(LabelOf(button).IsEffectivelyVisible));

                // Still a command each, with its icon and its tip.
                Assert.All(buttons, button => Assert.Equal(24, IconOf(button).Size));
                Assert.All(buttons, button => Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(button) as string)));

                // And nothing is pushed off the bar.
                Assert.True(BoundsIn(clear, bar).Right <= bar.Bounds.Width - bar.Padding.Right + 1, "the search block is off the bar");
                Assert.True(Named<TextBox>(page, "SearchBox").Bounds.Width >= 90, "the search box is squeezed");

                await LetTheHistoryFinishAsync(services);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void WidenedAgain_TheCommandsTakeBackTheirNames_AtTheCentre()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (MainWindow window, _, _, HistoryPageView page) = await ShowAsync(services, width: 1280);

            try
            {
                Border bar = Named<Border>(page, "CommandBar");
                StackPanel commands = Named<StackPanel>(page, "Commands");
                Controls.CommandBarPanel panel = bar.GetVisualDescendants().OfType<Controls.CommandBarPanel>().Single();
                Assert.True(panel.IsCompact);

                // A headless window is arranged at its new width once the platform's resize has been
                // through the dispatcher.
                window.Width = 1920;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.False(panel.IsCompact);
                Assert.Equal(bar.Bounds.Width / 2, BoundsIn(commands, bar).Center.X, 0.5);

                foreach (Button button in commands.Children.OfType<Button>())
                {
                    TextBlock label = LabelOf(button);

                    // Shown, laid out at its own width, under its icon.
                    Assert.True(label.IsEffectivelyVisible, $"{label.Text}'s name is not shown");
                    Assert.True(label.Bounds.Width > 0, $"{label.Text}'s name has no width");
                    Assert.True(BoundsIn(label, button).Top >= BoundsIn(IconOf(button), button).Bottom);
                    Assert.True(button.Bounds.Width >= 56, $"{label.Text} is narrower than a named command");
                }

                await LetTheHistoryFinishAsync(services);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheRepositoryStrip_HasNoButtonLeft()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (MainWindow window, _, _, _) = await ShowAsync(services);

            try
            {
                Assert.Empty(Named<Border>(window, "RepositoryStrip").GetVisualDescendants().OfType<Button>());

                await LetTheHistoryFinishAsync(services);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheBar_SpansThePage_OverTheDetailsPanelAndADiff()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (MainWindow window, _, _, HistoryPageView page) = await ShowAsync(services);

            try
            {
                DockPanel root = Named<DockPanel>(page, "PageRoot");
                Border bar = Named<Border>(page, "CommandBar");
                Border details = Named<Border>(page, "DetailsPanel");
                Border diff = Named<Border>(page, "DiffPage");

                // Both shown, as a selected line and a picked file show them.
                details.IsVisible = true;
                diff.IsVisible = true;
                window.UpdateLayout();

                Assert.Equal(root.Bounds.Width, bar.Bounds.Width);
                Assert.True(bar.IsEffectivelyVisible);
                Assert.True(BoundsIn(details, root).Top >= BoundsIn(bar, root).Bottom, "the details panel is over the bar");
                Assert.True(BoundsIn(diff, root).Top >= BoundsIn(bar, root).Bottom, "the diff is over the bar");

                await LetTheHistoryFinishAsync(services);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PullAndPush_SayHowManyCommitsAreWaiting_OnlyWhenThereAreAny()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build();
            (MainWindow window, _, _, HistoryPageView page) = await ShowAsync(services);

            try
            {
                Border behind = Named<Border>(page, "BehindCount");
                Border ahead = Named<Border>(page, "AheadCount");

                Assert.False(behind.IsVisible);
                Assert.False(ahead.IsVisible);

                FakeRefReader reader = (FakeRefReader)services.Get<IRefReader>();
                reader.Head = new HeadState(false, false, "main", "abc", RepositoryOperation.None);
                reader.Refs = new RefCollection(
                    [
                        new GitBranch(
                            "refs/heads/main",
                            "abc",
                            isCurrent: true,
                            upstreamShortName: "origin/main",
                            new BranchTracking(Ahead: 2, Behind: 3, IsUpstreamGone: false),
                            GitSignature.Empty,
                            DateTimeOffset.UnixEpoch,
                            "Subject"),
                    ],
                    [],
                    [],
                    []);
                await services.Get<IRepositoryContext>().RefreshAsync();
                window.UpdateLayout();

                Assert.True(behind.IsVisible);
                Assert.True(ahead.IsVisible);
                Assert.Equal("3", behind.GetVisualDescendants().OfType<TextBlock>().Single().Text);
                Assert.Equal("2", ahead.GetVisualDescendants().OfType<TextBlock>().Single().Text);

                // On the icon's corner, so the button is as wide as it was.
                Button pull = Named<Button>(page, "Pull");
                Assert.Equal(Named<Button>(page, "Fetch").Bounds.Width, pull.Bounds.Width);

                await LetTheHistoryFinishAsync(services);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
