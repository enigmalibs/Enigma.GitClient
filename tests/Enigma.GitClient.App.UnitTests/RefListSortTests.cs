using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// The branches and tags dialogs' lines, ordered by name or by date, either way — newest first unless
/// the reader chose otherwise, and remembered.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class RefListSortTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public RefListSortTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- the helper

    [Theory]
    [InlineData(RefSortKey.Date, SortDirection.Descending, "c,B,b2,a")]
    [InlineData(RefSortKey.Date, SortDirection.Ascending, "a,B,b2,c")]
    [InlineData(RefSortKey.Name, SortDirection.Ascending, "a,B,b2,c")]
    [InlineData(RefSortKey.Name, SortDirection.Descending, "c,b2,B,a")]
    public void RefSort_OrdersByNameOrDate_WithTheNameBreakingATieAToZ(RefSortKey key, SortDirection direction, string expected)
    {
        DateTimeOffset day = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        (string Name, DateTimeOffset Date)[] items =
        [
            ("b2", day.AddDays(1)),
            ("a", day),
            ("c", day.AddDays(2)),
            ("B", day.AddDays(1)),
        ];

        List<(string Name, DateTimeOffset Date)> ordered = RefSort.Order(items, item => item.Name, item => item.Date, key, direction);

        Assert.Equal(expected, string.Join(',', ordered.Select(item => item.Name)));
    }

    // ---------------------------------------------------------------- the branches

    [Fact]
    public void TheBranches_AreNewestFirstByDefault_InEveryGroup()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenBranchesAsync(services);

            Assert.Equal(RefSortKey.Date, page.SortKey);
            Assert.Equal(SortDirection.Descending, page.SortDirection);
            Assert.True(page.IsSortDescending);
            Assert.Equal("Newest first — click to reverse", page.SortDirectionTip);

            Assert.Equal(["main", "Beta", "zeta", "alpha"], Names(page, "Local"));
            Assert.Equal(["new", "old"], Names(page, "origin"));
        });
    }

    [Theory]
    [InlineData(RefSortKey.Date, SortDirection.Ascending, "alpha,Beta,zeta,main")]
    [InlineData(RefSortKey.Name, SortDirection.Ascending, "alpha,Beta,main,zeta")]
    [InlineData(RefSortKey.Name, SortDirection.Descending, "zeta,main,Beta,alpha")]
    public void TheBranches_FollowTheOrderChosen(RefSortKey key, SortDirection direction, string expected)
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenBranchesAsync(services);

            page.SortKey = key;
            page.SortDirection = direction;

            Assert.Equal(expected.Split(','), Names(page, "Local"));
        });
    }

    [Fact]
    public void TheDirectionButton_ReversesTheOrder()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenBranchesAsync(services);

            page.ToggleSortDirectionCommand.Execute(null);

            Assert.Equal(SortDirection.Ascending, page.SortDirection);
            Assert.Equal(["alpha", "Beta", "zeta", "main"], Names(page, "Local"));
        });
    }

    [Fact]
    public void TheBranchOrder_IsRemembered()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenBranchesAsync(services);

            page.SortKey = RefSortKey.Name;
            page.SortDirection = SortDirection.Ascending;

            AppSettings saved = services.Get<ISettingsService>().Current;
            Assert.Equal(RefSortKey.Name, saved.BranchSortKey);
            Assert.Equal(SortDirection.Ascending, saved.BranchSortDirection);

            // A page built afterwards — the next time the dialog opens in a new session — starts there.
            BranchesPageViewModel later = ActivatorUtilities.CreateInstance<BranchesPageViewModel>(services.Provider);
            Assert.Equal(RefSortKey.Name, later.SortKey);
            Assert.Equal(SortDirection.Ascending, later.SortDirection);
        });
    }

    [Fact]
    public void ResettingThePreferences_PutsTheBranchesBackNewestFirst()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenBranchesAsync(services);

            page.SortKey = RefSortKey.Name;
            await services.Get<ISettingsService>().ResetAsync();

            Assert.Equal(RefSortKey.Date, page.SortKey);
            Assert.Equal(["main", "Beta", "zeta", "alpha"], Names(page, "Local"));
        });
    }

    [Fact]
    public void TheBranchesHeader_HasTheSortControls()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            BranchesPageViewModel page = await OpenBranchesAsync(services);

            BranchesPageView view = services.Get<BranchesPageView>();
            view.DataContext = page;

            Window window = new() { Content = view, Width = 1100, Height = 500 };
            window.Show();

            try
            {
                ComboBox key = view.FindControl<ComboBox>("SortKey")
                    ?? throw new InvalidOperationException("The branches page has no sort box.");
                Button direction = view.FindControl<Button>("SortDirection")
                    ?? throw new InvalidOperationException("The branches page has no direction button.");

                Assert.Equal([RefSortKey.Date, RefSortKey.Name], key.Items.Cast<RefSortKey>());
                Assert.Equal(RefSortKey.Date, key.SelectedItem);
                Assert.Same(page.ToggleSortDirectionCommand, direction.Command);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------------------------------------------------------------- helpers

    private static string[] Names(BranchesPageViewModel page, string group)
        => [.. page.Groups.Single(candidate => candidate.Title == group).Rows.Select(row => row.Name)];

    private static async Task<BranchesPageViewModel> OpenBranchesAsync(TestServices services)
    {
        RepositoryHandle repository = await BuildRepositoryAsync(services);
        await services.Get<IRepositoryContext>().OpenAsync(repository);

        BranchesPageViewModel page = services.Get<BranchesPageViewModel>();
        await page.OnAppearingAsync();

        return page;
    }

    /// <summary>
    /// Four local branches whose tips were committed in January (alpha), February (Beta and zeta, on
    /// the same commit) and March (main), and two remote-tracking ones in January and March.
    /// </summary>
    internal static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "sorting"), "main");

        CommitAt(repository, "a.txt", "January", "2024-01-15T10:00:00+00:00");
        Git(repository, null, "branch", "alpha");
        Git(repository, null, "tag", "v1-january");
        Git(repository, null, "update-ref", "refs/remotes/origin/old", "HEAD");

        CommitAt(repository, "b.txt", "February", "2024-02-15T10:00:00+00:00");
        Git(repository, null, "branch", "zeta");
        Git(repository, null, "branch", "Beta");
        Git(repository, null, "tag", "V2-february");
        Git(repository, null, "tag", "v2b-february");

        CommitAt(repository, "c.txt", "March", "2024-03-15T10:00:00+00:00");
        Git(repository, null, "tag", "a3-march");
        Git(repository, null, "update-ref", "refs/remotes/origin/new", "HEAD");

        return repository;
    }

    private static void CommitAt(RepositoryHandle repository, string path, string message, string date)
    {
        File.WriteAllText(Path.Combine(repository.WorkTreePath, path), message + "\n");
        Git(repository, null, "add", "--all");
        Git(repository, date, "commit", "-m", message);
    }

    private static void Git(RepositoryHandle repository, string? date, params string[] arguments)
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

        if (date is not null)
        {
            startInfo.Environment["GIT_AUTHOR_DATE"] = date;
            startInfo.Environment["GIT_COMMITTER_DATE"] = date;
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }
    }
}
