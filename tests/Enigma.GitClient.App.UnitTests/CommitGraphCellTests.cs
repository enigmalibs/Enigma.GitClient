using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.App.Controls;
using Enigma.GitClient.App.Controls.Graph;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.Core.Graph;
using Enigma.GitClient.Core.Refs;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Exercises the control that draws the commit graph — measurement, the palette, and what actually
/// ends up on the canvas.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class CommitGraphCellTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public CommitGraphCellTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static GraphCommitInput Commit(string sha, params string[] parents) => new(sha, parents);

    private static GraphLayoutResult Diamond()
        => CommitGraphLayout.BuildComplete(
        [
            Commit("D", "B", "C"),
            Commit("C", "B"),
            Commit("B", "A"),
            Commit("A"),
        ]);

    // ---------------------------------------------------------------- measurement

    [Theory]
    [InlineData(0, 28)]
    [InlineData(1, 44)]
    [InlineData(4, 92)]
    public void Measure_GrowsWithTheNumberOfLanes(int maxLane, double expectedWidth)
        => Assert.Equal(expectedWidth, CommitGraphCell.CalculateWidth(maxLane, 16, 6, 14));

    [Fact]
    public void Measure_StopsGrowingAtTheLaneLimit()
    {
        double atLimit = CommitGraphCell.CalculateWidth(13, 16, 6, 14);
        double beyond = CommitGraphCell.CalculateWidth(200, 16, 6, 14);

        Assert.Equal(atLimit, beyond);
    }

    [Fact]
    public void Measure_ReportsTheWidthTheRowNeeds()
    {
        _fixture.Run(() =>
        {
            GraphLayoutResult layout = Diamond();
            CommitGraphCell cell = new() { Row = layout.Rows[0] };

            cell.Measure(new Size(double.PositiveInfinity, 26));

            Assert.Equal(
                CommitGraphCell.CalculateWidth(layout.Rows[0].MaxLane, cell.LaneWidth, cell.LanePadding, cell.MaximumLanes),
                cell.DesiredSize.Width);
        });
    }

    [Fact]
    public void LaneCentres_AreEvenlySpaced()
    {
        _fixture.Run(() =>
        {
            CommitGraphCell cell = new() { LaneWidth = 16, LanePadding = 6 };

            Assert.Equal(14, cell.GetLaneCentre(0));
            Assert.Equal(30, cell.GetLaneCentre(1));
            Assert.Equal(46, cell.GetLaneCentre(2));
        });
    }

    // ---------------------------------------------------------------- the node

    [Fact]
    public void Node_IsDrawnTwiceTheSizeItUsedToBe()
    {
        _fixture.Run(() =>
        {
            CommitGraphCell cell = new();

            Assert.Equal(9, cell.NodeRadius);
        });
    }

    [Theory]
    // The defaults: nothing is in the way, so the node is drawn at the size it asks for.
    [InlineData(36, 16, false, 9)]
    [InlineData(36, 16, true, 9)]
    // The smallest row height a preference allows: the node shrinks rather than being sliced off by
    // the rows above and below, and a HEAD node leaves room for its ring as well.
    [InlineData(18, 16, false, 7)]
    [InlineData(18, 16, true, 4.5)]
    // The smallest lane width: the node stops short of the next lane's line.
    [InlineData(36, 8, false, 6)]
    public void Node_IsFittedToItsRowAndItsLane(double rowHeight, double laneWidth, bool isHead, double expected)
        => Assert.Equal(expected, CommitGraphCell.CalculateNodeRadius(9, rowHeight, laneWidth, 2, isHead));

    [Fact]
    public void Node_NeverDisappearsHoweverSmallTheRowIs()
        => Assert.Equal(1, CommitGraphCell.CalculateNodeRadius(9, 1, 1, 2, isHead: true));

    [Fact]
    public void Node_IsNeverGrownToFillTheRoomItIsGiven()
        => Assert.Equal(4.5, CommitGraphCell.CalculateNodeRadius(4.5, 96, 40, 2, isHead: false));

    [Theory]
    // A merge is half the commit on the same row, whatever that row fitted the commit to.
    [InlineData(36, 16, false, 4.5)]
    [InlineData(18, 16, false, 3.5)]
    [InlineData(18, 16, true, 2.25)]
    [InlineData(36, 8, false, 3)]
    public void MergeNode_IsHalfTheSizeOfACommit(double rowHeight, double laneWidth, bool isHead, double expected)
    {
        double commit = CommitGraphCell.CalculateNodeRadius(9, rowHeight, laneWidth, 2, isHead);

        Assert.Equal(expected, CommitGraphCell.CalculateMergeNodeRadius(commit));
        Assert.Equal(commit / 2, CommitGraphCell.CalculateMergeNodeRadius(commit));
    }

    [Fact]
    public void MergeNode_NeverDisappearsHoweverSmallTheRowIs()
    {
        double commit = CommitGraphCell.CalculateNodeRadius(9, 1, 1, 2, isHead: true);

        Assert.Equal(1, commit);
        Assert.Equal(1, CommitGraphCell.CalculateMergeNodeRadius(commit));
    }

    // ---------------------------------------------------------------- palette

    [Fact]
    public void Palette_ResolvesTenDistinctLaneBrushes()
    {
        _fixture.Run(() =>
        {
            Border host = new();
            Window window = new() { Content = host };
            window.Show();

            GraphPalette palette = new();
            IReadOnlyList<IBrush> brushes = palette.Resolve(host);

            Assert.Equal(GraphPalette.Size, brushes.Count);
            Assert.All(brushes, brush => Assert.IsType<ISolidColorBrush>(brush, exactMatch: false));

            List<Color> colours = [.. brushes.Cast<ISolidColorBrush>().Select(brush => brush.Color)];
            Assert.Equal(colours.Count, colours.Distinct().Count());

            window.Close();
        });
    }

    [Fact]
    public void Palette_WrapsAnOutOfRangeIndexInsteadOfThrowing()
    {
        _fixture.Run(() =>
        {
            Border host = new();
            Window window = new() { Content = host };
            window.Show();

            GraphPalette palette = new();

            Assert.Same(palette.Get(host, 0), palette.Get(host, GraphPalette.Size));
            Assert.Same(palette.Get(host, 1), palette.Get(host, -(GraphPalette.Size - 1)));

            window.Close();
        });
    }

    [Fact]
    public void Palette_FollowsAThemeSwitch()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;

            try
            {
                Border host = new();
                Window window = new() { Content = host };
                window.Show();

                GraphPalette palette = new();

                application.RequestedThemeVariant = ThemeVariant.Dark;
                Dispatcher.UIThread.RunJobs();
                Color dark = ((ISolidColorBrush)palette.Get(host, 0)).Color;

                application.RequestedThemeVariant = ThemeVariant.Light;
                Dispatcher.UIThread.RunJobs();
                palette.Invalidate();
                Color light = ((ISolidColorBrush)palette.Get(host, 0)).Color;

                Assert.NotEqual(dark, light);

                window.Close();
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void Palette_FallsBackWhenTheKeyIsMissing()
    {
        _fixture.Run(() =>
        {
            Border host = new();

            // Not in the tree, so the application's resources are not reachable: the palette must
            // still answer rather than throw.
            Assert.NotNull(new GraphPalette().Get(host, 0));
        });
    }

    // ---------------------------------------------------------------- rendering

    /// <summary>
    /// Renders a stack of graph cells and measures how many distinct colours the frame holds, which
    /// is what tells "the graph was drawn" from "a control laid out and painted nothing".
    /// </summary>
    private static (int DistinctColours, string Path) RenderRows(
        IReadOnlyList<GraphRow> rows,
        string fileName,
        Action<CommitGraphCell, int>? configure = null)
    {
        StackPanel stack = new();

        for (int index = 0; index < rows.Count; index++)
        {
            CommitGraphCell cell = new() { Row = rows[index], Height = 26 };
            configure?.Invoke(cell, index);
            stack.Children.Add(cell);
        }

        Window window = new()
        {
            Content = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#1E1F22")),
                Child = stack,
                Padding = new Thickness(4),
            },
            Width = 240,
            Height = Math.Max(120, (rows.Count * 26) + 20),
        };

        window.Show();

        string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);

        int distinct = 0;

        for (int attempt = 0; attempt < 20; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            Dispatcher.UIThread.RunJobs();

            using Bitmap frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("The graph produced no rendered frame.");

            frame.Save(path, PngBitmapEncoderOptions.Default);
            distinct = CountDistinctColours(path);

            if (distinct >= 6)
            {
                break;
            }
        }

        window.Close();

        return (distinct, path);
    }

    private static int CountDistinctColours(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using WriteableBitmap bitmap = WriteableBitmap.Decode(stream);
        using ILockedFramebuffer buffer = bitmap.Lock();

        HashSet<int> colours = [];

        unsafe
        {
            byte* pixels = (byte*)buffer.Address;

            for (int y = 0; y < buffer.Size.Height; y++)
            {
                byte* row = pixels + (y * buffer.RowBytes);

                for (int x = 0; x < buffer.Size.Width; x++)
                {
                    colours.Add(
                        ((row[(x * 4) + 2] >> 4) << 8) |
                        ((row[(x * 4) + 1] >> 4) << 4) |
                        (row[(x * 4) + 0] >> 4));
                }
            }
        }

        return colours.Count;
    }

    /// <summary>
    /// Counts the pixels the graph painted over the frame's background, which is how much ink a row
    /// actually put on the canvas.
    /// </summary>
    private static int CountPaintedPixels(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using WriteableBitmap bitmap = WriteableBitmap.Decode(stream);
        using ILockedFramebuffer buffer = bitmap.Lock();

        int painted = 0;

        unsafe
        {
            byte* pixels = (byte*)buffer.Address;

            for (int y = 0; y < buffer.Size.Height; y++)
            {
                byte* row = pixels + (y * buffer.RowBytes);

                for (int x = 0; x < buffer.Size.Width; x++)
                {
                    // The frame's background is #1E1F22; anything else is the graph.
                    if (row[(x * 4) + 2] != 0x1E || row[(x * 4) + 1] != 0x1F || row[x * 4] != 0x22)
                    {
                        painted++;
                    }
                }
            }
        }

        return painted;
    }

    [Fact]
    public void Render_DrawsAMergeSmallerThanACommit()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                // The same row twice, differing in nothing but what it stands for: one lane, one
                // edge straight through it, so the only thing the two frames can differ by is the
                // node.
                GraphRow commit = Row(isMerge: false);
                GraphRow merge = Row(isMerge: true);

                (_, string commitFrame) = RenderRows([commit], "graph-node-commit.png");
                (_, string mergeFrame) = RenderRows([merge], "graph-node-merge.png");

                int commitInk = CountPaintedPixels(commitFrame);
                int mergeInk = CountPaintedPixels(mergeFrame);

                Assert.True(
                    mergeInk < commitInk,
                    $"the merge covered {mergeInk} pixels and the commit {commitInk}");
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    private static GraphRow Row(bool isMerge)
        => new("a", 0, 0, isMerge, isRoot: false, [new GraphEdge(0, 0, GraphEdgeKind.Straight, 0)], 0);

    [Fact]
    public void Render_DrawsABranchAndItsMergeInSeveralColours()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                (int distinct, _) = RenderRows(Diamond().Rows, "graph-diamond.png");

                // Two lanes in two different colours, plus the anti-aliased edges between them.
                Assert.True(distinct >= 6, $"the graph frame held only {distinct} distinct colours");
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void Render_DrawsARealisticHistory()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                GraphLayoutResult layout = CommitGraphLayout.BuildComplete(
                [
                    Commit("m5", "m4", "t2"),
                    Commit("t2", "t1"),
                    Commit("m4", "m3"),
                    Commit("t1", "m2"),
                    Commit("m3", "m2", "f2"),
                    Commit("f2", "f1"),
                    Commit("m2", "m1"),
                    Commit("f1", "m1"),
                    Commit("m1", "m0"),
                    Commit("m0"),
                ]);

                (int distinct, _) = RenderRows(
                    layout.Rows,
                    "graph-history.png",
                    (cell, index) => cell.IsHead = index == 0);

                Assert.True(distinct >= 8, $"the graph frame held only {distinct} distinct colours");
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void Render_HandlesARowWithNothingToDraw()
    {
        _fixture.Run(() =>
        {
            CommitGraphCell cell = new() { Row = null, Height = 26 };
            Window window = new() { Content = cell };

            window.Measure(new Size(200, 100));
            window.Arrange(new Rect(0, 0, 200, 100));
            window.UpdateLayout();

            Assert.True(cell.IsMeasureValid);
        });
    }

    // ---------------------------------------------------------------- ref badges

    [Fact]
    public void RefBadge_TakesItsKindIconAndClassFromTheReference()
    {
        _fixture.Run(() =>
        {
            GitBranch current = new(
                "refs/heads/main",
                "abc",
                isCurrent: true,
                upstreamShortName: "origin/main",
                BranchTracking.None,
                Core.History.GitSignature.Empty,
                DateTimeOffset.UnixEpoch,
                "Subject");

            RefBadge badge = RefBadge.For(current);

            Assert.Equal("main", badge.Text);
            Assert.True(badge.IsCurrent);
            Assert.Equal(Enigma.Icons.Phosphor.PhosphorIcon.GitBranch, badge.IconKind);
            Assert.Contains("head", badge.Classes);
        });
    }

    [Theory]
    [InlineData(GitRefKind.LocalBranch, "local", Enigma.Icons.Phosphor.PhosphorIcon.GitBranch)]
    [InlineData(GitRefKind.RemoteBranch, "remote", Enigma.Icons.Phosphor.PhosphorIcon.CloudArrowDown)]
    [InlineData(GitRefKind.Tag, "tag", Enigma.Icons.Phosphor.PhosphorIcon.Tag)]
    [InlineData(GitRefKind.Stash, "stash", Enigma.Icons.Phosphor.PhosphorIcon.Archive)]
    public void RefBadge_MapsEveryKindToAClassAndAnIcon(
        GitRefKind kind,
        string expectedClass,
        Enigma.Icons.Phosphor.PhosphorIcon expectedIcon)
    {
        _fixture.Run(() =>
        {
            RefBadge badge = new() { Kind = kind, Text = "name" };

            Assert.Contains(expectedClass, badge.Classes);
            Assert.Equal(expectedIcon, badge.IconKind);
        });
    }

    [Fact]
    public void RefBadge_RendersWithItsTemplate()
    {
        _fixture.Run(() =>
        {
            StackPanel stack = new() { Orientation = Orientation.Horizontal, Spacing = 4 };

            stack.Children.Add(new RefBadge { Kind = GitRefKind.LocalBranch, Text = "main", IsCurrent = true });
            stack.Children.Add(new RefBadge { Kind = GitRefKind.RemoteBranch, Text = "origin/main" });
            stack.Children.Add(new RefBadge { Kind = GitRefKind.Tag, Text = "v1.0.0" });
            stack.Children.Add(new RefBadge { Kind = GitRefKind.Stash, Text = "stash" });

            Window window = new() { Content = stack, Width = 500, Height = 60 };
            window.Measure(new Size(500, 60));
            window.Arrange(new Rect(0, 0, 500, 60));
            window.UpdateLayout();

            Assert.All(stack.Children, child => Assert.True(child.Bounds.Width > 0));

            string[] texts = [.. stack.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(block => block.Text ?? string.Empty)];

            Assert.Contains("main", texts);
            Assert.Contains("v1.0.0", texts);
        });
    }

    [Fact]
    public void RefBadge_EllipsisesAVeryLongName()
    {
        _fixture.Run(() =>
        {
            RefBadge badge = new()
            {
                Kind = GitRefKind.LocalBranch,
                Text = new string('x', 300),
                MaximumTextWidth = 120,
            };

            Window window = new() { Content = badge, Width = 600, Height = 60 };
            window.Measure(new Size(600, 60));
            window.Arrange(new Rect(0, 0, 600, 60));
            window.UpdateLayout();

            Assert.True(badge.Bounds.Width < 200, $"the badge grew to {badge.Bounds.Width}");
        });
    }
}
