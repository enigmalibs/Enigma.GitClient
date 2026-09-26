using System;
using Enigma.GitClient.Core.Identity;

namespace Enigma.GitClient.App.ViewModels.Dialogs;

/// <summary>
/// The fields of the dialog that adds or edits an identity profile, and the validation that decides
/// whether its primary button is enabled.
/// </summary>
public sealed class IdentityProfileDialogViewModel : ViewModelBase
{
    private bool _touched;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="label">What the profile is called, empty for a new one.</param>
    /// <param name="identity">The name and email the fields start with.</param>
    public IdentityProfileDialogViewModel(string label, GitIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        Label = label ?? string.Empty;
        Name = identity.Name;
        Email = identity.Email;

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
        => IdentityProfileRules.ValidateLabel(Label) ?? GitIdentityRules.Validate(new GitIdentity(Name, Email).Normalised());

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

        return existing is null ? IdentityProfile.Create(Label, identity) : existing.With(Label, identity);
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
