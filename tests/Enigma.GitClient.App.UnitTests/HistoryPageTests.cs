using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Media;
using Enigma.GitClient.App.Controls;
using Enigma.Icons.Avalonia;
using Enigma.GitClient.App.Formatting;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives the history page against a real repository built for the test, which is the only way to
/// know the graph, the paging and the selection agree with what git actually reports.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryPageTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryPageTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Builds a repository with a branch, a merge and an unmerged side branch, and opens it.
    /// </summary>
    private static async Task<RepositoryHandle> BuildHistoryAsync(TestServices services, int extraCommits = 0)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "history"), "main");

        await CommitAsync(repository, "README.md", "# one\n", "Add the readme");
        await CommitAsync(repository, "src/app.txt", "one\n", "Add the application file");

        await GitAsync(repository, "checkout", "-b", "topic");
        await CommitAsync(repository, "src/topic.txt", "topic\n", "Work on the topic branch");

        await GitAsync(repository, "checkout", "main");
        await GitAsync(repository, "merge", "--no-ff", "topic", "-m", "Merge the topic branch");

        await CommitAsync(repository, "src/app.txt", "one\ntwo\n", "Extend the application file");

        await GitAsync(repository, "checkout", "-b", "feature", "HEAD~2");
        await CommitAsync(repository, "src/feature.txt", "feature\n", "Start the feature branch");
        await GitAsync(repository, "checkout", "main");

        for (int index = 0; index < extraCommits; index++)
        {
            await CommitAsync(
                repository,
                $"bulk/file{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}.txt",
                "content\n",
                $"Bulk commit {index.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        return repository;
    }

    private static async Task CommitAsync(RepositoryHandle repository, string path, string content, string message)
    {
        string full = Path.Combine(repository.WorkTreePath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content);

        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", message);
    }

    private static Task GitAsync(RepositoryHandle repository, params string[] arguments)
    {
        System.Diagnostics.ProcessStartInfo startInfo = new()
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

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? Task.CompletedTask
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }

    // ---------------------------------------------------------------- loading

    [Fact]
    public void Page_IsEmptyWithNoRepositoryOpen()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();
            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();

            Assert.True(page.IsEmpty);
            Assert.Contains("Open a repository", page.EmptyMessage, StringComparison.Ordinal);
            Assert.False(page.RefreshCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Page_LoadsTheHistoryOfTheOpenRepository()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            Assert.False(page.IsEmpty);

            // Six commits across main, topic and feature, and every row carries a graph segment.
            Assert.Equal(6, page.Rows.Count);
            Assert.All(page.Rows, row => Assert.NotNull(row.Row));
            Assert.All(page.Rows, row => Assert.Equal(7, row.ShortSha.Length));
        });
    }

    [Fact]
    public void Page_MarksTheHeadCommitAndItsBadges()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            CommitRowViewModel head = page.Rows.Single(row => row.IsHead);

            Assert.Equal("Extend the application file", head.Subject);
            Assert.Contains(head.Refs, badge => badge.Name == "main" && badge.IsCurrent);
            Assert.True(head.HasRefs);
        });
    }

    [Fact]
    public void Page_FormatsTheAuthorAndTheDate()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            CommitRowViewModel row = page.Rows[0];

            Assert.Equal("Ada Lovelace", row.AuthorName);
            Assert.Equal("AL", row.AuthorInitials);
            Assert.Contains("ada@example.com", row.AuthorTooltip, StringComparison.Ordinal);
            Assert.Equal("just now", row.RelativeDate);
            Assert.NotEqual(string.Empty, row.AbsoluteDate);
        });
    }

    [Fact]
    public void Page_ShowsTheUncommittedRowOnlyWhenThereIsSomethingUncommitted()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            Assert.DoesNotContain(page.Rows, row => row.IsUncommitted);

            await File.WriteAllTextAsync(
                Path.Combine(repository.WorkTreePath, "src", "app.txt"),
                "one\ntwo\nthree uncommitted\n");

            await page.ReloadAsync();

            CommitRowViewModel uncommitted = Assert.Single(page.Rows, row => row.IsUncommitted);
            Assert.Same(page.Rows[0], uncommitted);
            Assert.Equal("Uncommitted changes", uncommitted.Subject);
            Assert.Null(uncommitted.Commit);
        });
    }

    [Fact]
    public void Page_ShowsOneUncommittedRowWhenTwoLoadsOverlap()
    {
        _fixture.RunAsync(async () =>
        {
            GatedWorkingTreeProbe probe = new();
            using TestServices services = BuildWithProbe(probe);
            HistoryPageViewModel page = await OpenOverProbeAsync(services, probe);

            // Two reloads back to back — the page appearing while the automatic refresh redraws it.
            Task superseded = page.ReloadAsync();
            Task current = page.ReloadAsync();

            // git had answered the first one before it was superseded: cancelling cannot take that
            // answer back, and it reaches a list the second one has already cleared.
            probe.AnswerNext(dirty: true);
            await superseded.WaitAsync(Patience);

            probe.AnswerNext(dirty: true);
            await current.WaitAsync(Patience);

            CommitRowViewModel uncommitted = Assert.Single(page.Rows, row => row.IsUncommitted);
            Assert.Same(page.Rows[0], uncommitted);

            string[] commits = [.. page.Rows.Where(row => !row.IsUncommitted).Select(row => row.Sha)];
            Assert.Equal(6, commits.Length);
            Assert.Equal(commits.Length, commits.Distinct(StringComparer.Ordinal).Count());
        });
    }

    [Fact]
    public void Page_StaysBusyUntilTheLastOverlappingLoadHasFinished()
    {
        _fixture.RunAsync(async () =>
        {
            GatedWorkingTreeProbe probe = new();
            using TestServices services = BuildWithProbe(probe);
            HistoryPageViewModel page = await OpenOverProbeAsync(services, probe);

            Task superseded = page.ReloadAsync();
            Task current = page.ReloadAsync();

            probe.AnswerNext(dirty: false);
            await superseded.WaitAsync(Patience);

            // The second load is still waiting on git: nothing may start a third in the meantime. Read
            // now and asserted once both loads are over, so a failure never leaves one waiting.
            bool busyInBetween = page.IsBusy;
            bool refreshableInBetween = page.RefreshCommand.CanExecute(null);

            probe.AnswerNext(dirty: false);
            await current.WaitAsync(Patience);

            Assert.True(busyInBetween);
            Assert.False(refreshableInBetween);
            Assert.False(page.IsBusy);
            Assert.True(page.RefreshCommand.CanExecute(null));
        });
    }

    // ---------------------------------------------------------------- paging

    [Fact]
    public void Page_PagesTheHistoryWithoutRepeatingACommit()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services, extraCommits: 8);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            page.PageSize = 5;
            await page.ReloadAsync();

            Assert.Equal(5, page.Rows.Count);
            Assert.True(page.HasMore);
            Assert.True(page.LoadMoreCommand.CanExecute(null));

            await page.LoadMoreCommand.ExecuteAsync(null);

            Assert.Equal(10, page.Rows.Count);

            while (page.HasMore)
            {
                await page.LoadMoreCommand.ExecuteAsync(null);
            }

            Assert.Equal(14, page.Rows.Count);
            Assert.Equal(14, page.Rows.Select(row => row.Sha).Distinct().Count());
            Assert.False(page.LoadMoreCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Page_KeepsTheGraphConsistentAcrossPages()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services, extraCommits: 8);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel paged = services.Get<HistoryPageViewModel>();
            paged.PageSize = 3;
            await paged.ReloadAsync();

            while (paged.HasMore)
            {
                await paged.LoadMoreCommand.ExecuteAsync(null);
            }

            List<string> pagedLanes = [.. paged.Rows.Select(row =>
                $"{row.Sha}:{row.Row.Lane.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{row.Row.Colour.ToString(System.Globalization.CultureInfo.InvariantCulture)}")];

            using TestServices second = TestServices.Build(useRealRefReader: true);
            HistoryPageViewModel whole = second.Get<HistoryPageViewModel>();
            await second.Get<IRepositoryContext>().OpenAsync(repository);
            await whole.ReloadAsync();

            List<string> wholeLanes = [.. whole.Rows.Select(row =>
                $"{row.Sha}:{row.Row.Lane.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{row.Row.Colour.ToString(System.Globalization.CultureInfo.InvariantCulture)}")];

            Assert.Equal(wholeLanes, pagedLanes);
        });
    }

    // ---------------------------------------------------------------- the search

    [Fact]
    public void Search_MarksWhatItFoundAndHidesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            int rows = page.Rows.Count;
            Assert.Equal(6, rows);

            page.SearchText = "branch";

            // Every line is still there, and the graph with it: three of them are marked.
            Assert.Equal(rows, page.Rows.Count);
            Assert.Equal(3, page.MatchCount);
            Assert.Equal("3 matches", page.MatchSummary);

            Assert.All(
                page.Rows.Where(row => row.IsSearchMatch),
                row => Assert.Contains("branch", row.Subject, StringComparison.OrdinalIgnoreCase));

            Assert.Contains(page.Rows, row => !row.IsSearchMatch);
        });
    }

    [Fact]
    public void Search_MatchesTheBodyAsWellAsTheSubject()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);

            await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "notes.txt"), "note\n");
            await GitAsync(repository, "add", "--all");
            await GitAsync(repository, "commit", "-m", "Add a note", "-m", "Refs SUPPORT-4213 for the record");

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            page.SearchText = "support-4213";

            CommitRowViewModel marked = Assert.Single(page.Rows, row => row.IsSearchMatch);

            Assert.Equal("Add a note", marked.Subject);
            Assert.Equal("1 match", page.MatchSummary);
        });
    }

    [Fact]
    public void Search_SaysSoWhenItFoundNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            page.SearchText = "nothing matches this";

            // The list is untouched — the page is not empty, and it says the search found nothing
            // rather than leaving the reader to wonder.
            Assert.False(page.IsEmpty);
            Assert.Equal(6, page.Rows.Count);
            Assert.Equal(0, page.MatchCount);
            Assert.Equal("no match", page.MatchSummary);
            Assert.All(page.Rows, row => Assert.False(row.IsSearchMatch));
        });
    }

    [Fact]
    public void Search_IsForgottenWhenTheBoxIsCleared()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            page.SearchText = "branch";
            Assert.True(page.HasSearch);
            Assert.Contains(page.Rows, row => row.IsSearchMatch);

            page.ClearSearchCommand.Execute(null);

            Assert.False(page.HasSearch);
            Assert.Equal(string.Empty, page.MatchSummary);
            Assert.Equal(0, page.MatchCount);
            Assert.All(page.Rows, row => Assert.False(row.IsSearchMatch));
        });
    }

    [Fact]
    public void Search_MarksThePageLoadedAfterIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services, extraCommits: 8);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            page.PageSize = 3;
            await page.ReloadAsync();

            Assert.Equal(3, page.Rows.Count);

            // "readme" is in the oldest commit, which the first page does not reach: the search
            // marks what is loaded, and what is loaded is what the reader asked for.
            page.SearchText = "readme";
            Assert.Equal(0, page.MatchCount);

            while (page.HasMore)
            {
                await page.LoadMoreCommand.ExecuteAsync(null);
            }

            Assert.Equal("Add the readme", Assert.Single(page.Rows, row => row.IsSearchMatch).Subject);
        });
    }

    [Fact]
    public void Search_LeavesTheUncommittedRowAlone()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);

            // A dirty working directory, which is what puts the pseudo-row at the top.
            await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "README.md"), "# dirty\n");

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            CommitRowViewModel uncommitted = Assert.Single(page.Rows, row => row.IsUncommitted);

            // Its label is "Uncommitted changes", and it is not a commit message.
            page.SearchText = "uncommitted";

            Assert.False(uncommitted.IsSearchMatch);
            Assert.Equal(0, page.MatchCount);
        });
    }

    [Fact]
    public void Search_MarksTheRowsOnScreen()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, Panel workspace, _) =
                await ShowHistoryPageAsync(services);

            Assert.All(RowGrids(workspace), row => Assert.DoesNotContain("match", row.Classes));

            model.SearchText = "branch";
            window.UpdateLayout();

            List<Grid> rows = RowGrids(workspace);

            Assert.Equal(
                model.Rows.Count(row => row.IsSearchMatch),
                rows.Count(row => row.Classes.Contains("match")));

            // The marked rows are the ones the page marked, and no row left the list for it.
            Assert.Equal(model.Rows.Count, rows.Count);

            foreach (Grid row in rows)
            {
                Assert.Equal(
                    ((CommitRowViewModel)row.DataContext!).IsSearchMatch,
                    row.Classes.Contains("match"));
            }

            window.Close();
        });
    }

    [Fact]
    public void Search_WashesTheRowsItFound()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, Panel workspace, _) =
                await ShowHistoryPageAsync(services);

            Application application = Application.Current!;
            Assert.True(application.TryFindResource("SearchMatchBrush", application.ActualThemeVariant, out object? brush));

            Color found = Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

            // A row is always painted with something: that is what makes the whole line a hit
            // target for its own menu, washed or not.
            static Color Painted(Grid row) => Assert.IsAssignableFrom<ISolidColorBrush>(row.Background).Color;

            Assert.All(RowGrids(workspace), row => Assert.NotEqual(found, Painted(row)));
            Assert.All(RowGrids(workspace), row => Assert.Equal(Colors.Transparent, Painted(row)));

            model.SearchText = "branch";
            window.UpdateLayout();

            List<Grid> washed = [.. RowGrids(workspace).Where(row => Painted(row) == found)];

            Assert.NotEmpty(washed);
            Assert.Equal(model.Rows.Count(row => row.IsSearchMatch), washed.Count);

            foreach (Grid row in RowGrids(workspace))
            {
                Assert.Equal(
                    ((CommitRowViewModel)row.DataContext!).IsSearchMatch,
                    Painted(row) == found);
            }

            model.ClearSearchCommand.Execute(null);
            window.UpdateLayout();

            Assert.All(RowGrids(workspace), row => Assert.Equal(Colors.Transparent, Painted(row)));

            window.Close();
        });
    }

    [Fact]
    public void Search_KeepsAFoundRowHoverableAndSelectable()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view, Panel workspace, _) =
                await ShowHistoryPageAsync(services);

            Application application = Application.Current!;

            static Color Resource(string key)
            {
                Application application = Application.Current!;
                Assert.True(
                    application.TryFindResource(key, application.ActualThemeVariant, out object? brush),
                    $"the theme has no {key}");

                return Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
            }

            Color found = Resource("SearchMatchBrush");
            Color hovered = Resource("SearchMatchHoverBrush");
            Color selected = Resource("SearchMatchSelectedBrush");

            // Three states a reader has to be able to tell apart.
            Assert.NotEqual(found, hovered);
            Assert.NotEqual(found, selected);
            Assert.NotEqual(hovered, selected);

            static Color Painted(Grid row) => Assert.IsAssignableFrom<ISolidColorBrush>(row.Background).Color;

            ListBox list = view.FindControl<ListBox>("CommitList")
                ?? throw new InvalidOperationException("The history page has no commit list.");

            model.SearchText = "branch";
            window.UpdateLayout();

            Grid match = RowGrids(workspace).First(row => ((CommitRowViewModel)row.DataContext!).IsSearchMatch);
            Grid miss = RowGrids(workspace).First(row => !((CommitRowViewModel)row.DataContext!).IsSearchMatch);

            Assert.Equal(found, Painted(match));
            Assert.Equal(Colors.Transparent, Painted(miss));

            // Hovered. The pseudo-class rather than a synthetic pointer, because it is the state the
            // style selects on and the one the branches page already sets by hand after a drag.
            ListBoxItem Container(Grid row) => list.GetRealizedContainers()
                .OfType<ListBoxItem>()
                .First(container => ReferenceEquals(container.DataContext, row.DataContext));

            ((IPseudoClasses)Container(match).Classes).Set(":pointerover", true);
            ((IPseudoClasses)Container(miss).Classes).Set(":pointerover", true);
            window.UpdateLayout();

            Assert.Equal(hovered, Painted(match));

            // A row the search did not find is still transparent under the pointer: its container
            // goes on painting the hover, which is what every other list in the application does.
            Assert.Equal(Colors.Transparent, Painted(miss));

            ((IPseudoClasses)Container(match).Classes).Set(":pointerover", false);
            ((IPseudoClasses)Container(miss).Classes).Set(":pointerover", false);
            window.UpdateLayout();

            Assert.Equal(found, Painted(match));

            // Selected.
            model.SelectedRow = (CommitRowViewModel)match.DataContext!;
            window.UpdateLayout();

            Assert.Equal(selected, Painted(match));
            Assert.Equal(Colors.Transparent, Painted(miss));

            // And a found row that is both selected and hovered still says "selected", which is the
            // state the reader is acting on.
            ((IPseudoClasses)Container(match).Classes).Set(":pointerover", true);
            window.UpdateLayout();

            Assert.Equal(selected, Painted(match));

            // Clearing the search puts every row back to the container's own states.
            ((IPseudoClasses)Container(match).Classes).Set(":pointerover", false);
            model.ClearSearchCommand.Execute(null);
            window.UpdateLayout();

            Assert.All(RowGrids(workspace), row => Assert.Equal(Colors.Transparent, Painted(row)));

            window.Close();
        });
    }

    [Fact]
    public void Search_HasAllThreeWashesInBothThemes()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;

            foreach (ThemeVariant variant in (ThemeVariant[])[ThemeVariant.Dark, ThemeVariant.Light])
            {
                foreach (string key in (string[])["SearchMatchBrush", "SearchMatchHoverBrush", "SearchMatchSelectedBrush"])
                {
                    Assert.True(
                        application.TryFindResource(key, variant, out object? brush),
                        $"the {variant} theme has no {key}");

                    Assert.IsAssignableFrom<ISolidColorBrush>(brush);
                }
            }
        });
    }

    // ---------------------------------------------------------------- filters

    [Fact]
    public void Page_HidesMergedInBranchesWithFirstParentOnly()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            page.SelectedScope = page.ScopeOptions.Single(option => option.Scope == CommitLogScope.Head);
            await page.ReloadAsync();

            int withBranches = page.Rows.Count;

            page.FirstParentOnly = true;
            await page.ReloadAsync();

            Assert.True(page.Rows.Count < withBranches);
            Assert.DoesNotContain(page.Rows, row => row.Subject == "Work on the topic branch");
        });
    }

    [Fact]
    public void Page_ScopeSelectorSwitchesBetweenAllBranchesAndTheCurrentOne()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            Assert.Contains(page.Rows, row => row.Subject == "Start the feature branch");

            page.SelectedScope = page.ScopeOptions.Single(option => option.Scope == CommitLogScope.Head);
            await page.ReloadAsync();

            Assert.DoesNotContain(page.Rows, row => row.Subject == "Start the feature branch");
        });
    }

    // ---------------------------------------------------------------- selection

    [Fact]
    public void Page_PublishesTheSelectedCommitToTheShell()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            IRepositoryContext context = services.Get<IRepositoryContext>();
            await context.OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            page.SelectedRow = page.Rows[1];

            Assert.True(page.HasSelection);
            Assert.Equal(page.Rows[1].Sha, context.SelectedCommit?.Sha);

            page.SelectedRow = null;

            Assert.Null(context.SelectedCommit);
        });
    }

    [Fact]
    public void Page_ClearsItselfWhenTheRepositoryIsClosed()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            IRepositoryContext context = services.Get<IRepositoryContext>();
            await context.OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.OnAppearingAsync();

            Assert.False(page.IsEmpty);

            context.Close();
            await page.ReloadAsync();

            Assert.True(page.IsEmpty);
            Assert.Null(page.SelectedRow);
        });
    }

    // ---------------------------------------------------------------- formatting

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(30, "just now")]
    [InlineData(60, "1 minute ago")]
    [InlineData(60 * 5, "5 minutes ago")]
    [InlineData(60 * 60, "1 hour ago")]
    [InlineData(60 * 60 * 5, "5 hours ago")]
    [InlineData(60 * 60 * 24, "1 day ago")]
    [InlineData(60 * 60 * 24 * 3, "3 days ago")]
    [InlineData(60 * 60 * 24 * 10, "1 week ago")]
    [InlineData(60 * 60 * 24 * 45, "1 month ago")]
    [InlineData(60 * 60 * 24 * 400, "1 year ago")]
    public void RelativeTime_ReadsTheWayAHistoryShould(int secondsAgo, string expected)
    {
        DateTimeOffset now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, RelativeTime.Format(now.AddSeconds(-secondsAgo), now));
    }

    [Fact]
    public void RelativeTime_HandlesAFutureTimestamp()
    {
        DateTimeOffset now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("just now", RelativeTime.Format(now.AddHours(3), now));
    }

    [Fact]
    public void RelativeTime_ReturnsNothingForAnUnsetTimestamp()
    {
        Assert.Equal(string.Empty, RelativeTime.Format(DateTimeOffset.MinValue));
        Assert.Equal(string.Empty, RelativeTime.FormatAbsolute(DateTimeOffset.MinValue));
    }

    [Fact]
    public void AuthorAvatar_GivesTheSameNameTheSameColourEveryTime()
    {
        int first = Controls.AuthorAvatar.IndexFor("Ada Lovelace");
        int second = Controls.AuthorAvatar.IndexFor("Ada Lovelace");

        Assert.Equal(first, second);
        Assert.NotEqual(first, Controls.AuthorAvatar.IndexFor("Grace Hopper"));
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void Page_RendersTheGraphWithItsColumns()
    {
        _fixture.RunAsync(async () =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                using TestServices services = TestServices.Build(useRealRefReader: true);
                RepositoryHandle repository = await BuildHistoryAsync(services, extraCommits: 4);
                await services.Get<IRepositoryContext>().OpenAsync(repository);

                HistoryPageViewModel model = services.Get<HistoryPageViewModel>();
                await model.ReloadAsync();

                HistoryPageView view = services.Get<HistoryPageView>();
                view.DataContext = model;

                Window window = new() { Content = view, Width = 1200, Height = 460 };
                window.Show();

                for (int attempt = 0; attempt < 20; attempt++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                    Dispatcher.UIThread.RunJobs();
                }

                using Bitmap frame = window.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException("The history page produced no rendered frame.");

                string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, "history-page.png"), PngBitmapEncoderOptions.Default);

                string[] texts = [.. view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(block => block.Text ?? string.Empty)];

                Assert.Contains("Extend the application file", texts);
                Assert.Contains("Ada Lovelace", texts);
                Assert.Contains("main", texts);

                // The columns are named above the commits they hold.
                Assert.Contains("Message", texts);
                Assert.Contains("Author", texts);
                Assert.Contains("Date", texts);
                Assert.Contains("Commit", texts);
                Assert.Contains(texts, text => text.Length == 7 && text.All(char.IsAsciiLetterOrDigit));

                window.Close();
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    // ---------------------------------------------------------------- the row's hit area

    /// <summary>
    /// Finds the grids the rows are drawn with — the ones carrying each row's own context menu.
    /// </summary>
    private static List<Grid> RowGrids(Panel workspace)
        => [.. workspace.GetVisualDescendants()
            .OfType<Grid>()
            .Where(grid => grid.ContextMenu is not null && grid.DataContext is CommitRowViewModel)];

    [Fact]
    public void Row_OpensItsMenuFromAnywhereOnTheLine()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, _, _, Panel workspace, _) = await ShowHistoryPageAsync(services);

            Grid row = Assert.IsType<Grid>(RowGrids(workspace).FirstOrDefault());

            // The line is the row: whatever the columns do, the grid is as wide as the list gives it.
            Assert.True(row.Bounds.Width > 400, $"the row was only {row.Bounds.Width} wide");

            TextBlock subject = row.Children.OfType<TextBlock>().First();

            // The gap between two columns — ten points of ColumnSpacing carrying no child at all,
            // which is exactly where a right-click used to fall through to the list.
            Point gap = new(subject.Bounds.Right + 5, row.Bounds.Height / 2);

            Assert.False(
                row.Children.Any(child => child.Bounds.Contains(gap)),
                "the point picked for the test is inside a cell, so it proves nothing");

            // Hit testing reads the composed frame, so the window has to have drawn one.
            Render(window);

            Assert.Same(row, MenuOwnerAt(window, row.TranslatePoint(gap, window)));

            // And a point over a cell still reaches the same menu, through the cell.
            Assert.Same(row, MenuOwnerAt(window, row.TranslatePoint(subject.Bounds.Center, window)));

            window.Close();
        });
    }

    [Fact]
    public void ALinesMenu_IsBuiltFromItsEntriesAndABranchBadgeHasItsOwn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, _, _, Panel workspace, _) = await ShowHistoryPageAsync(services);

            Grid line = RowGrids(workspace).First(grid => ((CommitRowViewModel)grid.DataContext!).HasBranch);
            CommitRowViewModel row = (CommitRowViewModel)line.DataContext!;

            ContextMenu menu = line.ContextMenu!;
            menu.Open(line);
            Render(window);

            string[] headers = [.. menu.GetLogicalDescendants()
                .OfType<MenuItem>()
                .Select(item => item.Header as string ?? string.Empty)
                .Where(header => header != "-")];

            Assert.Equal(
                row.MenuEntries.Where(entry => !entry.IsSeparator).Select(entry => entry.Header),
                headers);
            Assert.Contains(headers, header => header.EndsWith("as merge source", StringComparison.Ordinal));

            menu.Close();

            // Each branch badge on the line opens a menu about that branch; the line's own is
            // behind it for everything else.
            RefBadge[] badges = [.. line.GetVisualDescendants().OfType<RefBadge>()];
            Assert.Equal(row.Branches.Count, badges.Count(badge => badge.ContextMenu is not null));

            window.Close();
        });
    }

    /// <summary>
    /// Draws the window, which is what gives the headless platform a frame to hit-test against.
    /// </summary>
    private static void Render(Window window)
    {
        window.UpdateLayout();

        for (int attempt = 0; attempt < 10; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// The control a right-click at a point would open a menu from: the nearest ancestor of what
    /// the pointer lands on that carries one.
    /// </summary>
    private static Control? MenuOwnerAt(Window window, Point? point)
        => (window.InputHitTest(point ?? throw new InvalidOperationException("The point is not in the window.")) as Visual)?
            .GetSelfAndVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(control => control.ContextMenu is not null);

    // ---------------------------------------------------------------- the columns and their header

    [Fact]
    public void Columns_StartAtTheirDefaults()
    {
        HistoryColumnLayout columns = new();

        Assert.Equal(0, columns.RefsWidth);
        Assert.Equal(190, columns.AuthorWidth);
        Assert.Equal(110, columns.DateWidth);
        Assert.Equal(70, columns.ShaWidth);

        // Nothing has been laid out yet, so the header is Auto rather than a width of zero.
        Assert.True(double.IsNaN(columns.HeaderWidth));
        Assert.Equal(HistoryColumnLayout.MinimumMessageWidth, columns.MessageWidth);
    }

    [Fact]
    public void Columns_TakeTheMeasuredBadgeWidthUntilTheReaderResizesThatColumn()
    {
        HistoryColumnLayout columns = new() { Viewport = 1000 };

        columns.SeedRefsWidth(140);
        Assert.Equal(140, columns.RefsWidth);

        columns.SeedRefsWidth(60);
        Assert.Equal(60, columns.RefsWidth);

        columns.Resize(HistoryColumn.Refs, 40);
        Assert.Equal(100, columns.RefsWidth);

        // From here the width is the reader's: a refresh that measures the badges again leaves it.
        columns.SeedRefsWidth(60);
        Assert.Equal(100, columns.RefsWidth);
    }

    [Theory]
    // The badge column sits before the message and grows to the right; the three after it grow to
    // the left, so every grip follows the pointer.
    [InlineData(HistoryColumn.Refs, 30, 30)]
    [InlineData(HistoryColumn.Refs, -30, 0)]
    [InlineData(HistoryColumn.Author, -30, 220)]
    [InlineData(HistoryColumn.Author, 30, 160)]
    [InlineData(HistoryColumn.Date, -25, 135)]
    [InlineData(HistoryColumn.Sha, -25, 95)]
    public void Columns_ResizeTowardsTheMessage(HistoryColumn column, double delta, double expected)
    {
        HistoryColumnLayout columns = new() { Viewport = 1000 };

        columns.Resize(column, delta);

        Assert.Equal(expected, columns.WidthOf(column));
    }

    [Fact]
    public void Columns_StopAtTheirMinimum()
    {
        HistoryColumnLayout columns = new() { Viewport = 1000 };

        columns.Resize(HistoryColumn.Author, 500);
        Assert.Equal(HistoryColumnLayout.MinimumWidth(HistoryColumn.Author), columns.AuthorWidth);

        columns.Resize(HistoryColumn.Sha, 500);
        Assert.Equal(HistoryColumnLayout.MinimumWidth(HistoryColumn.Sha), columns.ShaWidth);

        // The badge column may go all the way: a history with nothing decorated asks for no column.
        columns.Resize(HistoryColumn.Refs, -500);
        Assert.Equal(0, columns.RefsWidth);
    }

    [Fact]
    public void Columns_NeverTakeTheMessageBelowItsMinimum()
    {
        // Graph, refs, author, date, sha and five gaps leave the message exactly its minimum plus 40.
        HistoryColumnLayout columns = new() { GraphWidth = 60 };
        columns.SeedRefsWidth(100);
        columns.Viewport = 60 + 100 + 190 + 110 + 70 + (HistoryColumnLayout.ColumnSpacing * 5)
            + HistoryColumnLayout.MinimumMessageWidth + 40;

        Assert.Equal(40, columns.Slack);

        columns.Resize(HistoryColumn.Author, -300);

        Assert.Equal(230, columns.AuthorWidth);
        Assert.Equal(0, columns.Slack);
        Assert.Equal(HistoryColumnLayout.MinimumMessageWidth, columns.MessageWidth);

        // And nothing else can grow either, while shrinking still works.
        columns.Resize(HistoryColumn.Date, -100);
        Assert.Equal(110, columns.DateWidth);

        columns.Resize(HistoryColumn.Date, 30);
        Assert.Equal(80, columns.DateWidth);
    }

    [Fact]
    public void Header_LinesUpWithEveryRow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view, Panel workspace, _) =
                await ShowHistoryPageAsync(services);

            Grid header = view.FindControl<Grid>("HeaderRow")
                ?? throw new InvalidOperationException("The history page has no column header.");

            // The header is drawn at the list's own viewport, which is what the rows are given too.
            Assert.True(model.Columns.Viewport > 0, "the list never reported its viewport");
            Assert.Equal(model.Columns.Viewport, header.Bounds.Width, 1);

            AssertColumnsLineUp(header, workspace);

            // And after a drag: the author column grows to the left, the message gives up the room.
            double message = model.Columns.MessageWidth;
            model.Columns.Resize(HistoryColumn.Author, -40);
            window.UpdateLayout();

            Assert.Equal(230, model.Columns.AuthorWidth);
            Assert.Equal(message - 40, model.Columns.MessageWidth, 1);

            AssertColumnsLineUp(header, workspace);

            window.Close();
        });
    }

    [Fact]
    public void Header_LinesUpWithTheRowsWhenAScrollbarTakesTheirWidth()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view, Panel workspace, _) =
                await ShowHistoryPageAsync(services, extraCommits: 40);

            Grid header = view.FindControl<Grid>("HeaderRow")
                ?? throw new InvalidOperationException("The history page has no column header.");
            ListBox list = view.FindControl<ListBox>("CommitList")
                ?? throw new InvalidOperationException("The history page has no commit list.");

            // Fluent's scrollbar hides itself over the content, so by default it costs the rows
            // nothing. Pinned, it takes its width out of the viewport — which is the case the
            // header exists to survive, and the reason it follows the viewport rather than the page.
            ScrollViewer.SetAllowAutoHide(list, false);

            // Twice: the first pass is where the viewport shrinks and the header is told, the
            // second is where the header is laid out at what it was told.
            window.UpdateLayout();
            window.UpdateLayout();

            Assert.True(
                model.Columns.Viewport < view.Bounds.Width,
                $"the scrollbar took nothing: viewport {model.Columns.Viewport}, page {view.Bounds.Width}");

            Assert.Equal(model.Columns.Viewport, header.Bounds.Width, 1);
            AssertColumnsLineUp(header, workspace);

            window.Close();
        });
    }

    /// <summary>
    /// Asserts every row's cells sit at the same x as the header cell above them.
    /// </summary>
    private static void AssertColumnsLineUp(Grid header, Panel workspace)
    {
        List<double> headerEdges = [.. header.Children.Select(cell => cell.Bounds.Right)];

        foreach (Grid row in RowGrids(workspace))
        {
            List<double> rowEdges = [.. row.Children.Select(cell => cell.Bounds.Right)];

            Assert.Equal(headerEdges.Count, rowEdges.Count);

            for (int column = 0; column < headerEdges.Count; column++)
            {
                Assert.True(
                    Math.Abs(headerEdges[column] - rowEdges[column]) <= 1,
                    $"column {column} ends at {headerEdges[column]} in the header and {rowEdges[column]} in a row");
            }
        }
    }

    // ---------------------------------------------------------------- the badge column

    [Fact]
    public void RefColumn_IsEmptyWhenNothingIsDecorated()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);

            string root = Path.Combine(services.ConfigurationRoot, "workspace");
            Directory.CreateDirectory(root);

            RepositoryHandle repository = await services.Get<IRepositoryService>()
                .InitAsync(Path.Combine(root, "bare-history"), "main");

            await CommitAsync(repository, "README.md", "# one\n", "Add the readme");

            // Only the checked-out branch decorates anything, and it is on the newest commit; the
            // older one carries nothing, which is the case the column must not pay for.
            await CommitAsync(repository, "README.md", "# two\n", "Extend the readme");

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            await page.ReloadAsync();
            Assert.True(page.RefColumnWidth > 0, "the checked-out branch's badge asks for a column");

            // A history read with no references at all asks for nothing.
            Assert.Equal(0, RefBadgeMetrics.Measure([]));
            Assert.Equal(0, RefBadgeMetrics.Measure(null));
        });
    }

    [Fact]
    public void RefColumn_GrowsWithTheLongestBadgeAndStopsAtItsMaximum()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            double before = page.RefColumnWidth;
            Assert.True(before > 0);

            await GitAsync(repository, "branch", "a-considerably-longer-branch-name-than-main", "main");
            await services.Get<IRepositoryContext>().RefreshAsync();
            await page.ReloadAsync();

            Assert.True(
                page.RefColumnWidth > before,
                "the column kept its width when a longer branch name appeared");

            // And one absurd name does not take the subject's room.
            await GitAsync(repository, "branch", new string('x', 200), "main");
            await services.Get<IRepositoryContext>().RefreshAsync();
            await page.ReloadAsync();

            Assert.Equal(HistoryPageViewModel.MaximumRefColumnWidth, page.RefColumnWidth);
        });
    }

    [Fact]
    public void RefColumn_PutsEveryRowsSubjectAtTheSameX()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, Panel workspace, _) =
                await ShowHistoryPageAsync(services);

            Assert.True(model.RefColumnWidth > 0);
            Assert.Contains(model.Rows, row => row.HasRefs);
            Assert.Contains(model.Rows, row => !row.HasRefs);

            List<ItemsControl> strips = [.. workspace.GetVisualDescendants()
                .OfType<ItemsControl>()
                .Where(control => control.Name == "RefStrip")];

            Assert.NotEmpty(strips);

            // Every row's strip is the page's one width, decorated or not — which is what keeps the
            // columns after it on the same horizontal position. Within a pixel: layout rounding
            // snaps a measured width to the device grid, and the measurement is in points.
            foreach (ItemsControl strip in strips)
            {
                Assert.True(
                    Math.Abs(strip.Bounds.Width - model.RefColumnWidth) <= 1,
                    $"a row's badge strip was {strip.Bounds.Width} wide, not the column's {model.RefColumnWidth}");
            }

            List<double> subjectLefts = [.. strips
                .Select(strip => strip.GetVisualParent() as Grid)
                .OfType<Grid>()
                .Select(row => row.Children.OfType<TextBlock>().First())
                .Select(subject => subject.Bounds.X)];

            Assert.NotEmpty(subjectLefts);
            Assert.All(subjectLefts, left => Assert.Equal(subjectLefts[0], left, 3));

            window.Close();
        });
    }

    // ---------------------------------------------------------------- the badges say the whole name

    [Fact]
    public void Badge_MeasuresItsWholeLabelHoweverLongItIs()
    {
        // A badge used to stop measuring at 180 points, which is the measurement side of the
        // ellipsis the template used to draw. Two names that differ only past that point must now
        // measure differently, or the column they seed cannot grow to hold them.
        string shorter = new('x', 200);
        string longer = new('x', 400);

        double shorterBadge = RefBadgeMetrics.MeasureBadge(shorter);
        double longerBadge = RefBadgeMetrics.MeasureBadge(longer);

        Assert.True(
            longerBadge > shorterBadge,
            $"a 400-character name measured {longerBadge}, no more than a 200-character one at {shorterBadge}");

        // And the chrome is still counted around the label, whatever the label is.
        double chrome = (RefBadgeMetrics.HorizontalPadding * 2) + RefBadgeMetrics.IconSize + RefBadgeMetrics.IconSpacing;

        Assert.Equal(chrome, RefBadgeMetrics.MeasureBadge(string.Empty));
        Assert.Equal(chrome, RefBadgeMetrics.MeasureBadge(null));
    }

    [Fact]
    public void Badge_DrawsItsNameWithoutTrimmingIt()
    {
        _fixture.Run(() =>
        {
            RefBadge badge = new() { Kind = GitRefKind.LocalBranch, Text = new string('x', 300) };

            Window window = new() { Content = badge, Width = 1100, Height = 120 };
            window.Show();
            window.UpdateLayout();

            TextBlock label = badge.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Text == badge.Text);

            // No ellipsis, and nothing bounding the label: how much of a name fits is the reader's
            // decision, taken with the Refs column's grip.
            Assert.Equal(TextTrimming.None, label.TextTrimming);
            Assert.True(double.IsPositiveInfinity(label.MaxWidth), $"the label was bounded at {label.MaxWidth}");

            // The full name is still reachable where the strip clips it.
            Assert.Equal(badge.Text, ToolTip.GetTip(badge.GetVisualDescendants().OfType<Border>().First()));

            window.Close();
        });
    }

    [Fact]
    public void RefColumn_StillSeedsItselfAndStillStopsAtItsMaximum()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            // The measurement is offered to the layout, and the layout takes it while the reader
            // has not claimed the column.
            Assert.True(page.RefColumnWidth > 0);
            Assert.False(page.Columns.IsRefsWidthOwnedByReader);
            Assert.Equal(page.RefColumnWidth, page.Columns.RefsWidth);

            // An untrimmed badge measures its whole name, but the column it seeds is still capped:
            // one absurd branch name does not take the subject's room on first paint.
            await GitAsync(repository, "branch", new string('x', 200), "main");
            await services.Get<IRepositoryContext>().RefreshAsync();
            await page.ReloadAsync();

            Assert.Equal(HistoryPageViewModel.MaximumRefColumnWidth, page.RefColumnWidth);
        });
    }

    [Fact]
    public void BadgeMetrics_AgreeWithWhatTheTemplateDraws()
    {
        _fixture.Run(() =>
        {
            RefBadge badge = new() { Kind = GitRefKind.LocalBranch, Text = "main" };

            Window window = new() { Content = badge, Width = 400, Height = 120 };
            window.Show();
            window.UpdateLayout();

            // The metrics are constants rather than bindings, because a per-badge binding to a
            // theme resource would measure thousands of rows through the resource system. This is
            // what keeps them honest: every one of them is read back off a realised badge.
            Border pill = badge.GetVisualDescendants().OfType<Border>().First();
            StackPanel content = pill.GetVisualDescendants().OfType<StackPanel>().First();
            TextBlock label = content.GetVisualDescendants().OfType<TextBlock>().First();

            Assert.Equal(RefBadgeMetrics.FontSize, label.FontSize);
            Assert.Equal(RefBadgeMetrics.IconSpacing, content.Spacing);
            Assert.Equal(RefBadgeMetrics.HorizontalPadding, pill.Padding.Left + pill.BorderThickness.Left);
            Assert.Equal(RefBadgeMetrics.HorizontalPadding, pill.Padding.Right + pill.BorderThickness.Right);

            Icon icon = content.GetVisualDescendants().OfType<Icon>().First();
            Assert.Equal(RefBadgeMetrics.IconSize, icon.Size);

            window.Close();
        });
    }

    [Fact]
    public void Badge_IsRoomierAndStillFitsAHistoryRow()
    {
        _fixture.Run(() =>
        {
            RefBadge badge = new() { Kind = GitRefKind.LocalBranch, Text = "main" };

            Window window = new() { Content = badge, Width = 400, Height = 200 };
            window.Show();
            window.UpdateLayout();

            Border pill = badge.GetVisualDescendants().OfType<Border>().First();
            StackPanel content = pill.GetVisualDescendants().OfType<StackPanel>().First();

            // Room on every side, not only beside the text.
            Assert.True(pill.Padding.Top > 0 && pill.Padding.Bottom > 0, "the badge has no vertical padding");

            Assert.True(
                pill.Bounds.Width > content.Bounds.Width,
                "the badge is no wider than the content it wraps");

            // And it still sits inside a history row at its default height.
            Assert.True(
                badge.Bounds.Height <= AppSettings.Defaults.GraphRowHeight,
                $"a badge is {badge.Bounds.Height} tall, more than a {AppSettings.Defaults.GraphRowHeight} px row");

            window.Close();
        });
    }

    [Fact]
    public void RefColumn_WidensForTheBiggerBadge()
    {
        // The same reference measured with the template's own numbers: a badge is the chrome the
        // template draws plus its label, and both grew.
        double chrome = (RefBadgeMetrics.HorizontalPadding * 2) + RefBadgeMetrics.IconSize + RefBadgeMetrics.IconSpacing;

        Assert.Equal(34, chrome);
        Assert.True(RefBadgeMetrics.MeasureBadge("main") > chrome);

        // Two badges on a row are still separated by the strip's own spacing.
        RefBadgeItem[] two = [new(GitRefKind.LocalBranch, "main", true), new(GitRefKind.Tag, "v1.0.0", false)];

        Assert.Equal(
            RefBadgeMetrics.MeasureBadge("main") + RefBadgeMetrics.MeasureBadge("v1.0.0") + RefBadgeMetrics.BadgeSpacing,
            RefBadgeMetrics.Measure(two));
    }

    // ---------------------------------------------------------------- the badges are not dragged

    [Fact]
    public void CommitList_TakesNoDrops()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, _, HistoryPageView view, Panel workspace, _) = await ShowHistoryPageAsync(services);

            ListBox list = view.FindControl<ListBox>("CommitList")
                ?? throw new InvalidOperationException("The history page has no commit list.");

            // Merging one branch into another by dropping it is the branches page's gesture; here
            // the badges say which references point at a commit and nothing more.
            Assert.False(DragDrop.GetAllowDrop(list));

            List<ItemsControl> strips = [.. workspace.GetVisualDescendants()
                .OfType<ItemsControl>()
                .Where(control => control.Name == "RefStrip")];

            Assert.NotEmpty(strips);
            Assert.All(strips, strip => Assert.Null(ToolTip.GetTip(strip)));

            window.Close();
        });
    }

    // ---------------------------------------------------------------- the diffs on the page

    /// <summary>
    /// Builds the page against a real repository, shows it, and hands the test the pieces the diff
    /// view's behaviour is read from.
    /// </summary>
    private static async Task<(Window Window, HistoryPageViewModel Model, HistoryPageView View, Panel Workspace, Border DiffPage)>
        ShowHistoryPageAsync(TestServices services, int extraCommits = 0)
    {
        RepositoryHandle repository = await BuildHistoryAsync(services, extraCommits);
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel model = services.Get<HistoryPageViewModel>();
        await model.ReloadAsync();

        HistoryPageView view = services.Get<HistoryPageView>();
        view.DataContext = model;

        Window window = new() { Content = view, Width = 1200, Height = 900 };
        window.Show();
        window.UpdateLayout();

        Panel workspace = view.FindControl<Panel>("Workspace")
            ?? throw new InvalidOperationException("The history page has no workspace.");
        Border diffPage = view.FindControl<Border>("DiffPage")
            ?? throw new InvalidOperationException("The history page has no diff view.");

        return (window, model, view, workspace, diffPage);
    }

    /// <summary>
    /// What the diff view says it is showing.
    /// </summary>
    private static string SubjectOf(HistoryPageView view)
        => view.FindControl<TextBlock>("DiffSubject")?.Text ?? string.Empty;

    [Fact]
    public void DiffView_StaysClosedWhenARowIsMerelySelected()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, Panel workspace, Border diffPage) =
                await ShowHistoryPageAsync(services);

            Assert.Null(model.SelectedRow);
            Assert.False(model.IsDiffViewOpen);
            Assert.False(diffPage.IsVisible);

            // Nothing is taken from the graph: the list is the page's body.
            ListBox list = workspace.GetVisualDescendants().OfType<ListBox>().First();
            Assert.Equal(workspace.Bounds.Height, list.Bounds.Height);

            // Selecting a line selects it. The diffs are asked for, not implied.
            model.SelectedRow = model.Rows[0];
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(model.IsDiffViewOpen);
            Assert.False(diffPage.IsVisible);
            Assert.Same(model.Rows[0], model.SelectedRow);

            // Asking shows them, and the graph underneath keeps the height — and therefore the
            // scroll position — it had.
            model.RowCommands.Activate.Execute(model.Rows[0]);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(model.IsDiffViewOpen);
            Assert.True(diffPage.IsVisible);
            Assert.Equal(workspace.Bounds.Height, list.Bounds.Height);

            window.Close();
        });
    }

    [Fact]
    public void DiffView_OpensOnADoubleClick()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view, _, Border diffPage) =
                await ShowHistoryPageAsync(services);

            CommitRowViewModel row = model.Rows.First(candidate => candidate.Commit is not null);

            // What the view's DoubleTapped handler runs.
            row.Commands!.Activate.Execute(row);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Same(row, model.SelectedRow);
            Assert.True(model.IsDiffViewOpen);
            Assert.True(diffPage.IsVisible);
            Assert.Equal(row.Subject, SubjectOf(view));

            window.Close();
        });
    }

    [Fact]
    public void DiffView_ShowsWhatTheSelectedCommitChanged()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view, _, Border diffPage) =
                await ShowHistoryPageAsync(services);

            CommitRowViewModel row = model.Rows.First(candidate => candidate.Commit is not null);
            model.RowCommands.ShowChanges.Execute(row);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(row.Subject, SubjectOf(view));

            // The changed files and the diff moved onto the page as they were.
            Assert.Single(diffPage.GetVisualDescendants().OfType<Views.Panels.ChangedFilesPanelView>());
            Assert.Single(diffPage.GetVisualDescendants().OfType<Views.Panels.DiffViewerView>());

            // Asking for another row's changes leaves the view open and moves it onto that commit.
            CommitRowViewModel other = model.Rows.Last(candidate => candidate.Commit is not null);
            model.RowCommands.ShowChanges.Execute(other);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(diffPage.IsVisible);
            Assert.Equal(other.Subject, SubjectOf(view));

            window.Close();
        });
    }

    [Fact]
    public void DiffView_TakesTheWholePageAndFollowsIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view, Panel workspace, Border diffPage) =
                await ShowHistoryPageAsync(services);

            model.RowCommands.Activate.Execute(model.Rows[0]);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(view.Bounds.Width, diffPage.Bounds.Width, 3);
            Assert.Equal(view.Bounds.Height, diffPage.Bounds.Height, 3);

            // The history is still laid out underneath, which is what keeps the reader's place in
            // it; the panel is simply drawn over it.
            Assert.True(workspace.Bounds.Height > 0);

            window.Width = 900;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(view.Bounds.Width, diffPage.Bounds.Width, 3);

            window.Close();
        });
    }

    [Fact]
    public void DiffView_ClosesWithoutLosingTheSelection()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, _, Border diffPage) =
                await ShowHistoryPageAsync(services);

            CommitRowViewModel row = model.Rows.First(candidate => candidate.Commit is not null);
            model.RowCommands.Activate.Execute(row);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            model.CloseDiffViewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(diffPage.IsVisible);
            Assert.False(model.IsDiffViewOpen);

            // The selection is also the start point "create a branch here" falls back to, and the
            // highlight that says where the reader is, so closing must not take it away.
            Assert.Same(row, model.SelectedRow);
            Assert.True(model.HasSelection);
            Assert.Equal(row.Sha, services.Get<IRepositoryContext>().SelectedCommit?.Sha);

            window.Close();
        });
    }

    [Fact]
    public void DiffView_ClosesFromItsBackButton()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view, _, Border diffPage) =
                await ShowHistoryPageAsync(services);

            model.RowCommands.Activate.Execute(model.Rows[0]);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Button back = view.FindControl<Button>("LeaveDiffView")
                ?? throw new InvalidOperationException("The diff view has no way back.");

            Assert.NotNull(back.Command);
            back.Command.Execute(back.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(model.IsDiffViewOpen);
            Assert.False(diffPage.IsVisible);
            Assert.NotNull(model.SelectedRow);

            window.Close();
        });
    }

    [Fact]
    public void DiffView_GoesAwayWithTheSelection()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, _, Border diffPage) =
                await ShowHistoryPageAsync(services);

            model.RowCommands.Activate.Execute(model.Rows[0]);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(diffPage.IsVisible);

            // What a reload does after a checkout: the selection it was showing is gone.
            model.SelectedRow = null;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(model.IsDiffViewOpen);
            Assert.False(diffPage.IsVisible);

            window.Close();
        });
    }

    [Fact]
    public void ShowChanges_BringsTheViewBackForTheSelectedRow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, _, Border diffPage) =
                await ShowHistoryPageAsync(services);

            CommitRowViewModel row = model.Rows.First(candidate => candidate.Commit is not null);
            model.RowCommands.Activate.Execute(row);
            Dispatcher.UIThread.RunJobs();

            model.CloseDiffViewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(diffPage.IsVisible);

            // The selection never changed, so nothing but the menu can bring the view back.
            model.RowCommands.ShowChanges.Execute(row);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(diffPage.IsVisible);
            Assert.Same(row, model.SelectedRow);

            window.Close();
        });
    }

    [Fact]
    public void ShowChanges_SelectsAnotherRowAndOpensItThere()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view, _, Border diffPage) =
                await ShowHistoryPageAsync(services);

            CommitRowViewModel row = model.Rows.Last(candidate => candidate.Commit is not null);

            Assert.Null(model.SelectedRow);

            model.RowCommands.ShowChanges.Execute(row);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Same(row, model.SelectedRow);
            Assert.True(diffPage.IsVisible);
            Assert.Equal(row.Subject, SubjectOf(view));

            window.Close();
        });
    }

    [Fact]
    public void DiffView_TakesTheFocusWhenItOpens()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, _, Border diffPage) =
                await ShowHistoryPageAsync(services);

            model.RowCommands.Activate.Execute(model.Rows[0]);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // Nothing was clicked: the page moved the focus itself, which is what makes the first
            // Escape work.
            Assert.True(diffPage.IsFocused, "the diff view did not take the focus when it opened");

            window.Close();
        });
    }

    [Fact]
    public void Escape_LeavesTheDiffViewWithoutAClickFirst()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, _, Border diffPage) =
                await ShowHistoryPageAsync(services);

            CommitRowViewModel row = model.Rows.First(candidate => candidate.Commit is not null);
            model.RowCommands.Activate.Execute(row);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.True(diffPage.IsVisible);

            // Straight to the key, with nothing clicked in between.
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(model.IsDiffViewOpen);
            Assert.False(diffPage.IsVisible);

            // Every other way out keeps the selection, and so does this one.
            Assert.Same(row, model.SelectedRow);

            window.Close();
        });
    }

    [Fact]
    public void Escape_ClosesTheDiffViewFromInsideItsOwnPanes()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, _, Border diffPage) =
                await ShowHistoryPageAsync(services);

            model.RowCommands.Activate.Execute(model.Rows[0]);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // The file filter is the control most likely to have the focus, and a TextBox is
            // exactly the kind of control that swallows a key it is offered.
            TextBox filter = diffPage.GetVisualDescendants().OfType<TextBox>().First();
            filter.Focus();
            Dispatcher.UIThread.RunJobs();

            Assert.True(filter.IsFocused);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(model.IsDiffViewOpen);
            Assert.False(diffPage.IsVisible);

            window.Close();
        });
    }

    [Fact]
    public void Escape_DoesNothingOnTheGraphItself()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, _, Border diffPage) =
                await ShowHistoryPageAsync(services);

            model.SelectedRow = model.Rows[0];
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(diffPage.IsVisible);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            // Nothing opened, nothing closed, and the selection is where the reader left it.
            Assert.False(model.IsDiffViewOpen);
            Assert.Same(model.Rows[0], model.SelectedRow);

            window.Close();
        });
    }

    [Fact]
    public void DiffView_LetsEachPaneScrollItself()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);

            string root = Path.Combine(services.ConfigurationRoot, "workspace");
            Directory.CreateDirectory(root);

            RepositoryHandle repository = await services.Get<IRepositoryService>()
                .InitAsync(Path.Combine(root, "big"), "main");

            // Big in both directions: more files than the list can show, and a patch longer than
            // the viewer can show.
            string before = string.Join('\n', Enumerable.Range(0, 2000).Select(line => $"line {line}")) + "\n";
            string after = string.Join('\n', Enumerable.Range(0, 2000).Select(line => line % 5 == 0 ? $"changed {line}" : $"line {line}")) + "\n";

            await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "big.txt"), before);
            await GitAsync(repository, "add", "--all");
            await GitAsync(repository, "commit", "-m", "Add the big file");

            await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "big.txt"), after);

            for (int index = 0; index < 40; index++)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(repository.WorkTreePath, $"file{index.ToString(CultureInfo.InvariantCulture)}.txt"),
                    "content\n");
            }

            await GitAsync(repository, "add", "--all");
            await GitAsync(repository, "commit", "-m", "Change a great deal");

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel model = services.Get<HistoryPageViewModel>();
            await model.ReloadAsync();

            HistoryPageView view = services.Get<HistoryPageView>();
            view.DataContext = model;

            Window window = new() { Content = view, Width = 1200, Height = 900 };
            window.Show();
            window.UpdateLayout();

            model.RowCommands.ShowChanges.Execute(model.Rows.First(row => row.Subject == "Change a great deal"));

            await WaitUntilAsync(() => model.Files.FileCount > 0);

            model.Files.ViewMode = ViewModels.Panels.ChangedFilesViewMode.List;

            Assert.True(model.Files.SelectPath("big.txt"));
            await WaitUntilAsync(() => model.Diff.HasPatch);

            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Border diffPage = view.FindControl<Border>("DiffPage")!;
            DockPanel body = view.FindControl<DockPanel>("DiffPageBody")!;

            // The body is bounded by the page, so nothing above the two panes scrolls at all.
            Assert.True(
                body.Bounds.Height <= diffPage.Bounds.Height,
                $"the body is {body.Bounds.Height} tall inside a {diffPage.Bounds.Height} page");

            // And each pane has more than it can show, with its own viewport to show it in.
            // The list's own scroll, not the filter box's: a TextBox templates one too.
            ScrollViewer files = ScrollOf(diffPage.GetVisualDescendants()
                .OfType<Views.Panels.ChangedFilesPanelView>()
                .Single());

            ScrollViewer diff = ScrollOf(diffPage.GetVisualDescendants()
                .OfType<Views.Panels.DiffViewerView>()
                .Single());

            Assert.True(
                files.Extent.Height > files.Viewport.Height,
                $"the file list does not scroll itself: extent {files.Extent.Height}, viewport {files.Viewport.Height}");

            Assert.True(
                diff.Extent.Height > diff.Viewport.Height,
                $"the diff does not scroll itself: extent {diff.Extent.Height}, viewport {diff.Viewport.Height}");

            // They are two scrolls, not one.
            Assert.NotSame(files, diff);

            window.Close();
        });
    }

    /// <summary>
    /// The scroll of the one list a panel is currently showing.
    /// </summary>
    private static ScrollViewer ScrollOf(Control panel)
        => panel.GetVisualDescendants()
            .OfType<ListBox>()
            .Where(list => list.IsVisible && list.Bounds.Height > 0)
            .SelectMany(list => list.GetVisualDescendants().OfType<ScrollViewer>())
            .First();

    private static async Task WaitUntilAsync(Func<bool> condition, int attempts = 200)
    {
        for (int attempt = 0; attempt < attempts && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "the page never reached the state the test waited for");
    }

    /// <summary>
    /// The container with the working-tree probe replaced, over the real reference reader.
    /// </summary>
    private static TestServices BuildWithProbe(IWorkingTreeProbe probe)
        => TestServices.Build(useRealRefReader: true, configure: services =>
        {
            services.RemoveAll<IWorkingTreeProbe>();
            services.AddSingleton(probe);
        });

    /// <summary>
    /// Opens the test history over a gated probe, and returns the page once its own first read is over.
    /// </summary>
    /// <remarks>
    /// Choosing its scope as it is built already reads the history once. That read is answered here,
    /// so the loads a test starts are the only ones left asking.
    /// </remarks>
    private static async Task<HistoryPageViewModel> OpenOverProbeAsync(TestServices services, GatedWorkingTreeProbe probe)
    {
        RepositoryHandle repository = await BuildHistoryAsync(services);
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();

        probe.AnswerNext(dirty: false);
        await WaitUntilAsync(() => page.IsNotBusy);

        return page;
    }

    /// <summary>
    /// A working-tree probe that answers when the test says so, oldest question first — and, like a
    /// <c>git status</c> that had already finished, answers a load that was cancelled in the meantime.
    /// </summary>
    private sealed class GatedWorkingTreeProbe : IWorkingTreeProbe
    {
        private readonly Queue<TaskCompletionSource<bool>> _questions = new();

        public Task<bool> IsDirtyAsync(
            RepositoryHandle repository,
            bool includeUntracked = true,
            CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<bool> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _questions.Enqueue(answer);
            return answer.Task;
        }

        public void AnswerNext(bool dirty) => _questions.Dequeue().SetResult(dirty);
    }
}
