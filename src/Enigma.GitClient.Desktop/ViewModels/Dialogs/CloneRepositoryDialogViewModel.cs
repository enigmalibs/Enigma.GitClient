using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Desktop.ViewModels.Dialogs;

/// <summary>
/// The fields of the "clone a repository" dialog, and the validation that decides whether its
/// primary button is enabled.
/// </summary>
public sealed class CloneRepositoryDialogViewModel : ViewModelBase
{
    private readonly IFolderDialogService _folderDialogs;
    private readonly IRepositoryService _repositories;
    private readonly DebouncedProbe<string> _targetProbe;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="folderDialogs">Raises the "choose a folder" picker.</param>
    /// <param name="repositories">Validates the URL.</param>
    /// <param name="defaultParentDirectory">Where the clone is created unless the user changes it.</param>
    public CloneRepositoryDialogViewModel(
        IFolderDialogService folderDialogs,
        IRepositoryService repositories,
        string defaultParentDirectory)
        : this(folderDialogs, repositories, defaultParentDirectory, TimeProvider.System, ProbeTarget)
    {
    }

    /// <summary>
    /// Initialises a new instance with its own clock and disk check, for the tests.
    /// </summary>
    /// <param name="folderDialogs">Raises the "choose a folder" picker.</param>
    /// <param name="repositories">Validates the URL.</param>
    /// <param name="defaultParentDirectory">Where the clone is created unless the user changes it.</param>
    /// <param name="time">Measures the delay before the disk check.</param>
    /// <param name="probe">Checks the target on disk: the reason it cannot be used, or an empty string.</param>
    internal CloneRepositoryDialogViewModel(
        IFolderDialogService folderDialogs,
        IRepositoryService repositories,
        string defaultParentDirectory,
        TimeProvider time,
        Func<string, string> probe)
    {
        ArgumentNullException.ThrowIfNull(folderDialogs);
        ArgumentNullException.ThrowIfNull(repositories);

        _folderDialogs = folderDialogs;
        _repositories = repositories;
        _targetProbe = new DebouncedProbe<string>(probe, time);

        ParentDirectory = defaultParentDirectory;
        BrowseCommand = new AsyncRelayCommand(OnBrowseAsync);

        // Also when the default left the parent directory as it was, which raised nothing.
        Revalidate();
    }

    /// <summary>
    /// Gets or sets the URL or local path to clone from.
    /// </summary>
    public string Url
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                // Keep the suggested directory name in step with the URL until the user types one —
                // before the check, so the fields are checked as they now stand.
                if (!_directoryNameEdited)
                {
                    DirectoryName = value.Trim().Length == 0 ? string.Empty : CloneRequest.DeriveDirectoryName(value);
                    _directoryNameEdited = false;
                }

                Revalidate();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets the directory the clone is created inside.
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
                _directoryNameEdited = true;
                Revalidate();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets the branch to check out, empty for the remote's default.
    /// </summary>
    public string Branch { get; set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to clone only the most recent commits.
    /// </summary>
    public bool IsShallow
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsDepthEnabled));
            }
        }
    }

    /// <summary>
    /// Gets or sets how many commits a shallow clone fetches.
    /// </summary>
    public int Depth { get; set => SetProperty(ref field, value); } = 50;

    /// <summary>
    /// Gets a value indicating whether the depth field is usable.
    /// </summary>
    public bool IsDepthEnabled => IsShallow;

    /// <summary>
    /// Gets or sets a value indicating whether submodules are cloned too — on unless unticked: a clone
    /// whose submodule folders are empty does not build.
    /// </summary>
    public bool RecurseSubmodules { get; set => SetProperty(ref field, value); } = true;

    /// <summary>
    /// Gets the reason the dialog cannot be confirmed, empty when it can, while the disk is checked, or
    /// before a URL is typed.
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
    /// Gets the absolute path the clone will occupy, shown under the fields so there is no surprise.
    /// </summary>
    public string TargetPathPreview
        => ParentDirectory.Trim().Length == 0 || DirectoryName.Trim().Length == 0
            ? string.Empty
            : Path.Combine(ParentDirectory, DirectoryName);

    /// <summary>
    /// Gets the command that opens the folder picker for the parent directory.
    /// </summary>
    public AsyncRelayCommand BrowseCommand { get; }

    /// <summary>
    /// Builds the request the service will run.
    /// </summary>
    /// <returns>The clone request.</returns>
    public CloneRequest ToRequest()
        => new()
        {
            Url = Url.Trim(),
            ParentDirectory = ParentDirectory.Trim(),
            DirectoryName = DirectoryName.Trim(),
            Branch = Branch.Trim().Length == 0 ? null : Branch.Trim(),
            Depth = IsShallow && Depth > 0 ? Depth : null,
            RecurseSubmodules = RecurseSubmodules,
        };

    /// <summary>
    /// Raised whenever the answer to <see cref="IsValid"/> may have changed, so the dialog can
    /// enable or disable its primary button.
    /// </summary>
    public event EventHandler? ValidationChanged;

    private bool _directoryNameEdited;

    /// <summary>
    /// Runs the pending disk check now and waits for its answer, after which <see cref="IsValid"/> is
    /// final. The confirm path awaits it, so a fast Enter cannot skip the check.
    /// </summary>
    /// <returns>A task that completes once no check is pending.</returns>
    public Task ValidateNowAsync() => _targetProbe.FlushAsync();

    /// <summary>
    /// The disk check: whether the target already holds something. Runs on the thread pool.
    /// </summary>
    private static string ProbeTarget(string target)
    {
        try
        {
            return Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()
                ? $"'{target}' already exists and is not empty."
                : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"'{target}' exists but cannot be read.";
        }
    }

    /// <summary>
    /// What is wrong with the fields themselves, if anything.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the fields are usable; otherwise the message to show, which is empty
    /// for a URL not typed yet.
    /// </returns>
    private string? FieldProblem()
    {
        RemoteUrlValidation validation = _repositories.ValidateCloneUrl(Url);

        if (!validation.IsValid)
        {
            return Url.Trim().Length == 0 ? string.Empty : validation.Message;
        }

        if (ParentDirectory.Trim().Length == 0)
        {
            return "Choose where the clone should be created.";
        }

        if (DirectoryName.Trim().Length == 0)
        {
            return "Give the new directory a name.";
        }

        return DirectoryName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            ? "That directory name contains characters the file system does not allow."
            : null;
    }

    private void Revalidate()
    {
        if (FieldProblem() is { } problem)
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
        OnPropertyChanged(nameof(TargetPathPreview));
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task OnBrowseAsync()
    {
        System.Collections.Generic.IEnumerable<string> folders = await _folderDialogs
            .ShowOpenFolderDialogAsync(
                title: "Choose where to create the clone",
                allowMultiple: false,
                suggestedStartLocation: ParentDirectory)
            .ConfigureAwait(true);

        string? chosen = folders.FirstOrDefault();

        if (chosen is { Length: > 0 })
        {
            ParentDirectory = chosen;
        }
    }

    /// <summary>
    /// Gets the depth as text, for the hint under the shallow-clone switch.
    /// </summary>
    public string DepthDescription
        => IsShallow
            ? $"Fetches the most recent {Depth.ToString(CultureInfo.InvariantCulture)} commits."
            : "Fetches the whole history.";
}
