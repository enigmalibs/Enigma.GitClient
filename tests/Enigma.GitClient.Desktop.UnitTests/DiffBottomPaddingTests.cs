using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
/// BUG-09AD: the horizontal scroll bar lies over the diff and grows under the pointer, and it used to
/// cover the last line even scrolled to the end. Every diff editor leaves the bar's room under it.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiffBottomPaddingTests
{
    private static readonly RepositoryHandle Repository = OperatingSystem.IsWindows()
        ? new RepositoryHandle(@"C:\src\client", @"C:\src\client\.git")
        : new RepositoryHandle("/src/client", "/src/client/.git");

    private readonly HeadlessAvaloniaFixture _fixture;

    public DiffBottomPaddingTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(false, "UnifiedEditor")]
    [InlineData(true, "LeftEditor")]
    [InlineData(true, "RightEditor")]
    public void ScrolledToTheEnd_TheLastLineEndsAboveTheHorizontalBar(bool sideBySide, string editorName)
    {
        _fixture.RunAsync(async () =>
        {
            (Window window, DiffTextEditor editor) = await ShowAsync(sideBySide, editorName);
            ScrollViewer scroll = editor.ScrollHost!;

            ScrollBar bar = scroll.GetVisualDescendants()
                .OfType<ScrollBar>()
                .Single(candidate => candidate.Orientation == Orientation.Horizontal);

            // The lines are wider than the pane, so the bar is there to grow.
            Assert.True(bar.IsVisible, "the horizontal bar is not shown");

            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Settle(window);

            TextView view = editor.TextArea.TextView;
            int last = editor.Document.LineCount;
            double bottom = view.GetVisualTopByDocumentLine(last) + view.DefaultLineHeight - view.VerticalOffset;
            double lastLineBottom = view.TranslatePoint(new Point(0, bottom), editor)!.Value.Y;
            double barTop = bar.TranslatePoint(new Point(0, 0), editor)!.Value.Y;

            Assert.True(
                lastLineBottom <= barTop + 0.5,
                $"the last line ends at {lastLineBottom}, under the bar that starts at {barTop}");

            window.Close();
        });
    }

    [Fact]
    public void TheRoomLeft_IsTheThemesScrollBarSize()
    {
        _fixture.RunAsync(async () =>
        {
            (Window window, DiffTextEditor editor) = await ShowAsync(sideBySide: false, "UnifiedEditor");
            ScrollViewer scroll = editor.ScrollHost!;

            Assert.True(editor.TryFindResource(DiffTextEditor.ScrollBarSizeKey, editor.ActualThemeVariant, out object? size));
            Assert.Equal((double)size!, scroll.Padding.Bottom);

            // And that is as much as the bar takes once it has grown.
            ScrollBar bar = scroll.GetVisualDescendants()
                .OfType<ScrollBar>()
                .Single(candidate => candidate.Orientation == Orientation.Horizontal);

            Assert.True(scroll.Padding.Bottom >= bar.Bounds.Height, $"{scroll.Padding.Bottom} of room for a {bar.Bounds.Height} bar");
            Assert.Equal(0, scroll.Padding.Left);
            Assert.Equal(0, scroll.Padding.Top);
            Assert.Equal(0, scroll.Padding.Right);

            window.Close();
        });
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<(Window Window, DiffTextEditor Editor)> ShowAsync(bool sideBySide, string editorName)
    {
        StringBuilder patch = new("diff --git a/src/a.txt b/src/a.txt\n--- a/src/a.txt\n+++ b/src/a.txt\n@@ -1,60 +1,60 @@\n");
        string tail = new('x', 300);

        for (int index = 0; index < 60; index++)
        {
            string number = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            patch.Append(index == 59
                ? $"-old line {number}{tail}\n+new line {number} changed{tail}\n"
                : $" line {number}{tail}\n");
        }

        RecordingDiffService diffs = new() { Patch = UnifiedDiffParser.Parse(patch.ToString()).Files[0] };
        DiffViewerViewModel viewer = new(
            diffs,
            new RecordingSystemInterop(),
            new SettingsService(
                new AppPaths(Path.Combine(Path.GetTempPath(), "enigma-bottom-padding-tests-" + Guid.NewGuid().ToString("N"))),
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

        return (window, view.FindControl<DiffTextEditor>(editorName)!);
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
