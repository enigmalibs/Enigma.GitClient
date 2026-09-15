using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.App.Services;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;

namespace Enigma.GitClient.App.ViewModels.Panels;

/// <summary>
/// How the changed files are arranged.
/// </summary>
public enum ChangedFilesViewMode
{
    /// <summary>One flat row per file, each showing its whole path.</summary>
    List,

    /// <summary>Files nested under their directories.</summary>
    Tree,
}

/// <summary>
/// The two actions a host page puts on every row of a changed-files panel.
/// </summary>
/// <remarks>
/// The panel itself has no opinion about what should happen to a file — in the history it is
/// something to read, on the changes page it is something to stage or throw away. The host supplies
/// the verbs; the panel only draws them.
/// </remarks>
/// <param name="PrimaryLabel">What the first action is called.</param>
/// <param name="Primary">The first action.</param>
/// <param name="SecondaryLabel">What the second action is called, or <see langword="null"/>.</param>
/// <param name="Secondary">The second action, or <see langword="null"/>.</param>
/// <param name="PrimaryIcon">The Phosphor icon name shown on the first action's button.</param>
public sealed record ChangedFileRowActions(
    string PrimaryLabel,
    AsyncRelayCommand<ChangedFileNodeViewModel> Primary,
    string? SecondaryLabel = null,
    AsyncRelayCommand<ChangedFileNodeViewModel>? Secondary = null,
    string PrimaryIcon = "Plus");

/// <summary>
/// One row of the changed-files panel, in either shape.
/// </summary>
/// <remarks>
/// The same row type serves the list and the tree, which is what lets the two views share a
/// selection and never disagree about what is selected.
/// </remarks>
public sealed class ChangedFileNodeViewModel : ViewModelBase
{
    private readonly ChangedFilesPanelViewModel _owner;

    /// <summary>
    /// Initialises a file row.
    /// </summary>
    /// <param name="owner">The panel the row belongs to, whose commands the row's menu runs.</param>
    /// <param name="file">The changed file.</param>
    /// <param name="label">What the row displays.</param>
    /// <param name="showDirectory">
    /// Whether the directory is shown beside the name. The flat list needs it to tell two files of
    /// the same name apart; the tree already says it in the parent row, where repeating it is noise.
    /// </param>
    public ChangedFileNodeViewModel(
        ChangedFilesPanelViewModel owner,
        ChangedFile file,
        string label,
        bool showDirectory = false)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(file);

