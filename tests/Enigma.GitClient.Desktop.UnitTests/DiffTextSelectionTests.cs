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
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Controls.Diff;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Selected text in the diff: what a selection covers row by row, the text it copies, and how an
/// editor's own selection is mirrored into it.
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

    // ---------------------------------------------------------------- the editors

    [Fact]
    public void AnEditorsSelection_IsMirroredInRowsAndCharacters()
    {
        _fixture.Run(() =>
        {
            DiffTextSelection selection = new();
            DiffTextEditor editor = Editor(DiffPane.Unified, selection);

            // From the "w" of "two" (row 2) to just after "three" (row 3).
            int start = editor.Document.GetLineByNumber(3).Offset + 1;
            int end = editor.Document.GetLineByNumber(4).EndOffset;

            editor.Select(start, end - start);

            Assert.True(selection.IsActive);
            Assert.Equal(DiffPane.Unified, selection.Pane);
            Assert.Equal(new DiffTextPosition(2, 1), selection.Start);
            Assert.Equal(new DiffTextPosition(3, 5), selection.End);

            // Nothing selected in the editor is nothing selected at all.
            editor.TextArea.ClearSelection();

            Assert.False(selection.IsActive);
        });
    }

    [Fact]
    public void APane_LetsItsSelectionGo_WhenAnotherPaneTakesTheSharedOne()
    {
        _fixture.Run(() =>
        {
            DiffTextSelection selection = new();
            DiffTextEditor left = Editor(DiffPane.Left, selection);
            DiffTextEditor right = Editor(DiffPane.Right, selection);
            Window window = new() { Content = new StackPanel { Children = { left, right } }, Width = 400, Height = 300 };
            window.Show();

            left.Select(left.Document.GetLineByNumber(2).Offset, 3);
            Assert.Equal(DiffPane.Left, selection.Pane);

            right.Select(right.Document.GetLineByNumber(3).Offset, 3);

            // One selection at a time: the right pane has it, and the left one shows none.
            Assert.Equal(DiffPane.Right, selection.Pane);
            Assert.True(selection.IsActive);
            Assert.True(left.TextArea.Selection.IsEmpty);
            Assert.False(right.TextArea.Selection.IsEmpty);

            // Clearing the shared selection — a new patch, a new rendering — clears the editor too.
            selection.Clear();

            Assert.True(right.TextArea.Selection.IsEmpty);

            window.Close();
        });
    }

    // ---------------------------------------------------------------- helpers

    private static DiffTextEditor Editor(DiffPane pane, DiffTextSelection selection)
    {
        DiffRenderOptions options = new();
        System.Collections.Generic.List<DiffRowViewModel> rows = [];
        FilePatch patch = UnifiedDiffParser.Parse(SamplePatch).Files[0];

        if (pane == DiffPane.Unified)
        {
            foreach (DiffRow row in DiffRowBuilder.BuildUnified(patch))
            {
                rows.Add(row.Kind == DiffRowKind.HunkHeader
                    ? new DiffRowViewModel(row.Hunk!, options) { Index = rows.Count }
                    : new DiffRowViewModel(new DiffCellViewModel(row.Line, options), null, null, options) { Index = rows.Count });
            }
        }
        else
        {
            foreach (DiffPairRow row in DiffRowBuilder.BuildSideBySide(patch))
            {
                rows.Add(row.Kind == DiffRowKind.HunkHeader
                    ? new DiffRowViewModel(row.Hunk!, options) { Index = rows.Count }
                    : new DiffRowViewModel(
                        null,
                        new DiffCellViewModel(row.Left, options, showOldNumber: true, showNewNumber: false),
                        new DiffCellViewModel(row.Right, options, showOldNumber: false, showNewNumber: true),
                        options)
                    {
                        Index = rows.Count,
                    });
            }
        }

        return new DiffTextEditor { Rows = rows, Pane = pane, TextSelection = selection, Height = 120 };
    }

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

    private static int IndexOf(System.Collections.Generic.IReadOnlyList<DiffRowViewModel> rows, Func<DiffRowViewModel, bool> match)
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

}
