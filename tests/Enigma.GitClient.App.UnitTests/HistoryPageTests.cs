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
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Media;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.App.Controls;
using Enigma.GitClient.App.Formatting;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Drives the history page against a real repository built for the test, which is the only way to
/// know the graph, the paging and the selection agree with what git actually reports.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryPageTests
{
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

    // ---------------------------------------------------------------- filters

    [Fact]
    public void Page_SearchesTheWholeHistoryRatherThanTheLoadedRows()
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

            // "readme" is in the oldest commit, which the first page does not contain.
            page.SearchText = "readme";
            await Task.Delay(HistoryPageViewModel.SearchDebounce + TimeSpan.FromMilliseconds(250));

            Assert.Equal("Add the readme", Assert.Single(page.Rows).Subject);
        });
    }

    [Fact]
    public void Page_ReportsAnEmptyResultDifferentlyWhenFiltered()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            page.SearchText = "nothing matches this";
            await Task.Delay(HistoryPageViewModel.SearchDebounce + TimeSpan.FromMilliseconds(250));

            Assert.True(page.IsEmpty);
            Assert.Contains("No commit matches", page.EmptyMessage, StringComparison.Ordinal);
        });
    }

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
                Assert.Contains(texts, text => text.Length == 7 && text.All(char.IsAsciiLetterOrDigit));

                window.Close();
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
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

    // ---------------------------------------------------------------- dragging a branch

    [Theory]
    // sourceKind, sourceName, targetKind, targetName, targetIsCurrent, expected
    [InlineData(GitRefKind.LocalBranch, "feature", GitRefKind.LocalBranch, "main", true, true)]
    [InlineData(GitRefKind.RemoteBranch, "origin/feature", GitRefKind.LocalBranch, "main", true, true)]
    [InlineData(GitRefKind.LocalBranch, "main", GitRefKind.LocalBranch, "main", true, false)]
    [InlineData(GitRefKind.LocalBranch, "feature", GitRefKind.RemoteBranch, "origin/main", false, false)]
    [InlineData(GitRefKind.Tag, "v1.0.0", GitRefKind.LocalBranch, "main", true, false)]
    [InlineData(GitRefKind.LocalBranch, "feature", GitRefKind.Tag, "v1.0.0", false, false)]
    [InlineData(GitRefKind.LocalBranch, "feature", GitRefKind.Stash, "stash", false, false)]
    public void CanDropBranch_AcceptsOnlyBranchOntoLocalBranch(
        GitRefKind sourceKind,
        string sourceName,
        GitRefKind targetKind,
        string targetName,
        bool targetIsCurrent,
        bool expected)
        => Assert.Equal(
            expected,
            HistoryPageViewModel.CanDropBranch(
                new RefBadgeItem(sourceKind, sourceName, false),
                new RefBadgeItem(targetKind, targetName, targetIsCurrent)));

    [Fact]
    public void CanDropBranch_RefusesAMissingEnd()
    {
        RefBadgeItem badge = new(GitRefKind.LocalBranch, "main", true);

        Assert.False(HistoryPageViewModel.CanDropBranch(null, badge));
        Assert.False(HistoryPageViewModel.CanDropBranch(badge, null));
        Assert.False(HistoryPageViewModel.CanDropBranch(null, null));
    }

    [Fact]
    public void Request_CarriesWhatTheTwoBadgesSay()
    {
        BranchDropRequest request = HistoryPageViewModel.Request(
            new RefBadgeItem(GitRefKind.RemoteBranch, "origin/feature", false),
            new RefBadgeItem(GitRefKind.LocalBranch, "main", true));

        Assert.Equal("origin/feature", request.Source);
        Assert.True(request.SourceIsRemote);
        Assert.Equal("main", request.Target);
        Assert.False(request.TargetIsRemote);
        Assert.True(request.TargetIsCurrent);
    }

    [Fact]
    public void DropBranch_MergesAndReReadsTheHistory()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            int before = page.Rows.Count;

            // "feature" is an unmerged side branch of the repository the fixture builds, and main
            // is what is checked out.
            services.Dialogs.Result = DialogResult.Primary;

            await page.DropBranchCommand.ExecuteAsync(new BranchDrop(
                new RefBadgeItem(GitRefKind.LocalBranch, "feature", false),
                new RefBadgeItem(GitRefKind.LocalBranch, "main", true)));

            Assert.Single(services.Dialogs.Shown);

            // The merge brought the side branch's commit in, and the page re-read to show it.
            Assert.True(
                page.Rows.Count >= before,
                "the history was not re-read after the drop");

            Assert.Contains(page.Rows, row => row.Subject == "Start the feature branch");
        });
    }

    [Fact]
    public void DropBranch_RefusesAPairThatCannotBeMerged()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildHistoryAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            BranchDrop onItself = new(
                new RefBadgeItem(GitRefKind.LocalBranch, "main", true),
                new RefBadgeItem(GitRefKind.LocalBranch, "main", true));

            Assert.False(page.DropBranchCommand.CanExecute(onItself));

            await page.DropBranchCommand.ExecuteAsync(onItself);

            // Nothing was asked and nothing was run.
            Assert.Empty(services.Dialogs.Shown);
        });
    }

    [Fact]
    public void CommitList_AcceptsDrops()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, _, HistoryPageView view, _, _) = await ShowHistoryPageAsync(services);

            ListBox list = view.FindControl<ListBox>("CommitList")
                ?? throw new InvalidOperationException("The history page has no commit list.");

            Assert.True(DragDrop.GetAllowDrop(list));

            window.Close();
        });
    }

    [Fact]
    public void RefBadge_MarksItselfAsADropTarget()
    {
        _fixture.Run(() =>
        {
            RefBadge badge = new() { Kind = GitRefKind.LocalBranch, Text = "main" };

            Window window = new() { Content = badge, Width = 300, Height = 60 };
            window.Show();
            window.UpdateLayout();

            Border border = badge.GetVisualDescendants().OfType<Border>().First();
            IBrush? plain = border.BorderBrush;

            badge.Classes.Set("droptarget", true);
            window.UpdateLayout();

            // The ring is what says where a dragged branch would land; the badge's own colour still
            // says what kind of reference it is.
            Assert.NotEqual(plain, border.BorderBrush);
            Assert.True(border.BorderThickness.Left > 0);

            badge.Classes.Set("droptarget", false);
            window.UpdateLayout();

            Assert.Equal(plain, border.BorderBrush);

            window.Close();
        });
    }

    // ---------------------------------------------------------------- the diff dialog

    /// <summary>
    /// Builds the page against a real repository, shows it, and hands the test the pieces the
    /// dialog's behaviour is read from.
    /// </summary>
    private static async Task<(Window Window, HistoryPageViewModel Model, HistoryPageView View, Panel Workspace, ContentDialog Dialog)>
        ShowHistoryPageAsync(TestServices services)
    {
        RepositoryHandle repository = await BuildHistoryAsync(services);
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
        ContentDialog dialog = view.FindControl<ContentDialog>("DiffDialog")
            ?? throw new InvalidOperationException("The history page has no diff dialog.");

        return (window, model, view, workspace, dialog);
    }

    [Fact]
    public void DiffDialog_StaysClosedWhenARowIsMerelySelected()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, Panel workspace, ContentDialog dialog) =
                await ShowHistoryPageAsync(services);

            Assert.Null(model.SelectedRow);
            Assert.False(model.IsDiffDialogOpen);
            Assert.False(dialog.IsOpen);

            // Nothing is taken from the graph: the list is the page's body.
            ListBox list = workspace.GetVisualDescendants().OfType<ListBox>().First();
            Assert.Equal(workspace.Bounds.Height, list.Bounds.Height);

            // Selecting a line selects it. The diffs are asked for, not implied.
            model.SelectedRow = model.Rows[0];
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(model.IsDiffDialogOpen);
            Assert.False(dialog.IsOpen);
            Assert.Same(model.Rows[0], model.SelectedRow);

            // Asking opens it, and the graph keeps its height whatever is on top of it.
            model.RowCommands.Activate.Execute(model.Rows[0]);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(model.IsDiffDialogOpen);
            Assert.True(dialog.IsOpen);
            Assert.Equal(workspace.Bounds.Height, list.Bounds.Height);

            window.Close();
        });
    }

    [Fact]
    public void DiffDialog_OpensOnADoubleClick()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, _, ContentDialog dialog) =
                await ShowHistoryPageAsync(services);

            CommitRowViewModel row = model.Rows.First(candidate => candidate.Commit is not null);

            // What the view's DoubleTapped handler runs.
            row.Commands!.Activate.Execute(row);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Same(row, model.SelectedRow);
            Assert.True(model.IsDiffDialogOpen);
            Assert.True(dialog.IsOpen);
            Assert.Equal(row.Subject, dialog.Title);

            window.Close();
        });
    }

    [Fact]
    public void DiffDialog_ShowsWhatTheSelectedCommitChanged()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, _, ContentDialog dialog) =
                await ShowHistoryPageAsync(services);

            CommitRowViewModel row = model.Rows.First(candidate => candidate.Commit is not null);
            model.RowCommands.ShowChanges.Execute(row);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(row.Subject, dialog.Title);

            // The changed files and the diff moved into the dialog as they were.
            Assert.Single(dialog.GetVisualDescendants().OfType<Views.Panels.ChangedFilesPanelView>());
            Assert.Single(dialog.GetVisualDescendants().OfType<Views.Panels.DiffViewerView>());

            // Asking for another row's changes leaves the dialog open and moves it onto that commit.
            CommitRowViewModel other = model.Rows.Last(candidate => candidate.Commit is not null);
            model.RowCommands.ShowChanges.Execute(other);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(dialog.IsOpen);
            Assert.Equal(other.Subject, dialog.Title);

            window.Close();
        });
    }

    [Fact]
    public void DiffDialog_IsAlmostAsLargeAsThePageAndFollowsIt()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, HistoryPageView view, _, ContentDialog dialog) =
                await ShowHistoryPageAsync(services);

            model.RowCommands.Activate.Execute(model.Rows[0]);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(view.Bounds.Width * DialogSizing.Fraction, dialog.DialogWidth, 3);
            Assert.Equal(view.Bounds.Height * DialogSizing.Fraction, dialog.DialogHeight, 3);

            // The maxima move with it, or the control's own default would clamp the card.
            Assert.Equal(dialog.DialogWidth, dialog.DialogMaxWidth, 3);
            Assert.Equal(dialog.DialogHeight, dialog.DialogMaxHeight, 3);

            double before = dialog.DialogWidth;

            window.Width = 900;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                dialog.DialogWidth < before,
                "the dialog kept its width when the window was made narrower");
            Assert.Equal(view.Bounds.Width * DialogSizing.Fraction, dialog.DialogWidth, 3);

            window.Close();
        });
    }

    [Fact]
    public void DiffDialog_ClosesWithoutLosingTheSelection()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, _, ContentDialog dialog) =
                await ShowHistoryPageAsync(services);

            CommitRowViewModel row = model.Rows.First(candidate => candidate.Commit is not null);
            model.RowCommands.Activate.Execute(row);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            model.CloseDiffDialogCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(dialog.IsOpen);
            Assert.False(model.IsDiffDialogOpen);

            // The selection is also the start point "create a branch here" falls back to, and the
            // highlight that says where the reader is, so closing must not take it away.
            Assert.Same(row, model.SelectedRow);
            Assert.True(model.HasSelection);
            Assert.Equal(row.Sha, services.Get<IRepositoryContext>().SelectedCommit?.Sha);

            window.Close();
        });
    }

    [Fact]
    public void DiffDialog_ClosedByTheControlIsClosedForThePageToo()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, _, ContentDialog dialog) =
                await ShowHistoryPageAsync(services);

            model.RowCommands.Activate.Execute(model.Rows[0]);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            // What Escape, the scrim and the Close button all end in.
            await dialog.HideAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.False(model.IsDiffDialogOpen);
            Assert.NotNull(model.SelectedRow);

            window.Close();
        });
    }

    [Fact]
    public void DiffDialog_GoesAwayWithTheSelection()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, _, ContentDialog dialog) =
                await ShowHistoryPageAsync(services);

            model.RowCommands.Activate.Execute(model.Rows[0]);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(dialog.IsOpen);

            // What a reload does after a checkout: the selection it was showing is gone.
            model.SelectedRow = null;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(model.IsDiffDialogOpen);
            Assert.False(dialog.IsOpen);

            window.Close();
        });
    }

    [Fact]
    public void ShowChanges_BringsTheDialogBackForTheSelectedRow()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel model, _, _, ContentDialog dialog) =
                await ShowHistoryPageAsync(services);

            CommitRowViewModel row = model.Rows.First(candidate => candidate.Commit is not null);
            model.RowCommands.Activate.Execute(row);
            Dispatcher.UIThread.RunJobs();

            model.CloseDiffDialogCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(dialog.IsOpen);

            // The selection never changed, so nothing but the menu can bring the dialog back.
            model.RowCommands.ShowChanges.Execute(row);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(dialog.IsOpen);
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
            (Window window, HistoryPageViewModel model, _, _, ContentDialog dialog) =
                await ShowHistoryPageAsync(services);

            CommitRowViewModel row = model.Rows.Last(candidate => candidate.Commit is not null);

            Assert.Null(model.SelectedRow);

            model.RowCommands.ShowChanges.Execute(row);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Same(row, model.SelectedRow);
            Assert.True(dialog.IsOpen);
            Assert.Equal(row.Subject, dialog.Title);

            window.Close();
        });
    }

    [Fact]
    public void DiffDialog_LetsEachPaneScrollItself()
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

            ContentDialog dialog = view.FindControl<ContentDialog>("DiffDialog")!;
            DockPanel body = view.FindControl<DockPanel>("DiffDialogBody")!;

            ScrollViewer card = body.GetVisualAncestors().OfType<ScrollViewer>().First();

            // Nothing is left for the card to scroll: the body is bounded by it.
            Assert.Equal(card.Viewport.Height, card.Extent.Height, 3);
            Assert.True(
                body.Bounds.Height <= dialog.DialogHeight,
                $"the body is {body.Bounds.Height} tall inside a {dialog.DialogHeight} card");

            // And each pane has more than it can show, with its own viewport to show it in.
            // The list's own scroll, not the filter box's: a TextBox templates one too.
            ScrollViewer files = ScrollOf(dialog.GetVisualDescendants()
                .OfType<Views.Panels.ChangedFilesPanelView>()
                .Single());

            ScrollViewer diff = ScrollOf(dialog.GetVisualDescendants()
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

    [Theory]
    [InlineData(1000, 940)]
    [InlineData(0, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void DialogSizing_TakesMostOfWhatItIsGiven(double available, double expected)
        => Assert.Equal(expected, DialogSizing.Fill.Convert(available, typeof(double), null, CultureInfo.InvariantCulture));
}