        _owner = owner;
        File = file;
        Label = label;
        Children = [];
        ShowDirectory = showDirectory;
    }

    /// <summary>
    /// Initialises a directory row.
    /// </summary>
    /// <param name="owner">The panel the row belongs to, whose commands the row's menu runs.</param>
    /// <param name="label">What the row displays.</param>
    /// <param name="fullPath">The directory's path.</param>
    /// <param name="counts">What the directory contains.</param>
    /// <param name="children">The rows beneath it.</param>
    /// <param name="isExpanded">Whether the row starts open.</param>
    public ChangedFileNodeViewModel(
        ChangedFilesPanelViewModel owner,
        string label,
        string fullPath,
        FileTreeCounts counts,
        IReadOnlyList<ChangedFileNodeViewModel> children,
        bool isExpanded)
    {
        ArgumentNullException.ThrowIfNull(owner);

        _owner = owner;
        Label = label;
        DirectoryPath = fullPath;
        Counts = counts;
        Children = children;
        IsExpanded = isExpanded;
    }

    /// <summary>Gets the command that copies the row's path, for the row's own menu.</summary>
    public AsyncRelayCommand<ChangedFileNodeViewModel> CopyPathCommand => _owner.CopyPathCommand;

    /// <summary>Gets the command that copies the row's file name.</summary>
    public AsyncRelayCommand<ChangedFileNodeViewModel> CopyNameCommand => _owner.CopyNameCommand;

    /// <summary>Gets the command that opens the row's file.</summary>
    public AsyncRelayCommand<ChangedFileNodeViewModel> OpenFileCommand => _owner.OpenFileCommand;

    /// <summary>Gets the command that shows the row's file in the file manager.</summary>
    public AsyncRelayCommand<ChangedFileNodeViewModel> RevealFileCommand => _owner.RevealFileCommand;

    /// <summary>Gets the actions the host page put on the row, if any.</summary>
    public ChangedFileRowActions? Actions => _owner.Actions;

    /// <summary>Gets a value indicating whether the row has a host action to offer.</summary>
    public bool HasActions => Actions is not null;

    /// <summary>Gets a value indicating whether the row has a second host action.</summary>
    public bool HasSecondaryAction => Actions?.Secondary is not null;

    /// <summary>
    /// Gets the file this row stands for, or <see langword="null"/> for a directory.
    /// </summary>
    public ChangedFile? File { get; }

    /// <summary>
    /// Gets what the row displays: a file name, or a directory (possibly a collapsed chain).
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the directory's path, empty for a file row.
    /// </summary>
    public string DirectoryPath { get; } = string.Empty;

    /// <summary>
    /// Gets the rows beneath this one.
    /// </summary>
    public IReadOnlyList<ChangedFileNodeViewModel> Children { get; }

    /// <summary>
    /// Gets what a directory row contains.
    /// </summary>
    public FileTreeCounts Counts { get; }

    /// <summary>
    /// Gets a value indicating whether this row is a directory.
    /// </summary>
    public bool IsDirectory => File is null;

    /// <summary>
    /// Gets or sets a value indicating whether a directory row is expanded.
    /// </summary>
    public bool IsExpanded { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// Gets the file's path, used for selection and for asking for its patch.
    /// </summary>
    public string Path => File?.Path ?? DirectoryPath;

    /// <summary>
    /// Gets the single letter git uses for the change, which the row shows in a coloured chip.
    /// </summary>
    public string StatusGlyph
        => File?.ChangeKind switch
        {
            FileChangeKind.Added => "A",
            FileChangeKind.Modified => "M",
            FileChangeKind.Deleted => "D",
            FileChangeKind.Renamed => "R",
            FileChangeKind.Copied => "C",
            FileChangeKind.TypeChanged => "T",
            FileChangeKind.ModeChanged => "M",
            FileChangeKind.Unmerged => "U",
            _ => "?",
        };

    /// <summary>Gets a value indicating whether the file was added.</summary>
    public bool IsAdded => File?.ChangeKind == FileChangeKind.Added;

    /// <summary>Gets a value indicating whether the file was deleted.</summary>
    public bool IsDeleted => File?.ChangeKind == FileChangeKind.Deleted;

    /// <summary>Gets a value indicating whether the file moved or was copied.</summary>
    public bool IsMoved => File?.ChangeKind is FileChangeKind.Renamed or FileChangeKind.Copied;

    /// <summary>Gets a value indicating whether the file has unresolved conflicts.</summary>
    public bool IsConflicted => File?.ChangeKind == FileChangeKind.Unmerged;

    /// <summary>
    /// Gets a value indicating whether the row shows the file's directory beside its name.
    /// </summary>
    public bool ShowDirectory { get; }

    /// <summary>
    /// Gets the directory a file lives in, shown dimmed beside its name in list mode.
    /// </summary>
    public string DirectoryLabel => File?.DirectoryPath ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether there is a directory to show beside the name.
    /// </summary>
    public bool HasDirectoryLabel => ShowDirectory && DirectoryLabel.Length > 0;

    /// <summary>
    /// Gets the full path a moved file came from, shown as the rename label's tooltip.
    /// </summary>
    public string RenameSource => File?.IsRenamed == true ? File.OldPath! : string.Empty;

    /// <summary>
    /// Gets the rename arrow shown for a moved file.
    /// </summary>
    /// <remarks>
    /// A rename inside one directory — by far the common case — shows only the old name: the
    /// directory is unchanged, and repeating it costs the width the file's own name needs. A file
    /// that actually moved shows the whole old path, because that is the part worth reading.
    /// </remarks>
    public string RenameLabel
    {
        get
        {
            if (File?.IsRenamed != true)
            {
                return string.Empty;
            }

            string oldPath = File.OldPath!;
            int separator = oldPath.LastIndexOf('/');
            string oldDirectory = separator < 0 ? string.Empty : oldPath.Substring(0, separator);

            return string.Equals(oldDirectory, File.DirectoryPath, StringComparison.Ordinal)
                ? $"← {oldPath.Substring(separator + 1)}"
                : $"← {oldPath}";
        }
    }

    /// <summary>
    /// Gets a value indicating whether the file moved.
    /// </summary>
    public bool IsRenamed => File?.IsRenamed == true;

    /// <summary>
    /// Gets the added and removed line counts, or an empty string for a binary file.
    /// </summary>
    public string LineCounts
    {
        get
        {
            if (File is null || File.IsBinary)
            {
                return File?.IsBinary == true ? "binary" : string.Empty;
            }

            // Nothing measured the change, so there is nothing honest to say about its size.
            if (!File.HasLineCounts)
            {
                return string.Empty;
            }

            return $"+{File.AddedLines.ToString(System.Globalization.CultureInfo.CurrentCulture)} "
                + $"−{File.RemovedLines.ToString(System.Globalization.CultureInfo.CurrentCulture)}";
        }
    }

    /// <summary>
    /// Gets the summary a directory row shows on the right.
    /// </summary>
    public string DirectorySummary
        => IsDirectory
            ? Counts.Total == 1
                ? "1 file"
                : $"{Counts.Total.ToString(System.Globalization.CultureInfo.CurrentCulture)} files"
            : string.Empty;

    /// <inheritdoc />
    public override string ToString() => IsDirectory ? $"{Label}/" : $"{StatusGlyph} {Label}";
}

