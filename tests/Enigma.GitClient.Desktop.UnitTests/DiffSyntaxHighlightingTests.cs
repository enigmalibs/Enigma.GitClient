using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Controls.Diff;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Panels;
using Enigma.GitClient.Desktop.Views.Panels;
using Microsoft.Extensions.Logging.Abstractions;
using TextMateSharp.Grammars;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The diff's syntax colours: a grammar by extension, plain text for the rest, a theme that follows
/// the window's, and a failure that costs the colours rather than the application.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiffSyntaxHighlightingTests
{
    private const string CSharpPatch =
        "diff --git a/src/App.cs b/src/App.cs\n" +
        "--- a/src/App.cs\n" +
        "+++ b/src/App.cs\n" +
        "@@ -1,2 +1,2 @@\n" +
        "-public sealed class Old\n" +
        "+public sealed class New\n" +
        " // a comment\n";

    private static readonly RepositoryHandle Repository = OperatingSystem.IsWindows()
        ? new RepositoryHandle(@"C:\src\client", @"C:\src\client\.git")
        : new RepositoryHandle("/src/client", "/src/client/.git");

    private readonly HeadlessAvaloniaFixture _fixture;

    public DiffSyntaxHighlightingTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public void ACSharpFile_IsTokenized_KeywordsAndCommentsInTheirOwnColours()
    {
        _fixture.RunAsync(async () =>
        {
            using Themed themed = new(ThemeVariant.Dark);
            (DiffTextEditor editor, Window window) = Show("src/App.cs");

            Assert.Equal("source.cs", editor.Highlighting!.Scope);

            // Document line 2 is "public sealed class Old", line 4 the comment. (Line 3 is the added
            // "public sealed class New": the grammar reads it as the rest of line 2's unfinished
            // declaration, which is the price of one document holding both sides' lines.)
            Color plain = Assert.IsAssignableFrom<ISolidColorBrush>(editor.Foreground).Color;

            await WaitUntilAsync(() => ForegroundAt(editor, line: 2, column: 0) is { } keyword && keyword != plain);

            Color keyword = ForegroundAt(editor, line: 2, column: 0)!.Value;
            Color comment = ForegroundAt(editor, line: 4, column: 3)!.Value;

            Assert.NotEqual(plain, keyword);
            Assert.NotEqual(keyword, comment);

            window.Close();
        });
    }

    [Fact]
    public void AnUnknownExtension_StaysPlain()
    {
        _fixture.RunAsync(async () =>
        {
            using Themed themed = new(ThemeVariant.Dark);
            (DiffTextEditor editor, Window window) = Show("notes.unknown-extension");

            Assert.Null(editor.Highlighting!.Scope);

            await Task.Delay(100);
            Settle(window);

            Color plain = Assert.IsAssignableFrom<ISolidColorBrush>(editor.Foreground).Color;

            Assert.Equal(plain, ForegroundAt(editor, line: 2, column: 0));

            // And so does a file with no extension at all.
            editor.FilePath = "Makefile-without-a-dot";

            Assert.Null(editor.Highlighting.Scope);

            window.Close();
        });
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("Makefile")]
    public void AHighlightedFileGoingPlain_IsReinstalled_NotStrippedOfItsGrammar(string path)
        => _fixture.RunAsync(() => GoesPlainByAReinstallAsync(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ClearingTheFile_GoesPlainTheSameWay(string? path)
        => _fixture.RunAsync(() => GoesPlainByAReinstallAsync(path));

    [Fact]
    public void PlainAndBack_KeepsTheCurrentTheme()
    {
        _fixture.RunAsync(async () =>
        {
            using Themed themed = new(ThemeVariant.Dark);
            (DiffTextEditor editor, Window window) = Show("src/App.cs");

            // The registry was built for Dark+; the installation the file comes back on must be in
            // the theme the window shows now.
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            Settle(window);

            editor.FilePath = "notes.txt";
            Settle(window);
            editor.FilePath = "src/App.cs";
            Settle(window);

            Assert.Equal(ThemeName.LightPlus, editor.Highlighting!.Theme);

            (DiffTextEditor fresh, Window freshWindow) = Show("src/App.cs");
            Color freshPlain = Assert.IsAssignableFrom<ISolidColorBrush>(fresh.Foreground).Color;
            Color plain = Assert.IsAssignableFrom<ISolidColorBrush>(editor.Foreground).Color;

            await WaitUntilAsync(() => ForegroundAt(fresh, line: 2, column: 0) is { } keyword && keyword != freshPlain);
            await WaitUntilAsync(() => ForegroundAt(editor, line: 2, column: 0) is { } keyword && keyword != plain);

            Assert.Equal(ForegroundAt(fresh, line: 2, column: 0), ForegroundAt(editor, line: 2, column: 0));

            freshWindow.Close();
            window.Close();
        });
    }

    [Fact]
    public void TheTokensTheme_FollowsTheWindowsThemeVariant()
    {
        _fixture.Run(() =>
        {
            using Themed themed = new(ThemeVariant.Dark);
            (DiffTextEditor editor, Window window) = Show("src/App.cs");

            Assert.Equal(ThemeName.DarkPlus, editor.Highlighting!.Theme);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            Settle(window);

            Assert.Equal(ThemeName.LightPlus, editor.Highlighting.Theme);

            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            Settle(window);

            Assert.Equal(ThemeName.DarkPlus, editor.Highlighting.Theme);

            window.Close();
        });
    }

    [Fact]
    public void AGrammarFailure_TakesTheColoursOff_AndNothingElse()
    {
        _fixture.Run(() =>
        {
            using Themed themed = new(ThemeVariant.Dark);
            (DiffTextEditor editor, Window window) = Show("src/App.cs");
            TextView view = editor.TextArea.TextView;

            Assert.NotEmpty(view.LineTransformers.OfType<TextMateColoringTransformer>());

            // What the tokenizer's background thread reports when a grammar throws.
            DiffSyntaxHighlighting highlighting = editor.Highlighting!;
            highlighting.OnFailure(new InvalidOperationException("a grammar that throws"));
            Settle(window);

            Assert.True(highlighting.IsDisposed);
            Assert.Empty(view.LineTransformers.OfType<TextMateColoringTransformer>());

            // The diff itself is untouched.
            Assert.Contains("public sealed class New", editor.Document.Text, StringComparison.Ordinal);

            window.Close();
        });
    }

    [Fact]
    public void AnEditorOffScreen_HasNoHighlightingInstalled()
    {
        _fixture.Run(() =>
        {
            using Themed themed = new(ThemeVariant.Dark);
            (DiffTextEditor editor, Window window) = Show("src/App.cs");
            DiffSyntaxHighlighting highlighting = editor.Highlighting!;

            window.Content = null;
            Settle(window);

            Assert.Null(editor.Highlighting);
            Assert.True(highlighting.IsDisposed);

            // Back on screen, it is highlighted again, with the grammar it had.
            window.Content = editor;
            Settle(window);

            Assert.Equal("source.cs", editor.Highlighting?.Scope);

            window.Close();
        });
    }

    [Fact]
    public void EveryPaneOfTheViewer_IsHighlightedForTheFileItShows()
    {
        _fixture.RunAsync(async () =>
        {
            using Themed themed = new(ThemeVariant.Dark);

            RecordingDiffService diffs = new() { Patch = UnifiedDiffParser.Parse(CSharpPatch).Files[0] };
            DiffViewerViewModel viewer = new(
                diffs,
                new RecordingSystemInterop(),
                new SettingsService(
                    new AppPaths(Path.Combine(Path.GetTempPath(), "enigma-syntax-tests-" + Guid.NewGuid().ToString("N"))),
                    NullLogger<SettingsService>.Instance),
                NullLogger<DiffViewerViewModel>.Instance);

            await viewer.ShowAsync(
                Repository,
                DiffTarget.Commit("abc123"),
                new ChangedFile { Path = "src/App.cs", ChangeKind = FileChangeKind.Modified, AddedLines = 1, RemovedLines = 1 });

            DiffViewerView view = new() { DataContext = viewer };
            Window window = new() { Content = view, Width = 900, Height = 300 };
            window.Show();
            Settle(window);

            foreach (string name in new[] { "UnifiedEditor", "LeftEditor", "RightEditor" })
            {
                DiffTextEditor editor = view.FindControl<DiffTextEditor>(name)!;

                Assert.Equal("src/App.cs", editor.FilePath);
                Assert.Equal("source.cs", editor.Highlighting?.Scope);
            }

            window.Close();
        });
    }

    [Fact]
    public void ClearingTheViewer_ReinstallsEveryPane()
    {
        _fixture.RunAsync(async () =>
        {
            using Themed themed = new(ThemeVariant.Dark);

            RecordingDiffService diffs = new() { Patch = UnifiedDiffParser.Parse(CSharpPatch).Files[0] };
            DiffViewerViewModel viewer = new(
                diffs,
                new RecordingSystemInterop(),
                new SettingsService(
                    new AppPaths(Path.Combine(Path.GetTempPath(), "enigma-syntax-tests-" + Guid.NewGuid().ToString("N"))),
                    NullLogger<SettingsService>.Instance),
                NullLogger<DiffViewerViewModel>.Instance);

            await viewer.ShowAsync(
                Repository,
                DiffTarget.Commit("abc123"),
                new ChangedFile { Path = "src/App.cs", ChangeKind = FileChangeKind.Modified, AddedLines = 1, RemovedLines = 1 });

            DiffViewerView view = new() { DataContext = viewer };
            Window window = new() { Content = view, Width = 900, Height = 300 };
            window.Show();
            Settle(window);

            string[] panes = ["UnifiedEditor", "LeftEditor", "RightEditor"];
            Dictionary<string, TextMateColoringTransformer> before = panes.ToDictionary(
                name => name,
                name => Assert.Single(Transformers(view.FindControl<DiffTextEditor>(name)!)));

            // Deselecting a file: the title the panes take their path from goes to "".
            viewer.Clear();
            Settle(window);

            foreach (string name in panes)
            {
                DiffTextEditor editor = view.FindControl<DiffTextEditor>(name)!;

                Assert.NotNull(editor.Highlighting);
                Assert.Null(editor.Highlighting.Scope);
                Assert.False(editor.Highlighting.IsDisposed);
                Assert.NotSame(before[name], Assert.Single(Transformers(editor)));
            }

            window.Close();
        });
    }

    [Theory]
    [InlineData("Dark", ThemeName.DarkPlus)]
    [InlineData("Light", ThemeName.LightPlus)]
    public void ThemeNameFor_MapsEachVariantOntoItsTextMateTheme(string variant, ThemeName expected)
        => Assert.Equal(
            expected,
            DiffSyntaxHighlighting.ThemeNameFor(variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light));

    // ---------------------------------------------------------------- helpers

    /// <summary>Puts the application in a theme variant for the length of a test.</summary>
    private sealed class Themed : IDisposable
    {
        private readonly ThemeVariant? _original;

        public Themed(ThemeVariant variant)
        {
            _original = Application.Current!.RequestedThemeVariant;
            Application.Current.RequestedThemeVariant = variant;
        }

        public void Dispose() => Application.Current!.RequestedThemeVariant = _original;
    }

    private static (DiffTextEditor Editor, Window Window) Show(string path)
    {
        DiffRenderOptions options = new();
        List<DiffRowViewModel> rows = [];

        foreach (DiffRow row in DiffRowBuilder.BuildUnified(UnifiedDiffParser.Parse(CSharpPatch).Files[0]))
        {
            rows.Add(row.Kind == DiffRowKind.HunkHeader
                ? new DiffRowViewModel(row.Hunk!, options) { Index = rows.Count }
                : new DiffRowViewModel(new DiffCellViewModel(row.Line, options), null, null, options) { Index = rows.Count });
        }

        DiffTextEditor editor = new() { Rows = rows, Pane = DiffPane.Unified, FilePath = path };
        Window window = new() { Content = editor, Width = 600, Height = 300 };
        window.Show();
        Settle(window);

        return (editor, window);
    }

    /// <summary>
    /// Takes a highlighted file to a path with no grammar, and checks it went plain by the safe route:
    /// a fresh installation, never its grammar taken away (BUG-1E83).
    /// </summary>
    /// <remarks>
    /// The deadlock itself cannot be tested: a lost race hangs the UI thread rather than failing. A new
    /// transformer is what shows the installation was replaced; a single one, that the one replaced
    /// left nothing behind.
    /// </remarks>
    private static async Task GoesPlainByAReinstallAsync(string? path)
    {
        using Themed themed = new(ThemeVariant.Dark);
        (DiffTextEditor editor, Window window) = Show("src/App.cs");
        Color plain = Assert.IsAssignableFrom<ISolidColorBrush>(editor.Foreground).Color;

        // Coloured first, so that plain afterwards says something.
        await WaitUntilAsync(() => ForegroundAt(editor, line: 2, column: 0) is { } keyword && keyword != plain);

        DiffSyntaxHighlighting highlighting = editor.Highlighting!;
        TextMateColoringTransformer before = Assert.Single(Transformers(editor));

        editor.FilePath = path;
        Settle(window);

        Assert.Same(highlighting, editor.Highlighting);
        Assert.Null(highlighting.Scope);
        Assert.False(highlighting.IsDisposed);
        Assert.NotSame(before, Assert.Single(Transformers(editor)));
        Assert.Equal(plain, ForegroundAt(editor, line: 2, column: 0));

        window.Close();
    }

    private static IEnumerable<TextMateColoringTransformer> Transformers(DiffTextEditor editor)
        => editor.TextArea.TextView.LineTransformers.OfType<TextMateColoringTransformer>();

    /// <summary>The colour a character of a document line is drawn in, once its visual line is built.</summary>
    private static Color? ForegroundAt(DiffTextEditor editor, int line, int column)
    {
        TextView view = editor.TextArea.TextView;
        view.EnsureVisualLines();

        if (view.GetVisualLine(line) is not { } visual)
        {
            return null;
        }

        foreach (VisualLineElement element in visual.Elements)
        {
            if (column >= element.RelativeTextOffset && column < element.RelativeTextOffset + element.DocumentLength)
            {
                return (element.TextRunProperties.ForegroundBrush as ISolidColorBrush)?.Color;
            }
        }

        return null;
    }

    private static void Settle(Window window)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Waits for the background tokenizer: a line is coloured only once its tokens arrive, and the
    /// installation redraws it then.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, int attempts = 300)
    {
        for (int attempt = 0; attempt < attempts && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            await Task.Delay(10);
        }

        Assert.True(condition(), "the tokens never arrived");
    }
}
