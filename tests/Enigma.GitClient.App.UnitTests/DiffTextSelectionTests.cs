using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Enigma.GitClient.App.Controls.Diff;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels.Panels;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Selected text in the diff: what a selection covers row by row, the text it copies, the character a
/// point lands on, and what the line draws for it.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiffTextSelectionTests
{
    private const string SamplePatch =
        "diff --git a/src/app.txt b/src/app.txt\n" +
        "index 1111111..2222222 100644\n" +
        "--- a/src/app.txt\n" +
        "+++ b/src/app.txt\n" +
        "@@ -1,4 +1,5 @@\n" +
        " one\n" +
        "-two\n" +
        "-three\n" +
        "+two changed\n" +
        "+three changed\n" +
        "+four\n" +
        " five\n";

    private const string NoNewlinePatch =
        "diff --git a/src/end.txt b/src/end.txt\n" +
        "index 1111111..2222222 100644\n" +
        "--- a/src/end.txt\n" +
        "+++ b/src/end.txt\n" +
        "@@ -1,1 +1,1 @@\n" +
        "-last\n" +
        "\\ No newline at end of file\n" +
        "+last line\n";

    private static readonly RepositoryHandle Repository = OperatingSystem.IsWindows()
        ? new RepositoryHandle(@"C:\src\client", @"C:\src\client\.git")
        : new RepositoryHandle("/src/client", "/src/client/.git");

    private static readonly ChangedFile File = new()
    {
        Path = "src/app.txt",
        ChangeKind = FileChangeKind.Modified,
        AddedLines = 3,
        RemovedLines = 2,
    };

    private readonly HeadlessAvaloniaFixture _fixture;

    public DiffTextSelectionTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- the selection

    [Fact]
    public void ANewSelectionIsEmptyUntilItIsExtended()
    {
        DiffTextSelection selection = new();
        Assert.False(selection.IsActive);
        Assert.True(selection.IsEmpty);

        selection.Begin(DiffPane.Left, new DiffTextPosition(3, 2));
        Assert.True(selection.IsActive);
        Assert.True(selection.IsEmpty);
        Assert.Null(selection.RangeOn(3, DiffPane.Left, 10));

        selection.ExtendTo(new DiffTextPosition(3, 5));
        Assert.False(selection.IsEmpty);
        Assert.Equal((2, 5), selection.RangeOn(3, DiffPane.Left, 10));

        selection.Clear();
        Assert.False(selection.IsActive);
        Assert.Null(selection.RangeOn(3, DiffPane.Left, 10));
    }

    [Fact]
    public void ASelectionDraggedBackwardsCoversTheSameText()
    {
        DiffTextSelection selection = new();
        selection.Begin(DiffPane.Unified, new DiffTextPosition(5, 1));
        selection.ExtendTo(new DiffTextPosition(3, 4));

        Assert.Equal(new DiffTextPosition(3, 4), selection.Start);
        Assert.Equal(new DiffTextPosition(5, 1), selection.End);

        Assert.Equal((4, 10), selection.RangeOn(3, DiffPane.Unified, 10));
        Assert.Equal((0, 7), selection.RangeOn(4, DiffPane.Unified, 7));
        Assert.Equal((0, 1), selection.RangeOn(5, DiffPane.Unified, 7));
        Assert.Null(selection.RangeOn(2, DiffPane.Unified, 7));
        Assert.Null(selection.RangeOn(6, DiffPane.Unified, 7));
    }

    [Fact]
    public void ARowInsideTheSelectionIsSelectedEvenWithoutText_AndOtherPanesNever()
    {
        DiffTextSelection selection = new();
        selection.Begin(DiffPane.Right, new DiffTextPosition(1, 2));
        selection.ExtendTo(new DiffTextPosition(4, 3));

        Assert.Equal((0, 0), selection.RangeOn(2, DiffPane.Right, 0));
        Assert.Null(selection.RangeOn(2, DiffPane.Left, 10));

        // Columns past a short line's end are held to it.
        Assert.Equal((1, 1), selection.RangeOn(1, DiffPane.Right, 1));
        Assert.Equal((0, 2), selection.RangeOn(4, DiffPane.Right, 2));
    }

    [Fact]
    public void Changed_IsRaisedForEveryChangeAndNothingElse()
    {
        DiffTextSelection selection = new();
        int changes = 0;
        selection.Changed += (_, _) => changes++;

        selection.Clear();
        selection.ExtendTo(new DiffTextPosition(1, 1));
        Assert.Equal(0, changes);

        selection.Begin(DiffPane.Unified, new DiffTextPosition(0, 0));
        selection.Begin(DiffPane.Unified, new DiffTextPosition(0, 0));
        selection.ExtendTo(new DiffTextPosition(0, 3));
        selection.ExtendTo(new DiffTextPosition(0, 3));
        selection.Clear();
        selection.Clear();

        Assert.Equal(3, changes);
    }

    [Fact]
    public void Text_TakesTheSelectedPartOfEachLineAndSkipsRowsWithoutOne()
    {
        string?[] lines = ["alpha", null, "beta", "gamma", "delta"];
        DiffTextSelection selection = new();

        Assert.Equal(string.Empty, selection.Text(row => lines[row]));

        selection.Begin(DiffPane.Unified, new DiffTextPosition(0, 2));
        selection.ExtendTo(new DiffTextPosition(3, 3));

        Assert.Equal("pha\nbeta\ngam", selection.Text(row => lines[row]));
    }

    // ---------------------------------------------------------------- the viewer

    [Fact]
    public void TheRowsAreNumberedInTheOrderTheyAreShown()
    {
        _fixture.RunAsync(async () =>
        {
            DiffViewerViewModel viewer = await ShownAsync(SamplePatch);

            Assert.Equal(Enumerable.Range(0, viewer.UnifiedRows.Count), viewer.UnifiedRows.Select(row => row.Index));
            Assert.Equal(Enumerable.Range(0, viewer.SideBySideRows.Count), viewer.SideBySideRows.Select(row => row.Index));
        });
    }

    [Fact]
    public void SelectedText_IsTheUnifiedLinesAsTheyRead_WithoutTheBandNumbersOrMarkers()
    {
        _fixture.RunAsync(async () =>
        {
            DiffViewerViewModel viewer = await ShownAsync(SamplePatch);

            int band = viewer.UnifiedRows.ToList().FindIndex(row => row.IsHunkHeader);
            int twoChanged = IndexOf(viewer.UnifiedRows, row => row.Single?.Text == "two changed");

            DiffTextSelection selection = viewer.Render.Selection;
            selection.Begin(DiffPane.Unified, new DiffTextPosition(band, 0));
            selection.ExtendTo(new DiffTextPosition(twoChanged, 3));

            Assert.Equal("one\ntwo\nthree\ntwo", viewer.SelectedText());
        });
    }

    [Fact]
    public void SelectedText_KeepsTheTwoSidesApart()
    {
        _fixture.RunAsync(async () =>
        {
            DiffViewerViewModel viewer = await ShownAsync(SamplePatch);

            int one = IndexOf(viewer.SideBySideRows, row => row.Left?.Text == "one");
            int five = IndexOf(viewer.SideBySideRows, row => row.Left?.Text == "five");

            DiffTextSelection selection = viewer.Render.Selection;

            selection.Begin(DiffPane.Left, new DiffTextPosition(one, 0));
            selection.ExtendTo(new DiffTextPosition(five, 4));
            Assert.Equal("one\ntwo\nthree\nfive", viewer.SelectedText());

            selection.Begin(DiffPane.Right, new DiffTextPosition(one, 1));
            selection.ExtendTo(new DiffTextPosition(five, 2));
            Assert.Equal("ne\ntwo changed\nthree changed\nfour\nfi", viewer.SelectedText());
        });
    }

    [Fact]
    public void SelectedText_LeavesOutGitsMissingNewlineMarker()
    {
        _fixture.RunAsync(async () =>
        {
            DiffViewerViewModel viewer = await ShownAsync(NoNewlinePatch);

            DiffTextSelection selection = viewer.Render.Selection;
            selection.Begin(DiffPane.Unified, new DiffTextPosition(0, 0));
            selection.ExtendTo(new DiffTextPosition(viewer.UnifiedRows.Count - 1, 9));

            Assert.Equal("last\nlast line", viewer.SelectedText());
        });
    }

    [Fact]
    public void AnotherFileOrAnotherRenderingClearsTheSelection()
    {
        _fixture.RunAsync(async () =>
        {
            DiffViewerViewModel viewer = await ShownAsync(SamplePatch);
            DiffTextSelection selection = viewer.Render.Selection;

            selection.Begin(DiffPane.Left, new DiffTextPosition(1, 0));
            selection.ExtendTo(new DiffTextPosition(2, 2));

            await viewer.ShowAsync(Repository, DiffTarget.Commit("def456"), File);
            Assert.False(selection.IsActive);

            selection.Begin(DiffPane.Left, new DiffTextPosition(1, 0));
            selection.ExtendTo(new DiffTextPosition(2, 2));

            viewer.ShowUnifiedCommand.Execute(null);
            Assert.False(selection.IsActive);
        });
    }

    // ---------------------------------------------------------------- the line

    [Fact]
    public void IndexAt_FindsThePlaceBetweenTwoCharacters_TabsAndScrollIncluded()
    {
        _fixture.Run(() =>
        {
            FontFamily? family = DiffTypography.MonospaceFamilies().FirstOrDefault();
            Assert.SkipWhen(family is null, "no monospace face on this machine");

            // "a", a tab drawn over three columns (to the stop at 4), then "bc".
            DiffLineText line = new() { Text = "a\tbc", TabWidth = 4, FontFamily = family!, FontSize = 14 };
            line.Measure(Size.Infinity);
            line.Arrange(new Rect(line.DesiredSize));

            double column = DiffTypography.MeasureCharacterWidth(family!, 14);
            double y = line.DesiredSize.Height / 2;

            Assert.Equal(0, line.IndexAt(new Point(-20, y)));
            Assert.Equal(0, line.IndexAt(new Point(column * 0.3, y)));
            Assert.Equal(1, line.IndexAt(new Point(column * 0.7, y)));
            Assert.Equal(1, line.IndexAt(new Point(column * 1.8, y)));
            Assert.Equal(2, line.IndexAt(new Point(column * 3.3, y)));
            Assert.Equal(3, line.IndexAt(new Point(column * 4.7, y)));
            Assert.Equal(4, line.IndexAt(new Point(column * 40, y)));

            // Scrolled a column to the right: the same point is a column further into the text.
            line.HorizontalOffset = 1;
            Assert.Equal(1, line.IndexAt(new Point(column * 0.3, y)));
        });
    }

    [Fact]
    public void IndexAt_OnAnEmptyLineIsItsStart()
    {
        _fixture.Run(() =>
        {
            DiffLineText line = new() { Text = string.Empty };
            line.Measure(Size.Infinity);

            Assert.Equal(0, line.IndexAt(new Point(50, 5)));
        });
    }

    [Fact]
    public void TheLineTintsExactlyItsSelectedCharacters()
    {
        _fixture.Run(() =>
        {
            FontFamily? family = DiffTypography.MonospaceFamilies().FirstOrDefault();
            Assert.SkipWhen(family is null, "no monospace face on this machine");

            DiffTextSelection selection = new();
            DiffLineText line = new()
            {
                Text = "abcdefgh",
                FontFamily = family!,
                FontSize = 20,
                Foreground = Brushes.Transparent,
                SelectionBrush = Brushes.Red,
                Selection = selection,
                Row = 4,
                Pane = DiffPane.Right,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            Window window = new() { Content = line, Width = 400, Height = 60, Background = Brushes.White };
            window.Show();

            double column = DiffTypography.MeasureCharacterWidth(family!, 20);

            // Selected in the other pane, then on another row: nothing on this line.
            selection.Begin(DiffPane.Left, new DiffTextPosition(4, 2));
            selection.ExtendTo(new DiffTextPosition(4, 5));
            Assert.False(RedAt(window, column * 3.5, 10));

            selection.Begin(DiffPane.Right, new DiffTextPosition(4, 2));
            selection.ExtendTo(new DiffTextPosition(4, 5));

            using (Bitmap frame = Frame(window))
            {
                Assert.True(IsRed(frame, column * 3.5, 10), "a selected character must be tinted");
                Assert.False(IsRed(frame, column * 1.5, 10), "a character before the selection must not be");
                Assert.False(IsRed(frame, column * 6.5, 10), "a character after the selection must not be");
            }

            selection.Clear();
            Assert.False(RedAt(window, column * 3.5, 10));

            window.Close();
        });
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<DiffViewerViewModel> ShownAsync(string patch)
    {
        RecordingDiffService diffs = new() { Patch = UnifiedDiffParser.Parse(patch).Files[0] };
        DiffViewerViewModel viewer = new(
            diffs,
            new RecordingSystemInterop(),
            new SettingsService(
                new AppPaths(Path.Combine(Path.GetTempPath(), "enigma-selection-tests-" + Guid.NewGuid().ToString("N"))),
                NullLogger<SettingsService>.Instance),
            NullLogger<DiffViewerViewModel>.Instance);

        await viewer.ShowAsync(Repository, DiffTarget.Commit("abc123"), File);

        return viewer;
    }

    private static int IndexOf(System.Collections.Generic.IList<DiffRowViewModel> rows, Func<DiffRowViewModel, bool> match)
    {
        for (int index = 0; index < rows.Count; index++)
        {
            if (match(rows[index]))
            {
                return index;
            }
        }

        throw new InvalidOperationException("No row matched.");
    }

    private static Bitmap Frame(Window window)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        }

        return window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was rendered.");
    }

    private static bool RedAt(Window window, double x, double y)
    {
        using Bitmap frame = Frame(window);
        return IsRed(frame, x, y);
    }

    private static bool IsRed(Bitmap frame, double x, double y)
    {
        byte[] pixel = new byte[4];
        GCHandle handle = GCHandle.Alloc(pixel, GCHandleType.Pinned);

        try
        {
            frame.CopyPixels(new PixelRect((int)x, (int)y, 1, 1), handle.AddrOfPinnedObject(), pixel.Length, 4);
        }
        finally
        {
            handle.Free();
        }

        // The frame is in whichever byte order the platform renders in.
        bool rgba = frame.Format == PixelFormats.Rgba8888;
        byte red = rgba ? pixel[0] : pixel[2];
        byte blue = rgba ? pixel[2] : pixel[0];

        return red > 200 && pixel[1] < 80 && blue < 80;
    }
}
