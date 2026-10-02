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
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons.Avalonia;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

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

    [Fact]
    public void ARemoteRowsActions_AreVisibleSelectedOrNot()
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

            ListBoxItem row = list.GetRealizedContainers()
                .OfType<ListBoxItem>()
                .First(container => container.DataContext is RemoteRowViewModel);

            Application application = Application.Current!;
            Assert.True(application.TryFindResource("EnigmaForegroundBrush", application.ActualThemeVariant, out object? full));

            // Fetch, edit and remove: the icons of the buttons at the end of the line.
            Icon[] Actions() =>
                [.. row.GetVisualDescendants()
                    .OfType<Button>()
                    .Where(button => button.Classes.Contains("toolbar"))
                    .SelectMany(button => button.GetVisualDescendants().OfType<Icon>())];

            Assert.True(Actions().Length >= 3, "a remote row drew fewer than three actions");
            Assert.All(Actions(), icon => Assert.Same(full, icon.Foreground));

            // And the selection plate does not swallow them.
            list.SelectedItem = row.DataContext;
            window.UpdateLayout();

            Assert.All(Actions(), icon => Assert.Same(full, icon.Foreground));

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

    // ---------------------------------------------------------------- a branch's pull and push, from the history

    /// <summary>
    /// The history, reloaded, and the branch of that name on it.
    /// </summary>
    private static async Task<HistoryBranchViewModel> BranchInHistoryAsync(TestServices services, string name)
    {
        HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
        await history.ReloadAsync();

        return history.Rows.SelectMany(row => row.Branches).Single(branch => branch.Name == name);
    }

    [Fact]
    public void History_PullsABranchThatIsNotCheckedOutWithoutMovingHead()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            await GitAsync(world.Local.WorkTreePath, "branch", "feature");
            await GitAsync(world.Local.WorkTreePath, "push", "--set-upstream", "origin", "feature");

            await GitAsync(world.OtherPath, "fetch", "origin");
            await GitAsync(world.OtherPath, "checkout", "feature");
            Write(world.OtherPath, "src/theirs.txt", "from elsewhere\n");
            await GitAsync(world.OtherPath, "add", "--all");
            await GitAsync(world.OtherPath, "commit", "-m", "Feature work elsewhere");
            await GitAsync(world.OtherPath, "push", "origin", "feature");
            string theirs = await ReadGitAsync(world.OtherPath, "rev-parse", "feature");

            await services.Get<IRepositoryContext>().OpenAsync(world.Local);
            HistoryBranchViewModel feature = await BranchInHistoryAsync(services, "feature");

            Assert.True(feature.Commands.Pull.CanExecute(feature));
            await feature.Commands.Pull.ExecuteAsync(feature);

            Assert.Equal(theirs, await ReadGitAsync(world.Local.WorkTreePath, "rev-parse", "feature"));
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);
            Assert.False(File.Exists(Path.Combine(world.Local.WorkTreePath, "src", "theirs.txt")));
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Pulled");
        });
    }

    [Fact]
    public void History_PullingABranchWithNoUpstream_SaysSo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            await GitAsync(world.Local.WorkTreePath, "branch", "local-only");
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            HistoryBranchViewModel branch = await BranchInHistoryAsync(services, "local-only");
            await branch.Commands.Pull.ExecuteAsync(branch);

            RecordedNotification note = Assert.Single(services.InfoBar.Shown);
            Assert.Contains("no upstream", note.Message, StringComparison.Ordinal);
            Assert.Equal(0, services.Overlay.ShowCount);
        });
    }

    [Fact]
    public void History_PullingTheCurrentBranch_IsTheOrdinaryPull()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            Write(world.OtherPath, "src/theirs.txt", "from elsewhere\n");
            await GitAsync(world.OtherPath, "add", "--all");
            await GitAsync(world.OtherPath, "commit", "-m", "Work done elsewhere");
            await GitAsync(world.OtherPath, "push", "origin", "main");

            await services.Get<IRepositoryContext>().OpenAsync(world.Local);
            HistoryBranchViewModel main = await BranchInHistoryAsync(services, "main");

            await main.Commands.Pull.ExecuteAsync(main);

            Assert.True(File.Exists(Path.Combine(world.Local.WorkTreePath, "src", "theirs.txt")));
        });
    }

    [Fact]
    public void History_PushesABranchThatIsNotCheckedOutAndTracksIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            await GitAsync(world.Local.WorkTreePath, "branch", "topic");
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            HistoryBranchViewModel topic = await BranchInHistoryAsync(services, "topic");
            await topic.Commands.Push.ExecuteAsync(topic);

            Assert.Equal(
                await ReadGitAsync(world.Local.WorkTreePath, "rev-parse", "topic"),
                await ReadGitAsync(world.OriginPath, "rev-parse", "topic"));
            Assert.Equal(
                "origin/topic",
                await ReadGitAsync(world.Local.WorkTreePath, "rev-parse", "--abbrev-ref", "topic@{upstream}"));
            Assert.Equal("main", services.Get<IRepositoryContext>().Head?.BranchName);
        });
    }

    [Fact]
    public void History_OffersNoPullOrPushOnARemoteBranch()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            HistoryBranchViewModel remote = await BranchInHistoryAsync(services, "origin/main");

            Assert.False(remote.CanSynchronise);
            Assert.False(remote.Commands.Pull.CanExecute(remote));
            Assert.False(remote.Commands.Push.CanExecute(remote));

            // It can still be checked out — which creates its tracking branch — and deleted.
            Assert.True(remote.Commands.Delete.CanExecute(remote));
        });
    }

    [Theory]
    [InlineData("origin/main", "origin", "main")]
    [InlineData("origin/feature/login", "origin", "feature/login")]
    [InlineData("main", "origin", "main")]
    public void SplitUpstream_TakesTheRemoteBeforeTheFirstSlash(string upstream, string remote, string branch)
        => Assert.Equal((remote, branch), SyncOperations.SplitUpstream(upstream));

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

    // ---------------------------------------------------------------- the profile a push goes out under

    private static readonly Core.Identity.GitIdentity WorkIdentity = new("Ada Lovelace", "ada@work.example");

    /// <summary>
    /// A commit the remote does not have yet, and a profile matching the identity the repository
    /// commits with — which, with nothing local, is the global one.
    /// </summary>
    private static async Task<Core.Identity.IdentityProfile> WorkProfileAsync(TestServices services, World world)
    {
        Write(world.Local.WorkTreePath, "src/ours.txt", "from here\n");
        await GitAsync(world.Local.WorkTreePath, "add", "--all");
        await GitAsync(world.Local.WorkTreePath, "commit", "-m", "Work done here");

        services.Identity.Global = WorkIdentity;

        return await services.Get<Core.Identity.IIdentityProfileStore>()
            .SaveAsync(Core.Identity.IdentityProfile.Create("Work", WorkIdentity));
    }

    [Fact]
    public void Shell_NeverPushesUnderAProfileWithoutAnIntegrationForTheRemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);
            await WorkProfileAsync(services, world);

            MainWindowViewModel shell = services.Get<MainWindowViewModel>();
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            await shell.PushCommand.ExecuteAsync(null);

            // The remote never saw the commit.
            Assert.Equal("Add the readme", await ReadGitAsync(world.OriginPath, "log", "-1", "--format=%s", "main"));

            RecordedNotification refusal = Assert.Single(services.InfoBar.Shown);
            Assert.Equal($"Work does not push to {world.OriginPath}", refusal.Title);
            Assert.Contains("the profile Work has no integration", refusal.Message, StringComparison.Ordinal);
            Assert.Contains("Profiles page", refusal.Message, StringComparison.Ordinal);
            Assert.Equal(Enigma.Avalonia.Desktop.Controls.InfoBar.InfoBarSeverity.Warning, refusal.Severity);
        });
    }

    [Fact]
    public void History_NeverPushesABranchUnderAProfileWithoutAnIntegrationForTheRemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);
            await WorkProfileAsync(services, world);
            await GitAsync(world.Local.WorkTreePath, "branch", "topic");

            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            Assert.False(await services.Get<ISyncOperations>().PushBranchAsync("topic"));

            Assert.Equal(string.Empty, await ReadGitAsync(world.OriginPath, "branch", "--list", "topic"));
            Assert.Contains(services.InfoBar.Shown, note => note.Title.StartsWith("Work does not push to", StringComparison.Ordinal));
        });
    }

    // ---------------------------------------------------------------- pushing one tag

    [Fact]
    public void TagPush_PublishesThatTagAndNoOther()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            await GitAsync(world.Local.WorkTreePath, "tag", "1.0.0");
            await GitAsync(world.Local.WorkTreePath, "tag", "-a", "2.0.0-rc", "-m", "Not yet");
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            Assert.True(await services.Get<ISyncOperations>().PushTagAsync("1.0.0"));

            Assert.Equal("1.0.0", await ReadGitAsync(world.OriginPath, "tag", "--list"));

            RecordedNotification pushed = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Pushed", pushed.Title);
            Assert.Equal("origin has the tag \"1.0.0\".", pushed.Message);
        });
    }

    [Fact]
    public void TagPush_GoesWhereTheCurrentBranchPushes()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            // main tracks a second remote: that is where its tags go too.
            string upstreamPath = Path.Combine(Path.GetDirectoryName(world.OriginPath)!, "upstream.git");
            Directory.CreateDirectory(upstreamPath);
            await GitAsync(world.Local.WorkTreePath, "init", "--bare", upstreamPath);
            await GitAsync(world.Local.WorkTreePath, "remote", "add", "upstream", upstreamPath);
            await GitAsync(world.Local.WorkTreePath, "push", "--set-upstream", "upstream", "main");

            await GitAsync(world.Local.WorkTreePath, "tag", "1.0.0");
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            Assert.True(await services.Get<ISyncOperations>().PushTagAsync("1.0.0"));

            Assert.Equal("1.0.0", await ReadGitAsync(upstreamPath, "tag", "--list"));
            Assert.Equal(string.Empty, await ReadGitAsync(world.OriginPath, "tag", "--list"));
        });
    }

    [Fact]
    public void History_ATagBadgePushesThatTag()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            await GitAsync(world.Local.WorkTreePath, "tag", "-a", "1.0.0", "-m", "First release");
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            HistoryTagViewModel tag = history.Rows.SelectMany(row => row.Badges).OfType<HistoryTagViewModel>().Single();
            Assert.True(tag.CanPush);
            Assert.Equal("Push \"1.0.0\"", tag.PushHeader);

            await tag.Push!.ExecuteAsync(tag.Name);

            Assert.Equal("1.0.0", await ReadGitAsync(world.OriginPath, "tag", "--list"));
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Pushed");
        });
    }

    [Fact]
    public void TagsDialog_ARowsMenuPushesThatRowsTag()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            await GitAsync(world.Local.WorkTreePath, "tag", "1.0.0");
            await GitAsync(world.Local.WorkTreePath, "tag", "2.0.0");
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            TagsPageViewModel page = services.Get<TagsPageViewModel>();
            await page.OnAppearingAsync();

            TagsPageView view = services.Get<TagsPageView>();
            view.DataContext = page;
            Window window = new() { Content = view, Width = 1000, Height = 600 };
            window.Show();

            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Border line = view.GetVisualDescendants()
                    .OfType<Border>()
                    .Single(border => border.ContextMenu is not null && border.DataContext is TagRowViewModel { Name: "1.0.0" });
                ContextMenu menu = line.ContextMenu!;
                menu.Open(line);

                // Right after the checkout, before the deletes and their separator.
                string?[] headers = [.. menu.Items.OfType<MenuItem>().Select(item => item.Header as string)];
                Assert.Equal(
                    ["Select in the history", "Check out (detaches HEAD)", "Push to the remote", "Delete locally…", "Delete from the remote…"],
                    headers);

                MenuItem push = menu.Items.OfType<MenuItem>().Single(item => (item.Header as string) == "Push to the remote");
                Assert.True(push.IsVisible);

                await ((IAsyncRelayCommand)push.Command!).ExecuteAsync(push.CommandParameter);
                menu.Close();
            }
            finally
            {
                window.Close();
            }

            // That row's tag, and not the other one.
            Assert.Equal("1.0.0", await ReadGitAsync(world.OriginPath, "tag", "--list"));
        });
    }

    [Fact]
    public void TagPush_NeverPushesUnderAProfileWithoutAnIntegrationForTheRemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);
            await WorkProfileAsync(services, world);
            await GitAsync(world.Local.WorkTreePath, "tag", "1.0.0");

            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            Assert.False(await services.Get<ISyncOperations>().PushTagAsync("1.0.0"));

            Assert.Equal(string.Empty, await ReadGitAsync(world.OriginPath, "tag", "--list"));
            Assert.Contains(services.InfoBar.Shown, note => note.Title.StartsWith("Work does not push to", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void TagPush_SaysTheRemoteHasThatTagElsewhere_AndReplacesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            await GitAsync(world.OtherPath, "tag", "1.0.0");
            await GitAsync(world.OtherPath, "push", "origin", "refs/tags/1.0.0");
            string theirs = await ReadGitAsync(world.OriginPath, "rev-parse", "refs/tags/1.0.0");

            Write(world.Local.WorkTreePath, "src/release.txt", "release\n");
            await GitAsync(world.Local.WorkTreePath, "add", "--all");
            await GitAsync(world.Local.WorkTreePath, "commit", "-m", "Prepare the release");
            await GitAsync(world.Local.WorkTreePath, "tag", "1.0.0");

            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            Assert.False(await services.Get<ISyncOperations>().PushTagAsync("1.0.0"));

            Assert.Equal(theirs, await ReadGitAsync(world.OriginPath, "rev-parse", "refs/tags/1.0.0"));

            // Not git's "pull first": there is nothing to pull, a tag is in the way.
            RecordedNotification failure = Assert.Single(services.InfoBar.Shown, note => note.Title == "Pushing 1.0.0 failed");
            Assert.Contains("origin already has a tag \"1.0.0\", on another commit", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Pull first", failure.Message, StringComparison.Ordinal);
            Assert.Equal(Enigma.Avalonia.Desktop.Controls.InfoBar.InfoBarSeverity.Warning, failure.Severity);
        });
    }

    // ---------------------------------------------------------------- deleting a tag, here or there

    /// <summary>
    /// The world, with the tag <c>1.0.0</c> here and on <c>origin</c>.
    /// </summary>
    private static async Task<World> BuildWorldWithAPublishedTagAsync(TestServices services)
    {
        World world = await BuildWorldAsync(services);

        await GitAsync(world.Local.WorkTreePath, "tag", "-a", "1.0.0", "-m", "First release");
        await GitAsync(world.Local.WorkTreePath, "push", "origin", "refs/tags/1.0.0");

        return world;
    }

    private static async Task<HistoryTagViewModel> TagInHistoryAsync(TestServices services, string name)
    {
        HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
        await history.ReloadAsync();

        return history.Rows.SelectMany(row => row.Badges).OfType<HistoryTagViewModel>().Single(tag => tag.Name == name);
    }

    [Fact]
    public void History_ATagBadgesMenu_OffersTheDeleteHereAndTheDeleteOnTheRemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldWithAPublishedTagAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            HistoryPageView view = services.Get<HistoryPageView>();
            view.DataContext = history;
            Window window = new() { Content = view, Width = 1200, Height = 700 };
            window.Show();

            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Controls.RefBadge badge = view.GetVisualDescendants()
                    .OfType<Controls.RefBadge>()
                    .Single(candidate => candidate.DataContext is HistoryTagViewModel { Name: "1.0.0" });
                ContextMenu menu = badge.ContextMenu!;
                menu.Open(badge);
                Dispatcher.UIThread.RunJobs();

                // The push, the two deletes, the copy — each group behind its own separator.
                string?[] shown = [.. menu.Items.OfType<Control>()
                    .Where(item => item.IsVisible)
                    .Select(item => item is MenuItem entry ? entry.Header as string : "-")];
                Assert.Equal(
                    ["Push \"1.0.0\"", "-", "Delete \"1.0.0\" locally…", "Delete \"1.0.0\" from the remote…", "-", "Copy tag name"],
                    shown);

                menu.Close();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void History_ATagBadgeDeletesItsTagHere_AfterAsking_AndTheRemoteKeepsIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldWithAPublishedTagAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            HistoryTagViewModel tag = await TagInHistoryAsync(services, "1.0.0");
            services.Dialogs.Result = DialogResult.Primary;

            await tag.Delete!.ExecuteAsync(tag.Name);

            ContentDialog question = Assert.Single(services.Dialogs.Shown);
            Assert.Equal("Delete tag", question.Title);
            Assert.Equal(DefaultButton.Close, question.DefaultButton);

            Assert.Equal(string.Empty, await ReadGitAsync(world.Local.WorkTreePath, "tag", "--list"));
            Assert.Equal("1.0.0", await ReadGitAsync(world.OriginPath, "tag", "--list"));

            // The history is read again, and its badge has gone with it.
            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            Assert.Empty(history.Rows.SelectMany(row => row.Badges).OfType<HistoryTagViewModel>());
        });
    }

    [Fact]
    public void History_ATagBadgeDeletesItsTagFromTheRemote_AfterAsking_AndKeepsItHere()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldWithAPublishedTagAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            HistoryTagViewModel tag = await TagInHistoryAsync(services, "1.0.0");
            services.Dialogs.Result = DialogResult.Primary;

            await tag.DeleteRemote!.ExecuteAsync(tag.Name);

            ContentDialog question = Assert.Single(services.Dialogs.Shown);
            Assert.Equal("Delete remote tag", question.Title);
            Assert.Equal(DefaultButton.Close, question.DefaultButton);
            Assert.Contains("\"1.0.0\" from \"origin\"", question.Content as string, StringComparison.Ordinal);

            Assert.Equal(string.Empty, await ReadGitAsync(world.OriginPath, "tag", "--list"));
            Assert.Equal("1.0.0", await ReadGitAsync(world.Local.WorkTreePath, "tag", "--list"));

            RecordedNotification deleted = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Deleted from the remote", deleted.Title);
            Assert.Equal("\"origin\" no longer has the tag \"1.0.0\".", deleted.Message);
        });
    }

    [Fact]
    public void TagDeletes_DeleteNothingWhenTheQuestionIsCancelled()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldWithAPublishedTagAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            services.Dialogs.Result = DialogResult.Close;
            ITagOperations tags = services.Get<ITagOperations>();

            Assert.False(await tags.DeleteAsync("1.0.0"));
            Assert.False(await tags.DeleteRemoteAsync("1.0.0"));

            Assert.Equal(2, services.Dialogs.Shown.Count);
            Assert.Equal("1.0.0", await ReadGitAsync(world.Local.WorkTreePath, "tag", "--list"));
            Assert.Equal("1.0.0", await ReadGitAsync(world.OriginPath, "tag", "--list"));
            Assert.Empty(services.InfoBar.Shown);
        });
    }

    [Fact]
    public void TagRemoteDelete_GoesWhereTheCurrentBranchPushes()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldWithAPublishedTagAsync(services);

            // main tracks a second remote, which has the tag too: that is the one it is deleted from.
            string upstreamPath = Path.Combine(Path.GetDirectoryName(world.OriginPath)!, "upstream.git");
            Directory.CreateDirectory(upstreamPath);
            await GitAsync(world.Local.WorkTreePath, "init", "--bare", upstreamPath);
            await GitAsync(world.Local.WorkTreePath, "remote", "add", "upstream", upstreamPath);
            await GitAsync(world.Local.WorkTreePath, "push", "--set-upstream", "upstream", "main");
            await GitAsync(world.Local.WorkTreePath, "push", "upstream", "refs/tags/1.0.0");

            await services.Get<IRepositoryContext>().OpenAsync(world.Local);
            services.Dialogs.Result = DialogResult.Primary;

            Assert.True(await services.Get<ITagOperations>().DeleteRemoteAsync("1.0.0"));

            Assert.Contains("\"upstream\"", services.Dialogs.Last!.Content as string, StringComparison.Ordinal);
            Assert.Equal(string.Empty, await ReadGitAsync(upstreamPath, "tag", "--list"));
            Assert.Equal("1.0.0", await ReadGitAsync(world.OriginPath, "tag", "--list"));
        });
    }

    [Fact]
    public void TagRemoteDelete_NeverDeletesUnderAProfileWithoutAnIntegrationForTheRemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldWithAPublishedTagAsync(services);
            await WorkProfileAsync(services, world);

            await services.Get<IRepositoryContext>().OpenAsync(world.Local);
            services.Dialogs.Result = DialogResult.Primary;

            Assert.False(await services.Get<ITagOperations>().DeleteRemoteAsync("1.0.0"));

            // Refused before anything is asked: there is no question worth answering.
            Assert.Empty(services.Dialogs.Shown);
            Assert.Equal("1.0.0", await ReadGitAsync(world.OriginPath, "tag", "--list"));
            Assert.Contains(services.InfoBar.Shown, note => note.Title.StartsWith("Work does not push to", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void TagRemoteDelete_SaysWhatGitSaidWhenItFails()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);
            await GitAsync(world.Local.WorkTreePath, "tag", "1.0.0");

            // No origin any more, and main no upstream: the delete goes to "origin", which git cannot
            // reach. (A tag the remote merely lacks is no failure to recent git: it warns, and the
            // remote ends up without the tag, which is what was asked.)
            await GitAsync(world.Local.WorkTreePath, "remote", "remove", "origin");

            await services.Get<IRepositoryContext>().OpenAsync(world.Local);
            services.Dialogs.Result = DialogResult.Primary;

            Assert.False(await services.Get<ITagOperations>().DeleteRemoteAsync("1.0.0"));

            RecordedNotification failure = Assert.Single(services.InfoBar.Shown);
            Assert.Equal("Could not delete \"1.0.0\" from \"origin\"", failure.Title);
            Assert.Equal("1.0.0", await ReadGitAsync(world.Local.WorkTreePath, "tag", "--list"));
        });
    }

    [Fact]
    public void TagsDialog_ARowsMenuDeletesThatRowsTagFromTheRemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldWithAPublishedTagAsync(services);
            await GitAsync(world.Local.WorkTreePath, "tag", "2.0.0");
            await GitAsync(world.Local.WorkTreePath, "push", "origin", "refs/tags/2.0.0");
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            TagsPageViewModel page = services.Get<TagsPageViewModel>();
            await page.OnAppearingAsync();

            TagRowViewModel row = page.Tags.Single(tag => tag.Name == "1.0.0");
            Assert.True(row.CanDeleteRemote);

            services.Dialogs.Result = DialogResult.Primary;
            await row.DeleteRemoteCommand!.ExecuteAsync(row);

            // That row's tag, and not the other one; and both are still here.
            Assert.Equal("2.0.0", await ReadGitAsync(world.OriginPath, "tag", "--list"));
            Assert.Equal(["1.0.0", "2.0.0"], page.Tags.Select(tag => tag.Name).Order());
        });
    }

    [Fact]
    public void Shell_PushesAsBeforeWhenTheRepositoryCommitsAsNoProfile()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);
            await WorkProfileAsync(services, world);

            // This repository commits as someone no profile describes.
            services.Identity.SetLocalDirectly(world.Local.WorkTreePath, new Core.Identity.GitIdentity("Grace Hopper", "grace@example.com"));

            MainWindowViewModel shell = services.Get<MainWindowViewModel>();
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            await shell.PushCommand.ExecuteAsync(null);

            Assert.Equal("Work done here", await ReadGitAsync(world.OriginPath, "log", "-1", "--format=%s", "main"));
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Pushed");
        });
    }

    [Fact]
    public void PushGuard_LetsAProfilePushWhereItsIntegrationLeads()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);
            Core.Identity.IdentityProfile work = await WorkProfileAsync(services, world);

            // Pushes go to GitHub while fetches still read the local directory: only the push URL
            // decides.
            await GitAsync(world.Local.WorkTreePath, "remote", "set-url", "--push", "origin", "git@github.com:contoso/project.git");

            IPushGuard guard = services.Get<IPushGuard>();

            Core.Hosting.PushPermission before = await guard.CheckAsync(world.Local, "origin");
            Assert.False(before.IsAllowed);
            Assert.Equal("github.com", before.Target);

            await services.Get<Core.Hosting.IHostAccountService>().AddAsync(
                Core.Hosting.HostAccount.Create(Core.Hosting.HostKind.GitHub, new Uri("https://github.com"), "ada", "Work GitHub", work.Id),
                new Core.Security.SecretString("ghp_token"));

            Core.Hosting.PushPermission after = await guard.CheckAsync(world.Local, "origin");
            Assert.True(after.IsAllowed);
            Assert.Equal(Core.Hosting.PushPermissionReason.Integration, after.Reason);
            Assert.Equal("Work GitHub", after.Account?.DisplayName);
        });
    }

    [Fact]
    public void PushGuard_RefusesWhatItCannotCheck()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);
            await WorkProfileAsync(services, world);

            MainWindowViewModel shell = services.Get<MainWindowViewModel>();
            await services.Get<IRepositoryContext>().OpenAsync(world.Local);

            // git's configuration cannot be read, so nobody knows which profile this is.
            services.Identity.Failure = FakeGitIdentityService.LockFailure();

            await shell.PushCommand.ExecuteAsync(null);

            Assert.Equal("Add the readme", await ReadGitAsync(world.OriginPath, "log", "-1", "--format=%s", "main"));
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Nothing was pushed");
        });
    }

    [Fact]
    public void PushGuard_ReadsNothingButTheProfilesWhenThereAreNone()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            World world = await BuildWorldAsync(services);

            // Were the identity read, this would throw: without profiles it is never asked.
            services.Identity.Failure = FakeGitIdentityService.LockFailure();

            Core.Hosting.PushPermission permission = await services.Get<IPushGuard>().CheckAsync(world.Local, "origin");

            Assert.True(permission.IsAllowed);
            Assert.Equal(Core.Hosting.PushPermissionReason.NoProfile, permission.Reason);
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
