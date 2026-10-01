namespace Enigma.GitClient.Desktop.ViewModels.Dialogs;

/// <summary>
/// The questions asked before the uncommitted work is put on the stash: only what to call it.
/// </summary>
/// <remarks>
/// A message is optional. Without one git names the entry after the branch and the commit it was made
/// on (<c>WIP on main: 1a2b3c4 subject</c>), which is enough for one stash and indistinguishable for
/// three.
/// </remarks>
public sealed class StashDialogViewModel : ViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="summary">What is about to be stashed, in words: how many files, on which branch.</param>
    public StashDialogViewModel(string summary)
    {
        Summary = summary ?? string.Empty;
    }

    /// <summary>Gets what is about to be stashed, in words.</summary>
    public string Summary { get; }

    /// <summary>Gets or sets what the entry should say about itself; empty for git's own name.</summary>
    public string Message
    {
        get;
        set => SetProperty(ref field, value ?? string.Empty);
    } = string.Empty;

    /// <summary>Gets the message as it will be handed to git, or <see langword="null"/> for none.</summary>
    public string? EffectiveMessage => Message.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}
