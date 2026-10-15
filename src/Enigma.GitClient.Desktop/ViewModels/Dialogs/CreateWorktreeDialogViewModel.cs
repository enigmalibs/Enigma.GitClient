using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Worktrees;

namespace Enigma.GitClient.Desktop.ViewModels.Dialogs;

/// <summary>
/// The fields of the "create a worktree" dialog: the branch to create, and the working directory it
/// is checked out in.
/// </summary>
/// <remarks>
/// <para>
/// The working directory follows the branch name — <c>Repo.worktree.&lt;branch&gt;</c>, next to the
/// repository — until it is edited, so the proposal stays right while the name is typed and a folder
/// the reader chose is never overwritten.
/// </para>
/// <para>
/// Both fields are judged on every keystroke by the rules the service applies again before git runs
/// (<see cref="RefNameValidator"/>, <see cref="WorktreePaths.Validate"/>), so the reason the dialog
/// cannot be confirmed is the one the service would give.
/// </para>
/// </remarks>
public sealed class CreateWorktreeDialogViewModel : ViewModelBase
{
    private readonly IFolderDialogService _folderDialogs;
    private readonly string _workTreePath;
    private readonly HashSet<string> _existingNames;

    // Where a proposed working directory goes: beside the repository until a folder is browsed to.
    private string? _parent;

    // Whether the working directory still follows the branch name: until the reader edits it.
    private bool _followsName = true;

    // Set while the dialog itself writes the working directory, which is not the reader editing it.
    private bool _proposing;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="folderDialogs">Raises the "choose a folder" picker.</param>
    /// <param name="workTreePath">The repository's working tree, which the worktree goes beside.</param>
    /// <param name="startPointLabel">What the new branch starts from, as the dialog names it.</param>
    /// <param name="existingNames">The branch names already taken.</param>
    public CreateWorktreeDialogViewModel(
        IFolderDialogService folderDialogs,
        string workTreePath,
        string startPointLabel,
        IReadOnlyCollection<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(folderDialogs);
        ArgumentException.ThrowIfNullOrWhiteSpace(workTreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(startPointLabel);
        ArgumentNullException.ThrowIfNull(existingNames);

        _folderDialogs = folderDialogs;
        _workTreePath = workTreePath;
        _existingNames = new HashSet<string>(existingNames, StringComparer.Ordinal);

        StartPointLabel = startPointLabel;
        BrowseCommand = new AsyncRelayCommand(OnBrowseAsync);

        Propose();
    }

    /// <summary>Gets what the new branch starts from.</summary>
    public string StartPointLabel { get; }

    /// <summary>
    /// Gets or sets the name of the branch to create.
    /// </summary>
    public string Name
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                if (_followsName)
                {
                    Propose();
                }

                RaiseValidation();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets the folder the worktree is created in.
    /// </summary>
    public string WorkingDirectory
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                if (!_proposing)
                {
                    _followsName = false;
                }

                RaiseValidation();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the worktree is opened in a new window once it exists.
    /// </summary>
    public bool OpenAfterCreate { get; set => SetProperty(ref field, value); } = true;

    /// <summary>
    /// Gets the reason the dialog cannot be confirmed, empty when it can.
    /// </summary>
    public string ValidationMessage
    {
        get
        {
            RefNameValidation validation = RefNameValidator.ValidateBranch(Name);

            if (!validation.IsValid)
            {
                return validation.Message;
            }

            return _existingNames.Contains(Name)
                ? $"A branch called \"{Name}\" already exists."
                : WorktreePaths.Validate(_workTreePath, WorkingDirectory);
        }
    }

    /// <summary>Gets a value indicating whether there is something to show in the validation line.</summary>
    public bool HasValidationMessage => ValidationMessage.Length > 0;

    /// <summary>Gets a value indicating whether the dialog can be confirmed.</summary>
    public bool IsValid => ValidationMessage.Length == 0;

    /// <summary>
    /// Gets the command that opens the folder picker for the folder the worktree goes in.
    /// </summary>
    public AsyncRelayCommand BrowseCommand { get; }

    /// <summary>
    /// Raised whenever the answer to <see cref="IsValid"/> may have changed.
    /// </summary>
    public event EventHandler? ValidationChanged;

    private void RaiseValidation()
    {
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(IsValid));
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Writes the proposed working directory: named after the repository and the branch, beside the
    /// repository or in the folder browsed to.
    /// </summary>
    private void Propose()
    {
        string beside = WorktreePaths.DefaultFor(_workTreePath, Name);
        string proposal = beside;

        if (_parent is not null)
        {
            // A repository at a root has no proposal of its own; in a chosen folder it still has a name.
            string leaf = beside.Length > 0 ? Path.GetFileName(beside) : WorktreePaths.ToFolderName(Name.Trim());
            proposal = leaf.Length > 0 ? Path.Combine(_parent, leaf) : _parent;
        }

        _proposing = true;

        try
        {
            WorkingDirectory = proposal;
        }
        finally
        {
            _proposing = false;
        }
    }

    /// <summary>
    /// Picks the folder the worktree goes in, keeping the worktree's own folder name: the proposed one
    /// while the field follows the branch, the one typed otherwise.
    /// </summary>
    private async Task OnBrowseAsync()
    {
        string current = WorkingDirectory.Trim();
        string? currentParent = Path.IsPathFullyQualified(current) ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current)) : null;

        IEnumerable<string> folders = await _folderDialogs
            .ShowOpenFolderDialogAsync(
                title: "Choose where to put the worktree's folder",
                allowMultiple: false,
                suggestedStartLocation: currentParent is not null && Directory.Exists(currentParent) ? currentParent : null)
            .ConfigureAwait(true);

        string? chosen = folders.FirstOrDefault();

        if (chosen is not { Length: > 0 })
        {
            return;
        }

        _parent = chosen;

        if (_followsName || current.Length == 0)
        {
            _followsName = true;
            Propose();
            return;
        }

        string leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(current));

        WorkingDirectory = leaf.Length > 0 ? Path.Combine(chosen, leaf) : chosen;
    }
}
