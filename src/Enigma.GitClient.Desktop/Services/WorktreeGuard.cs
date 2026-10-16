using System;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Worktrees;
using Enigma.GitClient.Desktop.Formatting;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Refuses, before git does, a checkout of a branch another working tree has checked out — and offers to
/// open that working tree instead.
/// </summary>
/// <remarks>
/// git would refuse with <c>'x' is already used by worktree at …</c>, which says what but not what to
/// do. A branch can be checked out in one working tree at a time; the one place to work on it is the
/// worktree that has it, which this offers to open in a new window. The branches dialog, the history's
/// badges, a drop-merge that checks its target out first and the checkout of a revision all ask here.
/// </remarks>
public interface IWorktreeGuard
{
    /// <summary>
    /// Says, when another working tree has a branch checked out, where — and offers to open it there.
    /// </summary>
    /// <param name="branch">The local branch about to be checked out.</param>
    /// <returns>
    /// <see langword="true"/> when the checkout is refused, whatever was answered;
    /// <see langword="false"/> when no other working tree has the branch and the checkout can go ahead.
    /// </returns>
    Task<bool> RefuseCheckoutAsync(string branch);
}

/// <summary>
/// Default <see cref="IWorktreeGuard"/>.
/// </summary>
public sealed class WorktreeGuard : IWorktreeGuard
{
    /// <summary>The title of the question a refused checkout asks.</summary>
    public const string Title = "Branch in a worktree";

    private readonly IRepositoryContext _context;
    private readonly IContentDialogService _dialogs;
    private readonly IInstanceLauncher _launcher;
    private readonly IInfoBarService _infoBar;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">Knows which working tree has which branch.</param>
    /// <param name="dialogs">Asks whether to open the worktree.</param>
    /// <param name="launcher">Opens it in another instance.</param>
    /// <param name="infoBar">Says when that instance could not start.</param>
    public WorktreeGuard(
        IRepositoryContext context,
        IContentDialogService dialogs,
        IInstanceLauncher launcher,
        IInfoBarService infoBar)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(infoBar);

        _context = context;
        _dialogs = dialogs;
        _launcher = launcher;
        _infoBar = infoBar;
    }

    /// <inheritdoc />
    public async Task<bool> RefuseCheckoutAsync(string branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        if (_context.WorktreeOf(branch) is not { } worktree)
        {
            return false;
        }

        string where = $"\"{branch}\" is checked out in {WorktreeText.Location(worktree)}.";

        // Nothing to open when the folder is gone: the message alone, and only removing the worktree
        // frees the branch.
        bool missing = worktree.IsMissing;

        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = Title;

            if (missing)
            {
                dialog.Content = $"{where} Its folder is missing: remove the worktree to free the branch.";
                dialog.CloseButtonText = "OK";
                dialog.DefaultButton = DefaultButton.Close;
                return;
            }

            dialog.Content = $"{where} Open it in a new window?";
            dialog.PrimaryButtonText = "Open";
            dialog.CloseButtonText = "Cancel";
            dialog.DefaultButton = DefaultButton.Primary;
        }).ConfigureAwait(true);

        if (answer == DialogResult.Primary && !missing)
        {
            Open(worktree);
        }

        return true;
    }

    private void Open(GitWorktree worktree)
    {
        if (!_launcher.Launch(worktree.Path))
        {
            _infoBar.Notify(
                "No new window",
                $"Another instance of Enigma.GitClient could not be started to open '{worktree.Path}'.",
                InfoBarSeverity.Error);
        }
    }
}
