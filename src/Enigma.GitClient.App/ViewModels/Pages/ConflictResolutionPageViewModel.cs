using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Merging;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.Icons.Phosphor;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// One line of a conflicted file, as a pane shows it.
/// </summary>
/// <remarks>
/// The terminator is stripped for display only. Everything written back comes from the document,
/// which still has it, so a pane can never change the file's line endings by rendering them.
/// </remarks>
public sealed class ConflictLineViewModel
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="number">The line's number within its side, or <see langword="null"/> for none.</param>
    /// <param name="text">The line, terminator included.</param>
    public ConflictLineViewModel(int? number, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Number = number is { } value ? value.ToString(CultureInfo.CurrentCulture) : string.Empty;
        Text = text.TrimEnd('\n').TrimEnd('\r');
    }

    /// <summary>Gets the line's number, shown in the gutter.</summary>
    public string Number { get; }

    /// <summary>Gets the line's text, without its terminator.</summary>
    public string Text { get; }

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// One region of the conflicted file: either text both sides agree on, or the disagreement to
/// settle and the six ways of settling it.
/// </summary>
public sealed class ConflictRegionViewModel : ViewModelBase
{
    private readonly ConflictResolutionPageViewModel _owner;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="owner">The page the region belongs to.</param>
    /// <param name="region">The region it stands for.</param>
    /// <param name="number">Which conflict this is, counting from one; zero for agreed text.</param>
    /// <param name="ourLine">How many lines of our side come before this region.</param>
    /// <param name="baseLine">How many lines of the base come before this region.</param>
    /// <param name="theirLine">How many lines of their side come before this region.</param>
    public ConflictRegionViewModel(
        ConflictResolutionPageViewModel owner,
        ConflictRegion region,
        int number,
        int ourLine = 0,
        int baseLine = 0,
        int theirLine = 0)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(region);

        _owner = owner;
        Region = region;
        Number = number;

