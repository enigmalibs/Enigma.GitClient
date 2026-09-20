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
using Enigma.GitClient.App.Controls.Diff;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.App.ViewModels.Pages;
using Enigma.GitClient.App.ViewModels.Panels;
using Enigma.GitClient.App.Views.Pages;
using Enigma.GitClient.App.Views.Panels;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

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
    public void Viewer_RealisesOnlyTheRowsOnScreen()
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

            int realised = view.GetVisualDescendants().OfType<DiffLineText>().Count();

            // A 600px window holds a few dozen rows. Anything near five thousand means the list
            // built the whole file, which is what freezes a viewer on a generated file.
            Assert.InRange(realised, 1, 200);

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
            Harness harness = await ShownAsync();
            harness.Viewer.ShowUnifiedCommand.Execute(null);

            Assert.False(harness.Viewer.CopySelectionCommand.CanExecute(null));

            foreach (DiffRowViewModel row in harness.Viewer.UnifiedRows.Where(row => row.Single?.IsAdded == true))
            {
                harness.Viewer.Selection.Add(row);
            }

            Assert.True(harness.Viewer.CopySelectionCommand.CanExecute(null));

            await harness.Viewer.CopySelectionCommand.ExecuteAsync(null);

            Assert.Equal("two changed\nthree changed\nfour\n", Assert.Single(harness.Interop.Copied));
        });
    }

    [Fact]
    public void Viewer_CopiesASelectionInRowOrderWhateverOrderItWasClickedIn()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();
            harness.Viewer.ShowUnifiedCommand.Execute(null);

            List<DiffRowViewModel> added =
                [.. harness.Viewer.UnifiedRows.Where(row => row.Single?.IsAdded == true)];

            harness.Viewer.Selection.Add(added[2]);
            harness.Viewer.Selection.Add(added[0]);
            harness.Viewer.Selection.Add(added[1]);

            await harness.Viewer.CopySelectionCommand.ExecuteAsync(null);

            Assert.Equal("two changed\nthree changed\nfour\n", Assert.Single(harness.Interop.Copied));
        });
    }

    [Fact]
    public void Viewer_CopiesAContextLineOnceFromTheSideBySideRendering()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();
            harness.Viewer.ShowSideBySideCommand.Execute(null);

            harness.Viewer.Selection.Add(harness.Viewer.SideBySideRows.First(row => row.Left?.IsContext == true));

            await harness.Viewer.CopySelectionCommand.ExecuteAsync(null);

            // The same line appears on both sides; copying it twice would double every unchanged
            // line of a selection.
            Assert.Equal("one\n", Assert.Single(harness.Interop.Copied));
        });
    }

    [Fact]
    public void Viewer_DropsTheSelectionWhenTheShapeChanges()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();
            harness.Viewer.ShowUnifiedCommand.Execute(null);

            harness.Viewer.Selection.Add(harness.Viewer.UnifiedRows[1]);
            harness.Viewer.ShowSideBySideCommand.Execute(null);

            // The rows are different objects in the other shape; keeping them would copy rows that
            // are no longer on screen.
            Assert.Empty(harness.Viewer.Selection);
        });
    }

    // ---------------------------------------------------------------- the line control

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

    [Theory]
    [InlineData("", 4)]
    [InlineData("plain text", 4)]
    [InlineData("\tindented", 4)]
    [InlineData("a\tb\tc", 4)]
    [InlineData("a\tb\tc", 8)]
    [InlineData("a\tb\tc", 1)]
    [InlineData("a\tb", 0)]
    [InlineData("\t\t\t", 3)]
    public void DiffLineText_CountsTheColumnsItWouldHaveExpandedTo(string text, int tabWidth)
    {
        // The counted length is what a pane's scroll extent is built from, for thousands of lines at
        // a time; it has to be the expansion's own arithmetic, not an approximation of it.
        Assert.Equal(
            DiffLineText.Expand(text, tabWidth, showWhitespace: false).Expanded.Length,
            DiffLineText.ExpandedLength(text, tabWidth));
    }

    [Fact]
    public void DiffLineText_MeasuresTheSameWhateverItIsScrolledTo()
    {
        _fixture.Run(() =>
        {
            DiffLineText line = new() { Text = "public sealed record Commit(string Hash, string Subject);" };

            line.Measure(Size.Infinity);
            Size unscrolled = line.DesiredSize;

            line.HorizontalOffset = 20;
            line.Measure(Size.Infinity);

            // A pane scrolling must not resize its rows: the gutter beside this line, and every
            // other row of the patch, would move with it.
            Assert.Equal(unscrolled, line.DesiredSize);
        });
    }

    [Fact]
    public void DiffLineText_DrawsItsTextFurtherLeftWhenScrolled()
    {
        _fixture.Run(() =>
        {
            DiffLineText line = new()
            {
                Text = "0123456789",
                Foreground = Brushes.White,
            };

            Assert.True(Rendered(line) > 1, "the unscrolled line drew nothing at all");

            // Far past the end of a ten-character line: every glyph is now left of the origin.
            line.HorizontalOffset = 60;

            Assert.Equal(1, Rendered(line));

            // And back again, so the offset is a view of the line rather than a change to it.
            line.HorizontalOffset = 0;

            Assert.True(Rendered(line) > 1);
        });
    }

    [Fact]
    public void DiffLineText_IgnoresAnOffsetWhileItWraps()
    {
        _fixture.Run(() =>
        {
            DiffLineText line = new()
            {
                Text = "0123456789",
                Foreground = Brushes.White,
                WrapLines = true,
                HorizontalOffset = 60,
            };

            // Wrapped text has no overflow to scroll to, so a leftover offset must not push it out
            // of view.
            Assert.True(Rendered(line) > 1);
        });
    }

    [Fact]
    public void DiffLineText_KeepsAScrolledLineOutOfTheGutterBesideIt()
    {
        _fixture.Run(() =>
        {
            const int gutter = 80;

            PixelSize frame = new(320, 24);
            PixelRect numbers = new(0, 0, gutter, frame.Height);
            PixelRect text = new(gutter, 0, frame.Width - gutter, frame.Height);

            DiffLineText line = new()
            {
                Text = new string('W', 200),
                Foreground = Brushes.White,
                FontSize = 12,
                HorizontalOffset = 40,
            };

            // A row in miniature: a strip standing in for the line-number gutter, and the line in
            // the column beside it. Nothing here clips, so the only containment is the control's.
            Grid row = new()
            {
                Background = Brushes.Black,
                ColumnDefinitions = new ColumnDefinitions($"{gutter},*"),
            };

            row.Children.Add(line);
            Grid.SetColumn(line, 1);

            // The control owns it, so a template that says nothing about clipping still gets it.
            Assert.True(line.ClipToBounds, "a diff line no longer contains its own ink");

            // The reported bug, reproduced by taking the containment away: the text scrolled left of
            // its own origin lands in the strip the line numbers occupy.
            line.ClipToBounds = false;

            Assert.True(
                ColoursIn(row, frame, numbers) > 1,
                "the scrolled line never reached the gutter, so this frame cannot show it kept out");

            line.ClipToBounds = true;

            Assert.Equal(1, ColoursIn(row, frame, numbers));

            Assert.True(
                ColoursIn(row, frame, text) > 1,
                "the clipped line drew nothing at all, so an untouched gutter proves nothing");
        });
    }

    /// <summary>
    /// Lays a tree out at a fixed size, renders it, and counts the colours one region of the frame
    /// came out in — "was anything painted <em>here</em>", which is what a clip is judged on.
    /// </summary>
    private static int ColoursIn(Control root, PixelSize frame, PixelRect region)
    {
        root.InvalidateMeasure();
        root.Measure(new Size(frame.Width, frame.Height));
        root.Arrange(new Rect(0, 0, frame.Width, frame.Height));

        using RenderTargetBitmap target = new(frame, new Vector(96, 96));
        target.Render(root);

        using MemoryStream stream = new();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;

        return SnapshotColours.Count(stream, region);
    }

    /// <summary>
    /// Draws one line on its own and counts the colours that came out: one means nothing was
    /// painted, which is how "the glyphs moved out of view" is told from "the glyphs are there".
    /// </summary>
    private static int Rendered(DiffLineText line)
    {
        const int width = 220;
        const int height = 40;

        line.InvalidateMeasure();
        line.Measure(new Size(width, height));
        line.Arrange(new Rect(0, 0, width, height));

        using RenderTargetBitmap target = new(new PixelSize(width, height), new Vector(96, 96));
        target.Render(line);

        using MemoryStream stream = new();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;

        return SnapshotColours.Count(stream);
    }

    // ---------------------------------------------------------------- horizontal scrolling

    [Fact]
    public void ScrollState_OffersWhatDoesNotFit()
    {
        DiffScrollState pane = new() { Columns = 100, Viewport = 40 };

        Assert.Equal(60, pane.Maximum);
        Assert.True(pane.IsScrollable);
        Assert.Equal(39, pane.PageSize);

        pane.Offset = 25;

        Assert.Equal(25, pane.Offset);
    }

    [Fact]
    public void ScrollState_HasNothingToOfferWhenEverythingFits()
    {
        DiffScrollState pane = new() { Columns = 30, Viewport = 80 };

        Assert.Equal(0, pane.Maximum);
        Assert.False(pane.IsScrollable);

        pane.Offset = 25;

        Assert.Equal(0, pane.Offset);
    }

    [Fact]
    public void ScrollState_ComesBackWhenWhatItWasShowingShrinks()
    {
        DiffScrollState pane = new() { Columns = 200, Viewport = 50, Offset = 150 };

        Assert.Equal(150, pane.Offset);

        // Another file, a shorter longest line: an offset past its end would show blank space.
        pane.Columns = 80;

        Assert.Equal(30, pane.Offset);
    }

    [Fact]
    public void ScrollState_GoesQuietWhenItIsTurnedOff()
    {
        DiffScrollState pane = new() { Columns = 200, Viewport = 50, Offset = 100 };

        pane.IsEnabled = false;

        Assert.Equal(0, pane.Offset);
        Assert.False(pane.IsScrollable);

        pane.Offset = 90;

        Assert.Equal(0, pane.Offset);
    }

    [Fact]
    public void Viewer_MeasuresTheSideBySideRenderingByItsWiderSide()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(LongLinePatch));

            // The 400-character line, plus the column of air that keeps it off the edge. The new
            // side holds only "short" and the context line, but the two panes share one extent, so
            // either can be scrolled to the end of the longest line on either of them.
            Assert.Equal(401, harness.Viewer.Render.SideBySideScroll.Columns);
            Assert.Equal(401, harness.Viewer.Render.UnifiedScroll.Columns);
        });
    }

    [Fact]
    public void Viewer_RemeasuresThePanesWhenATabGetsWider()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(TabbedPatch));

            double narrow = harness.Viewer.Render.SideBySideScroll.Columns;

            harness.Viewer.Render.TabWidth = 8;

            Assert.True(
                harness.Viewer.Render.SideBySideScroll.Columns > narrow,
                "a wider tab did not make the line it indents any wider");
        });
    }

    [Fact]
    public void Viewer_ScrollsBothSidesTogether()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(LongLinePatch));

            harness.Viewer.Render.SideBySideScroll.Viewport = 40;

            Assert.True(harness.Viewer.Render.SideBySideScroll.IsScrollable);

            harness.Viewer.Render.SideBySideScroll.Offset = 120;

            // One offset behind both sides: every DiffLineText of the rendering, old side and new,
            // is drawn at the same column. That is what "synchronised" means here — there is
            // nothing to keep in step, because there is only one thing.
            Assert.All(
                harness.Viewer.SideBySideRows,
                row =>
                {
                    Assert.Same(harness.Viewer.Render.SideBySideScroll, row.Options.SideBySideScroll);
                    Assert.Equal(120, row.Options.SideBySideScroll.Offset);
                });

            // And the unified rendering keeps its own, which the two share nothing with.
            Assert.Equal(0, harness.Viewer.Render.UnifiedScroll.Offset);
        });
    }

    [Fact]
    public void Viewer_HasOneVerticalScrollForBothSides()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(SamplePatch));

            // The two sides are cells of one row rather than two lists, so nothing can make them
            // drift vertically: scrolling the rendering scrolls both by construction.
            Assert.NotEmpty(harness.Viewer.SideBySideRows);

            Assert.All(
                harness.Viewer.SideBySideRows,
                row => Assert.True(
                    row.IsHunkHeader || row.Left is not null || row.Right is not null,
                    "a side-by-side row carried neither side"));

            Assert.Contains(harness.Viewer.SideBySideRows, row => row.Left is not null && row.Right is not null);
        });
    }

    [Fact]
    public void Viewer_PutsItsPanesAwayWhileLinesWrap()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(LongLinePatch));

            harness.Viewer.Render.UnifiedScroll.Viewport = 40;
            harness.Viewer.Render.SideBySideScroll.Viewport = 40;
            harness.Viewer.Render.SideBySideScroll.Offset = 120;

            harness.Viewer.Render.WrapLines = true;

            Assert.All(harness.Viewer.Render.Panes(), pane =>
            {
                Assert.False(pane.IsScrollable);
                Assert.Equal(0, pane.Offset);
            });

            harness.Viewer.Render.WrapLines = false;

            Assert.True(harness.Viewer.Render.SideBySideScroll.IsScrollable);
        });
    }

    [Fact]
    public void Viewer_StartsEveryFileAtItsBeginning()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(LongLinePatch));

            harness.Viewer.Render.SideBySideScroll.Viewport = 40;
            harness.Viewer.Render.SideBySideScroll.Offset = 200;

            harness.Diffs.Patch = Parse(SamplePatch);

            await harness.Viewer.ReloadAsync();

            Assert.All(harness.Viewer.Render.Panes(), pane => Assert.Equal(0, pane.Offset));
        });
    }

    [Fact]
    public void Viewer_KeepsALongLineInsideItsOwnPane()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(LongLinePatch));

            DiffViewerView view = new() { DataContext = harness.Viewer };
            Window window = new() { Content = view, Width = 900, Height = 420 };
            window.Show();

            for (int attempt = 0; attempt < 10; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                Dispatcher.UIThread.RunJobs();
            }

            DiffLineText wide = view.GetVisualDescendants()
                .OfType<DiffLineText>()
                .Single(line => line.Text?.Length == 400);

            Border pane = wide.GetVisualAncestors()
                .OfType<Border>()
                .First(border => border.Classes.Contains("diffrow"));

            // The premise: this line really is wider than the pane holding it. Measured from the
            // text rather than read off DesiredSize, which a layout pass has already constrained to
            // the space the pane offered.
            double drawn = wide.Text!.Length * DiffTypography.Current.CharacterWidth;

            Assert.True(
                drawn > pane.Bounds.Width,
                $"the sample line is {drawn} wide against a {pane.Bounds.Width} pane, so it never overflowed");

            // And the fix: the pane contains it, instead of letting it paint over the other side.
            Assert.True(pane.ClipToBounds, "the pane does not clip, so a long line reaches the pane beside it");
            Assert.True(pane.Bounds.Width < window.Width, "the pane is not half of a side-by-side row");

            // The view reported what it can show, which is what makes the bar appear at all.
            Assert.True(harness.Viewer.Render.SideBySideScroll.Viewport > 0);
            Assert.True(harness.Viewer.Render.SideBySideScroll.IsScrollable);

            window.Content = null;
            window.Close();
        });
    }

    [Fact]
    public void Viewer_KeepsAScrolledLineClearOfItsLineNumbers()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync(Parse(LongLinePatch));

            DiffViewerView view = new() { DataContext = harness.Viewer };
            Window window = new() { Content = view, Width = 900, Height = 420 };
            window.Show();

            harness.Viewer.Render.SideBySideScroll.Viewport = 40;
            harness.Viewer.Render.SideBySideScroll.Offset = 120;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
                Dispatcher.UIThread.RunJobs();
            }

            DiffLineText wide = view.GetVisualDescendants()
                .OfType<DiffLineText>()
                .Single(line => line.Text?.Length == 400);

            Assert.Equal(120, wide.HorizontalOffset);

            // The pane's own clip cannot do this one: it holds the gutter and the marker as well as
            // the text, so a line scrolled left of its column is still inside it.
            Assert.True(wide.ClipToBounds, "the scrolled line paints over the line numbers again");

            // And the column it is clipped to really is the one beside the numbers: the gutter and
            // the marker are laid out before it, and neither moves when the pane scrolls.
            DiffMetrics metrics = DiffTypography.Current;

            Assert.True(
                wide.Bounds.X >= metrics.GutterWidth + metrics.MarkerWidth - 0.5,
                $"the text column starts at {wide.Bounds.X}, inside the gutter and marker beside it");

            window.Content = null;
            window.Close();
        });
    }

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
                Assert.Contains("@@ -1,4 +1,5 @@", unified);
                Assert.Contains("+", unified);
                Assert.Contains("−", unified);

                // A selected line keeps its own colour: the selection is the bar in the gutter.
                harness.Viewer.Selection.Add(harness.Viewer.UnifiedRows.First(row => row.Single?.IsRemoved == true));

                IReadOnlyList<string> selected = RenderAndReadText(view, "diff-unified-selected.png");

                Assert.Contains("@@ -1,4 +1,5 @@", selected);

                harness.Viewer.ShowSideBySideCommand.Execute(null);

                IReadOnlyList<string> side = RenderAndReadText(view, "diff-side-by-side.png");

                Assert.Contains("@@ -1,4 +1,5 @@", side);
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

                List<DiffLineText> texts = [.. view.GetVisualDescendants().OfType<DiffLineText>()];

                DiffLineText addedLine = texts.Single(text => text.Text == "two changed");
                DiffLineText contextLine = texts.First(text => text.Text == "one");

                // The class the template binds is what carries the brush; a changed line with no
                // highlight brush draws its word-level segments as plain text and the whole point
                // of the word diff is lost.
                Assert.Equal(
                    Colour("DiffAddedWordColor", ThemeVariant.Dark),
                    Assert.IsType<SolidColorBrush>(addedLine.HighlightBrush).Color);

                Assert.NotEmpty(addedLine.Segments!);
                Assert.Contains(addedLine.Segments!, segment => segment.IsChanged);

                // Context is unchanged, so it must not be tinted at all.
                Assert.Null(contextLine.HighlightBrush);

                window.Content = null;
                window.Close();
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

                // The band is a TextBlock; the code lines are drawn by DiffLineText, so both have
                // to be looked for to prove the pane really rendered the patch.
                Assert.Contains(texts, text => text.StartsWith("@@", StringComparison.Ordinal));
                Assert.Contains("src/app.txt", texts);

                List<string> lines = [.. view.GetVisualDescendants()
                    .OfType<DiffLineText>()
                    .Select(line => line.Text ?? string.Empty)];

                Assert.Contains("two edited", lines);
                Assert.Contains("two", lines);
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
        ListBox list = view.FindControl<ListBox>(sideBySide ? "SideBySideList" : "UnifiedList")
            ?? throw new InvalidOperationException("The diff viewer has no diff list.");

        ScrollViewer scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();

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
