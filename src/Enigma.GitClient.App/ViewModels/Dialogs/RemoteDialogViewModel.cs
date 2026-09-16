using System;
using System.Collections.Generic;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.App.ViewModels.Dialogs;

/// <summary>
/// The fields of the add-and-edit-a-remote dialog.
/// </summary>
/// <remarks>
/// One dialog for both: adding and editing a remote ask exactly the same three questions, and a
/// second dialog that differs only in its title is a second place for a rule to drift.
/// </remarks>
public sealed class RemoteDialogViewModel : ViewModelBase
{
    private readonly HashSet<string> _takenNames;
    private readonly string? _originalName;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="takenNames">The remote names already in use.</param>
    /// <param name="existing">The remote being edited, or <see langword="null"/> when adding one.</param>
    public RemoteDialogViewModel(IReadOnlyCollection<string> takenNames, GitRemote? existing)
    {
        ArgumentNullException.ThrowIfNull(takenNames);

        _takenNames = new HashSet<string>(takenNames, StringComparer.Ordinal);
        _originalName = existing?.Name;

        Name = existing?.Name ?? GitRemote.DefaultName;
        FetchUrl = existing?.FetchUrl ?? string.Empty;
        PushUrl = existing?.HasSeparatePushUrl == true ? existing.PushUrl : string.Empty;
        IsEditing = existing is not null;
    }

    /// <summary>Gets a value indicating whether an existing remote is being edited.</summary>
    public bool IsEditing { get; }

    /// <summary>
    /// Gets or sets the remote's name.
    /// </summary>
    public string Name
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RaiseValidation();
            }
        }
    }

    /// <summary>
    /// Gets or sets the URL fetches read from.
    /// </summary>
    public string FetchUrl
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RaiseValidation();
            }
        }
    }

    /// <summary>
    /// Gets or sets a separate URL for pushes. Empty means pushes go where fetches come from.
    /// </summary>
    public string PushUrl
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RaiseValidation();
            }
        }
    }

    /// <summary>
    /// Gets the push URL to write, or <see langword="null"/> when pushes should use the fetch URL.
    /// </summary>
    public string? EffectivePushUrl => PushUrl.Trim().Length == 0 ? null : PushUrl.Trim();

    /// <summary>
    /// Gets the reason the dialog cannot be confirmed, empty when it can.
    /// </summary>
    public string ValidationMessage
    {
        get
        {
            RefNameValidation name = RefNameValidator.Validate(Name, "remote");

            if (!name.IsValid)
            {
                return name.Message;
            }

            if (!string.Equals(Name, _originalName, StringComparison.Ordinal) && _takenNames.Contains(Name))
            {
                return $"A remote called \"{Name}\" already exists.";
            }

            RemoteUrlValidation fetch = RemoteUrlValidator.Validate(FetchUrl);

            if (!fetch.IsValid)
            {
                return fetch.Message;
            }

            if (EffectivePushUrl is { } push && !RemoteUrlValidator.Validate(push).IsValid)
            {
                return RemoteUrlValidator.Validate(push).Message;
            }

            return string.Empty;
        }
    }

    /// <summary>Gets a value indicating whether there is something to show in the validation line.</summary>
    public bool HasValidationMessage => ValidationMessage.Length > 0;

    /// <summary>Gets a value indicating whether the dialog can be confirmed.</summary>
    public bool IsValid => ValidationMessage.Length == 0;

    /// <summary>
    /// Raised whenever the answer to <see cref="IsValid"/> may have changed.
    /// </summary>
    public event EventHandler? ValidationChanged;

    private void RaiseValidation()
    {
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(EffectivePushUrl));
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }
}
