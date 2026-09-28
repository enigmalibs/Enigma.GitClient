using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// "Select in the history" from the branches and tags dialogs: the dialog closes, and the history under
/// it selects the line — reading further, when the line is not loaded yet.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryRevealTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryRevealTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void FromTheBranchesDialog_TheDialogClosesAndTheBranchsLineIsSelected()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            RepositoryHandle repository = await OpenAsync(services);
            MainWindow window = Show(services);

            try
            {
                HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
                await history.ReloadAsync();

                Task opening = history.OpenBranchesCommand.ExecuteAsync(null);
                Assert.True(services.Get<IToolDialogService>().IsOpen);

                BranchesPageViewModel branches = services.Get<BranchesPageViewModel>();
                BranchRowViewModel feature = branches.Groups.SelectMany(group => group.Rows).Single(row => row.Name == "feature");

                Assert.True(branches.SelectInHistoryCommand.CanExecute(feature));
                branches.SelectInHistoryCommand.Execute(feature);

                await opening.WaitAsync(Patience);

                Assert.False(services.Get<IToolDialogService>().IsOpen);
                Assert.Equal(Git(repository, "rev-parse", "feature"), history.SelectedRow?.Sha);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void FromTheTagsDialog_TheTaggedCommitsLineIsSelected_AnAnnotatedTagIncluded()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = Build();
            RepositoryHandle repository = await OpenAsync(services);
            MainWindow window = Show(services);

            try
            {
                HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
                await history.ReloadAsync();

                Task opening = history.OpenTagsCommand.ExecuteAsync(null);

                TagsPageViewModel tags = services.Get<TagsPageViewModel>();
                TagRowViewModel release = tags.Tags.Single(row => row.Name == "release");
                Assert.Equal("annotated", release.Kind);

                release.SelectInHistoryCommand!.Execute(release);
                await opening.WaitAsync(Patience);

                // The commit the tag names, not the tag object.
                Assert.Equal(Git(repository, "rev-parse", "release^{commit}"), history.SelectedRow?.Sha);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ACommitPastTheLoadedPages_IsReadAndSelected()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await OpenAsync(services, extraCommits: 12);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            history.PageSize = 4;
            await history.ReloadAsync();

            Assert.True(history.HasMore);

            string first = Git(repository, "rev-list", "--max-parents=0", "HEAD");

            Assert.True(await history.RevealAsync(first));
            Assert.Equal(first, history.SelectedRow?.Sha);
            Assert.True(history.Rows.Count > 4);
        });
    }

    [Fact]
    public void ACommitOnlyAHiddenBranchReaches_IsReportedRatherThanSelected()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await OpenAsync(services);

            services.Get<IHiddenBranches>().SetHidden(GitBranch.LocalPrefix + "feature", true);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            Assert.False(await history.RevealAsync(Git(repository, "rev-parse", "feature")));
            Assert.Null(history.SelectedRow);

            Assert.Equal("Not in the history", services.InfoBar.Last!.Title);
            Assert.Contains("hidden", services.InfoBar.Last.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TheSelectedLine_IsBroughtIntoView()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await OpenAsync(services, extraCommits: 60);

            HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
            await history.ReloadAsync();

            HistoryPageView view = services.Get<HistoryPageView>();
            view.DataContext = history;

            Window window = new() { Content = view, Width = 1100, Height = 400 };
            window.Show();

            try
            {
                window.UpdateLayout();

                string first = Git(repository, "rev-list", "--max-parents=0", "HEAD");
                Assert.True(await history.RevealAsync(first));

                for (int attempt = 0; attempt < 5; attempt++)
                {
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                }

                ListBox list = view.FindControl<ListBox>("CommitList")
                    ?? throw new InvalidOperationException("The history page has no commit list.");

                Assert.Contains(
                    list.GetRealizedContainers(),
                    container => container.DataContext is CommitRowViewModel row && row.Sha == first);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// The real dialog services, so the tool dialogs open on the window's hosts.
    /// </summary>
    private static TestServices Build()
        => TestServices.Build(useRealRefReader: true, configure: services =>
        {
            services.RemoveAll<IContentDialogService>();
            services.AddSingleton<IContentDialogService, ContentDialogService>();
        });

    private static MainWindow Show(TestServices services)
    {
        MainWindow window = services.Get<MainWindow>();
        window.DataContext = services.Get<MainWindowViewModel>();

        services.Get<IContentDialogService>().RegisterHost(window.HostDialog);
        services.Get<IToolDialogService>().RegisterHost(window.ToolDialog);

        window.Show();
        return window;
    }

    /// <summary>
    /// main with a few commits, a feature branch one commit off it, and an annotated tag on the first
    /// commit.
    /// </summary>
    private static async Task<RepositoryHandle> OpenAsync(TestServices services, int extraCommits = 0)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "reveal"), "main");

        Commit(repository, "README.md", "Add the readme");
        Git(repository, "tag", "-a", "release", "-m", "The first release");

        Git(repository, "checkout", "-b", "feature");
        Commit(repository, "feature.txt", "Start the feature");
        Git(repository, "checkout", "main");

        for (int index = 0; index < extraCommits; index++)
        {
            Commit(repository, $"file{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}.txt", $"Commit {index.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        await services.Get<IRepositoryContext>().OpenAsync(repository);

        return repository;
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