        // Each side is numbered as its own file, because that is what each pane is showing: the
        // line numbers a reader would find if they opened that version in an editor.
        StableLines = Build(region.Stable, ourLine);
        OurLines = Build(region.OurLines, ourLine);
        BaseLines = Build(region.BaseLines, baseLine);
        TheirLines = Build(region.TheirLines, theirLine);
    }

    /// <summary>Gets the region this row stands for.</summary>
    public ConflictRegion Region { get; }

    /// <summary>Gets which conflict this is, counting from one.</summary>
    public int Number { get; }

    /// <summary>Gets a value indicating whether this region needs a decision.</summary>
    public bool IsConflicted => Region.IsConflicted;

    /// <summary>Gets a value indicating whether this region is text both sides agree on.</summary>
    public bool IsStable => !Region.IsConflicted;

    /// <summary>Gets the heading shown above the region's three panes.</summary>
    public string Title
        => $"Conflict {Number.ToString(CultureInfo.CurrentCulture)}";

    /// <summary>Gets the agreed lines, empty for a conflicted region.</summary>
    public IReadOnlyList<ConflictLineViewModel> StableLines { get; }

    /// <summary>Gets our version's lines.</summary>
    public IReadOnlyList<ConflictLineViewModel> OurLines { get; }

    /// <summary>Gets what both sides started from.</summary>
    public IReadOnlyList<ConflictLineViewModel> BaseLines { get; }

    /// <summary>Gets their version's lines.</summary>
    public IReadOnlyList<ConflictLineViewModel> TheirLines { get; }

    /// <summary>Gets a value indicating whether our side has anything at all.</summary>
    public bool HasOurs => OurLines.Count > 0;

    /// <summary>Gets a value indicating whether their side has anything at all.</summary>
    public bool HasTheirs => TheirLines.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the base pane has anything to show, which an add/add
    /// conflict does not.
    /// </summary>
    public bool HasBase => BaseLines.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the base pane is shown, which the page toggles for the whole
    /// file at once.
    /// </summary>
    public bool IsBaseVisible => _owner.ShowBase;

    /// <summary>
    /// Gets or sets what the reader chose to keep.
    /// </summary>
    public ConflictResolution Resolution
    {
        get => Region.Resolution;
        set
        {
            if (Region.Resolution == value)
            {
                return;
            }

            Region.Resolution = value;
            NotifyResolutionChanged();
            _owner.OnRegionChanged();
        }
    }

    /// <summary>Gets a value indicating whether a choice has been made.</summary>
    public bool IsResolved => IsConflicted && Region.Resolution != ConflictResolution.Unresolved;

    /// <summary>Gets a value indicating whether this region is still waiting for a decision.</summary>
    public bool NeedsDecision => Region.NeedsDecision;

    /// <summary>Gets a value indicating whether our side was chosen.</summary>
    public bool IsOurs => Region.Resolution == ConflictResolution.Ours;

    /// <summary>Gets a value indicating whether their side was chosen.</summary>
    public bool IsTheirs => Region.Resolution == ConflictResolution.Theirs;

    /// <summary>Gets a value indicating whether both were kept, ours first.</summary>
    public bool IsOursThenTheirs => Region.Resolution == ConflictResolution.OursThenTheirs;

    /// <summary>Gets a value indicating whether both were kept, theirs first.</summary>
    public bool IsTheirsThenOurs => Region.Resolution == ConflictResolution.TheirsThenOurs;

    /// <summary>Gets a value indicating whether the base was chosen.</summary>
    public bool IsBase => Region.Resolution == ConflictResolution.Base;

    /// <summary>Gets a value indicating whether the reader wrote the text themselves.</summary>
    public bool IsCustom => Region.Resolution == ConflictResolution.Custom;

    /// <summary>Gets the sentence describing the choice, shown on the region's pill.</summary>
    public string ChoiceLabel
        => Region.Resolution switch
        {
            ConflictResolution.Ours => "Keeping ours",
            ConflictResolution.Theirs => "Keeping theirs",
            ConflictResolution.OursThenTheirs => "Keeping both, ours first",
            ConflictResolution.TheirsThenOurs => "Keeping both, theirs first",
            ConflictResolution.Base => "Keeping the original",
            ConflictResolution.Custom => "Edited by hand",
            _ => "Undecided",
        };

    /// <summary>
    /// Gets or sets a value indicating whether the region is open in the editable box.
    /// </summary>
    public bool IsEditing { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// Gets or sets the text being edited, which becomes the region's content when applied.
    /// </summary>
    public string EditText { get; set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>Gets the command that keeps our version.</summary>
    public RelayCommand<ConflictRegionViewModel> TakeOursCommand => _owner.TakeOursCommand;

    /// <summary>Gets the command that keeps their version.</summary>
    public RelayCommand<ConflictRegionViewModel> TakeTheirsCommand => _owner.TakeTheirsCommand;

    /// <summary>Gets the command that keeps both, ours first.</summary>
    public RelayCommand<ConflictRegionViewModel> TakeBothOursFirstCommand => _owner.TakeBothOursFirstCommand;

    /// <summary>Gets the command that keeps both, theirs first.</summary>
    public RelayCommand<ConflictRegionViewModel> TakeBothTheirsFirstCommand => _owner.TakeBothTheirsFirstCommand;

    /// <summary>Gets the command that keeps what both sides started from.</summary>
    public RelayCommand<ConflictRegionViewModel> TakeBaseCommand => _owner.TakeBaseCommand;

    /// <summary>Gets the command that opens the region in the editable box.</summary>
    public RelayCommand<ConflictRegionViewModel> EditCommand => _owner.EditRegionCommand;

    /// <summary>Gets the command that keeps what was typed into the box.</summary>
    public RelayCommand<ConflictRegionViewModel> ApplyEditCommand => _owner.ApplyEditCommand;

    /// <summary>Gets the command that closes the box without keeping what was typed.</summary>
    public RelayCommand<ConflictRegionViewModel> CancelEditCommand => _owner.CancelEditCommand;

    /// <summary>
    /// Renders what this region currently contributes to the file.
    /// </summary>
    /// <param name="lineEnding">The file's terminator.</param>
    /// <returns>The text.</returns>
    public string Render(string lineEnding) => Region.Render(lineEnding);

    /// <summary>
    /// Raises every property that follows the choice. Called by the page when it sets a resolution
    /// without going through this row.
    /// </summary>
    public void NotifyResolutionChanged()
    {
        OnPropertyChanged(nameof(Resolution));
        OnPropertyChanged(nameof(IsResolved));
        OnPropertyChanged(nameof(NeedsDecision));
        OnPropertyChanged(nameof(IsOurs));
        OnPropertyChanged(nameof(IsTheirs));
        OnPropertyChanged(nameof(IsOursThenTheirs));
        OnPropertyChanged(nameof(IsTheirsThenOurs));
        OnPropertyChanged(nameof(IsBase));
        OnPropertyChanged(nameof(IsCustom));
        OnPropertyChanged(nameof(ChoiceLabel));
    }

    /// <summary>
    /// Raises the base pane's visibility, which the page owns for every region at once.
    /// </summary>
    public void NotifyBaseVisibility() => OnPropertyChanged(nameof(IsBaseVisible));

    /// <inheritdoc />
    public override string ToString() => IsConflicted ? $"{Title}: {ChoiceLabel}" : "agreed text";

    private static IReadOnlyList<ConflictLineViewModel> Build(IReadOnlyList<string> lines, int before)
    {
        if (lines.Count == 0)
        {
            return [];
        }

        List<ConflictLineViewModel> rows = new(lines.Count);

        for (int index = 0; index < lines.Count; index++)
        {
            rows.Add(new ConflictLineViewModel(before + index + 1, lines[index]));
        }

        return rows;
    }
}

/// <summary>
/// One conflicted file, as the page lists it.
/// </summary>
public sealed class ConflictFileRowViewModel : ViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="file">The file it stands for.</param>
    public ConflictFileRowViewModel(ConflictFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        File = file;
    }

    /// <summary>Gets the file this row stands for.</summary>
    public ConflictFile File { get; }

    /// <summary>Gets the file's path, relative to the work tree.</summary>
    public string Path => File.Path;

    /// <summary>Gets the file's name.</summary>
    public string Name
    {
        get
        {
            int slash = File.Path.LastIndexOf('/');
            return slash < 0 ? File.Path : File.Path[(slash + 1)..];
        }
    }

    /// <summary>Gets the folder the file is in, empty at the root.</summary>
    public string Directory
    {
        get
        {
            int slash = File.Path.LastIndexOf('/');
            return slash < 0 ? string.Empty : File.Path[..slash];
        }
    }

    /// <summary>Gets a value indicating whether there is a folder to show.</summary>
    public bool HasDirectory => Directory.Length > 0;

    /// <summary>Gets the sentence describing the conflict.</summary>
    public string Description => File.Description;

    /// <summary>Gets a value indicating whether the file has no text to merge.</summary>
    public bool IsBinary => File.IsBinary;

    /// <summary>Gets the icon standing for the kind of conflict.</summary>
    public PhosphorIcon Icon
        => File.Kind switch
        {
            ConflictKind.BothAdded => PhosphorIcon.FilePlus,
            ConflictKind.AddedByUs => PhosphorIcon.FilePlus,
            ConflictKind.AddedByThem => PhosphorIcon.FilePlus,
            ConflictKind.DeletedByUs => PhosphorIcon.FileX,
            ConflictKind.DeletedByThem => PhosphorIcon.FileX,
            ConflictKind.BothDeleted => PhosphorIcon.FileX,
            _ => File.IsBinary ? PhosphorIcon.FileImage : PhosphorIcon.FileText,
        };

    /// <summary>Gets the short state shown on the row's pill.</summary>
    public string StateLabel
        => IsResolved
            ? "Resolved"
            : File.Kind switch
            {
                ConflictKind.BothModified => File.IsBinary ? "Both changed, binary" : "Both changed",
                ConflictKind.BothAdded => "Both added",
                ConflictKind.AddedByUs => "Added by us",
                ConflictKind.AddedByThem => "Added by them",
                ConflictKind.DeletedByUs => "Deleted by us",
                ConflictKind.DeletedByThem => "Deleted by them",
                ConflictKind.BothDeleted => "Both deleted",
                _ => File.State,
            };

    /// <summary>
    /// Gets or sets a value indicating whether the file has been resolved during this merge.
    /// </summary>
    public bool IsResolved
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(StateLabel));
                OnPropertyChanged(nameof(IsUnresolved));
            }
        }
    }

    /// <summary>Gets a value indicating whether the file still needs work.</summary>
    public bool IsUnresolved => !IsResolved;

    /// <inheritdoc />
    public override string ToString() => $"{StateLabel} {Path}";
}

