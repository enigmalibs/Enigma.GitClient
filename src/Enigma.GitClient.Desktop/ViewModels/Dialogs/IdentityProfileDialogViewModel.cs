using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Identity;

namespace Enigma.GitClient.Desktop.ViewModels.Dialogs;

/// <summary>
/// The fields of the dialog that adds or edits an identity profile, and the validation that decides
/// whether its primary button is enabled.
/// </summary>
public sealed class IdentityProfileDialogViewModel : ViewModelBase
{
    private readonly IFolderDialogService _folderDialogs;
    private bool _touched;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="folderDialogs">Raises the folder picker behind <see cref="BrowseCommand"/>.</param>
    /// <param name="label">What the profile is called, empty for a new one.</param>
    /// <param name="identity">The name and email the fields start with.</param>
    /// <param name="baseDirectory">The base directory the field starts with, empty for none.</param>
    public IdentityProfileDialogViewModel(
        IFolderDialogService folderDialogs,
        string label,
        GitIdentity identity,
        string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(folderDialogs);
        ArgumentNullException.ThrowIfNull(identity);

        _folderDialogs = folderDialogs;

        Label = label ?? string.Empty;
        Name = identity.Name;
        Email = identity.Email;
        BaseDirectory = baseDirectory ?? string.Empty;

        BrowseCommand = new AsyncRelayCommand(OnBrowseAsync);

        // Starting values are not the reader's mistakes: nothing is said until something is typed.
        _touched = false;
    }

    /// <summary>Gets or sets what the profile is called.</summary>
    public string Label
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                RaiseValidation();
            }
        }
    } = string.Empty;

    /// <summary>Gets or sets the name the profile sets.</summary>
    public string Name
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                RaiseValidation();
            }
        }
    } = string.Empty;

    /// <summary>Gets or sets the email the profile sets.</summary>
    public string Email
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                RaiseValidation();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets the directory the profile's repositories live in, empty for none: where the home
    /// page's Open, Clone and Create start while the profile is selected.
    /// </summary>
    public string BaseDirectory
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                RaiseValidation();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets the command that picks the base directory in a folder picker, which starts on the one the
    /// field names while it exists, and on the home folder otherwise.
    /// </summary>
    public AsyncRelayCommand BrowseCommand { get; }

    /// <summary>
    /// Gets the reason the dialog cannot be confirmed, once something has been typed; empty otherwise.
    /// </summary>
    public string ValidationMessage => _touched ? Problem ?? string.Empty : string.Empty;

    /// <summary>Gets a value indicating whether there is something to show in the validation line.</summary>
    public bool HasValidationMessage => ValidationMessage.Length > 0;

    /// <summary>Gets a value indicating whether the dialog can be confirmed.</summary>
    public bool IsValid => Problem is null;

    /// <summary>
    /// Raised whenever the answer to <see cref="IsValid"/> may have changed, so the dialog can
    /// enable or disable its primary button.
    /// </summary>
    public event EventHandler? ValidationChanged;

    private string? Problem
        => IdentityProfileRules.ValidateLabel(Label)
            ?? IdentityProfileRules.ValidateIdentity(new GitIdentity(Name, Email))
            ?? IdentityProfileRules.ValidateBaseDirectory(BaseDirectory);

    /// <summary>
    /// Builds the profile the fields describe.
    /// </summary>
    /// <param name="existing">The profile being edited, or <see langword="null"/> for a new one.</param>
    /// <returns>The profile, trimmed; an edited one keeps its identifier.</returns>
    /// <exception cref="InvalidOperationException">The fields are not valid.</exception>
    public IdentityProfile ToProfile(IdentityProfile? existing)
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("The dialog is not valid.");
        }

        GitIdentity identity = new(Name, Email);

        IdentityProfile profile = existing is null ? IdentityProfile.Create(Label, identity) : existing.With(Label, identity);

        return profile.WithBaseDirectory(BaseDirectory);
    }

    private async Task OnBrowseAsync()
    {
        string current = BaseDirectory.Trim();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string? start = Path.IsPathFullyQualified(current) && Directory.Exists(current)
            ? current
            : home.Length > 0 ? home : null;

        IEnumerable<string> folders = await _folderDialogs
            .ShowOpenFolderDialogAsync(
                title: "Choose the profile's base directory",
                allowMultiple: false,
                suggestedStartLocation: start)
            .ConfigureAwait(true);

        string? chosen = folders.FirstOrDefault();

        if (chosen is { Length: > 0 })
        {
            BaseDirectory = chosen;
        }
    }

    private void RaiseValidation()
    {
        _touched = true;

        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(IsValid));
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }
}
