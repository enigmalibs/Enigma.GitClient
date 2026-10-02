using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Controls;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A double-click on a branch badge of the history checks that branch out, as GitKraken does —
/// through the real input system, against real git.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryDoubleClickTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryDoubleClickTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void ADoubleClickOnALocalBranchsBadge_ChecksItOut()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel page, HistoryPageView view) = await ShowAsync(services);

            try
            {
                DoubleClick(window, Badge(view, "feature"));
                await SettleAsync(page);

                RepositoryHandle repository = services.Get<IRepositoryContext>().Repository!;
                Assert.Equal("feature", Git(repository, "rev-parse", "--abbrev-ref", "HEAD"));
                Assert.False(view.IsDragging);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ASingleClickOnABadge_ChecksNothingOut()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel page, HistoryPageView view) = await ShowAsync(services);

            try
            {
                Point point = Centre(Badge(view, "feature"), window);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                await SettleAsync(page);

                Assert.Equal("main", Git(services.Get<IRepositoryContext>().Repository!, "rev-parse", "--abbrev-ref", "HEAD"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ADoubleClickOnTheCheckedOutBranch_DoesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel page, HistoryPageView view) = await ShowAsync(services);

            try
            {
                DoubleClick(window, Badge(view, "main"));
                await SettleAsync(page);

                Assert.Equal("main", Git(services.Get<IRepositoryContext>().Repository!, "rev-parse", "--abbrev-ref", "HEAD"));
                Assert.Empty(services.Dialogs.Shown);
                Assert.Empty(services.InfoBar.Shown);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ADoubleClickOnARemoteWhoseLocalIsElsewhere_AsksToResetLocalToHere()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel page, HistoryPageView view) = await ShowAsync(services, withApartRemote: true);

            try
            {
                services.Dialogs.Result = DialogResult.Close;

                DoubleClick(window, Badge(view, "origin/topic"));
                await SettleAsync(page);

                ContentDialog question = Assert.Single(services.Dialogs.Shown);
                Assert.Equal("Reset \"topic\" to \"origin/topic\"?", question.Title);
                Assert.Equal("main", Git(services.Get<IRepositoryContext>().Repository!, "rev-parse", "--abbrev-ref", "HEAD"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ADoubleClickOnTheLineBesideTheBadges_ChecksNothingOut_AndLeavesItSelected()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            (Window window, HistoryPageViewModel page, HistoryPageView view) = await ShowAsync(services);

            try
            {
                TextBlock subject = view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .First(text => text.Text == "Start the feature" && text.DataContext is CommitRowViewModel);

                DoubleClick(window, subject);
                await SettleAsync(page);

                Assert.Equal("main", Git(services.Get<IRepositoryContext>().Repository!, "rev-parse", "--abbrev-ref", "HEAD"));
                Assert.Equal("Start the feature", page.SelectedRow?.Subject);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<(Window Window, HistoryPageViewModel Page, HistoryPageView View)> ShowAsync(
        TestServices services,
        bool withApartRemote = false)
    {
        RepositoryHandle repository = await BuildRepositoryAsync(services, withApartRemote);
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
        await page.ReloadAsync();

        HistoryPageView view = services.Get<HistoryPageView>();
        view.DataContext = page;

        Window window = new() { Content = view, Width = 1200, Height = 700 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return (window, page, view);
    }

    /// <summary>
    /// main, a feature branch one commit ahead of it; with <paramref name="withApartRemote"/>, a local
    /// <c>topic</c> on main's commit and a remote-tracking <c>origin/topic</c> on feature's.
    /// </summary>
    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services, bool withApartRemote)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "double-click"), "main");

        File.WriteAllText(Path.Combine(repository.WorkTreePath, "README.md"), "# one\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Add the readme");

        Git(repository, "checkout", "-b", "feature");
        File.WriteAllText(Path.Combine(repository.WorkTreePath, "feature.txt"), "feature\n");
        Git(repository, "add", "--all");
        Git(repository, "commit", "-m", "Start the feature");
        Git(repository, "checkout", "main");

        if (withApartRemote)
        {
            Git(repository, "branch", "topic");
            Git(repository, "update-ref", "refs/remotes/origin/topic", "feature");
        }

        return repository;
    }

    /// <summary>
    /// Runs what the double-click posted, and waits for the checkout it started.
    /// </summary>
    private static async Task SettleAsync(HistoryPageViewModel page)
    {
        Dispatcher.UIThread.RunJobs();

        if (page.BranchCommands.Checkout.ExecutionTask is { } checkout)
        {
            await checkout;
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static RefBadge Badge(HistoryPageView view, string name)
        => view.GetVisualDescendants()
            .OfType<RefBadge>()
            .First(badge => badge.DataContext is HistoryBranchViewModel branch && branch.Name == name);

    private static void DoubleClick(Window window, Visual target)
    {
        Point point = Centre(target, window);

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static Point Centre(Visual target, Visual relativeTo)
        => target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), relativeTo)
            ?? throw new InvalidOperationException("The target is not in the same tree as the window.");

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
