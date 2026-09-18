using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.App.Controls.Diff;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.ViewModels.Panels;

/// <summary>
/// How a patch is laid out.
/// </summary>
public enum DiffViewMode
{
    /// <summary>One column, removed lines above added ones, the way git writes a patch.</summary>
    Unified,

    /// <summary>Two columns, the old file on the left and the new one on the right.</summary>
    SideBySide,
}

/// <summary>
/// How far one pane of a rendering is scrolled sideways, and how far it can be.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is counted in characters rather than pixels. The diff is monospace by
/// construction — it expands tabs to a fixed column grid precisely because a proportional face
/// would not line up — so a column is the unit the reader is actually moving by, and counting in
/// them keeps every font measurement in the view where it belongs.
/// </para>
/// <para>
/// <see cref="Columns"/> comes from the patch, <see cref="Viewport"/> from the control that shows
/// it, and <see cref="Offset"/> from the reader; whichever of them moves, the offset is clamped
/// back into what is left, so a pane can never end up scrolled past a line that just got shorter.
/// </para>
/// </remarks>
public sealed class DiffScrollState : ViewModelBase
{
    /// <summary>Gets or sets how many columns the widest line of this pane occupies.</summary>
    public double Columns
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                Recalculate();
            }
        }
    }

    /// <summary>Gets or sets how many columns of this pane are on screen.</summary>
    public double Viewport
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                Recalculate();
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether this pane can scroll at all. Wrapping turns it off:
    /// a re-flowed line has no overflow to reach.
    /// </summary>
    public bool IsEnabled
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                Recalculate();
            }
        }
    } = true;

    /// <summary>Gets or sets how far this pane is scrolled, in columns.</summary>
    public double Offset
    {
        get;
        set => SetProperty(ref field, Clamped(value));
    }

    /// <summary>Gets how many columns are out of view, which is how far the offset can go.</summary>
    public double Maximum => Math.Max(0, Columns - Viewport);

    /// <summary>Gets a value indicating whether this pane has anything to scroll to.</summary>
    public bool IsScrollable => IsEnabled && Maximum > 0;

    /// <summary>Gets how far a page of this pane moves — one screen less a column of overlap.</summary>
    public double PageSize => Math.Max(1, Viewport - 1);

    /// <summary>
    /// Puts the pane back to the start, which is where every newly loaded file begins.
    /// </summary>
    public void Reset() => Offset = 0;

    private double Clamped(double value)
        => IsEnabled ? Math.Clamp(value, 0, Maximum) : 0;

    private void Recalculate()
    {
        OnPropertyChanged(nameof(Maximum));
        OnPropertyChanged(nameof(IsScrollable));
        OnPropertyChanged(nameof(PageSize));

        double clamped = Clamped(Offset);

        if (clamped != Offset)
        {
            Offset = clamped;
        }
    }
}

/// <summary>
/// The rendering choices that belong to the reader rather than to git: they change how the same
/// patch is drawn, so nothing is re-read when one of them moves.
/// </summary>
/// <remarks>
/// Every row holds the same instance, which is what lets a toggle repaint thousands of rows without
/// rebuilding any of them — and what lets a whole rendering scroll sideways as one, since the
/// scroll states below are shared by exactly the same route.
/// </remarks>
public sealed class DiffRenderOptions : ViewModelBase
{
    /// <summary>Gets or sets a value indicating whether spaces and tabs are drawn as symbols.</summary>
    public bool ShowWhitespace { get; set => SetProperty(ref field, value); }

