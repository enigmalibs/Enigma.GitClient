using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Hiding a branch from the history in the branches dialog, and what the history does about it.
/// </summary>
/// <remarks>
/// <code>
///   main:    A --- B --------- D(merge) --- E
///                   \         /
///   topic:           C ------/                (merged; C is reached through main too)
///   feature: A --- F                          (not merged; F is feature's alone)
/// </code>
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class BranchVisibilityTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public BranchVisibilityTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- the branches dialog

    [Fact]
    public void ARow_HidesItsBranchAndShowsItAgain_InPlace()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await OpenAsync(services);

            BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
            await page.OnAppearingAsync();

            BranchRowViewModel feature = Row(page, "feature");
            int notified = 0;
            feature.PropertyChanged += (_, e) => notified += e.PropertyName == nameof(BranchRowViewModel.IsHiddenInHistory) ? 1 : 0;

            Assert.False(feature.IsHiddenInHistory);
            Assert.Equal("Hide from the history", feature.VisibilityHeader);

            feature.ToggleVisibilityCommand.Execute(feature);

            // The same row, told — not a rebuilt list scrolled back to the top.
            Assert.Same(feature, Row(page, "feature"));
            Assert.True(feature.IsHiddenInHistory);
            Assert.Equal(1, notified);
            Assert.Equal("Show in the history", feature.VisibilityHeader);
            Assert.True(services.Get<IHiddenBranches>().IsHidden("refs/heads/feature"));

            feature.ToggleVisibilityCommand.Execute(feature);

            Assert.False(feature.IsHiddenInHistory);
            Assert.False(services.Get<IHiddenBranches>().IsHidden("refs/heads/feature"));
        });
    }

    [Fact]
    public void TheCheckedOutBranch_CannotBeHidden_AndSaysWhy()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await OpenAsync(services);

            BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
            await page.OnAppearingAsync();

            BranchRowViewModel main = Row(page, "main");

            Assert.False(main.CanChangeVisibility);
            Assert.False(main.ToggleVisibilityCommand.CanExecute(main));
            Assert.Equal("The branch you are on is always shown in the history", main.VisibilityTip);
            Assert.True(Row(page, "feature").ToggleVisibilityCommand.CanExecute(Row(page, "feature")));
        });
    }

    [Fact]
    public void AHiddenBranchsLine_SaysSo_AndItsMenuShowsItAgain()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await OpenAsync(services);
            services.Get<IHiddenBranches>().SetHidden("refs/heads/feature", true);

            BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
            await page.OnAppearingAsync();

            BranchesPageView view = services.Get<BranchesPageView>();
            view.DataContext = page;
            Window window = new() { Content = view, Width = 1100, Height = 560 };
            window.Show();

            try
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                // The badge, with its struck-through eye, on the hidden branch's line alone.
                StackPanel[] hidden = [.. view.GetVisualDescendants()
                    .OfType<StackPanel>()
                    .Where(badge => badge.Classes.Contains("badge") && badge.IsEffectivelyVisible && badge.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "hidden"))];

                StackPanel badge = Assert.Single(hidden);
                Assert.Equal("feature", ((BranchRowViewModel)badge.DataContext!).Name);
                Assert.Contains(badge.GetVisualDescendants().OfType<Icon>(), icon => icon.Kind == PhosphorIcon.EyeSlash);
                Assert.NotNull(ToolTip.GetTip(badge));

                // Its line's menu shows it again; the checked-out branch cannot be hidden at all.
                BranchRowViewModel feature = page.Groups.SelectMany(group => group.Rows).Single(row => row.Name == "feature");
                BranchRowViewModel main = page.Groups.SelectMany(group => group.Rows).Single(row => row.Name == "main");

                Assert.Equal("Show in the history", feature.VisibilityHeader);
                Assert.True(feature.ToggleVisibilityCommand.CanExecute(feature));
                Assert.False(main.ToggleVisibilityCommand.CanExecute(main));
            }
            finally
            {
                window.Content = null;
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- the history

    [Fact]
    public void AHiddenBranch_LeavesTheGraph_AndComesBackWhenShown()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await OpenAsync(services);
            HistoryPageViewModel history = await ShowHistoryAsync(services);

            Assert.Contains(history.Rows, row => row.Subject == "Start the feature branch");

            services.Get<IHiddenBranches>().SetHidden("refs/heads/feature", true);
            await WaitUntilAsync(() => history.IsNotBusy && !history.Rows.Any(row => row.Subject == "Start the feature branch"));

            // A, where feature starts, is main's too.
            Assert.Contains(history.Rows, row => row.Subject == "Add the readme");
            Assert.DoesNotContain(history.Rows, row => HasBadge(row, "feature"));

            services.Get<IHiddenBranches>().SetHidden("refs/heads/feature", false);
            await WaitUntilAsync(() => history.IsNotBusy && history.Rows.Any(row => row.Subject == "Start the feature branch"));

            Assert.Contains(history.Rows, row => HasBadge(row, "feature"));
        });
    }

    [Fact]
    public void AHiddenMergedBranch_KeepsItsCommits_ButLosesItsBadge()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await OpenAsync(services);
            HistoryPageViewModel history = await ShowHistoryAsync(services);

            Assert.Contains(history.Rows, row => HasBadge(row, "topic"));

            services.Get<IHiddenBranches>().SetHidden("refs/heads/topic", true);
            await WaitUntilAsync(() => history.IsNotBusy && !history.Rows.Any(row => HasBadge(row, "topic")));

            Assert.Contains(history.Rows, row => row.Subject == "Work on the topic branch");
        });
    }

    [Fact]
    public void TheCheckedOutBranch_IsDrawnWhateverTheStoreSays()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAsync(services);

            // Hidden while it was not checked out — in a terminal, say — then checked out.
            services.Get<IHiddenBranchStore>().SetHidden(repository.WorkTreePath, "refs/heads/main", hidden: true);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel history = await ShowHistoryAsync(services);

            Assert.Equal(0, history.HiddenBranchCount);
            Assert.False(history.HasHiddenBranches);
            Assert.Contains(history.Rows, row => HasBadge(row, "main"));
            Assert.Contains(history.Rows, row => row.Subject == "Extend the application file");
        });
    }

    [Fact]
    public void TheToolbar_CountsTheHiddenBranches_AndShowsThemAllAgain()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await OpenAsync(services);
            HistoryPageViewModel history = await ShowHistoryAsync(services);
            IHiddenBranches hidden = services.Get<IHiddenBranches>();

            Assert.False(history.HasHiddenBranches);
            Assert.False(history.ShowHiddenBranchesCommand.CanExecute(null));

            hidden.SetHidden("refs/heads/feature", true);
            Assert.Equal("1 branch hidden", history.HiddenBranchesSummary);

            hidden.SetHidden("refs/heads/topic", true);
            Assert.Equal(2, history.HiddenBranchCount);
            Assert.Equal("2 branches hidden", history.HiddenBranchesSummary);
            Assert.True(history.ShowHiddenBranchesCommand.CanExecute(null));

            history.ShowHiddenBranchesCommand.Execute(null);
            await WaitUntilAsync(() => history.IsNotBusy && history.Rows.Any(row => row.Subject == "Start the feature branch"));

            Assert.Empty(hidden.Hidden);
            Assert.False(history.HasHiddenBranches);
            Assert.Equal(string.Empty, history.HiddenBranchesSummary);
        });
    }

    [Fact]
    public void HidingABranch_KeepsTheSelectedLine()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await OpenAsync(services);
            HistoryPageViewModel history = await ShowHistoryAsync(services);

            CommitRowViewModel merge = history.Rows.First(row => row.Subject == "Merge the topic branch");
            history.SelectedRow = merge;

            services.Get<IHiddenBranches>().SetHidden("refs/heads/feature", true);
            await WaitUntilAsync(() => history.IsNotBusy && !history.Rows.Any(row => row.Subject == "Start the feature branch"));

            Assert.Equal(merge.Sha, history.SelectedRow?.Sha);
        });
    }

    [Fact]
    public void WhileTheDiffsHaveThePage_TheGraphWaitsToBeRedrawn()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await OpenAsync(services);
            HistoryPageViewModel history = await ShowHistoryAsync(services);

            history.IsDiffViewOpen = true;
            services.Get<IHiddenBranches>().SetHidden("refs/heads/feature", true);

            Dispatcher.UIThread.RunJobs();
            Assert.Contains(history.Rows, row => row.Subject == "Start the feature branch");

            history.IsDiffViewOpen = false;
            await WaitUntilAsync(() => history.IsNotBusy && !history.Rows.Any(row => row.Subject == "Start the feature branch"));
        });
    }

    [Fact]
    public void TheChip_IsOnTheToolbarOnlyWhileBranchesAreHidden()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            await OpenAsync(services);
            HistoryPageViewModel history = await ShowHistoryAsync(services);

            HistoryPageView view = services.Get<HistoryPageView>();
            view.DataContext = history;
            Window window = new() { Content = view, Width = 1200, Height = 900 };
            window.Show();

            try
            {
                Border chip = view.FindControl<Border>("HiddenBranchesChip")!;
                Dispatcher.UIThread.RunJobs();
                Assert.False(chip.IsVisible);

                services.Get<IHiddenBranches>().SetHidden("refs/heads/feature", true);
                Dispatcher.UIThread.RunJobs();

                Assert.True(chip.IsVisible);
                Assert.Contains("1 branch hidden", chip.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text));
            }
            finally
            {
                window.Content = null;
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    private static BranchRowViewModel Row(BranchesPageViewModel page, string name)
        => page.Groups.SelectMany(group => group.Rows).Single(row => row.Name == name && row.IsLocal);

    private static bool HasBadge(CommitRowViewModel row, string branch)
        => row.Refs.Any(badge => badge.Kind == GitRefKind.LocalBranch && badge.Name == branch);

    private static async Task<HistoryPageViewModel> ShowHistoryAsync(TestServices services)
    {
        HistoryPageViewModel history = services.Get<HistoryPageViewModel>();
        await history.OnAppearingAsync();
        await WaitUntilAsync(() => history.IsNotBusy && history.Rows.Count > 0);

        return history;
    }

    private static async Task OpenAsync(TestServices services)
        => await services.Get<IRepositoryContext>().OpenAsync(await BuildAsync(services));

    private static async Task<RepositoryHandle> BuildAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "visibility"), "main");

        await CommitAsync(repository, "README.md", "# one\n", "Add the readme");
        await CommitAsync(repository, "src/app.txt", "one\n", "Add the application file");

        await GitAsync(repository, "checkout", "-b", "topic");
        await CommitAsync(repository, "src/topic.txt", "topic\n", "Work on the topic branch");
        await GitAsync(repository, "checkout", "main");
        await GitAsync(repository, "merge", "--no-ff", "topic", "-m", "Merge the topic branch");
        await CommitAsync(repository, "src/app.txt", "one\ntwo\n", "Extend the application file");

        await GitAsync(repository, "checkout", "-b", "feature", "HEAD~3");
        await CommitAsync(repository, "src/feature.txt", "feature\n", "Start the feature branch");
        await GitAsync(repository, "checkout", "main");

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

    private static async Task GitAsync(RepositoryHandle repository, params string[] arguments)
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
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int attempts = 300)
    {
        for (int attempt = 0; attempt < attempts && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "the page never reached the state the test waited for");
    }
}
