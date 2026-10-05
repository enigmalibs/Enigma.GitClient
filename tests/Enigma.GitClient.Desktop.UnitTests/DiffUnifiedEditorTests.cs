using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit.Rendering;
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
/// The unified rendering as the editor it now is: its bands, its search, its sideways scrolling, where
/// a file opens, and how little of a long patch it lays out.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiffUnifiedEditorTests
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

    private static readonly string LongLinePatch =
        "diff --git a/src/wide.txt b/src/wide.txt\n" +
        "index 1111111..2222222 100644\n" +
        "--- a/src/wide.txt\n" +
        "+++ b/src/wide.txt\n" +
        "@@ -1,2 +1,2 @@\n" +
        "-" + new string('x', 400) + "\n" +
        "+short\n" +
        " tail\n";

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

    public DiffUnifiedEditorTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void ABand_WidensTheContextWhenItIsPressed()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowUnifiedAsync(SamplePatch);
            int requests = shown.Diffs.PatchRequests.Count;

            Assert.True(shown.Editor.Diff.Lines[0].Kind == DiffDocumentLineKind.HunkHeader);

            Point band = At(shown.Editor, line: 1, column: 1, shown.Window);
            shown.Window.MouseMove(band, RawInputModifiers.None);

            // Pointing at the band says what it does, as the band button's tooltip did.
            Assert.Equal(DiffTextEditor.BandTip, ToolTip.GetTip(shown.Editor.TextArea));

            shown.Window.MouseDown(band, MouseButton.Left);
            shown.Window.MouseUp(band, MouseButton.Left);

            await WaitUntilAsync(() => shown.Diffs.PatchRequests.Count > requests);

            Assert.Equal(
                3 * DiffViewerViewModel.ExpansionFactor,
                shown.Diffs.PatchRequests[^1].ContextLines);

            // Off the band, the text area is text again.
            shown.Window.MouseMove(At(shown.Editor, line: 2, column: 2, shown.Window), RawInputModifiers.None);

            Assert.Null(ToolTip.GetTip(shown.Editor.TextArea));

            shown.Window.Close();
        });
    }

    [Fact]
    public void APressOnALine_StartsASelectionRatherThanWideningTheContext()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowUnifiedAsync(SamplePatch);
            int requests = shown.Diffs.PatchRequests.Count;

            Point line = At(shown.Editor, line: 2, column: 1, shown.Window);
            shown.Window.MouseDown(line, MouseButton.Left);
            shown.Window.MouseUp(line, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(requests, shown.Diffs.PatchRequests.Count);

            shown.Window.Close();
        });
    }

    [Fact]
    public void CtrlF_OpensTheSearch_WhichCannotReplaceAnything()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowUnifiedAsync(SamplePatch);
            string text = shown.Editor.Document.Text;

            Point line = At(shown.Editor, line: 2, column: 1, shown.Window);
            shown.Window.MouseDown(line, MouseButton.Left);
            shown.Window.MouseUp(line, MouseButton.Left);

            shown.Window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            Dispatcher.UIThread.RunJobs();

            Assert.True(shown.Editor.SearchPanel.IsOpened, "Ctrl+F did not open the editor's search");

            // The diff is read-only, and the search panel's replace must not be a way round it.
            shown.Editor.SearchPanel.IsReplaceMode = true;
            shown.Editor.SearchPanel.SearchPattern = "one";
            shown.Editor.SearchPanel.ReplacePattern = "edited";
            shown.Editor.SearchPanel.ReplaceAll();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(text, shown.Editor.Document.Text);

            shown.Window.Close();
        });
    }

    [Fact]
    public void ALongLine_ScrollsSideways_UnderAGutterThatStaysPut()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowUnifiedAsync(LongLinePatch);
            DiffGutterMargin gutter = shown.Editor.Gutter;
            TextView view = shown.Editor.TextArea.TextView;

            Point gutterBefore = gutter.TranslatePoint(default, shown.Window)!.Value;
            Point textBefore = view.TranslatePoint(default, shown.Window)!.Value;

            Assert.True(shown.Editor.ExtentWidth > shown.Editor.ViewportWidth, "the long line was expected to overflow");

            ScrollViewer scroll = shown.Editor.ScrollHost!;
            scroll.Offset = new Vector(500, scroll.Offset.Y);
            Settle(shown.Window);

            Assert.True(view.HorizontalOffset > 0, "the text did not scroll sideways");

            // The text moved, the gutter beside it did not: the numbers are a margin, outside the
            // scrolling, so a scrolled line can never paint over them.
            Assert.Equal(gutterBefore, gutter.TranslatePoint(default, shown.Window)!.Value);
            Assert.Equal(textBefore, view.TranslatePoint(default, shown.Window)!.Value);
            Assert.True(textBefore.X >= gutterBefore.X + gutter.Bounds.Width);

            shown.Window.Close();
        });
    }

    [Fact]
    public void AFile_OpensAtItsFirstChange()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowUnifiedAsync(PatchChangingLine(300, 200), height: 300);
            Settle(shown.Window);

            TextView view = shown.Editor.TextArea.TextView;
            int firstChange = shown.Viewer.UnifiedFirstChangeRow;

            Assert.True(firstChange > 100);

            // Two rows of context above the change, exactly: the top of the view is the top of the
            // row two above it — to the pixel, which is as exactly as an offset is kept — and the
            // change itself is the third line down.
            double expected = view.GetVisualTopByDocumentLine(firstChange - 2 + 1);

            Assert.True(
                Math.Abs(view.VerticalOffset - expected) < 1,
                $"the view starts at {view.VerticalOffset}, not at {expected}, the top of the row two above the change");

            shown.Window.Close();
        });
    }

    [Fact]
    public void ALongPatch_LaysOutOnlyTheLinesOnScreen()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowUnifiedAsync(LargePatch(5000), height: 600);
            Settle(shown.Window);

            Assert.Equal(5001, shown.Editor.Document.LineCount);

            // A 600px window holds a few dozen lines. Anything near five thousand means the whole
            // file was laid out, which is what freezes a viewer on a generated file.
            Assert.InRange(shown.Editor.TextArea.TextView.VisualLines.Count, 1, 200);

            shown.Window.Close();
        });
    }

    [Fact]
    public void ANewPatch_TakesTheOldSelectionAway()
    {
        _fixture.RunAsync(async () =>
        {
            Shown shown = await ShowUnifiedAsync(SamplePatch);

            shown.Editor.Select(shown.Editor.Document.GetLineByNumber(2).Offset, 3);

            Assert.Equal("one", shown.Viewer.SelectedText());

            shown.Diffs.Patch = UnifiedDiffParser.Parse(LongLinePatch).Files[0];
            await shown.Viewer.ReloadAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.True(shown.Editor.TextArea.Selection.IsEmpty);
            Assert.False(shown.Viewer.Render.Selection.IsActive);
            Assert.False(shown.Viewer.CopyCommand.CanExecute(null));

            shown.Window.Close();
        });
    }

    // ---------------------------------------------------------------- helpers

    private sealed record Shown(
        DiffViewerViewModel Viewer,
        RecordingDiffService Diffs,
        DiffTextEditor Editor,
        Window Window);

    private static async Task<Shown> ShowUnifiedAsync(string patch, double height = 420)
    {
        RecordingDiffService diffs = new() { Patch = UnifiedDiffParser.Parse(patch).Files[0] };
        DiffViewerViewModel viewer = new(
            diffs,
            new RecordingSystemInterop(),
            new SettingsService(
                new AppPaths(Path.Combine(Path.GetTempPath(), "enigma-unified-editor-tests-" + Guid.NewGuid().ToString("N"))),
                NullLogger<SettingsService>.Instance),
            NullLogger<DiffViewerViewModel>.Instance);

        await viewer.ShowAsync(Repository, DiffTarget.Commit("abc123"), File);

        viewer.ShowUnifiedCommand.Execute(null);
        await WaitUntilAsync(() => diffs.PatchRequests.Count >= 2 && !viewer.IsBusy);

        DiffViewerView view = new() { DataContext = viewer };
        Window window = new() { Content = view, Width = 900, Height = height };
        window.Show();
        Settle(window);

        DiffTextEditor editor = view.FindControl<DiffTextEditor>("UnifiedEditor")
            ?? throw new InvalidOperationException("The diff viewer has no unified editor.");

        return new Shown(viewer, diffs, editor, window);
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
    /// Where a character of the editor's document is on screen: the middle of its line, at the
    /// character's left edge.
    /// </summary>
    private static Point At(DiffTextEditor editor, int line, int column, Visual relativeTo)
    {
        TextView view = editor.TextArea.TextView;
        Point visual = view.GetVisualPosition(new AvaloniaEdit.TextViewPosition(line, column), VisualYPosition.LineMiddle);

        return view.TranslatePoint(visual - view.ScrollOffset, relativeTo)
            ?? throw new InvalidOperationException("The editor is not in the window.");
    }

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

    private static string LargePatch(int lines)
    {
        StringBuilder builder = new();

        builder.Append("diff --git a/src/big.txt b/src/big.txt\n");
        builder.Append("--- a/src/big.txt\n");
        builder.Append("+++ b/src/big.txt\n");
        builder.Append(CultureInfo.InvariantCulture, $"@@ -1,{lines} +1,{lines} @@\n");

        for (int index = 0; index < lines; index++)
        {
            string number = index.ToString(CultureInfo.InvariantCulture);
            builder.Append(index % 3 == 0 ? $"+line {number}\n" : $" line {number}\n");
        }

        return builder.ToString();
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
