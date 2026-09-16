using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Services;

namespace Enigma.GitClient.App.ViewModels.Dialogs;

/// <summary>
/// The fields of the "create a repository" dialog.
/// </summary>
public sealed class InitRepositoryDialogViewModel : ViewModelBase
{
    /// <summary>
    /// The branch name git itself has defaulted to since 2.28, and the one this client suggests.
    /// </summary>
    public const string DefaultBranchName = "main";

    private readonly IFolderDialogService _folderDialogs;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="folderDialogs">Raises the "choose a folder" picker.</param>
    /// <param name="defaultParentDirectory">Where the repository is created unless the user changes it.</param>
    public InitRepositoryDialogViewModel(IFolderDialogService folderDialogs, string defaultParentDirectory)
    {
        ArgumentNullException.ThrowIfNull(folderDialogs);

        _folderDialogs = folderDialogs;
        ParentDirectory = defaultParentDirectory;
        BrowseCommand = new AsyncRelayCommand(OnBrowseAsync);
    }

    /// <summary>
    /// Gets or sets the directory the repository is created inside.
    /// </summary>
    public string ParentDirectory
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RaiseValidation();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the directory to create.
    /// </summary>
    public string DirectoryName
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RaiseValidation();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets the branch the first commit will create.
    /// </summary>
    public string InitialBranch
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RaiseValidation();
            }
        }
    } = DefaultBranchName;

    /// <summary>
    /// Gets the reason the dialog cannot be confirmed, empty when it can.
    /// </summary>
    public string ValidationMessage
    {
        get
        {
            if (ParentDirectory.Trim().Length == 0)
            {
                return "Choose where the repository should be created.";
            }

            if (DirectoryName.Trim().Length == 0)
            {
                return "Give the new directory a name.";
            }

            if (DirectoryName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return "That directory name contains characters the file system does not allow.";
            }

            if (InitialBranch.Trim().Length == 0)
            {
                return "Give the initial branch a name.";
            }

            if (!IsPlausibleBranchName(InitialBranch.Trim()))
            {
                return "That is not a valid branch name.";
            }

            string target = Path.Combine(ParentDirectory, DirectoryName);

            return Directory.Exists(Path.Combine(target, ".git"))
                ? $"'{target}' is already a git repository."
                : string.Empty;
        }
    }

    /// <summary>
    /// Gets a value indicating whether there is something to show in the validation line.
    /// </summary>
    public bool HasValidationMessage => ValidationMessage.Length > 0;

    /// <summary>
    /// Gets a value indicating whether the dialog can be confirmed.
    /// </summary>
    public bool IsValid => ValidationMessage.Length == 0;

    /// <summary>
    /// Gets the absolute path the repository will occupy.
    /// </summary>
    public string TargetPath
        => ParentDirectory.Trim().Length == 0 || DirectoryName.Trim().Length == 0
            ? string.Empty
            : Path.Combine(ParentDirectory.Trim(), DirectoryName.Trim());

    /// <summary>
    /// Gets the command that opens the folder picker for the parent directory.
    /// </summary>
    public AsyncRelayCommand BrowseCommand { get; }

    /// <summary>
    /// Raised whenever the answer to <see cref="IsValid"/> may have changed.
    /// </summary>
    public event EventHandler? ValidationChanged;

    /// <summary>
    /// A cheap client-side branch-name check, so the user sees a problem while typing. git's own
    /// <c>check-ref-format</c> remains the authority before a branch is actually created.
    /// </summary>
    /// <param name="name">The candidate branch name.</param>
    /// <returns><see langword="true"/> when the name looks usable.</returns>
    public static bool IsPlausibleBranchName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0 ||
            name.StartsWith('-') ||
            name.StartsWith('/') ||
            name.EndsWith('/') ||
            name.EndsWith(".lock", StringComparison.Ordinal) ||
            name.Contains("..", StringComparison.Ordinal) ||
            name.Contains("//", StringComparison.Ordinal) ||
            name.Contains("@{", StringComparison.Ordinal) ||
            name == "@")
        {
            return false;
        }

        foreach (char character in name)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                return false;
            }

            if (character is '~' or '^' or ':' or '?' or '*' or '[' or '\\')
            {
                return false;
            }
        }

        return true;
    }

    private void RaiseValidation()
    {
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(TargetPath));
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task OnBrowseAsync()
    {
        IEnumerable<string> folders = await _folderDialogs
            .ShowOpenFolderDialogAsync(
                title: "Choose where to create the repository",
                allowMultiple: false,
                suggestedStartLocation: ParentDirectory)
            .ConfigureAwait(true);

        string? chosen = folders.FirstOrDefault();

        if (chosen is { Length: > 0 })
        {
            ParentDirectory = chosen;
        }
    }
}
