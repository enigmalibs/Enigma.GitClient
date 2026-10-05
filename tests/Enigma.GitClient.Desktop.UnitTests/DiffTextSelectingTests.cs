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
/// Selecting a diff's text with a real pointer on the headless platform, and copying it: both editors
/// of the side-by-side rendering, the unified one, Shift+click, a click that clears, and the menu.
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

            Drag(shown.Window, StartOf(shown, DiffPane.Left, "one"), PastTheEndOf(shown, DiffPane.Left, "three"));

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

            // Ends over the old file's last line: the selection stays in the new file, on that row —
            // at its start, since the pointer is left of the new file's text.
            Drag(shown.Window, StartOf(shown, DiffPane.Right, "two changed"), PastTheEndOf(shown, DiffPane.Left, "five"));

            Assert.Equal(DiffPane.Right, shown.Viewer.Render.Selection.Pane);
            Assert.Equal("two changed\nthree changed\nfour\n", shown.Viewer.SelectedText());

            // And the old file shows no selection of its own.
            Assert.True(Editor(shown, DiffPane.Left).TextArea.Selection.IsEmpty);

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

            Drag(shown.Window, StartOf(shown, DiffPane.Left, "one"), PastTheEndOf(shown, DiffPane.Left, "two"));
            Assert.False(selection.IsEmpty);

            Point start = StartOf(shown, DiffPane.Left, "three");
            shown.Window.MouseDown(start, MouseButton.Left);
            shown.Window.MouseUp(start, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(selection.IsEmpty);

            Point end = PastTheEndOf(shown, DiffPane.Left, "five");
            shown.Window.MouseDown(end, MouseButton.Left, RawInputModifiers.Shift);
            shown.Window.MouseUp(end, MouseButton.Left, RawInputModifiers.Shift);
            Dispatcher.UIThread.RunJobs();

            // The filler between them has no text, so the copy leaves it out.
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
            DiffTextEditor left = Editor(shown, DiffPane.Left);

            Point numbers = left.Gutter.TranslatePoint(new Point(4, Middle(left, line: 3)), shown.Window)!.Value;
            shown.Window.MouseDown(numbers, MouseButton.Left);
            shown.Window.MouseUp(numbers, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.True(shown.Viewer.Render.Selection.IsEmpty);
            Assert.False(shown.Viewer.CopyCommand.CanExecute(null));

            shown.Window.Close();
        });
    }

    [Fact]
    public void EachEditorsCopyMenuCopiesTheSelectedText()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(SamplePatch);

            foreach (DiffTextEditor editor in shown.View.GetVisualDescendants().OfType<DiffTextEditor>())
            {
                MenuItem copy = Assert.IsType<MenuItem>(Assert.Single(editor.ContextMenu!.Items));
                Assert.Equal("Copy", copy.Header);
                Assert.NotNull(copy.Icon);

                // A menu takes its editor's DataContext when it opens, which is when its command binds.
                if (editor.IsEffectivelyVisible)
                {
                    editor.ContextMenu.Open(editor);
                    Dispatcher.UIThread.RunJobs();

                    Assert.Same(shown.Viewer.CopyCommand, copy.Command);

                    editor.ContextMenu.Close();
                    Dispatcher.UIThread.RunJobs();
                }
            }

            Assert.False(shown.Viewer.CopyCommand.CanExecute(null));

            Drag(shown.Window, StartOf(shown, DiffPane.Right, "four"), PastTheEndOf(shown, DiffPane.Right, "five"));

            Assert.True(shown.Viewer.CopyCommand.CanExecute(null));
            await shown.Viewer.CopyCommand.ExecuteAsync(null);
            Assert.Equal("four\nfive", shown.Interop.Copied[^1]);

            shown.Window.Close();
        });
    }

    [Fact]
    public void HoldingTheSelectionBelowAnEditor_ScrollsItAndSelectsWhatComesIntoView()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowAsync(LongPatch(400), height: 300);
            DiffTextEditor left = Editor(shown, DiffPane.Left);
            ScrollViewer scroll = left.ScrollHost!;

            Point start = At(left, line: 2, column: 1, shown.Window);
            Point below = left.TranslatePoint(new Point(left.Bounds.Width / 2, left.Bounds.Height + 40), shown.Window)!.Value;

            shown.Window.MouseDown(start, MouseButton.Left);
            shown.Window.MouseMove(new Point(start.X + 8, start.Y + 8), RawInputModifiers.LeftMouseButton);

            int endBefore = -1;

            for (int step = 0; step < 20 && scroll.Offset.Y <= 100; step++)
            {
                shown.Window.MouseMove(new Point(below.X, below.Y + step), RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();

                if (endBefore < 0)
                {
                    endBefore = shown.Viewer.Render.Selection.End.Row;
                }

                await Task.Delay(10);
            }

            Assert.True(scroll.Offset.Y > 0, "holding the selection below the editor did not scroll it");
            Assert.True(shown.Viewer.Render.Selection.End.Row > endBefore, "the selection did not follow the scroll");

            shown.Window.MouseUp(below, MouseButton.Left);
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

    private static DiffTextEditor Editor(Shown shown, DiffPane pane)
        => shown.View.FindControl<DiffTextEditor>(pane switch
        {
            DiffPane.Left => "LeftEditor",
            DiffPane.Right => "RightEditor",
            _ => "UnifiedEditor",
        })!;

    /// <summary>The document line of a pane that holds a text, counted from one.</summary>
    private static int LineOf(DiffTextEditor editor, string text)
        => Array.IndexOf(editor.Document.Text.Split('\n'), text) + 1;

    private static Point StartOf(Shown shown, DiffPane pane, string text)
    {
        DiffTextEditor editor = Editor(shown, pane);

        return At(editor, LineOf(editor, text), 1, shown.Window);
    }

    private static Point PastTheEndOf(Shown shown, DiffPane pane, string text)
    {
        DiffTextEditor editor = Editor(shown, pane);

        return At(editor, LineOf(editor, text), text.Length + 1, shown.Window) + new Point(6, 0);
    }

    private static double Middle(DiffTextEditor editor, int line)
    {
        AvaloniaEdit.Rendering.TextView view = editor.TextArea.TextView;

        return view.GetVisualTopByDocumentLine(line) - view.VerticalOffset + (view.DefaultLineHeight / 2);
    }

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
