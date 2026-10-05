using System;
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
/// BUG-7823: a selection dragged to the bottom-right of a diff, past its last line, used to freeze the
/// application — the editor's text view asked for an offset its scroll viewer could not hold, and the
/// two fought for ever.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiffSelectionDragEndTests
{
    private static readonly RepositoryHandle Repository = OperatingSystem.IsWindows()
        ? new RepositoryHandle(@"C:\src\client", @"C:\src\client\.git")
        : new RepositoryHandle("/src/client", "/src/client/.git");

    private readonly HeadlessAvaloniaFixture _fixture;

    public DiffSelectionDragEndTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Drags to the corner, holds the button there, and makes the scroll viewer coerce its offset again
    /// and again — which a window does on every layout, and which is what kept the fight going.
    /// </summary>
    [Theory]
    [InlineData(false, "UnifiedEditor", false)]
    [InlineData(false, "UnifiedEditor", true)]
    [InlineData(true, "LeftEditor", false)]
    [InlineData(true, "RightEditor", false)]
    [InlineData(true, "RightEditor", true)]
    public void ASelectionDraggedPastTheEnd_SelectsToTheEndAndSettles(bool sideBySide, string editorName, bool longLines)
    {
        _fixture.RunAsync(async () =>
        {
            (DiffViewerViewModel viewer, Window window, DiffTextEditor editor) = await ShowAsync(sideBySide, editorName, longLines);
            TextView view = editor.TextArea.TextView;
            ScrollViewer scroll = editor.ScrollHost!;

            Point start = view.TranslatePoint(new Point(20, view.DefaultLineHeight * 3.5), window)!.Value;
            Point corner = new(window.Width - 2, window.Height - 2);

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Point(10, 10), RawInputModifiers.LeftMouseButton);

            for (int step = 0; step < 30; step++)
            {
                window.MouseMove(corner + new Point(step % 3, step % 2), RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                await Task.Delay(1);
            }

            int events = 0;
            view.ScrollOffsetChanged += (_, _) => events++;

            for (int nudge = 0; nudge < 6; nudge++)
            {
                window.Height = 300 + (nudge % 2);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                Dispatcher.UIThread.RunJobs();
            }

            // The view is where its scroll viewer says it is: there is nothing left to fight over.
            Assert.True(
                Math.Abs(view.ScrollOffset.X - scroll.Offset.X) <= 0.5 && Math.Abs(view.ScrollOffset.Y - scroll.Offset.Y) <= 0.5,
                $"the text view is at {view.ScrollOffset}, its scroll viewer at {scroll.Offset}");

            // A handful of events for six resizes, where the fight raised hundreds.
            Assert.True(events <= 24, $"{events} scroll events while the pointer held still");

            // And the drag did what it was for: the selection runs to the last row.
            int rows = sideBySide ? viewer.SideBySideRows.Count : viewer.UnifiedRows.Count;

            Assert.Equal(rows - 1, viewer.Render.Selection.End.Row);

            window.MouseUp(corner, MouseButton.Left);
            window.Close();
        });
    }

    [Fact]
    public void TheEditorsTextView_KeepsItsScrollRequestsReachable()
    {
        _fixture.Run(() =>
        {
            DiffTextEditor editor = new();

            Assert.IsType<DiffTextArea>(editor.TextArea);
            Assert.IsType<DiffTextView>(editor.TextArea.TextView);
        });
    }

    [Fact]
    public void BringingALineIntoView_StillScrollsInsideTheDocument()
    {
        _fixture.RunAsync(async () =>
        {
            (_, Window window, DiffTextEditor editor) = await ShowAsync(sideBySide: false, "UnifiedEditor", longLines: false);
            ScrollViewer scroll = editor.ScrollHost!;

            scroll.Offset = new Vector(0, 0);
            Settle(window);

            // The caret to a line well inside the document, as the keyboard or a search would put it.
            editor.TextArea.Caret.Offset = editor.Document.GetLineByNumber(40).Offset;
            editor.TextArea.Caret.BringCaretToView();
            Settle(window);

            TextView view = editor.TextArea.TextView;
            double line = view.GetVisualTopByDocumentLine(40);

            Assert.True(scroll.Offset.Y > 0, "bringing line 40 into view did not scroll");
            Assert.InRange(line, scroll.Offset.Y, scroll.Offset.Y + scroll.Viewport.Height);
            Assert.Equal(view.ScrollOffset.Y, scroll.Offset.Y, 1);

            window.Close();
        });
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<(DiffViewerViewModel Viewer, Window Window, DiffTextEditor Editor)> ShowAsync(
        bool sideBySide,
        string editorName,
        bool longLines)
    {
        StringBuilder patch = new("diff --git a/src/a.txt b/src/a.txt\n--- a/src/a.txt\n+++ b/src/a.txt\n@@ -1,60 +1,60 @@\n");
        string tail = longLines ? new string('x', 300) : string.Empty;

        for (int index = 0; index < 60; index++)
        {
            string number = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            patch.Append(index == 30
                ? $"-old line {number}{tail}\n+new line {number} changed{tail}\n"
                : $" line {number}{tail}\n");
        }

        RecordingDiffService diffs = new() { Patch = UnifiedDiffParser.Parse(patch.ToString()).Files[0] };
        DiffViewerViewModel viewer = new(
            diffs,
            new RecordingSystemInterop(),
            new SettingsService(
                new AppPaths(Path.Combine(Path.GetTempPath(), "enigma-drag-end-tests-" + Guid.NewGuid().ToString("N"))),
                NullLogger<SettingsService>.Instance),
            NullLogger<DiffViewerViewModel>.Instance);

        await viewer.ShowAsync(
            Repository,
            DiffTarget.Commit("abc123"),
            new ChangedFile { Path = "src/a.txt", ChangeKind = FileChangeKind.Modified, AddedLines = 1, RemovedLines = 1 });

        if (!sideBySide)
        {
            viewer.ShowUnifiedCommand.Execute(null);

            for (int attempt = 0; attempt < 200 && (diffs.PatchRequests.Count < 2 || viewer.IsBusy); attempt++)
            {
                await Task.Delay(5);
            }
        }

        DiffViewerView view = new() { DataContext = viewer };
        Window window = new() { Content = view, Width = 900, Height = 300 };
        window.Show();
        Settle(window);

        return (viewer, window, view.FindControl<DiffTextEditor>(editorName)!);
    }

    private static void Settle(Window window)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
