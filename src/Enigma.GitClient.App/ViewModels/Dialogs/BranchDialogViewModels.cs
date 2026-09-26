using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Enigma.GitClient.Core.Refs;

namespace Enigma.GitClient.App.ViewModels.Dialogs;

/// <summary>
/// A revision a new branch can start from, as the start-point selector lists it.
/// </summary>
/// <param name="Revision">What git is given — a sha, a branch name or a tag.</param>
/// <param name="Label">What the selector shows.</param>
/// <param name="Detail">The subject or hash shown beside the label, empty when there is none.</param>
public sealed record BranchStartPoint(string Revision, string Label, string Detail)
{
    /// <inheritdoc />
    public override string ToString() => Detail.Length == 0 ? Label : $"{Label} — {Detail}";
}

/// <summary>
/// The fields of the "create a branch" dialog.
/// </summary>
/// <remarks>
/// The name is judged by <see cref="RefNameValidator"/> on every keystroke, so the reason a name is
/// refused appears while it is being typed rather than after the button is pressed. git remains the
/// authority: the service validates again before it writes anything.
/// </remarks>
public sealed class CreateBranchDialogViewModel : ViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="startPoints">What the new branch may start from, most useful first.</param>
    /// <param name="existingNames">The branch names already taken.</param>
    public CreateBranchDialogViewModel(
        IReadOnlyList<BranchStartPoint> startPoints,
        IReadOnlyCollection<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(startPoints);
        ArgumentNullException.ThrowIfNull(existingNames);

        _existingNames = new HashSet<string>(existingNames, StringComparer.Ordinal);

        StartPoints = [.. startPoints];
        SelectedStartPoint = StartPoints.Count > 0 ? StartPoints[0] : null;
    }

    private readonly HashSet<string> _existingNames;

    /// <summary>Gets what the new branch may start from.</summary>
    public ObservableCollection<BranchStartPoint> StartPoints { get; }

    /// <summary>
    /// Gets or sets the name of the branch to create.
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
    } = string.Empty;

    /// <summary>
    /// Gets or sets what the branch starts from.
    /// </summary>
    public BranchStartPoint? SelectedStartPoint
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
    /// Gets or sets a value indicating whether the new branch is checked out once it exists.
    /// </summary>
    public bool CheckoutAfterCreate { get; set => SetProperty(ref field, value); } = true;

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

            if (_existingNames.Contains(Name))
            {
                return $"A branch called \"{Name}\" already exists.";
            }

            return SelectedStartPoint is null ? "Choose where the branch should start." : string.Empty;
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
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// The single field of the "rename a branch" dialog.
/// </summary>
public sealed class RenameBranchDialogViewModel : ViewModelBase
{
    private readonly HashSet<string> _existingNames;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="currentName">The branch being renamed.</param>
    /// <param name="existingNames">The branch names already taken.</param>
    public RenameBranchDialogViewModel(string currentName, IReadOnlyCollection<string> existingNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentName);
        ArgumentNullException.ThrowIfNull(existingNames);

        CurrentName = currentName;
        Name = currentName;
        _existingNames = new HashSet<string>(existingNames, StringComparer.Ordinal);
    }

    /// <summary>Gets the branch's current name.</summary>
    public string CurrentName { get; }

    /// <summary>
    /// Gets or sets the name the branch should have.
    /// </summary>
    public string Name
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(ValidationMessage));
                OnPropertyChanged(nameof(HasValidationMessage));
                OnPropertyChanged(nameof(IsValid));
                ValidationChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

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

            if (string.Equals(Name, CurrentName, StringComparison.Ordinal))
            {
                return "That is the name it already has.";
            }

            return _existingNames.Contains(Name)
                ? $"A branch called \"{Name}\" already exists."
                : string.Empty;
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
}

/// <summary>
/// The fields of the "set upstream" dialog.
/// </summary>
public sealed class SetUpstreamDialogViewModel : ViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="branchName">The branch whose upstream is being set.</param>
    /// <param name="candidates">The remote branches it could track.</param>
    /// <param name="current">The upstream it has now, if any.</param>
    public SetUpstreamDialogViewModel(
        string branchName,
        IReadOnlyList<string> candidates,
        string? current)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        ArgumentNullException.ThrowIfNull(candidates);

        BranchName = branchName;
        Candidates = [.. candidates];
        Selected = current is { Length: > 0 } && Candidates.Contains(current) ? current : null;
    }

    /// <summary>Gets the branch whose upstream is being set.</summary>
    public string BranchName { get; }

    /// <summary>Gets the remote branches the branch could track.</summary>
    public ObservableCollection<string> Candidates { get; }

    /// <summary>
    /// Gets or sets the chosen upstream. <see langword="null"/> clears the one it has.
    /// </summary>
    public string? Selected { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// Gets a value indicating whether there is any remote branch to choose from.
    /// </summary>
    public bool HasCandidates => Candidates.Count > 0;

    /// <summary>
    /// Gets the sentence shown when the repository has no remote branches yet.
    /// </summary>
    public string EmptyMessage => "This repository has no remote branches to track. Fetch a remote first.";
}

/// <summary>
/// The fields of the "create a tag" dialog.
/// </summary>
/// <remarks>
/// There is no "annotated" switch on purpose: writing a message is what makes a tag annotated, and
/// a switch that changes what the message box means is one more thing to explain.
/// </remarks>
public sealed class CreateTagDialogViewModel : ViewModelBase
{
    private readonly HashSet<string> _existingNames;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="targets">What the tag may point at, most useful first.</param>
    /// <param name="existingNames">The tag names already taken.</param>
    public CreateTagDialogViewModel(
        IReadOnlyList<BranchStartPoint> targets,
        IReadOnlyCollection<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(existingNames);

        _existingNames = new HashSet<string>(existingNames, StringComparer.Ordinal);

        Targets = [.. targets];
        SelectedTarget = Targets.Count > 0 ? Targets[0] : null;
    }

    /// <summary>Gets what the tag may point at.</summary>
    public ObservableCollection<BranchStartPoint> Targets { get; }

    /// <summary>
    /// Gets or sets the tag's name.
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
    } = string.Empty;

    /// <summary>
    /// Gets or sets what the tag points at.
    /// </summary>
    public BranchStartPoint? SelectedTarget
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
    /// Gets or sets the annotation. Leaving it empty creates a lightweight tag.
    /// </summary>
    public string Message
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(KindDescription));
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets the sentence explaining which kind of tag the current input produces.
    /// </summary>
    public string KindDescription
        => Message.Trim().Length == 0
            ? "With no message this will be a lightweight tag: a name pointing at the commit."
            : "With a message this will be an annotated tag, recording who tagged it and when.";

    /// <summary>
    /// Gets the reason the dialog cannot be confirmed, empty when it can.
    /// </summary>
    public string ValidationMessage
    {
        get
        {
            RefNameValidation validation = RefNameValidator.ValidateTag(Name);

            if (!validation.IsValid)
            {
                return validation.Message;
            }

            if (_existingNames.Contains(Name))
            {
                return $"A tag called \"{Name}\" already exists.";
            }

            return SelectedTarget is null ? "Choose what the tag should point at." : string.Empty;
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
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }
}
