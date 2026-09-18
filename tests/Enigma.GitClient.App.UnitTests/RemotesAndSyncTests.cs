using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives the remotes page and the shell's synchronise toolbar against a second directory standing
/// in as the remote — real fetch, pull and push semantics with no network.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RemotesAndSyncTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public RemotesAndSyncTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The pieces of a two-clone world: the bare remote, the repository under test, and a second
    /// clone that can move while the first is unaware.
    /// </summary>
    private sealed record World(RepositoryHandle Local, string OriginPath, string OtherPath);

    private static async Task<World> BuildWorldAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        string originPath = Path.Combine(root, "origin.git");
        Directory.CreateDirectory(originPath);

        RepositoryHandle local = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "local"), "main");

        Write(local.WorkTreePath, "README.md", "# one\n");
        await GitAsync(local.WorkTreePath, "add", "--all");
        await GitAsync(local.WorkTreePath, "commit", "-m", "Add the readme");

        await GitAsync(local.WorkTreePath, "init", "--bare", originPath);

        // "git init" picks its default branch from the machine's configuration, so the bare remote
        // is pointed at main explicitly — otherwise a clone of it checks out nothing.
        await GitAsync(originPath, "symbolic-ref", "HEAD", "refs/heads/main");

        await GitAsync(local.WorkTreePath, "remote", "add", "origin", originPath);
        await GitAsync(local.WorkTreePath, "push", "--set-upstream", "origin", "main");

        string otherPath = Path.Combine(root, "other");
        await GitAsync(root, "clone", originPath, otherPath);
        await GitAsync(otherPath, "config", "user.name", "Grace Hopper");
        await GitAsync(otherPath, "config", "user.email", "grace@example.com");

        return new World(local, originPath, otherPath);
    }

    private static void Write(string root, string relativePath, string content)
    {
        string full = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static Task GitAsync(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.Environment["GIT_AUTHOR_NAME"] = "Ada Lovelace";
        startInfo.Environment["GIT_AUTHOR_EMAIL"] = "ada@example.com";
        startInfo.Environment["GIT_COMMITTER_NAME"] = "Ada Lovelace";
        startInfo.Environment["GIT_COMMITTER_EMAIL"] = "ada@example.com";

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? Task.CompletedTask
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }

    private static async Task<string> ReadGitAsync(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output.TrimEnd('\n', '\r');
    }

    private static async Task<RemotesPageViewModel> OpenRemotesAsync(TestServices services, RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        RemotesPageViewModel page = services.Get<RemotesPageViewModel>();
        await page.OnAppearingAsync();

        return page;
    }

    // ---------------------------------------------------------------- the remotes page

    [Fact]
    public void Page_IsEmptyWithNoRepositoryOpen()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            RemotesPageViewModel page = services.Get<RemotesPageViewModel>();

            Assert.True(page.IsEmpty);
            Assert.Contains("Open a repository", page.EmptyMessage, StringComparison.Ordinal);
            Assert.False(page.AddCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Page_ListsTheRemoteWithItsUrlAndBranchCount()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            RemotesPageViewModel page = await OpenRemotesAsync(services, world.Local);

            RemoteRowViewModel origin = Assert.Single(page.Remotes);

            Assert.Equal("origin", origin.Name);
            Assert.Equal(world.OriginPath, origin.FetchUrl);
            Assert.False(origin.HasSeparatePushUrl);
            Assert.Equal("1 branch", origin.BranchSummary);
        });
    }

    [Fact]
    public void Page_AddsARemoteFromTheDialog()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            string mirror = Path.Combine(services.ConfigurationRoot, "workspace", "mirror.git");
            Directory.CreateDirectory(mirror);
            await GitAsync(world.Local.WorkTreePath, "init", "--bare", mirror);

            RemotesPageViewModel page = await OpenRemotesAsync(services, world.Local);

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: RemoteDialogViewModel model })
                {
                    model.Name = "mirror";
                    model.FetchUrl = mirror;
                }
            };

            services.Dialogs.Result = DialogResult.Primary;

            await page.AddCommand.ExecuteAsync(null);

            Assert.Contains(page.Remotes, remote => remote.Name == "mirror");
        });
    }

    [Fact]
    public void Page_AddsNothingWhenTheDialogIsCancelled()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            RemotesPageViewModel page = await OpenRemotesAsync(services, world.Local);

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: RemoteDialogViewModel model })
                {
                    model.Name = "never-added";
                    model.FetchUrl = world.OriginPath;
                }
            };

            services.Dialogs.Result = DialogResult.Close;

            await page.AddCommand.ExecuteAsync(null);

            Assert.DoesNotContain(page.Remotes, remote => remote.Name == "never-added");
        });
    }

    [Fact]
    public void Page_RenamesAndRepointsARemoteFromOneDialog()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            RemotesPageViewModel page = await OpenRemotesAsync(services, world.Local);

            services.Dialogs.OnShown = dialog =>
            {
                if (dialog.Content is Control { DataContext: RemoteDialogViewModel model })
                {
                    model.Name = "upstream";
                    model.PushUrl = world.OriginPath;
                }
            };

            services.Dialogs.Result = DialogResult.Primary;

            await page.EditCommand.ExecuteAsync(page.Remotes.Single());

            RemoteRowViewModel renamed = Assert.Single(page.Remotes);

            Assert.Equal("upstream", renamed.Name);
            Assert.Equal(world.OriginPath, renamed.FetchUrl);
        });
    }

    [Fact]
    public void Page_AsksBeforeRemovingARemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            RemotesPageViewModel page = await OpenRemotesAsync(services, world.Local);

            services.Dialogs.Result = DialogResult.Close;

            await page.RemoveCommand.ExecuteAsync(page.Remotes.Single());

            string message = Assert.IsType<string>(services.Dialogs.Last!.Content);

            Assert.Contains("origin", message, StringComparison.Ordinal);

            // Saying what is not affected is what stops the question reading as "delete the repo".
            Assert.Contains("Nothing on the remote itself is touched", message, StringComparison.Ordinal);
            Assert.Equal(DefaultButton.Close, services.Dialogs.Last.DefaultButton);
            Assert.Single(page.Remotes);
        });
    }

    [Fact]
    public void Page_RemovesTheRemoteOnceConfirmed()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            RemotesPageViewModel page = await OpenRemotesAsync(services, world.Local);

            services.Dialogs.Result = DialogResult.Primary;

            await page.RemoveCommand.ExecuteAsync(page.Remotes.Single());

            Assert.Empty(page.Remotes);
            Assert.True(page.IsEmpty);
            Assert.Contains("no remotes", page.EmptyMessage, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Page_FetchesFromOneRemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            Write(world.OtherPath, "src/theirs.txt", "from elsewhere\n");
            await GitAsync(world.OtherPath, "add", "--all");
            await GitAsync(world.OtherPath, "commit", "-m", "Work done elsewhere");
            await GitAsync(world.OtherPath, "push", "origin", "main");

            RemotesPageViewModel page = await OpenRemotesAsync(services, world.Local);

            await page.FetchCommand.ExecuteAsync(page.Remotes.Single());

            Assert.Equal(
                "Work done elsewhere",
                await ReadGitAsync(world.Local.WorkTreePath, "log", "-1", "--format=%s", "origin/main"));

            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Fetched");
        });
    }

    [Fact]
    public void RemoteDialog_RefusesANameOrUrlGitWouldNot()
    {
        RemoteDialogViewModel model = new(["origin"], null);

        model.Name = "origin";
        model.FetchUrl = "https://example.com/team/project.git";

        Assert.False(model.IsValid);
        Assert.Contains("already exists", model.ValidationMessage, StringComparison.Ordinal);

        model.Name = "bad name";
        Assert.Contains("spaces", model.ValidationMessage, StringComparison.Ordinal);

        model.Name = "upstream";
        Assert.True(model.IsValid);

        model.FetchUrl = "not a url at all";
        Assert.False(model.IsValid);
    }

    [Fact]
    public void RemoteDialog_TreatsAnEmptyPushUrlAsMeaningTheFetchOne()
    {
        RemoteDialogViewModel model = new([], null)
        {
            Name = "origin",
            FetchUrl = "https://example.com/team/project.git",
        };

        Assert.Null(model.EffectivePushUrl);

        model.PushUrl = "  ";
        Assert.Null(model.EffectivePushUrl);

        model.PushUrl = "ssh://git@example.com/team/project.git";
        Assert.Equal("ssh://git@example.com/team/project.git", model.EffectivePushUrl);
    }

    // ---------------------------------------------------------------- selection

    [Fact]
    public void Selection_IsTheRemoteTheReaderPickedAndSurvivesARefresh()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            RemotesPageViewModel page = await OpenRemotesAsync(services, world.Local);

            Assert.Null(page.SelectedRemote);

            page.SelectedRemote = page.Remotes.Single(remote => remote.Name == "origin");

            await page.RefreshAsync();

            // A new row object for the same remote: the selection followed the name.
            Assert.NotNull(page.SelectedRemote);
            Assert.Equal("origin", page.SelectedRemote!.Name);
            Assert.Same(page.Remotes.Single(), page.SelectedRemote);
        });
    }

    [Fact]
    public void Selection_IsClearedWhenTheRemoteIsRemoved()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            RemotesPageViewModel page = await OpenRemotesAsync(services, world.Local);

            page.SelectedRemote = page.Remotes.Single();
            Assert.NotNull(page.SelectedRemote);

            services.Dialogs.Result = DialogResult.Primary;
            await page.RemoveCommand.ExecuteAsync(page.Remotes.Single());

            Assert.Empty(page.Remotes);
            Assert.Null(page.SelectedRemote);
        });
    }

    [Fact]
    public void RemoteList_IsAListBoxWhoseSelectionFollowsThePage()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            RemotesPageViewModel page = await OpenRemotesAsync(services, world.Local);

            RemotesPageView view = services.Get<RemotesPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1000, Height = 400 };
            window.Show();
            window.UpdateLayout();

            ListBox list = view.FindControl<ListBox>("RemoteList")
                ?? throw new InvalidOperationException("The remotes page has no remote list.");

            Assert.Equal(page.Remotes.Count, list.ItemCount);

            list.SelectedItem = page.Remotes.Single();
            window.UpdateLayout();

            Assert.Same(page.SelectedRemote, list.SelectedItem);

            window.Close();
        });
    }

    // ---------------------------------------------------------------- the shell toolbar

    [Fact]
    public void Shell_ShowsHowFarAheadAndBehindTheBranchIs()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            // They push one commit; we make one of our own and never fetch.
            Write(world.OtherPath, "src/theirs.txt", "from elsewhere\n");
            await GitAsync(world.OtherPath, "add", "--all");
            await GitAsync(world.OtherPath, "commit", "-m", "Work done elsewhere");
            await GitAsync(world.OtherPath, "push", "origin", "main");

            Write(world.Local.WorkTreePath, "src/ours.txt", "from here\n");
            await GitAsync(world.Local.WorkTreePath, "add", "--all");
            await GitAsync(world.Local.WorkTreePath, "commit", "-m", "Work done here");

            MainWindowViewModel shell = services.Get<MainWindowViewModel>();
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            Assert.True(shell.CanSync);
            Assert.True(shell.HasUpstream);
            Assert.Equal("1", shell.Ahead);
            Assert.True(shell.IsAhead);

            // Behind is still zero until the fetch tells us otherwise, which is the honest answer.
            Assert.False(shell.IsBehind);

            await shell.FetchCommand.ExecuteAsync(null);

            Assert.Equal("1", shell.Behind);
            Assert.True(shell.IsBehind);
        });
    }

    [Fact]
    public void Shell_PushesTheCurrentBranch()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            Write(world.Local.WorkTreePath, "src/ours.txt", "from here\n");
            await GitAsync(world.Local.WorkTreePath, "add", "--all");
            await GitAsync(world.Local.WorkTreePath, "commit", "-m", "Work done here");

            MainWindowViewModel shell = services.Get<MainWindowViewModel>();
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            await shell.PushCommand.ExecuteAsync(null);

            Assert.Equal(
                "Work done here",
                await ReadGitAsync(world.OriginPath, "log", "-1", "--format=%s", "main"));

            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Pushed");
        });
    }

    [Fact]
    public void Shell_PullsWhatArrivedElsewhere()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            Write(world.OtherPath, "src/theirs.txt", "from elsewhere\n");
            await GitAsync(world.OtherPath, "add", "--all");
            await GitAsync(world.OtherPath, "commit", "-m", "Work done elsewhere");
            await GitAsync(world.OtherPath, "push", "origin", "main");

            MainWindowViewModel shell = services.Get<MainWindowViewModel>();
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            await shell.PullCommand.ExecuteAsync(null);

            Assert.True(File.Exists(Path.Combine(world.Local.WorkTreePath, "src", "theirs.txt")));
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Pulled");
        });
    }

    [Fact]
    public void Shell_SaysWhatToDoWhenAPushIsRejected()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            Write(world.OtherPath, "src/theirs.txt", "from elsewhere\n");
            await GitAsync(world.OtherPath, "add", "--all");
            await GitAsync(world.OtherPath, "commit", "-m", "Work done elsewhere");
            await GitAsync(world.OtherPath, "push", "origin", "main");

            Write(world.Local.WorkTreePath, "src/ours.txt", "from here\n");
            await GitAsync(world.Local.WorkTreePath, "add", "--all");
            await GitAsync(world.Local.WorkTreePath, "commit", "-m", "Work done here");

            MainWindowViewModel shell = services.Get<MainWindowViewModel>();
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            await shell.PushCommand.ExecuteAsync(null);

            RecordedNotification failure = Assert.Single(
                services.InfoBar.Shown,
                note => note.Title == "Pushing failed");

            // The whole point of classifying it: "rejected" alone leaves a user guessing.
            Assert.Contains("Pull first", failure.Message, StringComparison.Ordinal);

            // A failure the user can fix themselves is a warning, not an error.
            Assert.Equal(Enigma.Avalonia.Desktop.Controls.InfoBar.InfoBarSeverity.Warning, failure.Severity);
        });
    }

    [Fact]
    public void Shell_ShowsAndHidesTheProgressOverlayAroundATransfer()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            MainWindowViewModel shell = services.Get<MainWindowViewModel>();
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            await shell.FetchCommand.ExecuteAsync(null);

            Assert.Equal(1, services.Overlay.ShowCount);

            // Always closed afterwards: an overlay left open makes the whole window unusable.
            Assert.False(services.Overlay.IsOpen);
        });
    }

    [Fact]
    public void Shell_OffersNoSynchronisationWithNoRepositoryOpen()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            MainWindowViewModel shell = services.Get<MainWindowViewModel>();

            Assert.False(shell.CanSync);
            Assert.False(shell.FetchCommand.CanExecute(null));
            Assert.False(shell.PullCommand.CanExecute(null));
            Assert.False(shell.PushCommand.CanExecute(null));
        });
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void Page_DrawsItsRemotes()
    {
        _fixture.RunAsync(async () =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                using TestServices services = TestServices.Build(useRealRefReader: true);
                World world = await BuildWorldAsync(services);

                RemotesPageViewModel page = await OpenRemotesAsync(services, world.Local);

                RemotesPageView view = services.Get<RemotesPageView>();
                view.DataContext = page;

                Window window = new() { Content = view, Width = 1100, Height = 420 };
                window.Show();

                string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "remotes-page.png");

                int colours = 0;

                for (int attempt = 0; attempt < 20; attempt++)
                {
                    Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                    window.UpdateLayout();
                    window.InvalidateVisual();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                    Dispatcher.UIThread.RunJobs();

                    using Bitmap frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("The remotes page produced no rendered frame.");

                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    colours = SnapshotColours.Count(path);

                    if (colours >= 8)
                    {
                        break;
                    }
                }

                Assert.True(colours >= 8, "the remotes page drew nothing");

                string[] texts = [.. view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(block => block.Text ?? string.Empty)];

                Assert.Contains("origin", texts);
                Assert.Contains("1 branch", texts);
                Assert.Contains(texts, text => text.Contains("origin.git", StringComparison.Ordinal));

                window.Content = null;
                window.Close();
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }
}
