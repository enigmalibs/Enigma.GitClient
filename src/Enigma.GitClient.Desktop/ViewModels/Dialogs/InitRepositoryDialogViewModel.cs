using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Services;

namespace Enigma.GitClient.Desktop.ViewModels.Dialogs;

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
    private readonly DebouncedProbe<string> _targetProbe;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="folderDialogs">Raises the "choose a folder" picker.</param>
    /// <param name="defaultParentDirectory">Where the repository is created unless the user changes it.</param>
    public InitRepositoryDialogViewModel(IFolderDialogService folderDialogs, string defaultParentDirectory)
        : this(folderDialogs, defaultParentDirectory, TimeProvider.System, ProbeTarget)
    {
    }

    /// <summary>
    /// Initialises a new instance with its own clock and disk check, for the tests.
    /// </summary>
    /// <param name="folderDialogs">Raises the "choose a folder" picker.</param>
    /// <param name="defaultParentDirectory">Where the repository is created unless the user changes it.</param>
    /// <param name="time">Measures the delay before the disk check.</param>
    /// <param name="probe">Checks the target on disk: the reason it cannot be used, or an empty string.</param>
    internal InitRepositoryDialogViewModel(
        IFolderDialogService folderDialogs,
        string defaultParentDirectory,
        TimeProvider time,
        Func<string, string> probe)
    {
        ArgumentNullException.ThrowIfNull(folderDialogs);

        _folderDialogs = folderDialogs;
        _targetProbe = new DebouncedProbe<string>(probe, time);
        ParentDirectory = defaultParentDirectory;
        BrowseCommand = new AsyncRelayCommand(OnBrowseAsync);

        // Also when the default left the parent directory as it was, which raised nothing.
        Revalidate();
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
                Revalidate();
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
                Revalidate();
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
                Revalidate();
            }
        }
    } = DefaultBranchName;

    /// <summary>
    /// Gets the reason the dialog cannot be confirmed, empty when it can or while the disk is checked.
    /// </summary>
    /// <remarks>
    /// Worked out once per change of a field. The fields are checked at once; the target on disk is
    /// checked off the UI thread, a short while after the last change.
    /// </remarks>
    public string ValidationMessage { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether there is something to show in the validation line.
    /// </summary>
    public bool HasValidationMessage => ValidationMessage.Length > 0;

    /// <summary>
    /// Gets a value indicating whether the dialog can be confirmed: the fields are usable and the disk
    /// check has answered that the target is too.
    /// </summary>
    public bool IsValid { get; private set; }

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
    /// Runs the pending disk check now and waits for its answer, after which <see cref="IsValid"/> is
    /// final. The confirm path awaits it, so a fast Enter cannot skip the check.
    /// </summary>
    /// <returns>A task that completes once no check is pending.</returns>
    public Task ValidateNowAsync() => _targetProbe.FlushAsync();

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

    /// <summary>
    /// The disk check: whether the target is already a repository. Runs on the thread pool.
    /// </summary>
    private static string ProbeTarget(string target)
        => Directory.Exists(Path.Combine(target, ".git"))
            ? $"'{target}' is already a git repository."
            : string.Empty;

    private string FieldProblem()
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

        return IsPlausibleBranchName(InitialBranch.Trim()) ? string.Empty : "That is not a valid branch name.";
    }

    private void Revalidate()
    {
        string problem = FieldProblem();

        if (problem.Length > 0)
        {
            _targetProbe.Cancel();
            Publish(problem, isValid: false);
            return;
        }

        // Until the disk answers, the dialog cannot be confirmed, and says nothing about a target that
        // is no longer the one typed.
        Publish(string.Empty, isValid: false);
        _targetProbe.Start(Path.Combine(ParentDirectory, DirectoryName), answer => Publish(answer, isValid: answer.Length == 0));
    }

    private void Publish(string message, bool isValid)
    {
        ValidationMessage = message;
        IsValid = isValid;
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