    /// <summary>Gets or sets a value indicating whether long lines wrap instead of scrolling.</summary>
    public bool WrapLines
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                // Wrapping and scrolling are the two answers to the same question, so turning one
                // on puts the other away — bars and offsets both.
                UnifiedScroll.IsEnabled = !value;
                SideBySideScroll.IsEnabled = !value;
            }
        }
    }

    /// <summary>Gets or sets how many columns a tab advances to.</summary>
    public int TabWidth { get; set => SetProperty(ref field, value); } = 4;

    /// <summary>Gets how far the unified rendering is scrolled sideways.</summary>
    public DiffScrollState UnifiedScroll { get; } = new();

    /// <summary>
    /// Gets how far the side-by-side rendering is scrolled sideways — both panes at once.
    /// </summary>
    /// <remarks>
    /// One state, not two kept in step: the two bars and the two columns of text all bind it, so
    /// they cannot drift. Two mirrored states would, the moment the sides' maxima differed — and
    /// they do differ, because the old file and the new one have different longest lines.
    /// </remarks>
    public DiffScrollState SideBySideScroll { get; } = new();

    /// <summary>
    /// Enumerates the panes, for the things that apply to all of them.
    /// </summary>
    /// <returns>The scroll states.</returns>
    public IEnumerable<DiffScrollState> Panes()
    {
        yield return UnifiedScroll;
        yield return SideBySideScroll;
    }
}

/// <summary>
/// One side of one row: a line number, the marker, the text, and the stretches within it that the
/// word-level diff called changed.
/// </summary>
/// <remarks>
/// The unified and the side-by-side renderings both draw this, which is what keeps the two views
/// from drifting apart: a colour or a gutter fixed in one is fixed in both.
/// </remarks>
public sealed class DiffCellViewModel
{
    /// <summary>
    /// Initialises a cell.
    /// </summary>
    /// <param name="line">The line, or <see langword="null"/> for a filler.</param>
    /// <param name="options">The shared rendering options.</param>
    /// <param name="showOldNumber">Whether the cell shows the old-side line number.</param>
    /// <param name="showNewNumber">Whether the cell shows the new-side line number.</param>
    public DiffCellViewModel(
        DiffLine? line,
        DiffRenderOptions options,
        bool showOldNumber = true,
        bool showNewNumber = true)
    {
        ArgumentNullException.ThrowIfNull(options);

        Line = line;
        Options = options;

        OldNumber = showOldNumber ? Format(line?.OldLineNumber) : string.Empty;
        NewNumber = showNewNumber ? Format(line?.NewLineNumber) : string.Empty;
    }

    /// <summary>Gets the line, or <see langword="null"/> for a filler.</summary>
    public DiffLine? Line { get; }

    /// <summary>Gets the shared rendering options.</summary>
    public DiffRenderOptions Options { get; }

    /// <summary>Gets the old-side line number, empty when the cell has none to show.</summary>
    public string OldNumber { get; }

    /// <summary>Gets the new-side line number, empty when the cell has none to show.</summary>
    public string NewNumber { get; }

    /// <summary>Gets the line's text, empty for a filler.</summary>
    public string Text => Line?.Text ?? string.Empty;

    /// <summary>Gets the stretches the word-level diff marked as changed.</summary>
    public IReadOnlyList<DiffSegment> Segments => Line?.Segments ?? [];

    /// <summary>Gets the marker column's character.</summary>
    public string Marker
        => Line?.Kind switch
        {
            DiffLineKind.Added => "+",
            DiffLineKind.Removed => "−",
            DiffLineKind.NoNewline => "\\",
            _ => string.Empty,
        };

    /// <summary>Gets a value indicating whether the line was added.</summary>
    public bool IsAdded => Line?.Kind == DiffLineKind.Added;

    /// <summary>Gets a value indicating whether the line was removed.</summary>
    public bool IsRemoved => Line?.Kind == DiffLineKind.Removed;

    /// <summary>Gets a value indicating whether the line is unchanged context.</summary>
    public bool IsContext => Line?.Kind == DiffLineKind.Context;

    /// <summary>Gets a value indicating whether the line is git's missing-newline marker.</summary>
    public bool IsNoNewline => Line?.Kind == DiffLineKind.NoNewline;

    /// <summary>Gets a value indicating whether this side has nothing on this row.</summary>
    public bool IsFiller => Line is null;

    private static string Format(int? number)
        => number?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
}

