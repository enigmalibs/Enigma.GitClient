using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.ViewModels.Panels;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Opening what the user is looking at on the host its remote points at.
/// </summary>
/// <remarks>
/// The repositories here are real, with real remotes, because the question being asked is "does the
/// client recognise the remote git actually has?" — which a faked remote cannot answer.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class HostLinkTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public HostLinkTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static TestServices BuildServices()
        => TestServices.Build(useRealRefReader: true, configure: services =>
        {
            // The real GitHub provider builds links with no network at all, but it must never be
            // the one asked to list anything in a test, so the fake stands in for the whole kind.
            services.RemoveAll<IRepositoryHostProvider>();
            services.AddSingleton<IRepositoryHostProvider>(new FakeHostProvider());
        });

    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services, string? remote)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "linked"), "main");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "First");

        if (remote is not null)
        {
            await GitAsync(repository, "remote", "add", "origin", remote);
        }

        return repository;
    }

    private static Task GitAsync(RepositoryHandle repository, params string[] arguments)
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
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? Task.CompletedTask
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }

    [Fact]
    public void ARepositoryOnAKnownHostSaysWhichOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildServices();
            RepositoryHandle repository = await BuildRepositoryAsync(services, "https://github.com/octocat/hello-world.git");

            IHostLinkService links = services.Get<IHostLinkService>();

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            await links.RefreshAsync();

            Assert.Equal("GitHub", links.HostName);
        });
    }

    [Fact]
    public void ARepositoryWithNoRemoteHasNoHost()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildServices();
            RepositoryHandle repository = await BuildRepositoryAsync(services, remote: null);

            IHostLinkService links = services.Get<IHostLinkService>();

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            await links.RefreshAsync();

            Assert.Null(links.HostName);

            Assert.False(await links.OpenCommitAsync("abc123"));
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "No host to open this on");
        });
    }

    [Fact]
    public void ARemoteOnAHostNobodyClaimsHasNoHostEither()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildServices();
            RepositoryHandle repository = await BuildRepositoryAsync(services, "https://git.example.org/team/repo.git");

            IHostLinkService links = services.Get<IHostLinkService>();

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            await links.RefreshAsync();

            Assert.Null(links.HostName);
        });
    }

    [Fact]
    public void ASshRemoteIsTheSameRepositoryAsItsHttpsOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildServices();
            RepositoryHandle repository = await BuildRepositoryAsync(services, "git@github.com:octocat/hello-world.git");

            IHostLinkService links = services.Get<IHostLinkService>();

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            await links.RefreshAsync();

            Assert.True(await links.OpenCommitAsync("abc123"));

            Assert.Equal(
                "https://github.com/octocat/hello-world/commit/abc123",
                services.Interop.Opened.Single());
        });
    }

    [Fact]
    public void OpeningABranchAndAFileGoesThroughTheSameRemote()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildServices();
            RepositoryHandle repository = await BuildRepositoryAsync(services, "https://github.com/octocat/hello-world.git");

            IHostLinkService links = services.Get<IHostLinkService>();

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            await links.RefreshAsync();

            await links.OpenBranchAsync("main");
            await links.OpenFileAsync("abc123", "src/app.txt");

            Assert.Equal(
                [
                    "https://github.com/octocat/hello-world/tree/main",
                    "https://github.com/octocat/hello-world/blob/abc123/src/app.txt",
                ],
                services.Interop.Opened);
        });
    }

    [Fact]
    public void ALinkNothingCanOpenIsReported()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildServices();
            RepositoryHandle repository = await BuildRepositoryAsync(services, "https://github.com/octocat/hello-world.git");

            services.Interop.OpensUrls = false;

            IHostLinkService links = services.Get<IHostLinkService>();

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            await links.RefreshAsync();

            Assert.False(await links.OpenCommitAsync("abc123"));
            Assert.Contains(services.InfoBar.Shown, note => note.Title == "Could not open the link");
        });
    }

    // ---------------------------------------------------------------- from the pages

    [Fact]
    public void TheGraphsRowMenuOffersTheHostByName()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildServices();
            RepositoryHandle repository = await BuildRepositoryAsync(services, "https://github.com/octocat/hello-world.git");

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            await services.Get<IHostLinkService>().RefreshAsync();

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.OnAppearingAsync();

            CommitRowViewModel row = page.Rows.First(candidate => candidate.Commit is not null);

            Assert.True(row.CanOpenOnHost);
            Assert.Equal("Open this commit on GitHub", row.HostLabel);

            await row.Commands!.OpenOnHost!.ExecuteAsync(row);

            Assert.Contains(
                $"https://github.com/octocat/hello-world/commit/{row.Commit!.Sha}",
                services.Interop.Opened);
        });
    }

    [Fact]
    public void TheChangedFilesPanelOpensAFileAtTheSelectedCommit()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = BuildServices();
            RepositoryHandle repository = await BuildRepositoryAsync(services, "https://github.com/octocat/hello-world.git");

            await services.Get<IRepositoryContext>().OpenAsync(repository);
            await services.Get<IHostLinkService>().RefreshAsync();

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.OnAppearingAsync();

            page.SelectedRow = page.Rows.First(candidate => candidate.Commit is not null);

            for (int attempt = 0; attempt < 400 && page.Files.FileCount == 0; attempt++)
            {
                await Task.Delay(5);
            }

            Assert.True(page.Files.HasHostLink);
            Assert.True(page.Files.SelectPath("README.md"));

            await page.Files.OpenOnHostCommand.ExecuteAsync(page.Files.SelectedNode);

            Assert.Contains(
                $"https://github.com/octocat/hello-world/blob/{page.SelectedRow!.Sha}/README.md",
                services.Interop.Opened);
        });
    }

    [Fact]
    public void ADirectoryRowHasNothingToOpenOnAHost()
    {
        _fixture.Run(() =>
        {
            using TestServices services = BuildServices();

            ChangedFilesPanelViewModel panel = new(services.Interop)
            {
                OpenOnHost = _ => Task.CompletedTask,
            };

            Assert.True(panel.HasHostLink);
            Assert.False(panel.OpenOnHostCommand.CanExecute(null));
        });
    }
}
