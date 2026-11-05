using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Graph;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Controls;
using Enigma.GitClient.Desktop.Controls.Graph;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.Views.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A history line stacks its badges one under the other: a line with several is taller, and the rest
/// of the line stays in its first band, level with the first badge (FEATURE-D7D5).
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class HistoryStackedBadgesTests
{
    private static readonly double RowHeight = AppSettings.Defaults.GraphRowHeight;

    private readonly HeadlessAvaloniaFixture _fixture;

    public HistoryStackedBadgesTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- the panel's arithmetic

    [Fact]
    public void Height_IsNothingWithoutABadge()
        => Assert.Equal(0, BadgeStackPanel.CalculateHeight(0, 0, 0, 36, 4));

    [Theory]
    [InlineData(24)]
    // A badge taller than the band: the line still does not grow.
    [InlineData(30)]
    public void Height_IsTheBandWithOneBadge(double badge)
        => Assert.Equal(26, BadgeStackPanel.CalculateHeight(1, badge, badge, 26, 4));

    [Fact]
    public void Height_IsTheBadgesWithTheSameRoomAboveAndBelow()
    {
        // Three 24 px badges, 4 apart, the first centred in a 36 px band: 6 above, 6 below.
        Assert.Equal(6 + 24 + 4 + 24 + 4 + 24 + 6, BadgeStackPanel.CalculateHeight(3, 24, 72, 36, 4));
    }

    [Fact]
    public void TopOf_CentresInTheBand_NeverAboveTheTop()
    {
        Assert.Equal(6, BadgeStackPanel.TopOf(24, 36));
        Assert.Equal(0, BadgeStackPanel.TopOf(30, 18));
    }

    [Fact]
    public void ThePanel_CentresTheFirstChildInTheBand_AndStacksTheOthers()
    {
        _fixture.Run(() =>
        {
            BadgeStackPanel panel = new() { LineHeight = 36, Spacing = 4 };
            Border[] badges = [.. Enumerable.Range(0, 3).Select(index => new Border
            {
                Width = 40 + (index * 10),
                Height = 24,

                // As a badge is: a pill hugs its content, on the strip's left edge.
                HorizontalAlignment = HorizontalAlignment.Left,
            })];
            panel.Children.AddRange(badges);

            panel.Measure(new Size(200, double.PositiveInfinity));
            panel.Arrange(new Rect(panel.DesiredSize));

            Assert.Equal(new Size(60, 92), panel.DesiredSize);
            Assert.Equal([6d, 34d, 62d], badges.Select(badge => badge.Bounds.Y));
            Assert.All(badges, badge => Assert.Equal(0, badge.Bounds.X));
        });
    }

    [Fact]
    public void ThePanel_SkipsAHiddenChild()
    {
        _fixture.Run(() =>
        {
            BadgeStackPanel panel = new() { LineHeight = 36, Spacing = 4 };
            panel.Children.Add(new Border { Width = 40, Height = 24, IsVisible = false });
            panel.Children.Add(new Border { Width = 40, Height = 24 });

            panel.Measure(new Size(200, double.PositiveInfinity));

            Assert.Equal(36, panel.DesiredSize.Height);
        });
    }

    // ---------------------------------------------------------------- the graph's band

    [Fact]
    public void TheNode_IsInTheMiddleOfTheRow_WithoutABand()
    {
        _fixture.Run(() =>
        {
            CommitGraphCell cell = new();

            Assert.Equal(18, cell.GetNodeCentre(36));
        });
    }

    [Fact]
    public void TheNode_IsInTheMiddleOfTheFirstBand_OnATallerRow()
    {
        _fixture.Run(() =>
        {
            CommitGraphCell cell = new() { LineHeight = 36 };

            Assert.Equal(18, cell.GetNodeCentre(92));
            Assert.Equal(18, cell.GetNodeCentre(36));

            // A band taller than the row is no band.
            Assert.Equal(10, cell.GetNodeCentre(20));
        });
    }

    // ---------------------------------------------------------------- the page

    [Fact]
    public void ALineWithSeveralBadges_IsTaller_AndStacksThem()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAsync(services);
            (Window window, HistoryPageView view, HistoryPageViewModel page) = await ShowAsync(services, repository);

            try
            {
                Grid three = Row(view, page, "Third");
                Grid one = Row(view, page, "Second");
                Grid none = Row(view, page, "First");

                Assert.True(three.Bounds.Height > RowHeight, $"the three-badge line is {three.Bounds.Height} tall");
                Assert.Equal(RowHeight, one.Bounds.Height, 1);
                Assert.Equal(RowHeight, none.Bounds.Height, 1);

                RefBadge[] badges = [.. three.GetVisualDescendants().OfType<RefBadge>()];
                Assert.Equal(["main", "topic", "v2"], badges.Select(badge => badge.Text));

                Point[] tops = [.. badges.Select(badge => badge.TranslatePoint(default, three)!.Value)];
                Assert.All(tops, top => Assert.Equal(tops[0].X, top.X, 1));
                Assert.True(tops[0].Y < tops[1].Y && tops[1].Y < tops[2].Y, "the badges are not one under another");

                // The first badge is centred in the line's first band.
                double firstCentre = tops[0].Y + (badges[0].Bounds.Height / 2);
                Assert.Equal(RowHeight / 2, firstCentre, tolerance: 1);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OnATallLine_TheRestOfTheLineStaysInTheFirstBand()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAsync(services);
            (Window window, HistoryPageView view, HistoryPageViewModel page) = await ShowAsync(services, repository);

            try
            {
                Grid three = Row(view, page, "Third");

                TextBlock subject = three.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Third");
                TextBlock sha = three.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == Line(page, "Third").ShortSha);

                foreach (TextBlock text in new[] { subject, sha })
                {
                    double centre = text.TranslatePoint(default, three)!.Value.Y + (text.Bounds.Height / 2);
                    // Within a pixel: layout rounding snaps the text to the device grid.
                    Assert.Equal(RowHeight / 2, centre, tolerance: 1);
                }

                // The graph runs the whole line, its node drawn in the first band.
                CommitGraphCell graph = three.GetVisualDescendants().OfType<CommitGraphCell>().Single();
                Assert.Equal(three.Bounds.Height, graph.Bounds.Height, 1);
                Assert.Equal(RowHeight, graph.LineHeight);
                Assert.Equal(RowHeight / 2, graph.GetNodeCentre(graph.Bounds.Height), 1);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheRefsColumn_IsTheWidestBadge()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);
            RepositoryHandle repository = await BuildAsync(services);
            await services.Get<IRepositoryContext>().OpenAsync(repository);

            HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
            await page.ReloadAsync();

            double widest = page.Rows
                .SelectMany(row => row.Refs)
                .Max(badge => RefBadgeMetrics.MeasureBadge(badge.Name, badge.HasUpstream, badge.IsCurrent, badge.IsWorktree));

            Assert.Equal(widest, page.RefColumnWidth, 3);
        });
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Three commits: <c>First</c> with no badge, <c>Second</c> with the tag <c>v1</c>, and
    /// <c>Third</c> with <c>main</c> (checked out), <c>topic</c> and the tag <c>v2</c>.
    /// </summary>
    private static async Task<RepositoryHandle> BuildAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>().InitAsync(Path.Combine(root, "local"), "main");

        Commit(repository, "a.txt", "First");
        Commit(repository, "b.txt", "Second");
        Git(repository, "tag", "v1");
        Commit(repository, "c.txt", "Third");
        Git(repository, "tag", "v2");
        Git(repository, "branch", "topic");

        return repository;
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
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        return (window, view, page);
    }

    private static CommitRowViewModel Line(HistoryPageViewModel page, string subject)
        => page.Rows.Single(row => row.Subject == subject);

    private static Grid Row(HistoryPageView view, HistoryPageViewModel page, string subject)
    {
        ListBox list = view.FindControl<ListBox>("CommitList")!;
        Control item = list.ContainerFromItem(Line(page, subject))!;

        return item.GetVisualDescendants().OfType<Grid>().First(grid => grid.Classes.Contains("commitrow"));
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
