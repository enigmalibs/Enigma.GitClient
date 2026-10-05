using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Controls.Diff;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Enigma.GitClient.Desktop.Views.Panels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The side-by-side rendering as the two editors it now is: scrolled together down and sideways, each
/// side as far as its own longest line, never wrapped, and opened level at the first change.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiffSideBySideEditorTests
{
    private static readonly RepositoryHandle Repository = OperatingSystem.IsWindows()
        ? new RepositoryHandle(@"C:\src\client", @"C:\src\client\.git")
        : new RepositoryHandle("/src/client", "/src/client/.git");

    private static readonly ChangedFile File = new()
    {
        Path = "src/app.txt",
        ChangeKind = FileChangeKind.Modified,
        AddedLines = 1,
        RemovedLines = 1,
    };

    private readonly HeadlessAvaloniaFixture _fixture;

    public DiffSideBySideEditorTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void TheTwoSides_ScrollDownTogether_WhicheverOneIsScrolled()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(LongPatch(400), height: 300);
            ScrollViewer left = shown.Left.ScrollHost!;
            ScrollViewer right = shown.Right.ScrollHost!;

            // Level from the start: the two documents have a line for every row.
            Assert.Equal(shown.Left.Document.LineCount, shown.Right.Document.LineCount);
            Assert.Equal(left.Extent.Height, right.Extent.Height, 1);

            left.Offset = new Vector(0, 900);
            Settle(shown.Window);

            AssertNear(900, left.Offset.Y);
            AssertNear(left.Offset.Y, right.Offset.Y);

            right.Offset = new Vector(0, 300);
            Settle(shown.Window);

            AssertNear(300, right.Offset.Y);
            AssertNear(right.Offset.Y, left.Offset.Y);

            shown.Window.Close();
        });
    }

    [Fact]
    public void TheTwoSides_ScrollSidewaysTogether_EachAsFarAsItsOwnLongestLine()
    {
        _fixture.RunAsync(async () =>
        {
            // The old side's longest line is 400 characters, the new side's 250: both overflow, and
            // the old one can go further.
            Shown shown = await ShowAsync(WidePatch(old: 400, @new: 250), height: 300);
            ScrollViewer left = shown.Left.ScrollHost!;
            ScrollViewer right = shown.Right.ScrollHost!;

            double leftEnd = left.Extent.Width - left.Viewport.Width;
            double rightEnd = right.Extent.Width - right.Viewport.Width;

            Assert.True(rightEnd > 0, "the new side was expected to overflow too");
            Assert.True(leftEnd > rightEnd + 100, "the old side was expected to go further");

            // All the way along the old side: the new side goes as far as it can and stops there —
            // and does not drag the old side back to its own end.
            left.Offset = new Vector(leftEnd, 0);
            Settle(shown.Window);

            AssertNear(leftEnd, left.Offset.X);
            AssertNear(rightEnd, right.Offset.X);

            // Scrolling the new side back takes the old one with it.
            right.Offset = new Vector(50, 0);
            Settle(shown.Window);

            AssertNear(50, right.Offset.X);
            AssertNear(50, left.Offset.X);

            // And the numbers never moved: they are each editor's margin, outside its scrolling.
            AssertNear(0, shown.Left.Gutter.Bounds.X);
            AssertNear(0, shown.Right.Gutter.Bounds.X);

            shown.Window.Close();
        });
    }

    [Fact]
    public void ALongLine_StaysInsideItsOwnSide()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(WidePatch(old: 400, @new: 5), height: 300);

            Rect left = Bounds(shown.Left, shown.Window);
            Rect right = Bounds(shown.Right, shown.Window);

            // The premise: the old side's line is far wider than its half of the window.
            Assert.True(shown.Left.ScrollHost!.Extent.Width > left.Width * 2);

            // Each side is its own half, and draws inside it: the editor's scroll viewer clips its
            // text, so the line scrolls within the old side instead of running over the new one.
            Assert.True(left.Right <= right.Left, $"the old side ({left}) runs into the new one ({right})");
            Assert.True(
                shown.Left.ScrollHost.GetVisualDescendants().OfType<ScrollContentPresenter>().First().ClipToBounds,
                "the old side does not clip its text");

            shown.Window.Close();
        });
    }

    [Fact]
    public void NeitherSideWraps_AndTheWrapToggleIsTheUnifiedRenderingsOnly()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(WidePatch(old: 400, @new: 250), height: 300);
            DiffTextEditor unified = shown.View.FindControl<DiffTextEditor>("UnifiedEditor")!;
            ToggleButton wrap = shown.View.GetVisualDescendants()
                .OfType<ToggleButton>()
                .Single(button => AutomationProperties.GetName(button) == "Wrap long lines");

            shown.Viewer.Render.WrapLines = true;
            Settle(shown.Window);

            // Wrapped independently, the two sides would put their rows out of line.
            Assert.False(shown.Left.WordWrap);
            Assert.False(shown.Right.WordWrap);
            Assert.True(unified.WordWrap);

            Assert.False(wrap.IsEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(wrap));

            shown.Viewer.ShowUnifiedCommand.Execute(null);
            Settle(shown.Window);

            Assert.True(wrap.IsEnabled);

            shown.Window.Close();
        });
    }

    [Fact]
    public void ANewFile_StartsBothSidesAtTheirLeftEdge()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(WidePatch(old: 400, @new: 250), height: 300);

            shown.Left.ScrollHost!.Offset = new Vector(200, 0);
            Settle(shown.Window);
            Assert.True(shown.Right.ScrollHost!.Offset.X > 0);

            shown.Diffs.Patch = UnifiedDiffParser.Parse(WidePatch(old: 300, @new: 300)).Files[0];
            await shown.Viewer.ReloadAsync();
            Settle(shown.Window);

            AssertNear(0, shown.Left.ScrollHost.Offset.X);
            AssertNear(0, shown.Right.ScrollHost.Offset.X);

            shown.Window.Close();
        });
    }

    [Fact]
    public void AFile_OpensWithBothSidesLevelAtItsFirstChange()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(PatchChangingLine(300, 200), height: 300);

            double expected = shown.Left.TextArea.TextView.GetVisualTopByDocumentLine(shown.Viewer.SideBySideFirstChangeRow - 2 + 1);

            Assert.True(shown.Viewer.SideBySideFirstChangeRow > 100);
            AssertNear(expected, shown.Left.ScrollHost!.Offset.Y);
            AssertNear(expected, shown.Right.ScrollHost!.Offset.Y);

            shown.Window.Close();
        });
    }

    [Fact]
    public void TheMinimap_ScrollsBothSides()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(LongPatch(400), height: 300);
            DiffMinimap map = shown.View.FindControl<DiffMinimap>("SideBySideMinimap")!;
            ScrollViewer left = shown.Left.ScrollHost!;

            map.RequestScrollTo(map.Bounds.Height);
            Settle(shown.Window);

            double end = left.Extent.Height - left.Viewport.Height;

            AssertNear(end, left.Offset.Y);
            AssertNear(end, shown.Right.ScrollHost!.Offset.Y);
            Assert.Equal(1, map.ViewportEnd, 2);

            shown.Window.Close();
        });
    }

    [Fact]
    public void Reachable_ClampsAnOffsetToWhatASideCanShow()
    {
        Assert.Equal(new Vector(100, 50), DiffScrollLink.Reachable(new Vector(500, 50), new Size(300, 400), new Size(200, 100)));
        Assert.Equal(new Vector(0, 300), DiffScrollLink.Reachable(new Vector(-5, 900), new Size(300, 400), new Size(200, 100)));

        // Content smaller than its viewport has nowhere to go.
        Assert.Equal(default, DiffScrollLink.Reachable(new Vector(80, 80), new Size(100, 50), new Size(200, 100)));
    }

    // ---------------------------------------------------------------- helpers

    private sealed record Shown(
        DiffViewerViewModel Viewer,
        RecordingDiffService Diffs,
        DiffViewerView View,
        DiffTextEditor Left,
        DiffTextEditor Right,
        Window Window);

    private static async Task<Shown> ShowAsync(string patch, double height)
    {
        RecordingDiffService diffs = new() { Patch = UnifiedDiffParser.Parse(patch).Files[0] };
        DiffViewerViewModel viewer = new(
            diffs,
            new RecordingSystemInterop(),
            new SettingsService(
                new AppPaths(Path.Combine(Path.GetTempPath(), "enigma-side-editor-tests-" + Guid.NewGuid().ToString("N"))),
                NullLogger<SettingsService>.Instance),
            NullLogger<DiffViewerViewModel>.Instance);

        await viewer.ShowAsync(Repository, DiffTarget.Commit("abc123"), File);

        Assert.True(viewer.IsSideBySide);

        DiffViewerView view = new() { DataContext = viewer };
        Window window = new() { Content = view, Width = 900, Height = height };
        window.Show();
        Settle(window);

        return new Shown(
            viewer,
            diffs,
            view,
            view.FindControl<DiffTextEditor>("LeftEditor")!,
            view.FindControl<DiffTextEditor>("RightEditor")!,
            window);
    }

    private static void Settle(Window window)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Asserts an offset is where it was expected to the pixel: layout rounds an offset to a whole
    /// device pixel, so one set at 1423.5 comes back as 1423.
    /// </summary>
    private static void AssertNear(double expected, double actual)
        => Assert.True(Math.Abs(expected - actual) <= 1, $"expected {expected}, found {actual}");

    private static Rect Bounds(Control control, Visual relativeTo)
        => new(control.TranslatePoint(default, relativeTo)!.Value, control.Bounds.Size);

    private static string LongPatch(int lines)
    {
        StringBuilder patch = new(
            "diff --git a/src/long.txt b/src/long.txt\n" +
            "--- a/src/long.txt\n" +
            "+++ b/src/long.txt\n");

        patch.Append(CultureInfo.InvariantCulture, $"@@ -1,{lines} +1,{lines} @@\n");

        for (int index = 0; index < lines; index++)
        {
            patch.Append(" line ").Append(index.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return patch.ToString();
    }

    private static string WidePatch(int old, int @new)
        => "diff --git a/src/wide.txt b/src/wide.txt\n" +
           "--- a/src/wide.txt\n" +
           "+++ b/src/wide.txt\n" +
           "@@ -1,2 +1,2 @@\n" +
           "-" + new string('x', old) + "\n" +
           "+" + new string('y', @new) + "\n" +
           " tail\n";

    private static string PatchChangingLine(int lines, int changed)
    {
        StringBuilder builder = new();

        builder.Append("diff --git a/src/middle.txt b/src/middle.txt\n");
        builder.Append("--- a/src/middle.txt\n");
        builder.Append("+++ b/src/middle.txt\n");
        builder.Append(CultureInfo.InvariantCulture, $"@@ -1,{lines} +1,{lines} @@\n");

        for (int index = 0; index < lines; index++)
        {
            string number = index.ToString(CultureInfo.InvariantCulture);
            builder.Append(index == changed ? $"+line {number} changed\n" : $" line {number}\n");
        }

        return builder.ToString();
    }
}