/// <summary>
/// One row of the rendered patch, in either shape: a hunk band, or up to two cells.
/// </summary>
public sealed class DiffRowViewModel
{
    /// <summary>
    /// Initialises a hunk band.
    /// </summary>
    /// <param name="hunk">The hunk the band introduces.</param>
    /// <param name="options">The shared rendering options.</param>
    /// <param name="expandContext">
    /// The viewer's expand command, so clicking the band itself asks git for more of the file
    /// around the change — which is where a reader's hand already is.
    /// </param>
    public DiffRowViewModel(DiffHunk hunk, DiffRenderOptions options, AsyncRelayCommand? expandContext = null)
    {
        ArgumentNullException.ThrowIfNull(hunk);
        ArgumentNullException.ThrowIfNull(options);

        IsHunkHeader = true;
        HeaderText = hunk.Header;
        Options = options;
        ExpandContextCommand = expandContext;
    }

    /// <summary>
    /// Initialises a line row.
    /// </summary>
    /// <param name="single">The unified rendering's one cell, or <see langword="null"/>.</param>
    /// <param name="left">The old side, for the side-by-side rendering.</param>
    /// <param name="right">The new side, for the side-by-side rendering.</param>
    /// <param name="options">The shared rendering options.</param>
    public DiffRowViewModel(
        DiffCellViewModel? single,
        DiffCellViewModel? left,
        DiffCellViewModel? right,
        DiffRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Single = single;
        Left = left;
        Right = right;
        Options = options;
    }

    /// <summary>Gets a value indicating whether the row is a hunk band.</summary>
    public bool IsHunkHeader { get; }

    /// <summary>Gets a value indicating whether the row carries lines.</summary>
    public bool IsLine => !IsHunkHeader;

    /// <summary>Gets the band's text, empty for a line row.</summary>
    public string HeaderText { get; } = string.Empty;

    /// <summary>Gets the unified rendering's cell.</summary>
    public DiffCellViewModel? Single { get; }

    /// <summary>Gets the old side, for the side-by-side rendering.</summary>
    public DiffCellViewModel? Left { get; }

    /// <summary>Gets the new side, for the side-by-side rendering.</summary>
    public DiffCellViewModel? Right { get; }

    /// <summary>Gets the shared rendering options.</summary>
    public DiffRenderOptions Options { get; }

    /// <summary>Gets the command a hunk band runs when it is clicked.</summary>
    public AsyncRelayCommand? ExpandContextCommand { get; }

    /// <summary>
    /// Gets the lines this row stands for, which is what a copy of the selection collects.
    /// </summary>
    public IEnumerable<DiffLine> Lines
    {
        get
        {
            if (Single?.Line is not null)
            {
                yield return Single.Line;
                yield break;
            }

            if (Left?.Line is not null)
            {
                yield return Left.Line;
            }

            // A context line is the same line on both sides; yielding it twice would double every
            // unchanged line in a copied selection.
            if (Right?.Line is not null && !ReferenceEquals(Right.Line, Left?.Line))
            {
                yield return Right.Line;
            }
        }
    }
}

/// <summary>
/// The diff viewer: the patch for one file of one comparison, rendered unified or side by side.
/// </summary>
public sealed class DiffViewerViewModel : ViewModelBase
{
    /// <summary>
    /// The context-line count that means "the whole file". git takes any number; this one is past
    /// the length of anything a person reads in a viewer.
    /// </summary>
    public const int WholeFileContext = 100_000;

    /// <summary>
    /// How much the expand-context command multiplies the visible context by.
    /// </summary>
    public const int ExpansionFactor = 4;

    private readonly IDiffService _diffs;
    private readonly ISettingsService _settings;
    private readonly ISystemInterop _interop;
    private readonly ILogger<DiffViewerViewModel> _logger;

