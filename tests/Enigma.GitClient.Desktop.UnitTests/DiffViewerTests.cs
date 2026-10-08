using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Controls;
using Enigma.GitClient.Desktop.Controls.Diff;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Enigma.GitClient.Desktop.Views.Pages;
using Enigma.GitClient.Desktop.Views.Panels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Drives the diff viewer: the two shapes, the options that go back to git, the messages that stand
/// in for a patch, copying, and what the rows actually draw.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiffViewerTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public DiffViewerTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

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

    /// <summary>
    /// A patch whose old side carries a line far wider than any pane, and whose new side does not:
    /// the shape the overlap was reported on.
    /// </summary>
    private static readonly string LongLinePatch =
        "diff --git a/src/wide.txt b/src/wide.txt\n" +
        "index 1111111..2222222 100644\n" +
        "--- a/src/wide.txt\n" +
        "+++ b/src/wide.txt\n" +
        "@@ -1,2 +1,2 @@\n" +
        "-" + new string('x', 400) + "\n" +
        "+short\n" +
        " tail\n";

    private const string TabbedPatch =
        "diff --git a/src/tabs.txt b/src/tabs.txt\n" +
        "index 1111111..2222222 100644\n" +
        "--- a/src/tabs.txt\n" +
        "+++ b/src/tabs.txt\n" +
        "@@ -1,1 +1,1 @@\n" +
        "-\tindented\n" +
        "+\tindented too\n";

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

    private static FilePatch Parse(string patch)
        => UnifiedDiffParser.Parse(patch).Files[0];

    private sealed record Harness(
        DiffViewerViewModel Viewer,
        RecordingDiffService Diffs,
        RecordingSystemInterop Interop);

    /// <summary>
    /// A settings store on a throwaway directory: the viewer takes its defaults from one, and no
    /// test here is about the preferences.
    /// </summary>
    private static ISettingsService Settings()
        => new SettingsService(
            new AppPaths(Path.Combine(Path.GetTempPath(), "enigma-diff-tests-" + Guid.NewGuid().ToString("N"))),
            NullLogger<SettingsService>.Instance);

    private static Harness Build(FilePatch? patch = null)
    {
        RecordingDiffService diffs = new() { Patch = patch ?? Parse(SamplePatch) };
        RecordingSystemInterop interop = new();

        return new Harness(
            new DiffViewerViewModel(diffs, interop, Settings(), NullLogger<DiffViewerViewModel>.Instance),
            diffs,
            interop);
    }

    private static async Task<Harness> ShownAsync(FilePatch? patch = null)
    {
        Harness harness = Build(patch);
        await harness.Viewer.ShowAsync(Repository, DiffTarget.Commit("abc123"), File);
        return harness;
    }

    /// <summary>
    /// The same, switched to the unified rendering — which is the one that still asks git for the
    /// reader's own context, and so the one the context tests are about.
    /// </summary>
    private static async Task<Harness> UnifiedAsync(FilePatch? patch = null)
    {
        Harness harness = await ShownAsync(patch);

        harness.Viewer.ShowUnifiedCommand.Execute(null);
        await WaitForRequestsAsync(harness, 2);

        return harness;
    }

    // ---------------------------------------------------------------- loading

    [Fact]
    public void Viewer_InvitesASelectionBeforeThereIsOne()
    {
        DiffViewerViewModel viewer = Build().Viewer;

        Assert.True(viewer.HasMessage);
        Assert.False(viewer.HasPatch);
        Assert.Contains("Select a file", viewer.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Viewer_OpensSideBySide()
    {
        // The shape a fresh install gets, straight from the preferences: two columns is what a
        // reader comparing two revisions is looking for, and the unified shape is one click away.
        Assert.True(Build().Viewer.IsSideBySide);
    }

    [Fact]
    public void Viewer_BuildsBothRenderingsOfTheSamePatch()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            Assert.True(harness.Viewer.HasPatch);
            Assert.False(harness.Viewer.HasMessage);
            Assert.Equal("src/app.txt", harness.Viewer.Title);
            Assert.Equal("+3 −2", harness.Viewer.Summary);

            // Unified: the band plus every line git wrote.
            Assert.Equal(7, harness.Viewer.UnifiedRows.Count(row => row.IsLine));

            // Side by side: the same change, paired, with one filler row.
            Assert.Equal(5, harness.Viewer.SideBySideRows.Count(row => row.IsLine));
            Assert.Single(harness.Viewer.SideBySideRows, row => row.Left?.IsFiller == true);
        });
    }

    [Fact]
    public void Viewer_NumbersBothSidesOfTheUnifiedRendering()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            DiffRowViewModel last = harness.Viewer.UnifiedRows[^1];

            Assert.Equal("4", last.Single!.OldNumber);
            Assert.Equal("5", last.Single.NewNumber);
            Assert.Equal(string.Empty, last.Single.Marker);
            Assert.True(last.Single.IsContext);
        });
    }

    [Fact]
    public void Viewer_ShowsOnlyItsOwnNumberOnEachSide()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            DiffRowViewModel row = harness.Viewer.SideBySideRows.First(candidate => candidate.Right?.IsAdded == true);

            Assert.Equal(string.Empty, row.Left!.NewNumber);
            Assert.Equal(string.Empty, row.Right!.OldNumber);
            Assert.Equal("−", row.Left.Marker);
            Assert.Equal("+", row.Right.Marker);
        });
    }

    [Fact]
    public void Viewer_SwitchesShapeAndRereadsForIt()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            // Side by side is what a fresh store opens on, and it shows the whole file.
            Assert.True(harness.Viewer.IsSideBySide);
            Assert.Equal(DiffViewerViewModel.WholeFileContext, harness.Diffs.PatchRequests[^1].ContextLines);

            harness.Viewer.ShowUnifiedCommand.Execute(null);
            await WaitForRequestsAsync(harness, 2);

            Assert.True(harness.Viewer.IsUnified);
            Assert.False(harness.Viewer.IsSideBySide);

            // The two shapes are two questions for git, not one answer drawn twice: unified is
            // "what changed", so it goes back to the reader's own context.
            Assert.Equal(3, harness.Diffs.PatchRequests[^1].ContextLines);

            harness.Viewer.ShowSideBySideCommand.Execute(null);
            await WaitForRequestsAsync(harness, 3);

            Assert.True(harness.Viewer.IsSideBySide);
            Assert.Equal(DiffViewerViewModel.WholeFileContext, harness.Diffs.PatchRequests[^1].ContextLines);
        });
    }

    [Fact]
    public void Viewer_ClearsWhenTheSelectionGoesAway()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            harness.Viewer.Clear();

            Assert.False(harness.Viewer.HasPatch);
            Assert.True(harness.Viewer.HasMessage);
            Assert.Empty(harness.Viewer.UnifiedRows);
            Assert.Empty(harness.Viewer.SideBySideRows);
        });
    }

    // ---------------------------------------------------------------- what goes back to git

    [Fact]
    public void Viewer_AsksForTheWholeFileSideBySide()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            // Two views of one file read as two views of one file only when both of them are the
            // file; the changed parts alone are the other question.
            Assert.True(harness.Viewer.IsSideBySide);
            Assert.Equal(
                DiffViewerViewModel.WholeFileContext,
                Assert.Single(harness.Diffs.PatchRequests).ContextLines);
            Assert.Equal("src/app.txt", Assert.Single(harness.Diffs.PatchPaths));

            // The reader's own preference is untouched underneath; it is what unified goes back to.
            Assert.Equal(3, harness.Viewer.ContextLines);
        });
    }

    [Fact]
    public void Viewer_AsksForTheDefaultContextUnified()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await UnifiedAsync();

            Assert.Equal(3, harness.Diffs.PatchRequests[^1].ContextLines);
            Assert.Equal("src/app.txt", harness.Diffs.PatchPaths[^1]);
        });
    }

    [Fact]
    public void Viewer_CannotExpandWhatIsAlreadyTheWholeFile()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            Assert.True(harness.Viewer.IsSideBySide);
            Assert.False(harness.Viewer.ExpandContextCommand.CanExecute(null));
            Assert.False(harness.Viewer.ExpandAllContextCommand.CanExecute(null));

            harness.Viewer.ShowUnifiedCommand.Execute(null);
            await WaitForRequestsAsync(harness, 2);

            // Unified shows the changed parts, so there is something left to widen.
            Assert.True(harness.Viewer.ExpandContextCommand.CanExecute(null));
            Assert.True(harness.Viewer.ExpandAllContextCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Viewer_ExpandingContextRereadsWithAWiderUnified()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await UnifiedAsync();

            await harness.Viewer.ExpandContextCommand.ExecuteAsync(null);

            // Context is git's to produce, not the renderer's to invent — the only way to show more
            // of the file is to ask for it.
            Assert.Equal(3 * DiffViewerViewModel.ExpansionFactor, harness.Diffs.PatchRequests[^1].ContextLines);
            Assert.Equal(3 * DiffViewerViewModel.ExpansionFactor, harness.Viewer.ContextLines);
        });
    }

    [Fact]
    public void Viewer_ExpandsTheContextFromTheBandItself()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await UnifiedAsync();

            DiffRowViewModel band = harness.Viewer.UnifiedRows[0];

            Assert.True(band.IsHunkHeader);
            Assert.NotNull(band.ExpandContextCommand);

            await band.ExpandContextCommand!.ExecuteAsync(null);

            Assert.Equal(3 * DiffViewerViewModel.ExpansionFactor, harness.Diffs.PatchRequests[^1].ContextLines);
        });
    }

    [Fact]
    public void Viewer_CanAskForTheWholeFileAsContext()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await UnifiedAsync();

            await harness.Viewer.ExpandAllContextCommand.ExecuteAsync(null);

            Assert.Equal(DiffViewerViewModel.WholeFileContext, harness.Diffs.PatchRequests[^1].ContextLines);
            Assert.False(harness.Viewer.ExpandContextCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Viewer_StartsEachFileBackAtTheDefaultContext()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await UnifiedAsync();

            await harness.Viewer.ExpandAllContextCommand.ExecuteAsync(null);
            await harness.Viewer.ShowAsync(Repository, DiffTarget.Commit("abc123"), File);

            Assert.Equal(3, harness.Viewer.ContextLines);
            Assert.Equal(3, harness.Diffs.PatchRequests[^1].ContextLines);
        });
    }

    [Fact]
    public void Viewer_IgnoringWhitespaceIsAQuestionForGit()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            harness.Viewer.IgnoreAllWhitespace = true;
            await WaitForRequestsAsync(harness, 2);

            Assert.True(harness.Diffs.PatchRequests[^1].IgnoreAllWhitespace);

            harness.Viewer.IgnoreBlankLines = true;
            await WaitForRequestsAsync(harness, 3);

            Assert.True(harness.Diffs.PatchRequests[^1].IgnoreBlankLines);
        });
    }

    [Fact]
    public void Viewer_ShowingWhitespaceIsAQuestionForTheRenderer()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            harness.Viewer.Render.ShowWhitespace = true;
            harness.Viewer.Render.WrapLines = true;

            // Neither changes what git produced, so neither costs a read.
            Assert.Single(harness.Diffs.PatchRequests);
            Assert.True(harness.Viewer.UnifiedRows[1].Options.ShowWhitespace);
        });
    }

    // ---------------------------------------------------------------- what stands in for a patch

    [Fact]
    public void Viewer_SaysSoForABinaryFile()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(
                new FilePatch(null, "logo.png", FileChangeKind.Modified, [], isBinary: true));

            Assert.True(harness.Viewer.HasMessage);
            Assert.False(harness.Viewer.HasPatch);
            Assert.Contains("binary", harness.Viewer.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Viewer_DrawsNoRenderingAroundAMessage_InEitherShape()
    {
        _fixture.RunAsync(async () =>
        {
            // Nothing picked yet.
            DiffViewerViewModel empty = Build().Viewer;
            Assert.False(empty.ShowsUnified);
            Assert.False(empty.ShowsSideBySide);

            Harness binary = await ShownAsync(
                new FilePatch(null, "logo.png", FileChangeKind.Modified, [], isBinary: true));

            Assert.False(binary.Viewer.ShowsSideBySide);
            Assert.False(binary.Viewer.ShowsUnified);

            // The other shape says the same thing, alone too.
            binary.Viewer.ShowUnifiedCommand.Execute(null);
            await WaitForRequestsAsync(binary, 2);

            Assert.True(binary.Viewer.IsUnified);
            Assert.False(binary.Viewer.ShowsUnified);
            Assert.False(binary.Viewer.ShowsSideBySide);
        });
    }

    [Fact]
    public void Viewer_DrawsTheChosenRenderingOfAPatch_AndNoneOnceCleared()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            Assert.True(harness.Viewer.ShowsSideBySide);
            Assert.False(harness.Viewer.ShowsUnified);

            harness.Viewer.ShowUnifiedCommand.Execute(null);
            await WaitForRequestsAsync(harness, 2);

            Assert.True(harness.Viewer.ShowsUnified);
            Assert.False(harness.Viewer.ShowsSideBySide);

            harness.Viewer.Clear();

            Assert.False(harness.Viewer.ShowsUnified);
            Assert.False(harness.Viewer.ShowsSideBySide);
        });
    }

    [Fact]
    public void Viewer_SaysSoForASubmodule()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(new FilePatch(
                "vendor/lib",
                "vendor/lib",
                FileChangeKind.Modified,
                [],
                oldMode: "160000",
                newMode: "160000"));

            Assert.Contains("submodule", harness.Viewer.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Viewer_SaysSoForARenameThatChangedNothing()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(
                new FilePatch("old.txt", "new.txt", FileChangeKind.Renamed, []));

            Assert.Contains("moved", harness.Viewer.Message, StringComparison.Ordinal);
            Assert.Equal("← old.txt", harness.Viewer.Subtitle);
            Assert.True(harness.Viewer.HasSubtitle);
        });
    }

    [Fact]
    public void Viewer_SaysSoForAModeChange()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(new FilePatch(
                "run.sh",
                "run.sh",
                FileChangeKind.ModeChanged,
                [],
                oldMode: "100644",
                newMode: "100755"));

            Assert.Contains("mode", harness.Viewer.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Viewer_SaysSoWhenGitProducedNothingAtAll()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = Build();
            harness.Diffs.Patch = null;

            await harness.Viewer.ShowAsync(Repository, DiffTarget.Commit("abc123"), File);

            Assert.True(harness.Viewer.HasMessage);
            Assert.False(harness.Viewer.HasPatch);
        });
    }

    // ---------------------------------------------------------------- very large files

    [Fact]
    public void Viewer_OffersToShowATruncatedFileAnyway()
    {
        _fixture.RunAsync(async () =>
        {
            FilePatch truncated = Parse(SamplePatch);
            FilePatch marked = new(
                truncated.OldPath,
                truncated.NewPath,
                truncated.ChangeKind,
                truncated.Hunks,
                isTruncated: true);

            Harness harness = await ShownAsync(marked);

            Assert.True(harness.Viewer.IsTruncated);
            Assert.True(harness.Viewer.ShowAnywayCommand.CanExecute(null));
            Assert.Equal(
                DiffParseOptions.Default.MaxLinesPerFile,
                harness.Diffs.PatchRequests[0].Parsing.MaxLinesPerFile);

            await harness.Viewer.ShowAnywayCommand.ExecuteAsync(null);

            Assert.Equal(int.MaxValue, harness.Diffs.PatchRequests[^1].Parsing.MaxLinesPerFile);
        });
    }

    [Fact]
    public void Viewer_BuildsAFiveThousandLinePatchQuickly()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = Build(Parse(BuildLargePatch(5000)));

            Stopwatch stopwatch = Stopwatch.StartNew();
            await harness.Viewer.ShowAsync(Repository, DiffTarget.Commit("abc123"), File);
            stopwatch.Stop();

            Assert.Equal(5001, harness.Viewer.UnifiedRows.Count);
            Assert.True(
                stopwatch.ElapsedMilliseconds < 500,
                $"projecting the patch took {stopwatch.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} ms");
        });
    }

    [Fact]
    public void Viewer_LaysOutOnlyTheLinesOnScreen()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = Build(Parse(BuildLargePatch(5000)));
            await harness.Viewer.ShowAsync(Repository, DiffTarget.Commit("abc123"), File);

            DiffViewerView view = new() { DataContext = harness.Viewer };
            Window window = new() { Content = view, Width = 900, Height = 600 };
            window.Show();

            for (int attempt = 0; attempt < 10; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                Dispatcher.UIThread.RunJobs();
            }

            // A 600px window holds a few dozen lines. Anything near five thousand means a side laid
            // the whole file out, which is what freezes a viewer on a generated file.
            foreach (string name in new[] { "LeftEditor", "RightEditor" })
            {
                DiffTextEditor editor = view.FindControl<DiffTextEditor>(name)!;

                Assert.Equal(harness.Viewer.SideBySideRows.Count, editor.Document.LineCount);
                Assert.InRange(editor.TextArea.TextView.VisualLines.Count, 1, 200);
            }

            window.Content = null;
            window.Close();
        });
    }

    // ---------------------------------------------------------------- copying

    [Fact]
    public void Viewer_CopiesTheWholePatchWithItsMarkers()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            await harness.Viewer.CopyPatchCommand.ExecuteAsync(null);

            string copied = Assert.Single(harness.Interop.Copied);

            Assert.Contains("@@ -1,4 +1,5 @@", copied, StringComparison.Ordinal);
            Assert.Contains("-two\n", copied, StringComparison.Ordinal);
            Assert.Contains("+four\n", copied, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Viewer_CopiesASelectionAsPlainCode()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await UnifiedAsync();
            IReadOnlyList<DiffRowViewModel> rows = harness.Viewer.UnifiedRows;

            Assert.False(harness.Viewer.CopyCommand.CanExecute(null));

            int first = IndexOf(rows, row => row.Single?.Text == "two changed");
            int last = IndexOf(rows, row => row.Single?.Text == "four");

            harness.Viewer.Render.Selection.Begin(DiffPane.Unified, new DiffTextPosition(first, 0));
            harness.Viewer.Render.Selection.ExtendTo(new DiffTextPosition(last, 4));

            Assert.True(harness.Viewer.CopyCommand.CanExecute(null));

            await harness.Viewer.CopyCommand.ExecuteAsync(null);

            // The code as it reads: no markers, no numbers.
            Assert.Equal("two changed\nthree changed\nfour", Assert.Single(harness.Interop.Copied));
        });
    }

    [Fact]
    public void Viewer_CopiesASelectionInReadingOrderWhicheverWayItWasDragged()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await UnifiedAsync();
            IReadOnlyList<DiffRowViewModel> rows = harness.Viewer.UnifiedRows;

            int first = IndexOf(rows, row => row.Single?.Text == "two changed");
            int last = IndexOf(rows, row => row.Single?.Text == "four");

            // Dragged from the end back up to the start.
            harness.Viewer.Render.Selection.Begin(DiffPane.Unified, new DiffTextPosition(last, 4));
            harness.Viewer.Render.Selection.ExtendTo(new DiffTextPosition(first, 0));

            await harness.Viewer.CopyCommand.ExecuteAsync(null);

            Assert.Equal("two changed\nthree changed\nfour", Assert.Single(harness.Interop.Copied));
        });
    }

    [Fact]
    public void Viewer_CopiesAContextLineOnceFromTheSideBySideRendering()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();
            harness.Viewer.ShowSideBySideCommand.Execute(null);

            int row = IndexOf(harness.Viewer.SideBySideRows, candidate => candidate.Left?.IsContext == true);

            harness.Viewer.Render.Selection.Begin(DiffPane.Left, new DiffTextPosition(row, 0));
            harness.Viewer.Render.Selection.ExtendTo(new DiffTextPosition(row, 3));

            await harness.Viewer.CopyCommand.ExecuteAsync(null);

            // The same line appears on both sides; a selection is in one of them, so it is copied
            // once.
            Assert.Equal("one", Assert.Single(harness.Interop.Copied));
        });
    }

    [Fact]
    public void Viewer_DropsTheSelectionWhenTheShapeChanges()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await UnifiedAsync();

            harness.Viewer.Render.Selection.Begin(DiffPane.Unified, new DiffTextPosition(1, 0));
            harness.Viewer.Render.Selection.ExtendTo(new DiffTextPosition(1, 3));
            harness.Viewer.ShowSideBySideCommand.Execute(null);

            // Its rows are counted in the other shape's rows; keeping it would copy rows that are no
            // longer on screen.
            Assert.False(harness.Viewer.Render.Selection.IsActive);
            Assert.False(harness.Viewer.CopyCommand.CanExecute(null));
        });
    }

    private static int IndexOf(IReadOnlyList<DiffRowViewModel> rows, Func<DiffRowViewModel, bool> match)
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

    // ---------------------------------------------------------------- the conflict page's line control

    [Theory]
    [InlineData("\tvalue", "    value")]
    [InlineData("a\tb", "a   b")]
    [InlineData("abcd\te", "abcd    e")]
    [InlineData("no tabs", "no tabs")]
    public void DiffLineText_ExpandsTabsToTheNextTabStop(string text, string expected)
    {
        // Two lines differing only after a tab have to line up, which they only do if a tab always
        // lands on the same column.
        Assert.Equal(expected, DiffLineText.Expand(text, 4, showWhitespace: false).Expanded);
    }

    [Fact]
    public void DiffLineText_ShowsWhitespaceAsSymbols()
    {
        (string expanded, _) = DiffLineText.Expand("\ta b", 4, showWhitespace: true);

        Assert.Equal("\u2192   a\u00b7b", expanded);
    }

    [Fact]
    public void DiffLineText_MapsEveryExpandedCharacterBackToItsSource()
    {
        (string expanded, int[] map) = DiffLineText.Expand("\tab", 4, showWhitespace: false);

        Assert.Equal(expanded.Length, map.Length);

        // The four columns a tab produced all came from the one tab character, so a word-level
        // segment starting at "a" still lands on "a".
        Assert.Equal([0, 0, 0, 0, 1, 2], map);
    }

    [Fact]
    public void DiffLineText_TreatsAnImpossibleTabWidthAsOne()
        => Assert.Equal("a b", DiffLineText.Expand("a\tb", 0, showWhitespace: false).Expanded);

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void Viewer_DrawsBothRenderings()
    {
        _fixture.RunAsync(async () =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                Harness harness = await ShownAsync();
                harness.Viewer.ShowUnifiedCommand.Execute(null);

                DiffViewerView view = new() { DataContext = harness.Viewer };

                IReadOnlyList<string> unified = RenderAndReadText(view, "diff-unified.png");

                Assert.Contains("src/app.txt", unified);
                Assert.Contains("+3 −2", unified);

                // The unified rendering is an editor: its lines are a document, and the band, the
                // numbers and the markers are drawn around them rather than being text blocks.
                DiffTextEditor editor = view.FindControl<DiffTextEditor>("UnifiedEditor")!;

                Assert.Contains("two changed", editor.Document.Text, StringComparison.Ordinal);
                Assert.Equal("@@ -1,4 +1,5 @@", editor.Diff.Lines[0].HeaderText);
                Assert.Contains(editor.Diff.Lines, line => line.Marker == "+");
                Assert.Contains(editor.Diff.Lines, line => line.Marker == "−");

                harness.Viewer.ShowSideBySideCommand.Execute(null);

                IReadOnlyList<string> side = RenderAndReadText(view, "diff-side-by-side.png");

                Assert.Contains("src/app.txt", side);

                // Two editors, the old file and the new, level row for row.
                DiffTextEditor left = view.FindControl<DiffTextEditor>("LeftEditor")!;
                DiffTextEditor right = view.FindControl<DiffTextEditor>("RightEditor")!;

                Assert.Equal("@@ -1,4 +1,5 @@", left.Diff.Lines[0].HeaderText);
                Assert.Contains("three", left.Document.Text, StringComparison.Ordinal);
                Assert.Contains("three changed", right.Document.Text, StringComparison.Ordinal);
                Assert.Equal(left.Document.LineCount, right.Document.LineCount);
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void Viewer_DrawsItsMessageWhenThereIsNoPatch()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(
                new FilePatch(null, "logo.png", FileChangeKind.Modified, [], isBinary: true));

            IReadOnlyList<string> texts = RenderAndReadText(
                new DiffViewerView { DataContext = harness.Viewer },
                "diff-binary.png");

            Assert.Contains(texts, text => text.Contains("binary", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Viewer_DrawsAMessageAlone_AndAPatchWithoutIt()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(
                new FilePatch(null, "logo.png", FileChangeKind.Modified, [], isBinary: true));

            DiffViewerView view = new() { DataContext = harness.Viewer };
            Window window = new() { Content = view, Width = 900, Height = 420 };
            window.Show();

            try
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                // No editor, gutter or map around the message: nothing of a diff that is not there.
                EmptyState message = view.GetVisualDescendants().OfType<EmptyState>().Single();
                Assert.True(message.IsEffectivelyVisible);
                Assert.All(view.GetVisualDescendants().OfType<DiffTextEditor>(), editor => Assert.False(editor.IsEffectivelyVisible));
                Assert.All(view.GetVisualDescendants().OfType<DiffMinimap>(), map => Assert.False(map.IsEffectivelyVisible));

                // A file with lines: its rendering, and no message.
                harness.Diffs.Patch = Parse(SamplePatch);
                await harness.Viewer.ShowAsync(Repository, DiffTarget.Commit("abc123"), File);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.False(message.IsEffectivelyVisible);
                Assert.Contains(view.GetVisualDescendants().OfType<DiffTextEditor>(), editor => editor.IsEffectivelyVisible);
            }
            finally
            {
                window.Content = null;
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Viewer_GivesEveryChangedLineItsWordHighlight(bool sideBySide)
    {
        _fixture.RunAsync(async () =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                Harness harness = await ShownAsync();

                if (sideBySide)
                {
                    harness.Viewer.ShowSideBySideCommand.Execute(null);
                }
                else
                {
                    harness.Viewer.ShowUnifiedCommand.Execute(null);
                }

                DiffViewerView view = new() { DataContext = harness.Viewer };
                Window window = new() { Content = view, Width = 900, Height = 420 };
                window.Show();

                for (int attempt = 0; attempt < 10; attempt++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                    Dispatcher.UIThread.RunJobs();
                }

                // The editor paints the word tint behind the changed stretch of a changed line, and
                // none at all behind context.
                DiffTextEditor added = view.FindControl<DiffTextEditor>(sideBySide ? "RightEditor" : "UnifiedEditor")!;
                DiffTextEditor removed = view.FindControl<DiffTextEditor>(sideBySide ? "LeftEditor" : "UnifiedEditor")!;

                DiffDocumentLine addedLine = LineOf(added, "two changed");
                DiffDocumentLine contextLine = LineOf(added, "one");

                Assert.Equal(
                    Colour("DiffAddedWordColor", ThemeVariant.Dark),
                    Assert.IsType<SolidColorBrush>(added.AddedWordBrush).Color);
                Assert.Equal(
                    Colour("DiffRemovedWordColor", ThemeVariant.Dark),
                    Assert.IsType<SolidColorBrush>(removed.RemovedWordBrush).Color);

                Assert.Equal(DiffDocumentLineKind.Added, addedLine.Kind);
                Assert.Contains(addedLine.Segments, segment => segment.IsChanged);
                Assert.Equal(DiffDocumentLineKind.Context, contextLine.Kind);
                Assert.DoesNotContain(contextLine.Segments, segment => segment.IsChanged);

                window.Content = null;
                window.Close();
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    private static DiffDocumentLine LineOf(DiffTextEditor editor, string text)
        => editor.Diff.Lines[Array.IndexOf(editor.Document.Text.Split('\n'), text)];

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void DiffColours_AreDistinctAndKeepTheTextReadable(string variantName)
    {
        _fixture.Run(() =>
        {
            ThemeVariant variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = variant;

            try
            {
                Color added = Colour("DiffAddedLineColor", variant);
                Color removed = Colour("DiffRemovedLineColor", variant);
                Color addedWord = Colour("DiffAddedWordColor", variant);
                Color removedWord = Colour("DiffRemovedWordColor", variant);
                Color foreground = Brush("EnigmaForegroundBrush", variant);

                // An added line that looks like a removed one is worse than no colour at all.
                Assert.Equal(4, new HashSet<Color> { added, removed, addedWord, removedWord }.Count);

                foreach ((string name, Color tint) in new[]
                {
                    ("added", added), ("removed", removed), ("added word", addedWord), ("removed word", removedWord),
                })
                {
                    double ratio = Contrast(foreground, tint);

                    Assert.True(
                        ratio >= 4.5,
                        $"{variantName}/{name}: contrast is only {ratio.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}:1");
                }

                // The word tint has to stand out from the line tint it sits on, or the word diff is
                // there in the model and invisible on screen.
                foreach ((string name, Color line, Color word) in new[]
                {
                    ("added", added, addedWord), ("removed", removed, removedWord),
                })
                {
                    double ratio = Contrast(line, word);

                    Assert.True(
                        ratio >= 1.6,
                        $"{variantName}/{name}: the word tint is only {ratio.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}:1 against its line");
                }
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void DiffTints_StandOutFromTheBackground_AndKeepTheirMarkersReadable(string variantName)
    {
        _fixture.Run(() =>
        {
            ThemeVariant variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = variant;

            try
            {
                Color background = Colour("EnigmaBackgroundColor", variant);

                foreach ((string name, Color line, Color marker) in new[]
                {
                    ("added", Colour("DiffAddedLineColor", variant), Colour("DiffAddedMarkerColor", variant)),
                    ("removed", Colour("DiffRemovedLineColor", variant), Colour("DiffRemovedMarkerColor", variant)),
                })
                {
                    // A changed line the eye cannot tell from an unchanged one is the whole complaint:
                    // 1.04:1 was the old tint in both themes.
                    double visible = Contrast(background, line);

                    Assert.True(
                        visible >= 1.12,
                        $"{variantName}/{name}: the line tint is only {visible.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}:1 against the background");

                    // The + or − is text sitting on that tint.
                    double readable = Contrast(marker, line);

                    Assert.True(
                        readable >= 4.5,
                        $"{variantName}/{name}: the marker is only {readable.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}:1 on its line");
                }
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void StatusChips_AreDistinctVisibleAndReadable(string variantName)
    {
        _fixture.Run(() =>
        {
            ThemeVariant variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = variant;

            try
            {
                Color background = Colour("EnigmaBackgroundColor", variant);
                Color letter = Colour("DiffStatusForegroundColor", variant);

                (string Name, Color Fill)[] chips =
                [
                    ("added", Colour("DiffStatusAddedColor", variant)),
                    ("modified", Colour("DiffStatusModifiedColor", variant)),
                    ("deleted", Colour("DiffStatusDeletedColor", variant)),
                    ("renamed", Colour("DiffStatusRenamedColor", variant)),
                    ("conflicted", Colour("DiffStatusConflictedColor", variant)),
                ];

                Assert.Equal(chips.Length, chips.Select(chip => chip.Fill).Distinct().Count());

                foreach ((string name, Color fill) in chips)
                {
                    // The letter (A, M, D, R, U) is what still reads without colour.
                    double readable = Contrast(letter, fill);

                    Assert.True(
                        readable >= 4.5,
                        $"{variantName}/{name}: the chip's letter is only {readable.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}:1");

                    double visible = Contrast(background, fill);

                    Assert.True(
                        visible >= 1.5,
                        $"{variantName}/{name}: the chip is only {visible.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}:1 against the background");
                }
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    // ---------------------------------------------------------------- end to end

    [Fact]
    public void HistoryPage_ShowsTheRealDiffOfTheFileTheUserPicked()
    {
        _fixture.RunAsync(async () =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                using TestServices services = TestServices.Build(useRealRefReader: true);
                RepositoryHandle repository = await BuildRepositoryAsync(services);
                await services.Get<IRepositoryContext>().OpenAsync(repository);

                HistoryPageViewModel page = services.Get<HistoryPageViewModel>();
                await page.ReloadAsync();

                // The dialog is what draws the viewer, and it is asked for rather than implied by
                // the selection.
                page.RowCommands.ShowChanges.Execute(page.Rows.Single(row => row.Subject == "Edit the file"));
                await WaitUntilAsync(() => page.Files.FileCount > 0);

                Assert.True(page.Files.SelectPath("src/app.txt"));
                await WaitUntilAsync(() => page.Diff.HasPatch);

                // Everything from here came out of the repository the test built: git produced the
                // patch, the parser turned it into lines, and the projection put them in rows.
                Assert.Equal("src/app.txt", page.Diff.Title);
                Assert.Equal("+1 −1", page.Diff.Summary);
                Assert.Contains(page.Diff.UnifiedRows, row => row.Single?.Text == "two edited");
                Assert.Contains(page.Diff.UnifiedRows, row => row.Single?.Text == "two");

                HistoryPageView view = services.Get<HistoryPageView>();
                view.DataContext = page;

                IReadOnlyList<string> texts = RenderAndReadText(view, "history-page-diff.png", 1280, 760);

                Assert.Contains("src/app.txt", texts);

                // The patch is in the editors on screen: the band and the code lines both, to prove
                // the pane really rendered the patch.
                DiffTextEditor[] editors = [.. view.GetVisualDescendants()
                    .OfType<DiffTextEditor>()
                    .Where(editor => editor.IsEffectivelyVisible)];

                Assert.NotEmpty(editors);
                Assert.Contains(editors, editor => editor.Diff.Lines.Any(line => line.HeaderText.StartsWith("@@", StringComparison.Ordinal)));
                Assert.Contains(editors, editor => editor.Document.Text.Split('\n').Contains("two edited"));
                Assert.Contains(editors, editor => editor.Document.Text.Split('\n').Contains("two"));
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void Viewer_ShowsTheWholeFileSideBySideAndTheChangeAloneUnified()
    {
        _fixture.RunAsync(async () =>
        {
            using TestServices services = TestServices.Build(useRealRefReader: true);

            string root = Path.Combine(services.ConfigurationRoot, "workspace");
            Directory.CreateDirectory(root);

            RepositoryHandle repository = await services.Get<IRepositoryService>()
                .InitAsync(Path.Combine(root, "whole"), "main");

            // A long file with one line changed in the middle: three lines of context show a
            // handful of rows, the whole file shows every one of them.
            const int lines = 200;

            string before = string.Join('\n', Enumerable.Range(0, lines).Select(line => $"line {line}")) + "\n";
            string after = string.Join(
                '\n',
                Enumerable.Range(0, lines).Select(line => line == 100 ? "line one hundred, edited" : $"line {line}")) + "\n";

            WriteFile(repository, "src/long.txt", before);
            await GitAsync(repository, "add", "--all");
            await GitAsync(repository, "commit", "-m", "Add the long file");

            WriteFile(repository, "src/long.txt", after);
            await GitAsync(repository, "add", "--all");
            await GitAsync(repository, "commit", "-m", "Edit one line of it");

            await services.Get<IRepositoryContext>().OpenAsync(repository);

            DiffViewerViewModel viewer = services.Get<DiffViewerViewModel>();

            await viewer.ShowAsync(
                repository,
                DiffTarget.Commit("HEAD"),
                new ChangedFile { Path = "src/long.txt", ChangeKind = FileChangeKind.Modified });

            // Side by side: every line of the file, on both sides. Filler rows only appear where
            // one side has no line, and this change replaces a line rather than adding one.
            Assert.True(viewer.IsSideBySide);

            int rows = viewer.SideBySideRows.Count(row => row.IsLine);

            Assert.Equal(lines, rows);
            Assert.Equal(lines, viewer.SideBySideRows.Count(row => row.Left?.Line is not null));
            Assert.Equal(lines, viewer.SideBySideRows.Count(row => row.Right?.Line is not null));

            // The first and last lines of the file are there, which three lines of context around
            // line 100 could never have shown.
            Assert.Contains(viewer.SideBySideRows, row => row.Left?.Text == "line 0");
            Assert.Contains(viewer.SideBySideRows, row => row.Left?.Text == $"line {lines - 1}");

            // Unified is the other question — what changed — and keeps the reader's context.
            viewer.ShowUnifiedCommand.Execute(null);

            for (int attempt = 0; attempt < 200 && viewer.UnifiedRows.Count(row => row.IsLine) >= lines; attempt++)
            {
                await Task.Delay(5);
            }

            int unified = viewer.UnifiedRows.Count(row => row.IsLine);

            Assert.True(
                unified < lines,
                $"unified showed {unified} of {lines} lines, so it is not showing the change alone");

            Assert.Contains(viewer.UnifiedRows, row => row.Single?.Text == "line one hundred, edited");
            Assert.DoesNotContain(viewer.UnifiedRows, row => row.Single?.Text == "line 0");
        });
    }

    private static async Task<RepositoryHandle> BuildRepositoryAsync(TestServices services)
    {
        string root = Path.Combine(services.ConfigurationRoot, "workspace");
        Directory.CreateDirectory(root);

        RepositoryHandle repository = await services.Get<IRepositoryService>()
            .InitAsync(Path.Combine(root, "diffs"), "main");

        WriteFile(repository, "src/app.txt", "one\ntwo\nthree\n");
        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "Add the file");

        WriteFile(repository, "src/app.txt", "one\ntwo edited\nthree\n");
        await GitAsync(repository, "add", "--all");
        await GitAsync(repository, "commit", "-m", "Edit the file");

        return repository;
    }

    private static void WriteFile(RepositoryHandle repository, string relativePath, string content)
    {
        string full = Path.Combine(repository.WorkTreePath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
    }

    private static Task GitAsync(RepositoryHandle repository, params string[] arguments)
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
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? Task.CompletedTask
            : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 400 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }
    }

    // ---------------------------------------------------------------- the minimap in the view

    /// <summary>
    /// Shows a diff viewer over a patch long enough to scroll, and returns the window, the map on
    /// screen and the scroll it drives.
    /// </summary>
    private static (Window Window, DiffViewerView View, DiffMinimap Map, ScrollViewer Scroll) ShowScrollable(
        Harness harness,
        bool sideBySide)
    {
        DiffViewerView view = new() { DataContext = harness.Viewer };

        Window window = new() { Content = view, Width = 900, Height = 300 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        DiffMinimap map = view.FindControl<DiffMinimap>(sideBySide ? "SideBySideMinimap" : "UnifiedMinimap")
            ?? throw new InvalidOperationException("The diff viewer has no minimap.");
        DiffTextEditor patch = view.FindControl<DiffTextEditor>(sideBySide ? "LeftEditor" : "UnifiedEditor")
            ?? throw new InvalidOperationException("The diff viewer has no editor.");

        ScrollViewer scroll = patch.ScrollHost ?? throw new InvalidOperationException("The editor has no scroll viewer.");

        return (window, view, map, scroll);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Viewer_ShowsNoVerticalScrollbar(bool sideBySide)
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(BuildLargePatch(400)));

            if (!sideBySide)
            {
                harness.Viewer.ShowUnifiedCommand.Execute(null);
                await WaitForRequestsAsync(harness, 2);
            }

            (Window window, _, _, ScrollViewer scroll) = ShowScrollable(harness, sideBySide);

            // Hidden, not disabled: the map replaces the bar, not the scrolling.
            Assert.Equal(ScrollBarVisibility.Hidden, scroll.VerticalScrollBarVisibility);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "the patch was expected to scroll");

            window.Close();
        });
    }

    [Fact]
    public void Minimap_FollowsThePatchAsItIsScrolled()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(BuildLargePatch(400)));
            harness.Viewer.ShowUnifiedCommand.Execute(null);
            await WaitForRequestsAsync(harness, 2);

            (Window window, _, DiffMinimap map, ScrollViewer scroll) = ShowScrollable(harness, sideBySide: false);

            Assert.Equal(0, map.ViewportStart, 3);
            Assert.True(map.ViewportEnd < 1, "the whole patch cannot be on screen in a 300px window");

            scroll.Offset = new Vector(scroll.Offset.X, scroll.Extent.Height / 2);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // Around the middle rather than exactly on it: the list snaps an offset to a row, and
            // a virtualising panel's extent is an estimate that firms up as rows are realised.
            Assert.InRange(map.ViewportStart, 0.45, 0.6);
            Assert.True(map.ViewportEnd > map.ViewportStart);

            scroll.Offset = new Vector(scroll.Offset.X, scroll.Extent.Height);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // At the end of the patch the window sits against the bottom of the strip.
            Assert.Equal(1, map.ViewportEnd, 2);

            window.Close();
        });
    }

    [Fact]
    public void Minimap_ScrollsThePatchWhenItIsPressed()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(BuildLargePatch(400)));
            harness.Viewer.ShowUnifiedCommand.Execute(null);
            await WaitForRequestsAsync(harness, 2);

            (Window window, _, DiffMinimap map, ScrollViewer scroll) = ShowScrollable(harness, sideBySide: false);

            Assert.Equal(0, scroll.Offset.Y);

            // Pressed at the bottom of the strip: the end of the patch, and no further.
            map.RequestScrollTo(map.Bounds.Height);
            window.UpdateLayout();

            Assert.Equal(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y, 1);

            // And back to the top.
            map.RequestScrollTo(0);
            window.UpdateLayout();

            Assert.Equal(0, scroll.Offset.Y, 1);

            window.Close();
        });
    }

    [Fact]
    public void Minimap_DrawsTheRenderingOnScreen()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(BuildLargePatch(400)));

            (Window window, DiffViewerView view, DiffMinimap side, _) = ShowScrollable(harness, sideBySide: true);

            DiffMinimap unified = view.FindControl<DiffMinimap>("UnifiedMinimap")
                ?? throw new InvalidOperationException("The diff viewer has no unified minimap.");

            // One map per rendering, each describing its own rows — which is why switching between
            // them needs no rebinding.
            Assert.Same(harness.Viewer.SideBySideMap, side.Marks);
            Assert.Same(harness.Viewer.UnifiedMap, unified.Marks);
            Assert.Equal(harness.Viewer.SideBySideRows.Count, side.RowCount);
            Assert.Equal(harness.Viewer.UnifiedRows.Count, unified.RowCount);

            // Only the rendering on screen is shown, map and all.
            Assert.True(side.IsEffectivelyVisible);
            Assert.False(unified.IsEffectivelyVisible);

            harness.Viewer.ShowUnifiedCommand.Execute(null);
            await WaitForRequestsAsync(harness, 2);
            window.UpdateLayout();

            Assert.False(side.IsEffectivelyVisible);
            Assert.True(unified.IsEffectivelyVisible);

            window.Close();
        });
    }

    [Fact]
    public void Minimap_IsNotShownWhenThereIsNoPatch()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(
                new FilePatch(null, "logo.png", FileChangeKind.Modified, [], isBinary: true));

            (Window window, _, DiffMinimap map, _) = ShowScrollable(harness, sideBySide: true);

            // A strip beside an empty state says there is something to navigate, and there is not.
            Assert.False(harness.Viewer.HasPatch);
            Assert.False(map.IsEffectivelyVisible);

            window.Close();
        });
    }

    // ---------------------------------------------------------------- the change map

    [Fact]
    public void Map_HasOneRunPerStretchOfTheSameKind()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await UnifiedAsync();

            // The sample patch: a hunk band, a context line, two removals, three additions, a
            // context line. Context contributes nothing — a map that marked it would be a solid bar.
            Assert.Collection(
                harness.Viewer.UnifiedMap,
                mark =>
                {
                    Assert.Equal(DiffMarkKind.Hunk, mark.Kind);
                    Assert.Equal(0, mark.FirstRow);
                    Assert.Equal(1, mark.RowCount);
                },
                mark =>
                {
                    Assert.Equal(DiffMarkKind.Removed, mark.Kind);
                    Assert.Equal(2, mark.FirstRow);
                    Assert.Equal(2, mark.RowCount);
                },
                mark =>
                {
                    Assert.Equal(DiffMarkKind.Added, mark.Kind);
                    Assert.Equal(4, mark.FirstRow);
                    Assert.Equal(3, mark.RowCount);
                });

            // Every run lands inside the rendering it describes.
            Assert.All(
                harness.Viewer.UnifiedMap,
                mark => Assert.True(mark.EndRow <= harness.Viewer.UnifiedRows.Count));
        });
    }

    [Fact]
    public void Map_MarksBothRenderings()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            Assert.NotEmpty(harness.Viewer.UnifiedMap);
            Assert.NotEmpty(harness.Viewer.SideBySideMap);

            // Side by side pairs a removal with the addition that replaced it, so the same patch is
            // fewer rows and the runs are not the same — which is why there are two maps.
            Assert.True(harness.Viewer.SideBySideRows.Count < harness.Viewer.UnifiedRows.Count);

            Assert.All(
                harness.Viewer.SideBySideMap,
                mark => Assert.True(mark.EndRow <= harness.Viewer.SideBySideRows.Count));

            // A row that is a removal on the left and an addition on the right reads as an
            // addition: the reader is looking at the file as it will be.
            Assert.Contains(harness.Viewer.SideBySideMap, mark => mark.Kind == DiffMarkKind.Added);
        });
    }

    [Fact]
    public void Map_IsEmptyWhenThereIsNoPatch()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            Assert.NotEmpty(harness.Viewer.UnifiedMap);

            harness.Viewer.Clear();

            Assert.Empty(harness.Viewer.UnifiedMap);
            Assert.Empty(harness.Viewer.SideBySideMap);
        });
    }

    [Fact]
    public void Map_MergesConsecutiveLinesOfOneKind()
    {
        _fixture.RunAsync(async () =>
        {
            // Twelve additions in a row, and nothing else but the band and one context line.
            System.Text.StringBuilder patch = new();
            patch.Append("diff --git a/src/run.txt b/src/run.txt\n");
            patch.Append("--- a/src/run.txt\n");
            patch.Append("+++ b/src/run.txt\n");
            patch.Append("@@ -1,1 +1,13 @@\n");
            patch.Append(" one\n");

            for (int index = 0; index < 12; index++)
            {
                patch.Append(System.Globalization.CultureInfo.InvariantCulture, $"+added {index.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n");
            }

            Harness harness = await UnifiedAsync(Parse(patch.ToString()));

            DiffChangeMark added = Assert.Single(harness.Viewer.UnifiedMap, mark => mark.Kind == DiffMarkKind.Added);

            Assert.Equal(12, added.RowCount);
        });
    }

    [Theory]
    // y, height, how much of the patch is on screen, expected start
    [InlineData(0, 200, 0.2, 0)]
    [InlineData(100, 200, 0.2, 0.4)]
    [InlineData(200, 200, 0.2, 0.8)]
    // Past either edge, and the window still stops at the ends of the patch.
    [InlineData(-50, 200, 0.2, 0)]
    [InlineData(400, 200, 0.2, 0.8)]
    // The whole patch on screen: there is nowhere to scroll to.
    [InlineData(100, 200, 1, 0)]
    public void Minimap_PutsThePointerInTheMiddleOfTheView(
        double y,
        double height,
        double viewportFraction,
        double expected)
        => Assert.Equal(expected, DiffMinimap.StartFor(y, height, viewportFraction), 6);

    [Fact]
    public void Minimap_AsksToScrollWhereItWasPressed()
    {
        _fixture.Run(() =>
        {
            DiffMinimap map = new()
            {
                RowCount = 100,
                Marks = [new DiffChangeMark(DiffMarkKind.Added, 50, 4)],
                ViewportStart = 0,
                ViewportEnd = 0.25,
                Height = 200,
            };

            Window window = new() { Content = map, Width = 60, Height = 200 };
            window.Show();
            window.UpdateLayout();

            List<double> asked = [];
            map.ScrollRequested += (_, start) => asked.Add(start);

            map.RequestScrollTo(map.Bounds.Height);

            // Pressed at the very bottom: as far down as the patch goes, and no further.
            double request = Assert.Single(asked);
            Assert.Equal(0.75, request, 6);

            window.Close();
        });
    }

    // ---------------------------------------------------------------- where a file opens

    /// <summary>
    /// A patch whose only change is in the middle of the file, which is the case a scroll position
    /// can get wrong in both directions.
    /// </summary>
    private static string BuildPatchChangingLine(int lines, int changed)
    {
        System.Text.StringBuilder builder = new();

        builder.Append("diff --git a/src/middle.txt b/src/middle.txt\n");
        builder.Append("--- a/src/middle.txt\n");
        builder.Append("+++ b/src/middle.txt\n");
        builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"@@ -1,{lines} +1,{lines} @@\n");

        for (int index = 0; index < lines; index++)
        {
            string number = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            builder.Append(index == changed ? $"+line {number} changed\n" : $" line {number}\n");
        }

        return builder.ToString();
    }

    [Fact]
    public void Viewer_SaysWhereEachRenderingsFirstChangeIs()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(BuildPatchChangingLine(200, 120)));

            // The hunk band is row zero and every context line before the change follows it, so the
            // first run of the map is the change itself, well down the file.
            Assert.True(
                harness.Viewer.UnifiedFirstChangeRow > 100,
                $"the unified first change is row {harness.Viewer.UnifiedFirstChangeRow}");

            Assert.True(
                harness.Viewer.SideBySideFirstChangeRow > 100,
                $"the side-by-side first change is row {harness.Viewer.SideBySideFirstChangeRow}");

            // Each rendering answers for its own rows.
            Assert.True(harness.Viewer.UnifiedFirstChangeRow < harness.Viewer.UnifiedRows.Count);
            Assert.True(harness.Viewer.SideBySideFirstChangeRow < harness.Viewer.SideBySideRows.Count);
        });
    }

    [Fact]
    public void Viewer_FirstChangeIsTheTopWhenThePatchChangesNothing()
    {
        _fixture.RunAsync(async () =>
        {
            // A rename with no content change: rows, but no run to aim at.
            Harness harness = await ShownAsync(new FilePatch(
                "src/old.txt",
                "src/new.txt",
                FileChangeKind.Renamed,
                [],
                similarityIndex: 100));

            Assert.Equal(0, harness.Viewer.UnifiedFirstChangeRow);
            Assert.Equal(0, harness.Viewer.SideBySideFirstChangeRow);
        });
    }

    [Fact]
    public void Viewer_FirstChangeIsTheTopWhenTheChangeIsTheFirstLine()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(BuildPatchChangingLine(40, 0)));

            // Row zero is the hunk band and row one the change itself, so the view — which keeps two
            // rows of context above it — opens this file at the top.
            Assert.True(harness.Viewer.UnifiedFirstChangeRow <= 2);
            Assert.True(harness.Viewer.SideBySideFirstChangeRow <= 2);
        });
    }

    [Fact]
    public void Viewer_SaysSoEveryTimeAPatchReachesTheRows()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(BuildLargePatch(40)));

            int announced = 0;
            harness.Viewer.PatchChanged += (_, _) => announced++;

            await harness.Viewer.ReloadAsync();
            Assert.Equal(1, announced);

            harness.Viewer.Clear();
            Assert.Equal(2, announced);
        });
    }

    [Fact]
    public void Viewer_OpensAFileAtItsFirstChange()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(BuildPatchChangingLine(300, 200)));

            (Window window, _, DiffMinimap map, ScrollViewer scroll) = ShowScrollable(harness, sideBySide: true);

            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // Where the change is, in the extent the list reports now: a virtualising panel
            // re-estimates its extent as rows are realised, so the assertion is about what is on
            // screen rather than about the number the offset was computed from.
            double rowHeight = scroll.Extent.Height / harness.Viewer.SideBySideRows.Count;
            double change = harness.Viewer.SideBySideFirstChangeRow * rowHeight;

            Assert.True(scroll.Offset.Y > 0, "the file opened at the top, not at its change");

            // The change is on screen, and near the top of it rather than merely somewhere in it.
            Assert.InRange(change, scroll.Offset.Y, scroll.Offset.Y + scroll.Viewport.Height);
            Assert.True(
                change - scroll.Offset.Y < scroll.Viewport.Height / 2,
                $"the change sits {change - scroll.Offset.Y} into a {scroll.Viewport.Height} viewport");

            // And the map agrees with where the patch actually is.
            Assert.True(map.ViewportStart > 0);

            window.Close();
        });
    }

    [Fact]
    public void Viewer_OpensTheNextFileAtItsOwnChangeRatherThanTheLastOffset()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(BuildPatchChangingLine(300, 200)));

            (Window window, _, _, ScrollViewer scroll) = ShowScrollable(harness, sideBySide: true);

            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            double deepInTheFirstFile = scroll.Offset.Y;
            Assert.True(deepInTheFirstFile > 0);

            // The next file's change is near its top, so the offset the last one left would be far
            // past it — which is exactly the "a bit random" the reader saw.
            harness.Diffs.Patch = Parse(BuildPatchChangingLine(300, 5));
            await harness.Viewer.ReloadAsync();

            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                scroll.Offset.Y < deepInTheFirstFile / 4,
                $"the second file opened at {scroll.Offset.Y}, near where the first was left ({deepInTheFirstFile})");

            window.Close();
        });
    }

    [Fact]
    public void Minimap_IsWideEnoughToAimAt()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(BuildLargePatch(400)));

            (Window window, _, DiffMinimap map, _) = ShowScrollable(harness, sideBySide: true);

            // The strip is the patch's only vertical control, so it is sized to be dragged with a
            // trackpad rather than clicked with a cursor.
            Assert.Equal(36, map.Bounds.Width, 3);

            window.Close();
        });
    }

    [Fact]
    public void Minimap_ScalesItsMarksToTheStrip()
    {
        _fixture.Run(() =>
        {
            // One run over every row: the strip is the mark, apart from the inset kept clear on
            // each side of it. Explicit brushes so the assertion is about geometry, not the theme.
            DiffMinimap map = new()
            {
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                RowCount = 10,
                Marks = [new DiffChangeMark(DiffMarkKind.Added, 0, 10)],
                TrackBrush = Brushes.Black,
                AddedBrush = Brushes.White,
                ViewportStart = 0,
                ViewportEnd = 1,
            };

            Window window = new() { Content = map, Width = 80, Height = 200 };
            window.Show();
            window.UpdateLayout();

            Assert.Equal(36, map.Bounds.Width, 3);

            string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "diff-minimap-width.png");

            int strip = 0;

            for (int attempt = 0; attempt < 20; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                Dispatcher.UIThread.RunJobs();

                using Bitmap frame = window.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException("The minimap produced no rendered frame.");

                frame.Save(path, PngBitmapEncoderOptions.Default);

                using FileStream whole = System.IO.File.OpenRead(path);
                strip = SnapshotColours.Count(whole, new PixelRect(0, 0, 36, 200));

                if (strip >= 2)
                {
                    break;
                }
            }

            // The track and the mark, plus whatever the anti-aliased edge of the run blends into:
            // more than one colour across the strip is what says the run does not fill it.
            Assert.True(strip >= 2, $"the strip holds {strip} colours, so nothing was drawn on it");

            // The inset is a tenth of the width, so at thirty-six pixels the first three columns
            // are track and nothing else. A two-pixel inset — what a fourteen-pixel strip wanted —
            // would have put the mark inside this band.
            using FileStream edge = System.IO.File.OpenRead(path);
            Assert.Equal(1, SnapshotColours.Count(edge, new PixelRect(0, 0, 3, 200)));

            // And the middle of the strip is the mark.
            using FileStream middle = System.IO.File.OpenRead(path);
            Assert.Equal(1, SnapshotColours.Count(middle, new PixelRect(16, 0, 4, 200)));

            window.Close();
        });
    }

    [Fact]
    public void Minimap_DrawsItsMarksAndItsWindow()
    {
        _fixture.Run(() =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            application.RequestedThemeVariant = ThemeVariant.Dark;

            try
            {
                DiffMinimap map = new()
                {
                    RowCount = 60,
                    Marks =
                    [
                        new DiffChangeMark(DiffMarkKind.Hunk, 0, 1),
                        new DiffChangeMark(DiffMarkKind.Removed, 4, 6),
                        new DiffChangeMark(DiffMarkKind.Added, 10, 12),
                    ],
                    ViewportStart = 0.1,
                    ViewportEnd = 0.4,
                };

                Window window = new() { Content = map, Width = 40, Height = 300 };
                window.Show();

                string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "diff-minimap.png");

                int colours = 0;

                for (int attempt = 0; attempt < 20; attempt++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                    Dispatcher.UIThread.RunJobs();

                    using Bitmap frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("The minimap produced no rendered frame.");

                    frame.Save(path, PngBitmapEncoderOptions.Default);
                    colours = SnapshotColours.Count(path);

                    if (colours >= 4)
                    {
                        break;
                    }
                }

                // The track, two mark colours, the band's, and the window washed over them.
                Assert.True(colours >= 4, $"the minimap frame holds only {colours} distinct colours");

                window.Close();
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });
    }

    [Fact]
    public void Minimap_DrawsNothingButItsTrackWithNoPatch()
    {
        _fixture.Run(() =>
        {
            DiffMinimap map = new() { RowCount = 0, Marks = [] };

            Window window = new() { Content = map, Width = 40, Height = 200 };
            window.Show();
            window.UpdateLayout();

            // No rows, no marks, and the whole of nothing is on screen: nothing to draw and nothing
            // that throws while not drawing it.
            Assert.Equal(0, map.RowCount);
            Assert.Empty(map.Marks!);

            window.Close();
        });
    }

    // ---------------------------------------------------------------- helpers

    private static Color Colour(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out object? value), $"{key} is not defined");
        return Assert.IsType<Color>(value);
    }

    private static Color Brush(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out object? value), $"{key} is not defined");
        return Assert.IsType<SolidColorBrush>(value).Color;
    }

    /// <summary>
    /// The WCAG relative-luminance contrast ratio between two opaque colours.
    /// </summary>
    private static double Contrast(Color first, Color second)
    {
        double one = Luminance(first);
        double two = Luminance(second);

        return (Math.Max(one, two) + 0.05) / (Math.Min(one, two) + 0.05);
    }

    private static double Luminance(Color colour)
    {
        static double Channel(byte value)
        {
            double normalised = value / 255.0;
            return normalised <= 0.03928 ? normalised / 12.92 : Math.Pow((normalised + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));
    }

    private static string BuildLargePatch(int lines)
    {
        System.Text.StringBuilder builder = new();

        builder.Append("diff --git a/src/big.txt b/src/big.txt\n");
        builder.Append("--- a/src/big.txt\n");
        builder.Append("+++ b/src/big.txt\n");
        builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"@@ -1,{lines} +1,{lines} @@\n");

        for (int index = 0; index < lines; index++)
        {
            string number = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            builder.Append(index % 3 == 0 ? $"+line {number}\n" : $" line {number}\n");
        }

        return builder.ToString();
    }

    private static async Task WaitForRequestsAsync(Harness harness, int count)
    {
        for (int attempt = 0; attempt < 200 && harness.Diffs.PatchRequests.Count < count; attempt++)
        {
            await Task.Delay(5);
        }
    }

    private static IReadOnlyList<string> RenderAndReadText(
        Control view,
        string fileName,
        double width = 900,
        double height = 420)
    {
        Window window = new() { Content = view, Width = width, Height = height };
        window.Show();

        string directory = Path.Combine(AppContext.BaseDirectory, "snapshots");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);

        int colours = 0;

        for (int attempt = 0; attempt < 20; attempt++)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            window.UpdateLayout();
            window.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            Dispatcher.UIThread.RunJobs();

            using Bitmap frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("The diff viewer produced no rendered frame.");

            frame.Save(path, PngBitmapEncoderOptions.Default);
            colours = SnapshotColours.Count(path);

            if (colours >= 8)
            {
                break;
            }
        }

        Assert.True(
            colours >= 8,
            $"{fileName}: the frame holds only {colours.ToString(System.Globalization.CultureInfo.InvariantCulture)} distinct colours");

        string[] texts = [.. view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty)
            .Where(text => text.Length > 0)];

        window.Content = null;
        window.Close();

        return texts;
    }
}