/// <summary>
/// The panel listing what a change touched, as a flat list or as a tree — the user's choice, which
/// the specification asks for explicitly.
/// </summary>
public sealed class ChangedFilesPanelViewModel : ViewModelBase
{
    /// <summary>
    /// How many files a change may touch before the tree stops opening itself.
    /// </summary>
    /// <remarks>
    /// An expanded row is a realised row. A commit touching thousands of files would otherwise pay
    /// for every one of them the moment it is selected, so past this many the tree opens collapsed
    /// and the user expands what they care about.
    /// </remarks>
    public const int AutoExpandLimit = 500;

    private readonly ISystemInterop _interop;

    private IReadOnlyList<ChangedFile> _files = [];

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="interop">Runs the row menu's clipboard and open-file actions.</param>
    public ChangedFilesPanelViewModel(ISystemInterop interop)
    {
        ArgumentNullException.ThrowIfNull(interop);

        _interop = interop;

        ShowAsListCommand = new RelayCommand(() => ViewMode = ChangedFilesViewMode.List);
        ShowAsTreeCommand = new RelayCommand(() => ViewMode = ChangedFilesViewMode.Tree);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, () => SearchText.Length > 0);

        CopyPathCommand = new AsyncRelayCommand<ChangedFileNodeViewModel>(
            node => _interop.CopyTextAsync(node?.Path ?? string.Empty));

        CopyNameCommand = new AsyncRelayCommand<ChangedFileNodeViewModel>(
            node => _interop.CopyTextAsync(node?.File?.Name ?? node?.Label ?? string.Empty));

        OpenFileCommand = new AsyncRelayCommand<ChangedFileNodeViewModel>(
            async node => await OpenAsync(node, reveal: false),
            CanReachOnDisk);

