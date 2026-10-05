using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
/// Selecting a diff's text with a real pointer on the headless platform, and copying it: both panes of
/// the side-by-side rendering, the unified one, Shift+click, a click that clears, and the menu.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiffTextSelectingTests
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

    public DiffTextSelectingTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void DraggingAcrossTheOldFile_SelectsItsLinesAndCtrlCCopiesThem()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(SamplePatch);

            DiffLineText from = Line(shown, DiffPane.Left, "one");
            DiffLineText to = Line(shown, DiffPane.Left, "three");

            Drag(shown.Window, StartOf(from, shown.Window), PastTheEndOf(to, shown.Window));

            Assert.False(shown.View.IsSelectingText);
            Assert.Equal(DiffPane.Left, shown.Viewer.Render.Selection.Pane);
            Assert.Equal("one\ntwo\nthree", shown.Viewer.SelectedText());

            shown.Window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
            await WaitUntilAsync(() => shown.Interop.Copied.Count == 1);

            Assert.Equal("one\ntwo\nthree", Assert.Single(shown.Interop.Copied));

            shown.Window.Close();
        });
    }

    [Fact]
    public void TheNewFileSelectsOnItsOwn_EvenWhenTheDragCrossesToTheOtherSide()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(SamplePatch);

            DiffLineText from = Line(shown, DiffPane.Right, "two changed");
            DiffLineText over = Line(shown, DiffPane.Left, "five");

            // Ends over the old file's last line: the selection stays in the new file, on that row —
            // at its start, since the pointer is left of the new file's text.
            Drag(shown.Window, StartOf(from, shown.Window), PastTheEndOf(over, shown.Window));

            Assert.Equal(DiffPane.Right, shown.Viewer.Render.Selection.Pane);
            Assert.Equal("two changed\nthree changed\nfour\n", shown.Viewer.SelectedText());

            shown.Window.Close();
        });
    }

    [Fact]
    public void TheUnifiedRenderingSelectsTheSameWay()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(SamplePatch);

            shown.Viewer.ShowUnifiedCommand.Execute(null);

            DiffTextEditor editor = shown.View.FindControl<DiffTextEditor>("UnifiedEditor")!;
            await WaitUntilAsync(() => shown.Viewer.IsUnified && editor.Document.Text.Contains("four", StringComparison.Ordinal) && editor.TextArea.TextView.VisualLinesValid);

            // Rows 3 to 6 of the unified rendering — "three" to "four" — are document lines 4 to 7.
            Drag(shown.Window, At(editor, line: 4, column: 1, shown.Window), At(editor, line: 7, column: 5, shown.Window));

            Assert.Equal(DiffPane.Unified, shown.Viewer.Render.Selection.Pane);
            Assert.Equal("three\ntwo changed\nthree changed\nfour", shown.Viewer.SelectedText());

            // The editor has the keyboard now, and the copy gesture is the viewer's: code only.
            shown.Window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
            await WaitUntilAsync(() => shown.Interop.Copied.Count == 1);

            Assert.Equal("three\ntwo changed\nthree changed\nfour", Assert.Single(shown.Interop.Copied));

            shown.Window.Close();
        });
    }

    [Fact]
    public void AClickClearsTheSelection_AndShiftClickExtendsIt()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(SamplePatch);
            DiffTextSelection selection = shown.Viewer.Render.Selection;

            Drag(
                shown.Window,
                StartOf(Line(shown, DiffPane.Left, "one"), shown.Window),
                PastTheEndOf(Line(shown, DiffPane.Left, "two"), shown.Window));
            Assert.False(selection.IsEmpty);

            Point start = StartOf(Line(shown, DiffPane.Left, "three"), shown.Window);
            shown.Window.MouseDown(start, MouseButton.Left);
            shown.Window.MouseUp(start, MouseButton.Left);
            Assert.True(selection.IsEmpty);

            Point end = PastTheEndOf(Line(shown, DiffPane.Left, "five"), shown.Window);
            shown.Window.MouseDown(end, MouseButton.Left, RawInputModifiers.Shift);
            shown.Window.MouseUp(end, MouseButton.Left, RawInputModifiers.Shift);

            Assert.Equal("three\nfive", shown.Viewer.SelectedText());

            shown.Window.Close();
        });
    }

    [Fact]
    public void APressOnTheLineNumbers_SelectsNoText()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(SamplePatch);

            DiffLineText line = Line(shown, DiffPane.Left, "one");
            Point numbers = line.TranslatePoint(new Point(-40, line.Bounds.Height / 2), shown.Window)!.Value;

            Drag(shown.Window, numbers, PastTheEndOf(Line(shown, DiffPane.Left, "three"), shown.Window));

            Assert.False(shown.Viewer.Render.Selection.IsActive);

            shown.Window.Close();
        });
    }

    [Fact]
    public void TheListsCopyMenuCopiesTheSelectedText_OrElseTheSelectedRows()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(SamplePatch);

            foreach (ListBox list in shown.View.GetVisualDescendants().OfType<ListBox>())
            {
                MenuItem copy = Assert.IsType<MenuItem>(Assert.Single(list.ContextMenu!.Items));
                Assert.Equal("Copy", copy.Header);
                Assert.NotNull(copy.Icon);

                // A menu takes its list's DataContext when it opens, which is when its command binds.
                if (list.IsEffectivelyVisible)
                {
                    list.ContextMenu.Open(list);
                    Dispatcher.UIThread.RunJobs();

                    Assert.Same(shown.Viewer.CopyCommand, copy.Command);

                    list.ContextMenu.Close();
                    Dispatcher.UIThread.RunJobs();
                }
            }

            Assert.False(shown.Viewer.CopyCommand.CanExecute(null));

            Drag(
                shown.Window,
                StartOf(Line(shown, DiffPane.Right, "four"), shown.Window),
                PastTheEndOf(Line(shown, DiffPane.Right, "five"), shown.Window));

            Assert.True(shown.Viewer.CopyCommand.CanExecute(null));
            await shown.Viewer.CopyCommand.ExecuteAsync(null);
            Assert.Equal("four\nfive", shown.Interop.Copied[^1]);

            // No text selected: the rows the list has selected are what is copied.
            shown.Viewer.Render.Selection.Clear();
            shown.Viewer.Selection.Clear();
            shown.Viewer.Selection.Add(shown.Viewer.SideBySideRows.First(row => row.Left?.Text == "one"));

            await shown.Viewer.CopyCommand.ExecuteAsync(null);
            Assert.Equal("one", shown.Interop.Copied[^1].TrimEnd('\n'));

            shown.Window.Close();
        });
    }

    [Fact]
    public void HoldingTheSelectionBelowTheList_ScrollsItAndSelectsWhatComesIntoView()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(LongPatch(400), height: 300);

            DiffLineText first = Lines(shown.View, DiffPane.Left).OrderBy(line => line.Row).First(line => line.Text?.Length > 0);
            ListBox list = shown.View.GetVisualDescendants().OfType<ListBox>().Single(box => box.Name == "SideBySideList");
            ScrollViewer scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();

            Point start = StartOf(first, shown.Window);
            Point below = list.TranslatePoint(new Point(list.Bounds.Width / 4, list.Bounds.Height - 2), shown.Window)!.Value;

            shown.Window.MouseDown(start, MouseButton.Left);
            shown.Window.MouseMove(new Point(start.X + 8, start.Y + 8), RawInputModifiers.LeftMouseButton);
            shown.Window.MouseMove(below, RawInputModifiers.LeftMouseButton);

            Assert.True(shown.View.IsSelectingText);

            int endBefore = shown.Viewer.Render.Selection.End.Row;
            await WaitUntilAsync(() => scroll.Offset.Y > 100 && shown.Viewer.Render.Selection.End.Row > endBefore + 3);

            shown.Window.MouseUp(below, MouseButton.Left);
            Assert.False(shown.View.IsSelectingText);

            shown.Window.Close();
        });
    }

    // ---------------------------------------------------------------- helpers

    private sealed record Shown(DiffViewerViewModel Viewer, DiffViewerView View, Window Window, RecordingSystemInterop Interop);

    private static async Task<Shown> ShowAsync(string patch, double height = 500)
    {
        RecordingDiffService diffs = new() { Patch = UnifiedDiffParser.Parse(patch).Files[0] };
        RecordingSystemInterop interop = new();
        DiffViewerViewModel viewer = new(
            diffs,
            interop,
            new SettingsService(
                new AppPaths(Path.Combine(Path.GetTempPath(), "enigma-selecting-tests-" + Guid.NewGuid().ToString("N"))),
                NullLogger<SettingsService>.Instance),
            NullLogger<DiffViewerViewModel>.Instance);

        await viewer.ShowAsync(Repository, DiffTarget.Commit("abc123"), File);

        DiffViewerView view = new() { DataContext = viewer };
        Window window = new() { Content = view, Width = 900, Height = height };
        window.Show();

        for (int attempt = 0; attempt < 10; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            Dispatcher.UIThread.RunJobs();
        }

        return new Shown(viewer, view, window, interop);
    }

    private static string LongPatch(int lines)
    {
        StringBuilder patch = new(
            "diff --git a/src/long.txt b/src/long.txt\n" +
            "index 1111111..2222222 100644\n" +
            "--- a/src/long.txt\n" +
            "+++ b/src/long.txt\n");

        patch.Append("@@ -1,").Append(lines).Append(" +1,").Append(lines).Append(" @@\n");

        for (int index = 0; index < lines; index++)
        {
            patch.Append(" line ").Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        }

        return patch.ToString();
    }

    private static DiffLineText[] Lines(DiffViewerView view, DiffPane pane)
        => [.. view.GetVisualDescendants().OfType<DiffLineText>().Where(line => line.Pane == pane && line.IsEffectivelyVisible)];

    private static DiffLineText Line(Shown shown, DiffPane pane, string text)
        => Lines(shown.View, pane).First(line => line.Text == text);

    private static Point StartOf(DiffLineText line, Visual relativeTo)
        => line.TranslatePoint(new Point(1, line.Bounds.Height / 2), relativeTo)
            ?? throw new InvalidOperationException("The line is not in the window.");

    private static Point PastTheEndOf(DiffLineText line, Visual relativeTo)
        => line.TranslatePoint(new Point(line.Bounds.Width - 2, line.Bounds.Height / 2), relativeTo)
            ?? throw new InvalidOperationException("The line is not in the window.");

    /// <summary>
    /// Where a character of an editor's document is on screen: the middle of its line, at the
    /// character's left edge.
    /// </summary>
    private static Point At(DiffTextEditor editor, int line, int column, Visual relativeTo)
    {
        AvaloniaEdit.Rendering.TextView view = editor.TextArea.TextView;
        Point visual = view.GetVisualPosition(
            new AvaloniaEdit.TextViewPosition(line, column),
            AvaloniaEdit.Rendering.VisualYPosition.LineMiddle);

        return view.TranslatePoint(visual - view.ScrollOffset, relativeTo)
            ?? throw new InvalidOperationException("The editor is not in the window.");
    }

    private static void Drag(Window window, Point from, Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(from.X + 8, from.Y + 4), RawInputModifiers.LeftMouseButton);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int attempts = 300)
    {
        for (int attempt = 0; attempt < attempts && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "the viewer never reached the state the test waited for");
    }
}