    private RepositoryHandle? _repository;
    private DiffTarget? _target;
    private ChangedFile? _file;
    private FilePatch? _patch;
    private int _maxLines = DiffParseOptions.Default.MaxLinesPerFile;
    private int _generation;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="diffs">Reads the patches.</param>
    /// <param name="interop">Puts a copied patch on the clipboard.</param>
    /// <param name="logger">Records a failed read.</param>
    public DiffViewerViewModel(
        IDiffService diffs,
        ISystemInterop interop,
        ISettingsService settings,
        ILogger<DiffViewerViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(diffs);
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _diffs = diffs;
        _interop = interop;
        _settings = settings;
        _logger = logger;

        ShowUnifiedCommand = new RelayCommand(() => ViewMode = DiffViewMode.Unified);
        ShowSideBySideCommand = new RelayCommand(() => ViewMode = DiffViewMode.SideBySide);

        // Both refuse once the whole file is already on screen, which the side-by-side rendering
        // always is: widening the context there would re-read the identical patch.
        ExpandContextCommand = new AsyncRelayCommand(
            () => SetContextAsync(Math.Min(ContextLines * ExpansionFactor, WholeFileContext)),
            () => HasPatch && EffectiveContextLines < WholeFileContext);

        ExpandAllContextCommand = new AsyncRelayCommand(
            () => SetContextAsync(WholeFileContext),
            () => HasPatch && EffectiveContextLines < WholeFileContext);

        ShowAnywayCommand = new AsyncRelayCommand(ShowAnywayAsync, () => IsTruncated);

        CopyPatchCommand = new AsyncRelayCommand(
            () => _interop.CopyTextAsync(_patch is null ? string.Empty : DiffRowBuilder.PatchText(_patch)),
            () => HasPatch);

        CopySelectionCommand = new AsyncRelayCommand(CopySelectionAsync, () => Selection.Count > 0);

        Selection.CollectionChanged += (_, _) => CopySelectionCommand.NotifyCanExecuteChanged();

        // The tab width changes how wide a line is without changing the patch, and the toolbar's
        // wrap toggle writes straight to the options rather than through this ViewModel, so the
        // extents follow the options rather than the other way round.
        Render.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DiffRenderOptions.TabWidth))
            {
                MeasureExtents();
            }
        };

        // Last, and not before the commands: taking the stored preferences on sets ViewMode, whose
        // setter now tells the expand commands their answer changed.
        Apply(_settings.Current);
        _settings.Changed += (_, e) => Apply(e.Settings);
    }

    /// <summary>Gets the rendering choices shared by every row.</summary>
    public DiffRenderOptions Render { get; } = new();

    /// <summary>Gets the rows of the unified rendering.</summary>
    public ObservableCollection<DiffRowViewModel> UnifiedRows { get; } = [];

    /// <summary>Gets the rows of the side-by-side rendering.</summary>
    public ObservableCollection<DiffRowViewModel> SideBySideRows { get; } = [];

    /// <summary>Gets the rows the reader has selected, which a copy collects from.</summary>
    public ObservableCollection<object> Selection { get; } = [];

    /// <summary>
    /// Gets where the changes are in the unified rendering, as runs of consecutive rows.
    /// </summary>
    /// <remarks>
    /// One map per rendering, because the two have different rows: the side-by-side projection
    /// pairs a removal with the addition that replaced it and pads the shorter side with fillers,
    /// so a run's row indices mean nothing in the other rendering.
    /// </remarks>
    public IReadOnlyList<DiffChangeMark> UnifiedMap { get; private set => SetProperty(ref field, value); } = [];

    /// <summary>Gets where the changes are in the side-by-side rendering.</summary>
    public IReadOnlyList<DiffChangeMark> SideBySideMap { get; private set => SetProperty(ref field, value); } = [];

    /// <summary>
    /// Gets or sets how the patch is laid out.
    /// </summary>
    /// <remarks>
    /// Changing it re-reads the patch, because the two renderings ask git for different things: the
    /// side-by-side one shows the whole file, the unified one the reader's own context. Rendering
    /// the rows already in hand in the other shape would show the wrong amount of file.
    /// </remarks>
    public DiffViewMode ViewMode
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsUnified));
                OnPropertyChanged(nameof(IsSideBySide));
                OnPropertyChanged(nameof(EffectiveContextLines));
                Selection.Clear();

                ExpandContextCommand.NotifyCanExecuteChanged();
                ExpandAllContextCommand.NotifyCanExecuteChanged();

                _ = ReloadAsync();
            }
        }
    } = DiffViewMode.Unified;

    /// <summary>Gets a value indicating whether the unified rendering is shown.</summary>
    public bool IsUnified => ViewMode == DiffViewMode.Unified;

    /// <summary>Gets a value indicating whether the side-by-side rendering is shown.</summary>
    public bool IsSideBySide => ViewMode == DiffViewMode.SideBySide;

    /// <summary>
    /// Gets or sets how many unchanged lines are shown around each change.
    /// </summary>
    public int ContextLines
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(EffectiveContextLines));
            }
        }
    } = 3;

    /// <summary>
    /// Gets the context git is actually asked for: the whole file while the side-by-side rendering
    /// is shown, the reader's own <see cref="ContextLines"/> otherwise.
    /// </summary>
    /// <remarks>
    /// Two views of one file read as two views of one file only when both of them *are* the file.
    /// The unified rendering is the other question — "what changed" — and keeps the context
    /// preference and its expand buttons.
    /// </remarks>
    public int EffectiveContextLines => IsSideBySide ? WholeFileContext : ContextLines;

    /// <summary>
    /// Gets or sets a value indicating whether whitespace-only changes are ignored, which is a
    /// question for git rather than for the renderer, so changing it re-reads the patch.
    /// </summary>
    public bool IgnoreAllWhitespace
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                _ = ReloadAsync();
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether changes that only add or remove blank lines are
    /// ignored.
    /// </summary>
    public bool IgnoreBlankLines
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                _ = ReloadAsync();
            }
        }
    }

    /// <summary>Gets the file the viewer is showing, empty when there is none.</summary>
    public string Title { get; private set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>Gets the path the file came from, for a rename.</summary>
    public string Subtitle { get; private set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>Gets a value indicating whether there is a subtitle to show.</summary>
    public bool HasSubtitle => Subtitle.Length > 0;

    /// <summary>Gets the added and removed line counts of the shown patch.</summary>
    public string Summary { get; private set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>
    /// Gets what to say instead of a patch: nothing selected, a binary file, a submodule, or a
    /// change with no lines in it. Empty when there is a patch to draw.
    /// </summary>
    public string Message { get; private set => SetProperty(ref field, value); }
        = "Select a file to see what changed in it.";

    /// <summary>Gets a value indicating whether a message is shown instead of a patch.</summary>
    public bool HasMessage => Message.Length > 0;

    /// <summary>Gets a value indicating whether there is a patch with lines to draw.</summary>
    public bool HasPatch => UnifiedRows.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the patch was cut short because the file is very large.
    /// </summary>
    public bool IsTruncated { get; private set => SetProperty(ref field, value); }

    /// <summary>Gets the command that switches to the unified rendering.</summary>
    public RelayCommand ShowUnifiedCommand { get; }

    /// <summary>Gets the command that switches to the side-by-side rendering.</summary>
    public RelayCommand ShowSideBySideCommand { get; }

    /// <summary>Gets the command that widens the context around each change.</summary>
    public AsyncRelayCommand ExpandContextCommand { get; }

    /// <summary>Gets the command that shows the whole file as context.</summary>
    public AsyncRelayCommand ExpandAllContextCommand { get; }

    /// <summary>Gets the command that renders a file past the truncation limit anyway.</summary>
    public AsyncRelayCommand ShowAnywayCommand { get; }

    /// <summary>Gets the command that copies the whole patch, markers included.</summary>
    public AsyncRelayCommand CopyPatchCommand { get; }

    /// <summary>Gets the command that copies the selected lines without their markers.</summary>
    public AsyncRelayCommand CopySelectionCommand { get; }

    /// <summary>
    /// Points the viewer at one file of one comparison and reads it.
    /// </summary>
    /// <param name="repository">The repository, or <see langword="null"/> to clear.</param>
    /// <param name="target">What is being compared.</param>
    /// <param name="file">The file to show, or <see langword="null"/> to clear.</param>
    /// <returns>A task that completes once the patch is on screen.</returns>
    public async Task ShowAsync(RepositoryHandle? repository, DiffTarget? target, ChangedFile? file)
    {
        _repository = repository;
        _target = target;
        _file = file;

        // Every new file starts from the reader's default context and the ordinary size limit.
        ContextLines = 3;
        _maxLines = DiffParseOptions.Default.MaxLinesPerFile;

        await ReloadAsync();
    }

    /// <summary>
    /// Shows a patch that was read somewhere else.
    /// </summary>
    /// <param name="patch">The patch to render.</param>
    /// <remarks>
    /// A stash's contents come from <c>git stash show</c> rather than from a comparison, so there is
    /// no target to re-read and the context and whitespace options have nothing to act on. What is
    /// shown is what was handed over.
    /// </remarks>
    public void ShowPatch(FilePatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        _repository = null;
        _target = null;
        _file = null;

        Apply(patch, patch.Hunks.Count > 0 ? string.Empty : "This change touches no lines of text.");
    }

    /// <summary>
    /// Clears the viewer.
    /// </summary>
    public void Clear()
    {
        _repository = null;
        _target = null;
        _file = null;

        Apply(null, "Select a file to see what changed in it.");
    }

    /// <summary>
    /// Re-reads the current file with the options as they stand.
    /// </summary>
    /// <returns>A task that completes once the patch is on screen.</returns>
    public async Task ReloadAsync()
    {
        if (_repository is null || _target is null || _file is null)
        {
            Apply(null, "Select a file to see what changed in it.");
            return;
        }

        int generation = ++_generation;
        RepositoryHandle repository = _repository;
        DiffTarget target = _target;
        ChangedFile file = _file;

        DiffOptions options = new()
        {
            ContextLines = EffectiveContextLines,
            IgnoreAllWhitespace = IgnoreAllWhitespace,
            IgnoreBlankLines = IgnoreBlankLines,
            Parsing = DiffParseOptions.Default with { MaxLinesPerFile = _maxLines },
        };

        IsBusy = true;

        try
        {
            FilePatch? patch = await _diffs
                .GetPatchAsync(repository, target, file, options, CancellationToken.None)
                .ConfigureAwait(true);

            // The reader may have moved to another file while git was running.
            if (generation != _generation)
            {
                return;
            }

            Apply(patch, MessageFor(patch, file));
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository changes.
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Reading the patch of {Path} failed", file.Path);

            if (generation == _generation)
            {
                Apply(null, "The diff could not be read.");
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsBusy = false;
            }
        }
    }

    private async Task SetContextAsync(int lines)
    {
        ContextLines = lines;
        await ReloadAsync();
    }

    private async Task ShowAnywayAsync()
    {
        _maxLines = int.MaxValue;
        await ReloadAsync();
    }

    private async Task CopySelectionAsync()
    {
        List<DiffLine> lines = [];
        ObservableCollection<DiffRowViewModel> rows = IsUnified ? UnifiedRows : SideBySideRows;

        // Walked in row order rather than in the order the rows were clicked, so a copied block
        // reads the way it looks.
        foreach (DiffRowViewModel row in rows)
        {
            if (Selection.Contains(row))
            {
                lines.AddRange(row.Lines);
            }
        }

        await _interop.CopyTextAsync(DiffRowBuilder.CopyText(lines));
    }

    private string MessageFor(FilePatch? patch, ChangedFile file)
    {
        if (patch is null)
        {
            return file.IsBinary
                ? "This file is binary, so there is no text to compare."
                : "This change touches no lines of text.";
        }

        if (patch.IsBinary)
        {
            return "This file is binary, so there is no text to compare.";
        }

        if (patch.IsSubmodule)
        {
            return "This is a submodule: the recorded commit changed, not the file's content.";
        }

        if (patch.Hunks.Count > 0)
        {
            return string.Empty;
        }

        return patch.ChangeKind switch
        {
            FileChangeKind.Renamed => "The file moved; its content is unchanged.",
            FileChangeKind.Copied => "The file was copied; its content is unchanged.",
            FileChangeKind.ModeChanged => "Only the file's mode changed.",
            FileChangeKind.TypeChanged => "The entry changed type.",
            _ => "This change touches no lines of text.",
        };
    }

    private void Apply(FilePatch? patch, string message)
    {
        _patch = patch;

        UnifiedRows.Clear();
        SideBySideRows.Clear();
        Selection.Clear();

        if (patch is not null)
        {
            foreach (DiffRow row in DiffRowBuilder.BuildUnified(patch))
            {
                UnifiedRows.Add(row.Kind == DiffRowKind.HunkHeader
                    ? new DiffRowViewModel(row.Hunk!, Render, ExpandContextCommand)
                    : new DiffRowViewModel(new DiffCellViewModel(row.Line, Render), null, null, Render));
            }

            foreach (DiffPairRow row in DiffRowBuilder.BuildSideBySide(patch))
            {
                SideBySideRows.Add(row.Kind == DiffRowKind.HunkHeader
                    ? new DiffRowViewModel(row.Hunk!, Render, ExpandContextCommand)
                    : new DiffRowViewModel(
                        null,
                        new DiffCellViewModel(row.Left, Render, showOldNumber: true, showNewNumber: false),
                        new DiffCellViewModel(row.Right, Render, showOldNumber: false, showNewNumber: true),
                        Render));
            }
        }

        UnifiedMap = BuildMap(UnifiedRows);
        SideBySideMap = BuildMap(SideBySideRows);

        foreach (DiffScrollState pane in Render.Panes())
        {
            // Another file starts at its own beginning, whatever the last one was scrolled to.
            pane.Reset();
        }

        MeasureExtents();

        Title = patch?.DisplayPath ?? _file?.Path ?? string.Empty;
        Subtitle = patch?.OldPath is { Length: > 0 } old && !string.Equals(old, Title, StringComparison.Ordinal)
            ? $"← {old}"
            : string.Empty;

        Summary = patch is null
            ? string.Empty
            : BuildSummary(patch);

        IsTruncated = patch?.IsTruncated == true;
        Message = message;

        OnPropertyChanged(nameof(HasPatch));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(HasSubtitle));

        ExpandContextCommand.NotifyCanExecuteChanged();
        ExpandAllContextCommand.NotifyCanExecuteChanged();
        ShowAnywayCommand.NotifyCanExecuteChanged();
        CopyPatchCommand.NotifyCanExecuteChanged();
        CopySelectionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Reduces a rendering's rows to the runs a minimap draws.
    /// </summary>
    /// <param name="rows">The rendering's rows.</param>
    /// <returns>One run per stretch of consecutive rows of the same kind, in row order.</returns>
    /// <remarks>
    /// Context lines produce nothing: a map of a whole-file diff would otherwise be a solid bar
    /// with the changes invisible inside it. A hunk band is kept, because it is where a change
    /// begins and it is what a reader aims at.
    /// </remarks>
    private static IReadOnlyList<DiffChangeMark> BuildMap(IReadOnlyList<DiffRowViewModel> rows)
    {
        List<DiffChangeMark> marks = [];
        DiffMarkKind? open = null;
        int start = 0;

        for (int index = 0; index < rows.Count; index++)
        {
            DiffMarkKind? kind = KindOf(rows[index]);

            if (kind == open)
            {
                continue;
            }

            if (open is { } previous)
            {
                marks.Add(new DiffChangeMark(previous, start, index - start));
            }

            open = kind;
            start = index;
        }

        if (open is { } last)
        {
            marks.Add(new DiffChangeMark(last, start, rows.Count - start));
        }

        return marks;
    }

    /// <summary>
    /// What a row contributes to the map, or <see langword="null"/> when it contributes nothing.
    /// </summary>
    /// <remarks>
    /// A side-by-side row carries both sides, and a change shows as a removal on the left and an
    /// addition on the right. It is marked as an addition: a map with one colour per row cannot say
    /// "both", and what the reader is looking at is the file as it will be.
    /// </remarks>
    private static DiffMarkKind? KindOf(DiffRowViewModel row)
    {
        if (row.IsHunkHeader)
        {
            return DiffMarkKind.Hunk;
        }

        if (row.Single is { } single)
        {
            return single.IsAdded ? DiffMarkKind.Added
                : single.IsRemoved ? DiffMarkKind.Removed
                : null;
        }

        if (row.Right?.IsAdded == true)
        {
            return DiffMarkKind.Added;
        }

        return row.Left?.IsRemoved == true ? DiffMarkKind.Removed : null;
    }

    /// <summary>
    /// Works out how many columns wide each pane's widest line is.
    /// </summary>
    /// <remarks>
    /// Counted from the rows rather than measured from the glyphs: a patch is thousands of lines,
    /// and an extent that grew as rows were realised would make the thumb jump under the hand that
    /// is dragging it. One column of air is added so the last character is not flush against the
    /// edge of the pane.
    /// </remarks>
    private void MeasureExtents()
    {
        Render.UnifiedScroll.Columns = LongestLine(UnifiedRows, row => row.Single);

        // The wider of the two sides: one shared extent, so either pane can be scrolled to the end
        // of the longest line on either of them and the two bars agree about how far there is left.
        Render.SideBySideScroll.Columns = Math.Max(
            LongestLine(SideBySideRows, row => row.Left),
            LongestLine(SideBySideRows, row => row.Right));
    }

    private double LongestLine(
        IEnumerable<DiffRowViewModel> rows,
        Func<DiffRowViewModel, DiffCellViewModel?> side)
    {
        int longest = 0;

        foreach (DiffRowViewModel row in rows)
        {
            if (side(row) is { Line: not null } cell)
            {
                longest = Math.Max(longest, DiffLineText.ExpandedLength(cell.Text, Render.TabWidth));
            }
        }

        return longest == 0 ? 0 : longest + 1;
    }

    private static string BuildSummary(FilePatch patch)
    {
        StringBuilder builder = new();

        builder.Append(CultureInfo.CurrentCulture, $"+{patch.AddedLines.ToString(CultureInfo.CurrentCulture)}");
        builder.Append(CultureInfo.CurrentCulture, $" −{patch.RemovedLines.ToString(CultureInfo.CurrentCulture)}");

        return builder.ToString();
    }

    /// <summary>
    /// Takes the stored diff preferences on, at startup and whenever they change.
    /// </summary>
    /// <param name="settings">The preferences.</param>
    /// <remarks>
    /// One way only: this viewer's own toolbar changes this viewer for as long as it is open, and
    /// does not rewrite the preference behind every other viewer in the window.
    /// </remarks>
    private void Apply(AppSettings settings)
    {
        ViewMode = settings.DiffView == DiffView.SideBySide ? DiffViewMode.SideBySide : DiffViewMode.Unified;

        Render.ShowWhitespace = settings.ShowWhitespace;
        Render.WrapLines = settings.WrapLines;
        Render.TabWidth = settings.TabWidth;

        bool reload = ContextLines != settings.DiffContextLines;

        ContextLines = settings.DiffContextLines;

        if (IgnoreAllWhitespace != settings.IgnoreWhitespace)
        {
            // Its own setter re-reads the patch, so this branch must not do it twice.
            IgnoreAllWhitespace = settings.IgnoreWhitespace;
        }
        else if (reload && HasPatch)
        {
            // The context is a question for git rather than for the renderer.
            _ = ReloadAsync();
        }
    }
}