        RevealFileCommand = new AsyncRelayCommand<ChangedFileNodeViewModel>(
            async node => await OpenAsync(node, reveal: true),
            CanReachOnDisk);
    }

    /// <summary>
    /// Gets or sets the actions the host page offers on every row, or <see langword="null"/> when
    /// the panel is read-only.
    /// </summary>
    public ChangedFileRowActions? Actions
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                // The rows hand the actions through, so they have to be rebuilt to pick them up.
                Rebuild();
            }
        }
    }

    /// <summary>
    /// Gets or sets the work tree the shown paths are relative to, which the row menu needs to
    /// reach a file on disk. Empty when the panel is showing a change from somewhere else.
    /// </summary>
    public string WorkTreePath
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OpenFileCommand.NotifyCanExecuteChanged();
                RevealFileCommand.NotifyCanExecuteChanged();
            }
        }
    } = string.Empty;

    /// <summary>Gets the command that copies a row's path.</summary>
    public AsyncRelayCommand<ChangedFileNodeViewModel> CopyPathCommand { get; }

    /// <summary>Gets the command that copies a row's file name.</summary>
    public AsyncRelayCommand<ChangedFileNodeViewModel> CopyNameCommand { get; }

    /// <summary>Gets the command that opens a row's file with the desktop's default handler.</summary>
    public AsyncRelayCommand<ChangedFileNodeViewModel> OpenFileCommand { get; }

    /// <summary>Gets the command that shows a row's file in the file manager.</summary>
    public AsyncRelayCommand<ChangedFileNodeViewModel> RevealFileCommand { get; }

    /// <summary>
    /// Gets the rows currently shown, in whichever shape the user picked.
    /// </summary>
    public ObservableCollection<ChangedFileNodeViewModel> Nodes { get; } = [];

    /// <summary>
    /// Gets or sets how the files are arranged.
    /// </summary>
    public ChangedFilesViewMode ViewMode
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsListMode));
                OnPropertyChanged(nameof(IsTreeMode));
                Rebuild();
            }
        }
    } = ChangedFilesViewMode.Tree;

    /// <summary>Gets a value indicating whether the flat list is shown.</summary>
    public bool IsListMode => ViewMode == ChangedFilesViewMode.List;

    /// <summary>Gets a value indicating whether the tree is shown.</summary>
    public bool IsTreeMode => ViewMode == ChangedFilesViewMode.Tree;

    /// <summary>
    /// Gets or sets a substring the shown paths must contain.
    /// </summary>
    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ClearSearchCommand.NotifyCanExecuteChanged();
                Rebuild();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether a chain of single-child directories is shown as one
    /// row.
    /// </summary>
    public bool CollapseDirectories
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                Rebuild();
            }
        }
    } = true;

    /// <summary>
    /// Gets or sets the selected row, which the diff viewer follows.
    /// </summary>
    public ChangedFileNodeViewModel? SelectedNode
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(SelectedFile));
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Gets the selected file, or <see langword="null"/> when nothing or a directory is selected.
    /// </summary>
    public ChangedFile? SelectedFile => SelectedNode?.File;

    /// <summary>
    /// Raised when the selection changes, so the diff viewer can follow it.
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Gets the command that switches to the flat list.</summary>
    public RelayCommand ShowAsListCommand { get; }

    /// <summary>Gets the command that switches to the tree.</summary>
    public RelayCommand ShowAsTreeCommand { get; }

    /// <summary>Gets the command that clears the search box.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>Gets how many files the change touches.</summary>
    public int FileCount => _files.Count;

    /// <summary>Gets how many lines the change adds.</summary>
    public int AddedLines { get; private set => SetProperty(ref field, value); }

    /// <summary>Gets how many lines the change removes.</summary>
    public int RemovedLines { get; private set => SetProperty(ref field, value); }

    /// <summary>
    /// Gets the one-line summary shown in the panel's header.
    /// </summary>
    public string Summary
    {
        get
        {
            if (_files.Count == 0)
            {
                return "No files";
            }

            // Nothing measured these changes, so the header says how many files and stops there.
            return HasLineCounts
                ? $"{FileLabel(_files.Count)}, +{AddedLines.ToString(System.Globalization.CultureInfo.CurrentCulture)} "
                  + $"−{RemovedLines.ToString(System.Globalization.CultureInfo.CurrentCulture)}"
                : FileLabel(_files.Count);
        }
    }

    /// <summary>
    /// Gets a value indicating whether any of the shown files came with measured line counts.
    /// </summary>
    public bool HasLineCounts { get; private set => SetProperty(ref field, value); }

    /// <summary>
    /// Gets a value indicating whether the panel has nothing to show.
    /// </summary>
    public bool IsEmpty => Nodes.Count == 0;

    /// <summary>
    /// Replaces the files the panel shows, keeping the selected path when it is still there.
    /// </summary>
    /// <param name="files">The changed files.</param>
    public void SetFiles(IReadOnlyList<ChangedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        _files = files;

        int added = 0;
        int removed = 0;

        foreach (ChangedFile file in files)
        {
            added += file.AddedLines;
            removed += file.RemovedLines;
        }

        AddedLines = added;
        RemovedLines = removed;

        bool counted = false;

        foreach (ChangedFile file in files)
        {
            if (file.HasLineCounts)
            {
                counted = true;
                break;
            }
        }

        HasLineCounts = counted;

        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(Summary));

        Rebuild();
    }

    /// <summary>
    /// Clears the panel.
    /// </summary>
    public void Clear() => SetFiles([]);

    /// <summary>
    /// Selects a file by path, if it is shown.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <returns><see langword="true"/> when a row was selected.</returns>
    public bool SelectPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (ChangedFileNodeViewModel node in Flatten(Nodes))
        {
            if (!node.IsDirectory && string.Equals(node.Path, path, StringComparison.Ordinal))
            {
                SelectedNode = node;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Enumerates every row, directories included, depth first.
    /// </summary>
    /// <param name="nodes">The rows to walk.</param>
    /// <returns>The rows and their descendants.</returns>
    public static IEnumerable<ChangedFileNodeViewModel> Flatten(IEnumerable<ChangedFileNodeViewModel> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        foreach (ChangedFileNodeViewModel node in nodes)
        {
            yield return node;

            foreach (ChangedFileNodeViewModel child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private void Rebuild()
    {
        string? previous = SelectedNode?.IsDirectory == false ? SelectedNode.Path : null;

        List<ChangedFile> visible = [];

        foreach (ChangedFile file in _files)
        {
            if (Matches(file))
            {
                visible.Add(file);
            }
        }

        Nodes.Clear();

        if (ViewMode == ChangedFilesViewMode.List)
        {
            foreach (ChangedFile file in visible)
            {
                Nodes.Add(new ChangedFileNodeViewModel(this, file, file.Name, showDirectory: true));
            }
        }
        else
        {
            IReadOnlyList<FileTreeNode> tree = FileTreeBuilder.Build(
                visible,
                new FileTreeOptions { CollapseSingleChildDirectories = CollapseDirectories });

            bool expand = visible.Count <= AutoExpandLimit;

            foreach (FileTreeNode node in tree)
            {
                Nodes.Add(Convert(node, expand));
            }
        }

        OnPropertyChanged(nameof(IsEmpty));

        // Keeping the selection across a view-mode switch or a search is what makes the toggle feel
        // like a different view of the same thing rather than a reset.
        if (!SelectPath(previous))
        {
            SelectedNode = null;
        }
    }

    private bool Matches(ChangedFile file)
        => SearchText.Trim().Length == 0 ||
           file.Path.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) ||
           (file.OldPath?.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) ?? false);

    private ChangedFileNodeViewModel Convert(FileTreeNode node, bool expand)
    {
        if (!node.IsDirectory)
        {
            return new ChangedFileNodeViewModel(this, node.Change!, node.Name);
        }

        List<ChangedFileNodeViewModel> children = new(node.Children.Count);

        foreach (FileTreeNode child in node.Children)
        {
            children.Add(Convert(child, expand));
        }

        return new ChangedFileNodeViewModel(this, node.Name, node.FullPath, node.Counts, children, expand);
    }

    /// <summary>
    /// Whether a row names something the menu can reach on disk. A deleted file and a directory row
    /// both fail this, and so does any row while the work tree is unknown.
    /// </summary>
    private bool CanReachOnDisk(ChangedFileNodeViewModel? node)
        => node is { IsDirectory: false, IsDeleted: false } && WorkTreePath.Length > 0;

    private async Task OpenAsync(ChangedFileNodeViewModel? node, bool reveal)
    {
        if (!CanReachOnDisk(node))
        {
            return;
        }

        string full = System.IO.Path.Combine(WorkTreePath, node!.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));

        if (reveal)
        {
            await _interop.RevealPathAsync(full);
        }
        else
        {
            await _interop.OpenPathAsync(full);
        }
    }

    private static string FileLabel(int count)
        => count == 1
            ? "1 file"
            : $"{count.ToString(System.Globalization.CultureInfo.CurrentCulture)} files";
}