/// <summary>
/// ViewModel behind the conflict resolution page: the files left to settle, the selected file's
/// regions, and the preview of exactly what will be written.
/// </summary>
/// <remarks>
/// <para>
/// The regions are rendered as one list rather than as three independently scrolling panes. Each
/// conflicted region is a card carrying our version, the base and theirs side by side with the
/// choices between them, so the three columns are aligned by construction — there is no scroll
/// synchronisation to drift, and a region's controls are beside the lines they decide.
/// </para>
/// <para>
/// Nothing is written until the file is saved. Until then a choice only changes the preview, which
/// is rendered by the same method that writes the file, so what the pane shows is what lands on
/// disk.
/// </para>
/// </remarks>
public sealed class ConflictResolutionPageViewModel : PageViewModelBase
{
    private readonly IConflictService _conflicts;
    private readonly IMergeService _merges;
    private readonly IMergeOperations _mergeOperations;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<ConflictResolutionPageViewModel> _logger;

    private readonly Dictionary<string, ConflictFile> _seen = [];
    private readonly HashSet<string> _resolvedPaths = [];

    private ConflictDocument? _document;
    private MergeHeads _heads = MergeHeads.None;
    private int _loadGeneration;
    private bool _read;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="repositoryContext">The repository the application is looking at.</param>
    /// <param name="conflicts">Reads the conflicts and writes the resolutions.</param>
    /// <param name="merges">Names the two sides of the merge in progress.</param>
    /// <param name="mergeOperations">Commits or abandons the merge, with its confirmations.</param>
    /// <param name="dialogs">Raises the confirmations this page asks itself.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public ConflictResolutionPageViewModel(
        IRepositoryContext repositoryContext,
        IConflictService conflicts,
        IMergeService merges,
        IMergeOperations mergeOperations,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<ConflictResolutionPageViewModel> logger)
        : base(repositoryContext)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        ArgumentNullException.ThrowIfNull(merges);
        ArgumentNullException.ThrowIfNull(mergeOperations);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _conflicts = conflicts;
        _merges = merges;
        _mergeOperations = mergeOperations;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _logger = logger;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsRepositoryOpen);

        TakeOursCommand = new RelayCommand<ConflictRegionViewModel>(row => Choose(row, ConflictResolution.Ours));
        TakeTheirsCommand = new RelayCommand<ConflictRegionViewModel>(row => Choose(row, ConflictResolution.Theirs));
        TakeBothOursFirstCommand = new RelayCommand<ConflictRegionViewModel>(row => Choose(row, ConflictResolution.OursThenTheirs));
        TakeBothTheirsFirstCommand = new RelayCommand<ConflictRegionViewModel>(row => Choose(row, ConflictResolution.TheirsThenOurs));
        TakeBaseCommand = new RelayCommand<ConflictRegionViewModel>(row => Choose(row, ConflictResolution.Base));

        EditRegionCommand = new RelayCommand<ConflictRegionViewModel>(OnEditRegion);
        ApplyEditCommand = new RelayCommand<ConflictRegionViewModel>(OnApplyEdit);
        CancelEditCommand = new RelayCommand<ConflictRegionViewModel>(OnCancelEdit);

        TakeAllOursCommand = new RelayCommand(() => ResolveEveryRegion(ConflictResolution.Ours), () => HasDocument);
        TakeAllTheirsCommand = new RelayCommand(() => ResolveEveryRegion(ConflictResolution.Theirs), () => HasDocument);
        ClearChoicesCommand = new RelayCommand(() => ResolveEveryRegion(ConflictResolution.Unresolved), () => HasDocument);

        SaveCommand = new AsyncRelayCommand(OnSaveAsync, () => CanSave);
        KeepOursCommand = new AsyncRelayCommand(() => OnKeepSideAsync(ConflictSide.Ours), () => HasSelection);
        KeepTheirsCommand = new AsyncRelayCommand(() => OnKeepSideAsync(ConflictSide.Theirs), () => HasSelection);

        TakeEveryFileOursCommand = new AsyncRelayCommand(() => OnTakeEveryFileAsync(ConflictSide.Ours), () => HasUnresolved);
        TakeEveryFileTheirsCommand = new AsyncRelayCommand(() => OnTakeEveryFileAsync(ConflictSide.Theirs), () => HasUnresolved);

        CommitMergeCommand = new AsyncRelayCommand(OnCommitMergeAsync, () => CanCommitMerge);
        AbortMergeCommand = new AsyncRelayCommand(OnAbortMergeAsync, () => IsMergeInProgress);

        TakeOursForSelectedCommand = new RelayCommand(() => Choose(SelectedRegion, ConflictResolution.Ours));
        TakeTheirsForSelectedCommand = new RelayCommand(() => Choose(SelectedRegion, ConflictResolution.Theirs));
        NextUnresolvedCommand = new RelayCommand(OnNextUnresolved);

        // Subscribed from the constructor rather than on first appearance: the shell's banner shows
        // this page's progress, so it has to be right before the page has ever been visited.
        EnsureSubscribed();
    }

    /// <summary>Gets the page's title, shown in its header.</summary>
    public string Title => "Resolve conflicts";

    /// <summary>Gets the conflicted files, in path order.</summary>
    public ObservableCollection<ConflictFileRowViewModel> Files { get; } = [];

    /// <summary>Gets the selected file's regions, in file order.</summary>
    public ObservableCollection<ConflictRegionViewModel> Regions { get; } = [];

    /// <summary>
    /// Gets or sets the file being resolved.
    /// </summary>
    public ConflictFileRowViewModel? SelectedFile
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedPath));
                KeepOursCommand.NotifyCanExecuteChanged();
                KeepTheirsCommand.NotifyCanExecuteChanged();

                _ = LoadDocumentAsync();
            }
        }
    }

    /// <summary>
    /// Gets or sets the region the keyboard acts on.
    /// </summary>
    public ConflictRegionViewModel? SelectedRegion { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// Gets or sets a value indicating whether the base pane is shown between the two sides.
    /// </summary>
    public bool ShowBase
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                foreach (ConflictRegionViewModel region in Regions)
                {
                    region.NotifyBaseVisibility();
                }
            }
        }
    } = true;

    /// <summary>
    /// Gets the exact text the file will contain, rendered from the same method that writes it.
    /// </summary>
    public string Preview { get; private set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>Gets a value indicating whether a file is selected.</summary>
    public bool HasSelection => SelectedFile is not null;

    /// <summary>Gets the selected file's path, for the pane header.</summary>
    public string SelectedPath => SelectedFile?.Path ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether the selected file can be merged line by line.
    /// </summary>
    public bool HasDocument => _document is not null;

    /// <summary>
    /// Gets a value indicating whether the selected file's three versions are still being read.
    /// </summary>
    public bool IsLoadingFile { get; private set => SetProperty(ref field, value); }

    /// <summary>
    /// Gets a value indicating whether the selected file is a whole-file choice: a binary file, or
    /// one side that no longer exists.
    /// </summary>
    /// <remarks>
    /// Never while the file is still being read. "There is no document" is true of a file that has
    /// no line-by-line merge and of one that has simply not been read yet, and offering the
    /// whole-file choice for the second would flash the wrong card at every selection.
    /// </remarks>
    public bool IsWholeFileChoice => SelectedFile is { IsResolved: false } && !IsLoadingFile && _document is null;

    /// <summary>
    /// Gets a value indicating whether the selected file has already been resolved.
    /// </summary>
    public bool IsSelectionResolved => SelectedFile?.IsResolved ?? false;

    /// <summary>Gets the sentence explaining a whole-file choice.</summary>
    public string WholeFileMessage
        => SelectedFile is null
            ? string.Empty
            : SelectedFile.IsBinary
                ? SelectedFile.Description + " It has no text to merge line by line, so keep one version or the other."
                : SelectedFile.Description + " There is no second version to merge with, so keep one side or the other.";

    /// <summary>Gets what keeping our side does, which for a deletion is removing the file.</summary>
    public string KeepOursDescription
        => SelectedFile?.File.Kind == ConflictKind.DeletedByUs
            ? "Keep ours (delete the file)"
            : "Keep ours";

    /// <summary>Gets what keeping their side does, which for a deletion is removing the file.</summary>
    public string KeepTheirsDescription
        => SelectedFile?.File.Kind == ConflictKind.DeletedByThem
            ? "Keep theirs (delete the file)"
            : "Keep theirs";

    /// <summary>Gets the label of our side, naming the branch being merged into.</summary>
    public string OursLabel
        => _heads.OursName.Length > 0 ? $"Ours — {_heads.OursName}" : "Ours";

    /// <summary>Gets the label of their side, naming the branch being merged in.</summary>
    public string TheirsLabel
        => _heads.TheirsName.Length > 0 ? $"Theirs — {_heads.TheirsName}" : "Theirs";

    /// <summary>Gets the label of the base pane.</summary>
    public string BaseLabel => "Original";

    /// <summary>Gets a value indicating whether a merge is waiting to be finished or abandoned.</summary>
    public bool IsMergeInProgress => RepositoryContext.Head?.Operation == RepositoryOperation.Merge;

    /// <summary>Gets how many files this merge has conflicted on.</summary>
    public int FileCount => Files.Count;

    /// <summary>Gets how many of them have been resolved.</summary>
    public int ResolvedFileCount
    {
        get
        {
            int count = 0;

            foreach (ConflictFileRowViewModel row in Files)
            {
                if (row.IsResolved)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Gets a value indicating whether anything is still unresolved.</summary>
    public bool HasUnresolved => ResolvedFileCount < Files.Count;

    /// <summary>Gets a value indicating whether there is anything on this page at all.</summary>
    public bool HasConflicts => Files.Count > 0;

    /// <summary>
    /// Gets the progress sentence, which the page's header and the shell's banner both show.
    /// </summary>
    public string Progress
    {
        get
        {
            if (Files.Count == 0)
            {
                return IsMergeInProgress ? "Nothing left to resolve." : string.Empty;
            }

            return $"{ResolvedFileCount.ToString(CultureInfo.CurrentCulture)} of "
                + $"{Files.Count.ToString(CultureInfo.CurrentCulture)} "
                + (Files.Count == 1 ? "file resolved" : "files resolved");
        }
    }

    /// <summary>
    /// Gets the selected file's own progress, shown above its regions.
    /// </summary>
    public string RegionProgress
    {
        get
        {
            if (_document is null)
            {
                return string.Empty;
            }

            return $"{_document.ResolvedCount.ToString(CultureInfo.CurrentCulture)} of "
                + $"{_document.ConflictCount.ToString(CultureInfo.CurrentCulture)} "
                + (_document.ConflictCount == 1 ? "conflict resolved" : "conflicts resolved");
        }
    }

    /// <summary>
    /// Gets a value indicating whether the selected file is ready to be written back.
    /// </summary>
    public bool CanSave => _document is not null && _document.IsFullyResolved && !IsBusy;

    /// <summary>
    /// Gets a value indicating whether the merge can be committed: every file resolved, and a merge
    /// to commit at all.
    /// </summary>
    /// <remarks>
    /// The conflicts have to have been read at least once. Before that the list is empty because
    /// nothing has looked, not because there is nothing left, and the two must not offer the same
    /// button.
    /// </remarks>
    public bool CanCommitMerge => IsMergeInProgress && _read && !HasUnresolved && !IsBusy;

    /// <summary>Gets the command that re-reads the conflicts.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Gets the command that keeps our version of a region.</summary>
    public RelayCommand<ConflictRegionViewModel> TakeOursCommand { get; }

    /// <summary>Gets the command that keeps their version of a region.</summary>
    public RelayCommand<ConflictRegionViewModel> TakeTheirsCommand { get; }

    /// <summary>Gets the command that keeps both versions of a region, ours first.</summary>
    public RelayCommand<ConflictRegionViewModel> TakeBothOursFirstCommand { get; }

    /// <summary>Gets the command that keeps both versions of a region, theirs first.</summary>
    public RelayCommand<ConflictRegionViewModel> TakeBothTheirsFirstCommand { get; }

    /// <summary>Gets the command that keeps what both sides started from.</summary>
    public RelayCommand<ConflictRegionViewModel> TakeBaseCommand { get; }

    /// <summary>Gets the command that opens a region in the editable box.</summary>
    public RelayCommand<ConflictRegionViewModel> EditRegionCommand { get; }

    /// <summary>Gets the command that keeps what was typed into the box.</summary>
    public RelayCommand<ConflictRegionViewModel> ApplyEditCommand { get; }

    /// <summary>Gets the command that closes the box without keeping what was typed.</summary>
    public RelayCommand<ConflictRegionViewModel> CancelEditCommand { get; }

    /// <summary>Gets the command that keeps our version of every region in the file.</summary>
    public RelayCommand TakeAllOursCommand { get; }

    /// <summary>Gets the command that keeps their version of every region in the file.</summary>
    public RelayCommand TakeAllTheirsCommand { get; }

    /// <summary>Gets the command that undoes every choice made in the file.</summary>
    public RelayCommand ClearChoicesCommand { get; }

    /// <summary>Gets the command that writes the resolved file and stages it.</summary>
    public AsyncRelayCommand SaveCommand { get; }

    /// <summary>Gets the command that keeps our whole version of the selected file.</summary>
    public AsyncRelayCommand KeepOursCommand { get; }

    /// <summary>Gets the command that keeps their whole version of the selected file.</summary>
    public AsyncRelayCommand KeepTheirsCommand { get; }

    /// <summary>Gets the command that keeps our side of every conflicted file.</summary>
    public AsyncRelayCommand TakeEveryFileOursCommand { get; }

    /// <summary>Gets the command that keeps their side of every conflicted file.</summary>
    public AsyncRelayCommand TakeEveryFileTheirsCommand { get; }

    /// <summary>Gets the command that records the merge commit.</summary>
    public AsyncRelayCommand CommitMergeCommand { get; }

    /// <summary>Gets the command that abandons the merge.</summary>
    public AsyncRelayCommand AbortMergeCommand { get; }

    /// <summary>Gets the keyboard command that keeps our version of the focused region.</summary>
    public RelayCommand TakeOursForSelectedCommand { get; }

    /// <summary>Gets the keyboard command that keeps their version of the focused region.</summary>
    public RelayCommand TakeTheirsForSelectedCommand { get; }

    /// <summary>Gets the keyboard command that moves to the next region needing a decision.</summary>
    public RelayCommand NextUnresolvedCommand { get; }

    /// <inheritdoc />
    public override async Task OnAppearingAsync(object? parameter = null)
    {
        await base.OnAppearingAsync(parameter).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Re-reads the conflicts and rebuilds the file list.
    /// </summary>
    /// <returns>A task that completes once the page is up to date.</returns>
    public async Task RefreshAsync()
    {
        RepositoryHandle? repository = RepositoryContext.Repository;

        if (repository is null)
        {
            Apply([], MergeHeads.None);
            return;
        }

        try
        {
            CancellationToken token = RepositoryContext.RepositoryLifetime;

            IReadOnlyList<ConflictFile> files = await _conflicts
                .GetConflictsAsync(repository, token)
                .ConfigureAwait(true);

            MergeHeads heads = await _merges.GetMergeHeadsAsync(repository, token).ConfigureAwait(true);

            Apply(files, heads);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository changes under the read.
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Reading the conflicts failed");
            Apply([], MergeHeads.None);
        }
    }

    /// <summary>
    /// Called by a region when its choice changed: the preview, the counters and the commands all
    /// follow from it.
    /// </summary>
    public void OnRegionChanged()
    {
        UpdatePreview();

        OnPropertyChanged(nameof(RegionProgress));
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <inheritdoc />
    protected override void OnRepositoryChanged()
    {
        base.OnRepositoryChanged();

        _seen.Clear();
        _resolvedPaths.Clear();

        RefreshCommand.NotifyCanExecuteChanged();

        _ = RefreshAsync();
    }

    /// <inheritdoc />
    protected override void OnRepositoryStateRefreshed()
    {
        // Only while there is something to show: every other repository refresh would otherwise pay
        // for a status read this page has no use for.
        if (IsMergeInProgress || Files.Count > 0)
        {
            _ = RefreshAsync();
        }
    }

    /// <inheritdoc />
    protected override void OnBusyChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanCommitMerge));
        SaveCommand.NotifyCanExecuteChanged();
        CommitMergeCommand.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------- loading

    private void Apply(IReadOnlyList<ConflictFile> files, MergeHeads heads)
    {
        _heads = heads;
        _read = true;

        if (!heads.Exists)
        {
            // The merge is over — committed or abandoned. Nothing about the last one is worth
            // keeping on screen.
            _seen.Clear();
            _resolvedPaths.Clear();
        }
        else
        {
            foreach (ConflictFile file in files)
            {
                _seen[file.Path] = file;

                // A conflict that came back — a resolution undone outside the app — is unresolved
                // again, whatever this page believed.
                _resolvedPaths.Remove(file.Path);
            }

            foreach (string path in _seen.Keys)
            {
                if (!Contains(files, path))
                {
                    _resolvedPaths.Add(path);
                }
            }
        }

        string? previous = SelectedFile?.Path;

        List<ConflictFileRowViewModel> rows = [];

        if (heads.Exists)
        {
            List<string> paths = [.. _seen.Keys];
            paths.Sort(StringComparer.Ordinal);

            foreach (string path in paths)
            {
                rows.Add(new ConflictFileRowViewModel(_seen[path])
                {
                    IsResolved = _resolvedPaths.Contains(path),
                });
            }
        }

        // Read first, replace after: clearing before the rows are built lets a second refresh
        // interleave and show the same file twice.
        Files.Clear();

        foreach (ConflictFileRowViewModel row in rows)
        {
            Files.Add(row);
        }

        NotifyCounts();

        SelectedFile = Choose(rows, previous);
    }

    private static bool Contains(IReadOnlyList<ConflictFile> files, string path)
    {
        foreach (ConflictFile file in files)
        {
            if (string.Equals(file.Path, path, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Picks the row to select after a refresh: the one that was selected if it is still there,
    /// otherwise the first file still needing work.
    /// </summary>
    private static ConflictFileRowViewModel? Choose(List<ConflictFileRowViewModel> rows, string? previous)
    {
        if (previous is not null)
        {
            foreach (ConflictFileRowViewModel row in rows)
            {
                if (string.Equals(row.Path, previous, StringComparison.Ordinal))
                {
                    return row;
                }
            }
        }

        foreach (ConflictFileRowViewModel row in rows)
        {
            if (!row.IsResolved)
            {
                return row;
            }
        }

        return rows.Count > 0 ? rows[0] : null;
    }

    private async Task LoadDocumentAsync()
    {
        int generation = ++_loadGeneration;

        _document = null;
        Regions.Clear();
        Preview = string.Empty;
        SelectedRegion = null;

        ConflictFileRowViewModel? row = SelectedFile;
        RepositoryHandle? repository = RepositoryContext.Repository;

        if (row is null || repository is null || row.IsResolved)
        {
            IsLoadingFile = false;
            NotifyDocument();
            return;
        }

        IsLoadingFile = true;

        ConflictDocument? document = null;

        try
        {
            document = await _conflicts
                .GetDocumentAsync(repository, row.Path, RepositoryContext.RepositoryLifetime)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Reading the conflicted file {Path} failed", row.Path);
        }

        if (generation != _loadGeneration)
        {
            // A different file was selected while this one was being read.
            return;
        }

        IsLoadingFile = false;
        _document = document;

        if (document is not null)
        {
            int number = 0;
            int ourLine = 0;
            int baseLine = 0;
            int theirLine = 0;

            foreach (ConflictRegion region in document.Regions)
            {
                Regions.Add(new ConflictRegionViewModel(
                    this,
                    region,
                    region.IsConflicted ? ++number : 0,
                    ourLine,
                    baseLine,
                    theirLine));

                if (region.IsConflicted)
                {
                    ourLine += region.OurLines.Count;
                    baseLine += region.BaseLines.Count;
                    theirLine += region.TheirLines.Count;
                }
                else
                {
                    ourLine += region.Stable.Count;
                    baseLine += region.Stable.Count;
                    theirLine += region.Stable.Count;
                }
            }

            UpdatePreview();
            SelectedRegion = FirstUnresolved();
        }

        NotifyDocument();
    }

    private void UpdatePreview() => Preview = _document?.RenderPreview() ?? string.Empty;

    private void NotifyDocument()
    {
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(IsLoadingFile));
        OnPropertyChanged(nameof(IsWholeFileChoice));
        OnPropertyChanged(nameof(IsSelectionResolved));
        OnPropertyChanged(nameof(WholeFileMessage));
        OnPropertyChanged(nameof(KeepOursDescription));
        OnPropertyChanged(nameof(KeepTheirsDescription));
        OnPropertyChanged(nameof(RegionProgress));
        OnPropertyChanged(nameof(CanSave));

        SaveCommand.NotifyCanExecuteChanged();
        TakeAllOursCommand.NotifyCanExecuteChanged();
        TakeAllTheirsCommand.NotifyCanExecuteChanged();
        ClearChoicesCommand.NotifyCanExecuteChanged();
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(ResolvedFileCount));
        OnPropertyChanged(nameof(HasUnresolved));
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(IsMergeInProgress));
        OnPropertyChanged(nameof(CanCommitMerge));
        OnPropertyChanged(nameof(OursLabel));
        OnPropertyChanged(nameof(TheirsLabel));

        CommitMergeCommand.NotifyCanExecuteChanged();
        AbortMergeCommand.NotifyCanExecuteChanged();
        TakeEveryFileOursCommand.NotifyCanExecuteChanged();
        TakeEveryFileTheirsCommand.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------- region commands

    private static void Choose(ConflictRegionViewModel? region, ConflictResolution resolution)
    {
        if (region is { IsConflicted: true })
        {
            region.Resolution = resolution;
        }
    }

    private void ResolveEveryRegion(ConflictResolution resolution)
    {
        if (_document is null)
        {
            return;
        }

        _document.ResolveAll(resolution);

        foreach (ConflictRegionViewModel region in Regions)
        {
            region.NotifyResolutionChanged();
        }

        OnRegionChanged();
    }

    private void OnEditRegion(ConflictRegionViewModel? region)
    {
        if (region is not { IsConflicted: true } || _document is null)
        {
            return;
        }

        // The box opens on what the region currently contributes, so editing starts from the choice
        // already made rather than from an empty field.
        string text = region.Resolution == ConflictResolution.Unresolved
            ? string.Concat(region.Region.OurLines)
            : region.Render(_document.LineEnding);

        region.EditText = text;
        region.IsEditing = true;
    }

    private void OnApplyEdit(ConflictRegionViewModel? region)
    {
        if (region is not { IsConflicted: true })
        {
            return;
        }

        region.Region.CustomText = region.EditText;
        region.IsEditing = false;
        region.Resolution = ConflictResolution.Custom;

        // Custom is the one resolution whose text can change without the value changing, so the
        // preview is refreshed even when the region was already Custom.
        region.NotifyResolutionChanged();
        OnRegionChanged();
    }

    private static void OnCancelEdit(ConflictRegionViewModel? region)
    {
        if (region is not null)
        {
            region.IsEditing = false;
        }
    }

    private ConflictRegionViewModel? FirstUnresolved()
    {
        foreach (ConflictRegionViewModel region in Regions)
        {
            if (region.NeedsDecision)
            {
                return region;
            }
        }

        return null;
    }

    private void OnNextUnresolved()
    {
        bool passedCurrent = SelectedRegion is null;

        foreach (ConflictRegionViewModel region in Regions)
        {
            if (!passedCurrent)
            {
                passedCurrent = ReferenceEquals(region, SelectedRegion);
                continue;
            }

            if (region.NeedsDecision)
            {
                SelectedRegion = region;
                return;
            }
        }

        // Past the last one, start again from the top, so the shortcut always lands somewhere while
        // anything is left.
        SelectedRegion = FirstUnresolved();
    }

    // ---------------------------------------------------------------- file commands

    private async Task OnSaveAsync()
    {
        ConflictFileRowViewModel? row = SelectedFile;

        if (row is null || _document is null)
        {
            return;
        }

        string text = _document.RenderPreview();
        string path = row.Path;

        bool saved = await RunAsync(
            (handle, token) => _conflicts.ResolveAsync(handle, path, text, token),
            "Could not save the resolution").ConfigureAwait(true);

        if (saved)
        {
            await ReportAsync("File resolved", $"{path} is staged for the merge commit.", InfoBarSeverity.Success)
                .ConfigureAwait(true);
        }
    }

    private async Task OnKeepSideAsync(ConflictSide side)
    {
        ConflictFileRowViewModel? row = SelectedFile;

        if (row is null)
        {
            return;
        }

        string path = row.Path;

        bool kept = await RunAsync(
            (handle, token) => _conflicts.ResolveWithAsync(handle, path, side, token),
            "Could not resolve the file").ConfigureAwait(true);

        if (kept)
        {
            await ReportAsync(
                "File resolved",
                $"{path} keeps {(side == ConflictSide.Ours ? "our" : "their")} version.",
                InfoBarSeverity.Success).ConfigureAwait(true);
        }
    }

    private async Task OnTakeEveryFileAsync(ConflictSide side)
    {
        string which = side == ConflictSide.Ours ? "ours" : "theirs";

        bool confirmed = await ConfirmAsync(
            $"Keep {which} everywhere",
            $"Resolve every conflicted file by keeping {which}, whole?\n\n"
            + "Choices you have made but not saved are thrown away. This does not commit the merge.")
            .ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        IReadOnlyList<string> resolved = [];

        bool done = await RunAsync(
            async (handle, token) =>
                resolved = await _conflicts.ResolveAllWithAsync(handle, side, token).ConfigureAwait(false),
            "Could not resolve the conflicts").ConfigureAwait(true);

        if (done)
        {
            await ReportAsync(
                "Conflicts resolved",
                $"{Count(resolved.Count)} now keep{(resolved.Count == 1 ? "s" : string.Empty)} {which}.",
                InfoBarSeverity.Success).ConfigureAwait(true);
        }
    }

    private async Task OnCommitMergeAsync()
    {
        IsBusy = true;

        try
        {
            await _mergeOperations.ContinueAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }

        await RepositoryContext.RefreshAsync().ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task OnAbortMergeAsync()
    {
        IsBusy = true;

        try
        {
            await _mergeOperations.AbortAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }

        await RepositoryContext.RefreshAsync().ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    // ---------------------------------------------------------------- plumbing

    private async Task<bool> RunAsync(
        Func<RepositoryHandle, CancellationToken, Task> operation,
        string failureTitle)
    {
        if (!IsRepositoryOpen)
        {
            return false;
        }

        IsBusy = true;

        try
        {
            await RepositoryContext.RunExclusiveAsync(operation).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);

            return true;
        }
        catch (GitOperationRefusedException refusal)
        {
            await ReportAsync(failureTitle, refusal.Message, InfoBarSeverity.Warning).ConfigureAwait(true);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "{Title}", failureTitle);

            await ReportAsync(failureTitle, FirstLine(exception.StandardError), InfoBarSeverity.Error)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the operation.
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        DialogResult result = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = title;
            dialog.Content = message;
            dialog.PrimaryButtonText = "Continue";
            dialog.CloseButtonText = "Cancel";
            dialog.DefaultButton = DefaultButton.Close;
        }).ConfigureAwait(true);

        return result == DialogResult.Primary;
    }

    private Task ReportAsync(string title, string message, InfoBarSeverity severity)
        => _infoBar.ShowAsync(bar =>
        {
            bar.Title = title;
            bar.Message = message;
            bar.Severity = severity;
        });

    private static string Count(int files)
        => files == 1 ? "1 file" : $"{files.ToString(CultureInfo.CurrentCulture)} files";

    private static string FirstLine(string text)
    {
        foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return "git reported no reason.";
    }
}
