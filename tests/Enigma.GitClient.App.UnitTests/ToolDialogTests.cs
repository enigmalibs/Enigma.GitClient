using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Branches, tags and remotes as dialogs over the history, on a host of their own.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ToolDialogTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly HeadlessAvaloniaFixture _fixture;

    public ToolDialogTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static RepositoryHandle Handle(string name)
        => OperatingSystem.IsWindows()
            ? new RepositoryHandle($@"C:\src\{name}", $@"C:\src\{name}\.git")
            : new RepositoryHandle($"/src/{name}", $"/src/{name}/.git");

    /// <summary>
    /// A container whose dialog service is the real one, so a question asked from inside a tool
    /// dialog really lands on the operations host.
    /// </summary>
    private static TestServices Build()
        => TestServices.Build(configure: services =>
        {
            services.RemoveAll<IContentDialogService>();
            services.AddSingleton<IContentDialogService, ContentDialogService>();
        });

    /// <summary>
    /// A shown repository window with both dialog hosts handed over.
    /// </summary>
    private static MainWindow Show(TestServices services)
    {
        MainWindow window = services.Get<MainWindow>();
        window.DataContext = services.Get<MainWindowViewModel>();

        services.Get<IContentDialogService>().RegisterHost(window.HostDialog);
        services.Get<IToolDialogService>().RegisterHost(window.ToolDialog);

        window.Show();
        return window;
    }

    [Theory]
    [InlineData(ToolDialog.Branches, typeof(BranchesPageView), typeof(BranchesPageViewModel))]
    [InlineData(ToolDialog.Tags, typeof(TagsPageView), typeof(TagsPageViewModel))]
    [InlineData(ToolDialog.Remotes, typeof(RemotesPageView), typeof(RemotesPageViewModel))]
    public void ShowAsync_ShowsThePageWithItsViewModelUntilItIsClosed(ToolDialog dialog, Type view, Type viewModel)
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            MainWindow window = Show(services);
            IToolDialogService tools = services.Get<IToolDialogService>();

            try
            {
                Task showing = tools.ShowAsync(dialog);

                Assert.True(tools.IsOpen);
                Assert.True(window.ToolDialog.IsOpen);
                Control page = Assert.IsAssignableFrom<Control>(window.ToolDialog.Content);
                Assert.IsType(view, page);
                Assert.IsType(viewModel, page.DataContext);
                Assert.Equal("Close", window.ToolDialog.CloseButtonText);

                // Shown, not only set: a content template once drew the page's type name in its place.
                Settle(window);
                Assert.Contains(page, window.ToolDialog.GetVisualDescendants());

                await window.ToolDialog.HideAsync().WaitAsync(Patience);
                await showing.WaitAsync(Patience);

                Assert.False(tools.IsOpen);
                Assert.Null(window.ToolDialog.Content);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AQuestionAskedFromInsideATool_OpensAboveItAndLeavesItOpen()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            MainWindow window = Show(services);
            IToolDialogService tools = services.Get<IToolDialogService>();

            try
            {
                Task showing = tools.ShowAsync(ToolDialog.Branches);
                object? branches = window.ToolDialog.Content;

                // What deleting a branch from the dialog does: ask, on the operations host.
                Task<DialogResult> question = services.Get<IContentDialogService>().ShowAsync(dialog =>
                {
                    dialog.Title = "Delete the branch";
                    dialog.PrimaryButtonText = "Delete";
                    dialog.CloseButtonText = "Keep it";
                });

                Assert.True(window.HostDialog.IsOpen);
                Assert.True(window.ToolDialog.IsOpen);
                Assert.Same(branches, window.ToolDialog.Content);

                // Drawn above it: the operations host comes after the tool host in the window.
                Panel root = Assert.IsType<Panel>(window.Content);
                Assert.True(root.Children.IndexOf(window.HostDialog) > root.Children.IndexOf(window.ToolDialog));

                await window.HostDialog.HideAsync().WaitAsync(Patience);
                await question.WaitAsync(Patience);

                Assert.True(window.ToolDialog.IsOpen);
                Assert.Same(branches, window.ToolDialog.Content);

                await window.ToolDialog.HideAsync().WaitAsync(Patience);
                await showing.WaitAsync(Patience);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ASecondToolWhileOneIsOpen_IsNotShownOverIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            MainWindow window = Show(services);
            IToolDialogService tools = services.Get<IToolDialogService>();

            try
            {
                Task showing = tools.ShowAsync(ToolDialog.Branches);
                await tools.ShowAsync(ToolDialog.Tags).WaitAsync(Patience);

                Assert.IsType<BranchesPageView>(window.ToolDialog.Content);

                await window.ToolDialog.HideAsync().WaitAsync(Patience);
                await showing.WaitAsync(Patience);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void APageThatFailsToAppear_IsLoggedAndTheDialogStaysOpen()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            MainWindow window = Show(services);
            IToolDialogService tools = services.Get<IToolDialogService>();

            try
            {
                // No such directory: the remotes page's git call fails as it appears.
                await services.Get<IRepositoryContext>().OpenAsync(Handle("nowhere"));

                Task showing = tools.ShowAsync(ToolDialog.Remotes);

                Assert.True(window.ToolDialog.IsOpen);
                Assert.False(showing.IsFaulted);

                await window.ToolDialog.HideAsync().WaitAsync(Patience);
                await showing.WaitAsync(Patience);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ShowAsync_BeforeAHostIsRegistered_SaysSo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => services.Get<IToolDialogService>().ShowAsync(ToolDialog.Tags));
        });
    }

    [Fact]
    public void TheHistoryToolbar_OpensEachToolOnceARepositoryIsOpen()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            MainWindow window = Show(services);
            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();

            try
            {
                await history.OnAppearingAsync();
                Assert.False(history.OpenBranchesCommand.CanExecute(null));

                // A real repository: the remotes page reads git as it appears.
                string root = System.IO.Path.Combine(services.ConfigurationRoot, "workspace");
                System.IO.Directory.CreateDirectory(root);
                RepositoryHandle repository = await services.Get<IRepositoryService>()
                    .InitAsync(System.IO.Path.Combine(root, "tools"), "main");
                await services.Get<IRepositoryContext>().OpenAsync(repository);

                (AsyncRelayCommandProbe command, Type page)[] buttons =
                [
                    (new(history.OpenBranchesCommand), typeof(BranchesPageView)),
                    (new(history.OpenTagsCommand), typeof(TagsPageView)),
                    (new(history.OpenRemotesCommand), typeof(RemotesPageView)),
                ];

                foreach ((AsyncRelayCommandProbe command, Type page) in buttons)
                {
                    Assert.True(command.CanExecute);

                    Task running = command.Execute();

                    Assert.IsType(page, window.ToolDialog.Content);

                    await window.ToolDialog.HideAsync().WaitAsync(Patience);
                    await running.WaitAsync(Patience);
                }

                // The history reads the repository it was told of on its own, and nothing awaits that
                // reading. It ends before the repository is deleted: a git still running in it holds
                // the directory.
                await WaitUntilAsync(() => history.IsNotBusy);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Pumps the dispatcher until the condition holds.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

        while (!condition())
        {
            Assert.True(watch.Elapsed < Patience, "the condition never held");

            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public void TheToolDialog_IsDrawnOnTheSecondaryDialogSurface_InBothThemes()
    {
        _fixture.RunAsync(async () =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            using TestServices services = Build();
            MainWindow window = Show(services);
            IToolDialogService tools = services.Get<IToolDialogService>();

            try
            {
                // The library's own look for content laid out for the window's background — not a
                // resource redefined on the host to get it.
                Assert.Contains("secondary", window.ToolDialog.Classes);
                Assert.False(window.ToolDialog.Resources.ContainsKey("EnigmaSurfaceHighBrush"));

                Task showing = tools.ShowAsync(ToolDialog.Tags);

                // Switched while the dialog is open: the card follows the theme, it is not a colour
                // frozen at the moment it was first drawn.
                foreach (ThemeVariant variant in (ThemeVariant[])[ThemeVariant.Dark, ThemeVariant.Light])
                {
                    application.RequestedThemeVariant = variant;
                    Settle(window);

                    Assert.Equal(Colour(window, "EnigmaDialogSecondaryBackgroundColor"), CardColour(window.ToolDialog));
                    Assert.NotEqual(Colour(window, "EnigmaSurfaceHighColor"), CardColour(window.ToolDialog));
                }

                await window.ToolDialog.HideAsync().WaitAsync(Patience);
                await showing.WaitAsync(Patience);
            }
            finally
            {
                window.Close();
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void AQuestion_KeepsTheLibrarysDialogSurface()
    {
        _fixture.RunAsync(async () =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            using TestServices services = Build();
            MainWindow window = Show(services);

            try
            {
                Task<DialogResult> question = services.Get<IContentDialogService>().ShowAsync(dialog =>
                {
                    dialog.Title = "Delete the branch";
                    dialog.CloseButtonText = "Keep it";
                });

                Settle(window);

                Assert.DoesNotContain("secondary", window.HostDialog.Classes);
                Assert.Equal(Colour(window, "EnigmaSurfaceHighColor"), CardColour(window.HostDialog));

                await window.HostDialog.HideAsync().WaitAsync(Patience);
                await question.WaitAsync(Patience);
            }
            finally
            {
                window.Close();
                application.RequestedThemeVariant = original;
            }
        });
    }

    /// <summary>
    /// Lets the dialog open, template itself and resolve its resources.
    /// </summary>
    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The colour a theme key has in the window's current theme.
    /// </summary>
    private static Color Colour(Window window, string key)
        => window.TryFindResource(key, window.ActualThemeVariant, out object? value) && value is Color colour
            ? colour
            : throw new InvalidOperationException($"The theme has no colour {key}.");

    /// <summary>
    /// The colour the dialog's card is painted in — its template's <c>PART_Card</c>.
    /// </summary>
    private static Color CardColour(ContentDialog dialog)
    {
        Border card = dialog.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Name == "PART_Card" && ReferenceEquals(border.TemplatedParent, dialog));

        return Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color;
    }

    [Fact]
    public void TheToolDialog_IsAsLargeAsTheWindowAllows()
    {
        Assert.Equal(new Size(1100, 760), MainWindow.ToolDialogSizeFor(new Size(1440, 900)));
        Assert.Equal(new Size(804, 464), MainWindow.ToolDialogSizeFor(new Size(900, 560)));
        Assert.Equal(new Size(0, 0), MainWindow.ToolDialogSizeFor(new Size(50, 50)));
    }

    [Fact]
    public async Task AStamp_ChangesWhenAReferenceMovesAndOnlyThen()
    {
        FakeRefReader reader = new();
        using RepositoryContext context = new(reader, Microsoft.Extensions.Logging.Abstractions.NullLogger<RepositoryContext>.Instance);

        reader.Head = new HeadState(false, false, "main", "aaa", RepositoryOperation.None);
        reader.Refs = new RefCollection([Branch("main", "aaa")], [], [], []);
        await context.OpenAsync(Handle("stamps"), TestContext.Current.CancellationToken);

        RepositoryStateStamp first = RepositoryStateStamp.Of(context);

        // The same state, read again: new objects, the same stamp.
        await context.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(first, RepositoryStateStamp.Of(context));

        reader.Refs = new RefCollection([Branch("main", "aaa"), Branch("feature", "bbb")], [], [], []);
        await context.RefreshAsync(TestContext.Current.CancellationToken);
        RepositoryStateStamp second = RepositoryStateStamp.Of(context);
        Assert.NotEqual(first, second);

        reader.Head = new HeadState(false, false, "feature", "bbb", RepositoryOperation.None);
        await context.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(second, RepositoryStateStamp.Of(context));
    }

    private static GitBranch Branch(string name, string sha)
        => new($"refs/heads/{name}", sha, false, null, BranchTracking.None, GitSignature.Empty, DateTimeOffset.UnixEpoch, name);

    /// <summary>
    /// Runs an async command the way a button does, and hands back the running task.
    /// </summary>
    private sealed class AsyncRelayCommandProbe(CommunityToolkit.Mvvm.Input.AsyncRelayCommand command)
    {
        public bool CanExecute => command.CanExecute(null);

        public Task Execute() => command.ExecuteAsync(null);
    }
}
