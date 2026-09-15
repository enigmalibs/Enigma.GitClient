using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.App.Services;
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
/// The rendering choices that belong to the reader rather than to git: they change how the same
/// patch is drawn, so nothing is re-read when one of them moves.
/// </summary>
/// <remarks>
/// Every row holds the same instance, which is what lets a toggle repaint thousands of rows without
/// rebuilding any of them.
/// </remarks>
public sealed class DiffRenderOptions : ViewModelBase
{
    /// <summary>Gets or sets a value indicating whether spaces and tabs are drawn as symbols.</summary>
    public bool ShowWhitespace { get; set => SetProperty(ref field, value); }

    /// <summary>Gets or sets a value indicating whether long lines wrap instead of scrolling.</summary>
    public bool WrapLines { get; set => SetProperty(ref field, value); }

    /// <summary>Gets or sets how many columns a tab advances to.</summary>
    public int TabWidth { get; set => SetProperty(ref field, value); } = 4;
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
    public DiffViewerViewModel(IDiffService diffs, ISystemInterop interop, ILogger<DiffViewerViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(diffs);
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentNullException.ThrowIfNull(logger);

        _diffs = diffs;
        _interop = interop;
        _logger = logger;

        ShowUnifiedCommand = new RelayCommand(() => ViewMode = DiffViewMode.Unified);
        ShowSideBySideCommand = new RelayCommand(() => ViewMode = DiffViewMode.SideBySide);

        ExpandContextCommand = new AsyncRelayCommand(
            () => SetContextAsync(Math.Min(ContextLines * ExpansionFactor, WholeFileContext)),
            () => HasPatch && ContextLines < WholeFileContext);

        ExpandAllContextCommand = new AsyncRelayCommand(
            () => SetContextAsync(WholeFileContext),
            () => HasPatch && ContextLines < WholeFileContext);

        ShowAnywayCommand = new AsyncRelayCommand(ShowAnywayAsync, () => IsTruncated);

        CopyPatchCommand = new AsyncRelayCommand(
            () => _interop.CopyTextAsync(_patch is null ? string.Empty : DiffRowBuilder.PatchText(_patch)),
            () => HasPatch);

        CopySelectionCommand = new AsyncRelayCommand(CopySelectionAsync, () => Selection.Count > 0);

        Selection.CollectionChanged += (_, _) => CopySelectionCommand.NotifyCanExecuteChanged();
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
    /// Gets or sets how the patch is laid out.
    /// </summary>
    public DiffViewMode ViewMode
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsUnified));
                OnPropertyChanged(nameof(IsSideBySide));
                Selection.Clear();
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
    public int ContextLines { get; private set => SetProperty(ref field, value); } = 3;

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
            ContextLines = ContextLines,
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

    private static string BuildSummary(FilePatch patch)
    {
        StringBuilder builder = new();

        builder.Append(CultureInfo.CurrentCulture, $"+{patch.AddedLines.ToString(CultureInfo.CurrentCulture)}");
        builder.Append(CultureInfo.CurrentCulture, $" −{patch.RemovedLines.ToString(CultureInfo.CurrentCulture)}");

        return builder.ToString();
    }
}
