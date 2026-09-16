using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
    public void Viewer_SwitchesShapeWithoutRereadingThePatch()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            // Side by side is what a fresh store opens on.
            Assert.True(harness.Viewer.IsSideBySide);
            Assert.Single(harness.Diffs.PatchRequests);

            harness.Viewer.ShowUnifiedCommand.Execute(null);

            Assert.True(harness.Viewer.IsUnified);
            Assert.False(harness.Viewer.IsSideBySide);

            harness.Viewer.ShowSideBySideCommand.Execute(null);

            Assert.True(harness.Viewer.IsSideBySide);

            // The shape is a rendering choice; git has nothing to say about it.
            Assert.Single(harness.Diffs.PatchRequests);
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
    public void Viewer_AsksForTheDefaultContextFirst()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            Assert.Equal(3, Assert.Single(harness.Diffs.PatchRequests).ContextLines);
            Assert.Equal("src/app.txt", Assert.Single(harness.Diffs.PatchPaths));
        });
    }

    [Fact]
    public void Viewer_ExpandingContextRereadsWithAWiderUnified()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

            await harness.Viewer.ExpandContextCommand.ExecuteAsync(null);

            // Context is git's to produce, not the renderer's to invent — the only way to show more
            // of the file is to ask for it.
            Assert.Equal(2, harness.Diffs.PatchRequests.Count);
            Assert.Equal(3 * DiffViewerViewModel.ExpansionFactor, harness.Diffs.PatchRequests[^1].ContextLines);
            Assert.Equal(3 * DiffViewerViewModel.ExpansionFactor, harness.Viewer.ContextLines);
        });
    }

    [Fact]
    public void Viewer_ExpandsTheContextFromTheBandItself()
    {
        _fixture.RunAsync(async () =>
        {
            Harness harness = await ShownAsync();

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
            Harness harness = await ShownAsync();

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
            Harness harness = await ShownAsync();

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

                page.SelectedRow = page.Rows.Single(row => row.Subject == "Edit the file");
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
