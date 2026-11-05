using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Tags;
using Enigma.GitClient.Desktop.Controls;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A tag's badge carries the remote's cloud when the remote has the tag on the same commit, as a
/// branch's badge does for its upstream (FEATURE-0123). Against real git, with a bare <c>origin</c>.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RemoteTagsTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public RemoteTagsTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- what the remote says

    [Fact]
    public void AnUnpushedTag_IsOnNoRemote_APushedOneIsOnOrigin()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, _) = await BuildPublishedAsync(services);
            Git(repository, "tag", "pushed");
            Git(repository, "tag", "unpushed");
            Git(repository, "push", "origin", "refs/tags/pushed");
            string head = Git(repository, "rev-parse", "HEAD");

            IRemoteTags remoteTags = await OpenAsync(services, repository);

            Assert.Equal("origin", remoteTags.Remote);
            Assert.Equal("origin", remoteTags.RemoteOf("pushed", head));
            Assert.Null(remoteTags.RemoteOf("unpushed", head));
        });
    }

    [Fact]
    public void ATagMovedAfterItWasPushed_IsNotOnTheRemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, _) = await BuildPublishedAsync(services);
            Git(repository, "tag", "v1");
            Git(repository, "push", "origin", "refs/tags/v1");
            string first = Git(repository, "rev-parse", "HEAD");
            Commit(repository, "b.txt", "Second");
            Git(repository, "tag", "--force", "v1");
            string second = Git(repository, "rev-parse", "HEAD");

            IRemoteTags remoteTags = await OpenAsync(services, repository);

            Assert.Equal("origin", remoteTags.RemoteOf("v1", first));
            Assert.Null(remoteTags.RemoteOf("v1", second));
        });
    }

    [Fact]
    public void AnAnnotatedTag_IsComparedByItsCommit()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, _) = await BuildPublishedAsync(services);
            Git(repository, "tag", "-a", "v1", "-m", "A release");
            Git(repository, "push", "origin", "refs/tags/v1");

            IRemoteTags remoteTags = await OpenAsync(services, repository);

            Assert.Equal("origin", remoteTags.RemoteOf("v1", Git(repository, "rev-parse", "HEAD")));
        });
    }

    [Fact]
    public void ARepositoryWithoutTheRemote_IsNotAsked_AndIsUnknown()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await InitAsync(services);
            Git(repository, "tag", "v1");

            IRemoteTags remoteTags = await OpenAsync(services, repository);

            Assert.Null(remoteTags.Remote);
            Assert.Null(remoteTags.Tags);
        });
    }

    [Fact]
    public void AReadThatFails_IsLogged_AndLeavesTheStateUnknown()
    {
        _fixture.RunAsync(async () =>
        {
            RecordingLogger<RemoteTags> logger = new();
            using TestServices services = TestServices.Build(
                useRealRefReader: true,
                configure: collection => collection.AddSingleton<ILogger<RemoteTags>>(logger));

            RepositoryHandle repository = await InitAsync(services);
            Git(repository, "remote", "add", "origin", Path.Combine(services.ConfigurationRoot, "nowhere.git"));
            Git(repository, "tag", "v1");

            IRemoteTags remoteTags = await OpenAsync(services, repository);

            Assert.Null(remoteTags.Remote);
            Assert.Null(remoteTags.Tags);
            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Debug);
        });
    }

    [Fact]
    public void AReadThatFails_KeepsWhatTheSameRemoteSaidLast()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, string origin) = await BuildPublishedAsync(services);
            Git(repository, "tag", "v1");
            Git(repository, "push", "origin", "refs/tags/v1");
            string head = Git(repository, "rev-parse", "HEAD");

            IRemoteTags remoteTags = await OpenAsync(services, repository);
            Assert.Equal("origin", remoteTags.RemoteOf("v1", head));

            // Offline, as far as git can tell.
            Directory.Move(origin, origin + ".away");
            await remoteTags.RefreshAsync();

            Assert.Equal("origin", remoteTags.RemoteOf("v1", head));
        });
    }

    [Fact]
    public void AnAnswerForARepositoryNoLongerOpen_IsDropped()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle published, _) = await BuildPublishedAsync(services);
            Git(published, "tag", "v1");
            Git(published, "push", "origin", "refs/tags/v1");
            string head = Git(published, "rev-parse", "HEAD");

            IRemoteTags remoteTags = await OpenAsync(services, published);
            int changes = 0;
            remoteTags.Changed += (_, _) => changes++;

            Task overtaken = remoteTags.RefreshAsync();

            RepositoryHandle other = await InitAsync(services, "other");
            await services.Get<IRepositoryContext>().OpenAsync(other);
            await overtaken;
            await remoteTags.RefreshAsync();

            Assert.Null(remoteTags.Remote);
            Assert.Null(remoteTags.RemoteOf("v1", head));

            // Forgotten once, when the other repository opened; never set again by the old read.
            Assert.Equal(1, changes);
        });
    }

    // ---------------------------------------------------------------- the badge

    [Fact]
    public void PushingATagFromItsBadge_DrawsTheCloud_AndDeletingItFromTheRemoteTakesItAway()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, _) = await BuildPublishedAsync(services);
            Git(repository, "tag", "v1");
            (Window window, HistoryPageView view, HistoryPageViewModel page) = await ShowAsync(services, repository);

            try
            {
                HistoryTagViewModel tag = page.Rows.SelectMany(row => row.Badges).OfType<HistoryTagViewModel>().Single();
                RefBadge badge = TagBadge(view, "v1");

                Assert.Null(tag.Remote);
                Assert.False(badge.HasUpstream);

                await tag.Push!.ExecuteAsync(tag.Name);
                Render(window);

                Assert.Equal("origin", tag.Remote);
                Assert.True(badge.HasUpstream);
                Assert.True(Named(badge, "UpstreamIcon").IsEffectivelyVisible);
                Assert.Equal("v1, on origin", badge.Description);

                services.Dialogs.Result = DialogResult.Primary;
                await tag.DeleteRemote!.ExecuteAsync(tag.Name);
                Render(window);

                Assert.Null(tag.Remote);
                Assert.False(badge.HasUpstream);
                Assert.Equal("v1", badge.Description);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ATagAlreadyOnTheRemote_HasItsCloudWhenTheHistoryOpens()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, _) = await BuildPublishedAsync(services);
            Git(repository, "tag", "pushed");
            Git(repository, "tag", "unpushed");
            Git(repository, "push", "origin", "refs/tags/pushed");

            (Window window, HistoryPageView view, HistoryPageViewModel page) = await ShowAsync(services, repository);

            try
            {
                await services.Get<IRemoteTags>().RefreshAsync();
                Render(window);

                Assert.True(TagBadge(view, "pushed").HasUpstream);
                Assert.False(TagBadge(view, "unpushed").HasUpstream);

                CommitRowViewModel line = page.Rows.Single(row => row.Subject == "Add the readme");
                Assert.Equal("origin", line.Refs.Single(item => item.Name == "pushed").Upstream);
                Assert.Null(line.Refs.Single(item => item.Name == "unpushed").Upstream);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheRefsColumn_WidensForTheCloud()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (RepositoryHandle repository, _) = await BuildPublishedAsync(services);

            // The widest badge on the page, so the column is measured from it.
            const string Name = "a-release-with-a-rather-long-name";
            Git(repository, "tag", Name);

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            double before = page.RefColumnWidth;
            Assert.Equal(Math.Min(RefBadgeMetrics.MeasureBadge(Name), HistoryPageViewModel.MaximumRefColumnWidth), before, 3);

            Git(repository, "push", "origin", $"refs/tags/{Name}");
            await services.Get<IRemoteTags>().RefreshAsync();

            Assert.Equal(
                Math.Min(RefBadgeMetrics.MeasureBadge(Name, withUpstream: true), HistoryPageViewModel.MaximumRefColumnWidth),
                page.RefColumnWidth,
                3);
            Assert.True(page.RefColumnWidth > before);
        });
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<IRemoteTags> OpenAsync(TestServices services, RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        IRemoteTags remoteTags = services.Get<IRemoteTags>();
        await remoteTags.RefreshAsync();

        return remoteTags;
    }

    private static async Task<(Window Window, HistoryPageView View, HistoryPageViewModel Page)> ShowAsync(
        TestServices services,
        RepositoryHandle repository)
    {
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
        await page.ReloadAsync();

        HistoryPageView view = services.Get<HistoryPageView>();
        view.DataContext = page;

        Window window = new() { Content = view, Width = 1200, Height = 700 };
        window.Show();
        Render(window);

        return (window, view, page);
    }

    private static void Render(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static RefBadge TagBadge(HistoryPageView view, string name)
        => view.GetVisualDescendants()
            .OfType<RefBadge>()
            .Single(badge => badge.DataContext is HistoryTagViewModel tag && tag.Name == name);

    private static Icon Named(RefBadge badge, string name)
        => badge.GetVisualDescendants().OfType<Icon>().Single(icon => icon.Name == name);

    private static async Task<RepositoryHandle> InitAsync(TestServices services, string name = "local")
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, name), "main");
        Commit(repository, "README.md", "Add the readme");

        return repository;
    }

    /// <summary>
    /// A repository whose one commit is on <c>main</c>, published to a bare <c>origin</c> that
    /// <c>main</c> tracks.
    /// </summary>
    private static async Task<(RepositoryHandle Repository, string Origin)> BuildPublishedAsync(TestServices services)
    {
        RepositoryHandle repository = await InitAsync(services);

        string origin = Path.Combine(services.ConfigurationRoot, "workspace", "origin.git");
        Directory.CreateDirectory(origin);
        Git(repository, "init", "--bare", origin);
        Git(repository, "remote", "add", "origin", origin);
        Git(repository, "push", "--set-upstream", "origin", "main");

        return (repository, origin);
    }

    private static void Commit(RepositoryHandle repository, string path, string message)
    {
        File.WriteAllText(Path.Combine(repository.WorkTreePath, path), message + "\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", message);
    }

    private static string Git(RepositoryHandle repository, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = repository.WorkTreePath,
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
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? output.Trim()
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }
}
