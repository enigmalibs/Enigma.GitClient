using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Branches;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Worktrees;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Worktrees as a user handles them: creating one (the dialog, the write under the repository lock,
/// the sentence in the info bar afterwards, the new window it opens in), removing one after saying what
/// that costs, and opening one in a new window, a file manager or a terminal.
/// </summary>
/// <remarks>
/// A history line, a branch on one, a row of the branches page and the Worktrees dialog all offer these,
/// and must ask the same questions — which is why the flows are a service of their own rather than
/// copies in every page.
/// </remarks>
public interface IWorktreeOperations
{
    /// <summary>
    /// Asks for the branch to create and the working directory, then creates the worktree there.
    /// </summary>
    /// <param name="startPoint">What the new branch starts from — a sha, a branch name, a tag.</param>
    /// <param name="startPointLabel">What to call that revision in the dialog.</param>
    /// <returns><see langword="true"/> when a worktree was created.</returns>
    Task<bool> CreateAsync(string startPoint, string startPointLabel);

    /// <summary>
    /// Says what removing a worktree costs — its uncommitted changes, the windows working in it, and
    /// whether its branch goes too — then removes it, and its branch when asked.
    /// </summary>
    /// <param name="worktree">The worktree.</param>
    /// <returns><see langword="true"/> when the worktree was removed.</returns>
    /// <remarks>
    /// The main working tree, the one this window has open and a locked one are refused before anything
    /// is asked, as Core refuses them.
    /// </remarks>
    Task<bool> RemoveAsync(GitWorktree worktree);

    /// <summary>
    /// Opens a worktree in another instance of the application.
    /// </summary>
    /// <param name="worktree">The worktree.</param>
    /// <returns><see langword="true"/> when the instance started; otherwise the info bar says so.</returns>
    Task<bool> OpenAsync(GitWorktree worktree);

    /// <summary>
    /// Opens a worktree's folder in the file manager.
    /// </summary>
    /// <param name="worktree">The worktree.</param>
    /// <returns><see langword="true"/> when the folder opened; otherwise the info bar says so.</returns>
    Task<bool> OpenFolderAsync(GitWorktree worktree);

    /// <summary>
    /// Opens a terminal in a worktree's folder.
    /// </summary>
    /// <param name="worktree">The worktree.</param>
    /// <returns><see langword="true"/> when a terminal started; otherwise the info bar says so.</returns>
    Task<bool> OpenTerminalAsync(GitWorktree worktree);
}

/// <summary>
/// Default <see cref="IWorktreeOperations"/>.
/// </summary>
public sealed class WorktreeOperations : IWorktreeOperations
{
    /// <summary>The title of the removal's question.</summary>
    public const string RemoveTitle = "Remove worktree";

    private readonly IRepositoryContext _context;
    private readonly IWorktreeService _worktrees;
    private readonly IBranchService _branches;
    private readonly IContentDialogService _dialogs;
    private readonly IFolderDialogService _folderDialogs;
    private readonly IInstanceLauncher _launcher;
    private readonly ISystemInterop _interop;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<WorktreeOperations> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the application is looking at.</param>
    /// <param name="worktrees">Creates, probes and removes the worktree.</param>
    /// <param name="branches">Reads whether a worktree's branch is merged, and deletes it when asked.</param>
    /// <param name="dialogs">Raises the form and the confirmation.</param>
    /// <param name="folderDialogs">Raises the form's folder picker.</param>
    /// <param name="launcher">Opens a worktree in another instance.</param>
    /// <param name="interop">Opens a worktree's folder or a terminal in it.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public WorktreeOperations(
        IRepositoryContext context,
        IWorktreeService worktrees,
        IBranchService branches,
        IContentDialogService dialogs,
        IFolderDialogService folderDialogs,
        IInstanceLauncher launcher,
        ISystemInterop interop,
        IInfoBarService infoBar,
        ILogger<WorktreeOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(worktrees);
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(folderDialogs);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _worktrees = worktrees;
        _branches = branches;
        _dialogs = dialogs;
        _folderDialogs = folderDialogs;
        _launcher = launcher;
        _interop = interop;
        _infoBar = infoBar;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> CreateAsync(string startPoint, string startPointLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startPoint);

        if (_context.Repository is not { } repository)
        {
            return false;
        }

        CreateWorktreeDialogViewModel model = new(
            _folderDialogs,
            repository.WorkTreePath,
            string.IsNullOrWhiteSpace(startPointLabel) ? startPoint : startPointLabel,
            ExistingNames());
        CreateWorktreeDialogView view = new() { DataContext = model };

        ContentDialog? shown = null;

        void OnValidationChanged(object? sender, EventArgs e)
        {
            if (shown is not null)
            {
                shown.IsPrimaryButtonEnabled = model.IsValid;
            }
        }

        model.ValidationChanged += OnValidationChanged;

        DialogResult result;

        try
        {
            result = await _dialogs.ShowAsync(dialog =>
            {
                shown = dialog;
                dialog.Title = "Create a worktree";
                dialog.Content = view;
                dialog.PrimaryButtonText = "Create";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
                dialog.IsPrimaryButtonEnabled = model.IsValid;
            }).ConfigureAwait(true);
        }
        finally
        {
            model.ValidationChanged -= OnValidationChanged;
        }

        if (result != DialogResult.Primary || !model.IsValid)
        {
            return false;
        }

        string branch = model.Name;
        string folder = Path.GetFullPath(model.WorkingDirectory.Trim());
        bool open = model.OpenAfterCreate;

        bool created = await RunAsync(
            (handle, token) => _worktrees.AddAsync(handle, folder, branch, startPoint, token),
            $"Could not create the worktree for \"{branch}\"").ConfigureAwait(true);

        if (!created)
        {
            return false;
        }

        if (open && !_launcher.Launch(folder))
        {
            _infoBar.Notify(
                "No new window",
                $"The worktree is in '{folder}', but another instance of Enigma.GitClient could not be started to open it.",
                InfoBarSeverity.Error);
        }
        else
        {
            _infoBar.Notify("Worktree created", $"\"{branch}\" is checked out in '{folder}'.", InfoBarSeverity.Success);
        }

        return true;
    }

    // ---------------------------------------------------------------- removing

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(GitWorktree worktree)
    {
        ArgumentNullException.ThrowIfNull(worktree);

        if (_context.Repository is not { } repository)
        {
            return false;
        }

        const string failureTitle = "Could not remove the worktree";

        // The main working tree, the open one and a locked one: refused in Core's own words, before
        // anything is read or asked.
        try
        {
            WorktreeService.FindRemovable(repository, _context.Worktrees, worktree.Path);
        }
        catch (GitOperationRefusedException refusal)
        {
            _infoBar.Notify(failureTitle, refusal.Message, InfoBarSeverity.Warning);
            return false;
        }

        if (await ProbeAsync(repository, worktree, failureTitle).ConfigureAwait(true) is not { } probe)
        {
            return false;
        }

        RemoveWorktreeDialogViewModel model = new(
            worktree.Path,
            worktree.IsMissing,
            probe.Changes,
            worktree.IsDetached ? null : worktree.Branch,
            probe.UnmergedCommits,
            probe.UnmergedSubjects);

        if (!await ConfirmRemovalAsync(model).ConfigureAwait(true))
        {
            return false;
        }

        bool force = model.Changes > 0;
        string? branch = model.DeleteBranch ? model.Branch : null;
        bool unmerged = model.IsUnmerged;
        string? branchFailure = null;

        bool removed = await RunAsync(
            async (handle, token) =>
            {
                await _worktrees.RemoveAsync(handle, worktree.Path, force, token).ConfigureAwait(true);

                if (branch is not null)
                {
                    // The worktree is gone whatever happens next: a branch that cannot be deleted is
                    // said afterwards, never taken for a failed removal.
                    branchFailure = await DeleteBranchAsync(handle, branch, unmerged, token).ConfigureAwait(true);
                }
            },
            failureTitle).ConfigureAwait(true);

        if (!removed)
        {
            return false;
        }

        if (branch is not null && branchFailure is not null)
        {
            _infoBar.Notify(
                "Worktree removed",
                $"'{worktree.Path}' is gone, but the branch \"{branch}\" was not deleted: {branchFailure}",
                InfoBarSeverity.Warning);
        }
        else
        {
            _infoBar.Notify(
                "Worktree removed",
                branch is null ? $"'{worktree.Path}' is gone." : $"'{worktree.Path}' is gone and \"{branch}\" is deleted.",
                InfoBarSeverity.Success);
        }

        return true;
    }

    /// <summary>
    /// Reads what removing a worktree would cost: the uncommitted changes in it and, for one on a
    /// branch, the commits only that branch has — as the delete flow reads them.
    /// </summary>
    /// <returns>What it read, or <see langword="null"/> when git could not read the worktree.</returns>
    private async Task<RemovalProbe?> ProbeAsync(RepositoryHandle repository, GitWorktree worktree, string failureTitle)
    {
        CancellationToken token = _context.RepositoryLifetime;

        try
        {
            int changes = await _worktrees.CountChangesAsync(worktree, token).ConfigureAwait(true);

            if (worktree.IsDetached || worktree.Branch is not { Length: > 0 } branch
                || await _branches.IsMergedAsync(repository, branch, cancellationToken: token).ConfigureAwait(true))
            {
                return new RemovalProbe(changes, 0, []);
            }

            IReadOnlyList<string> subjects = await _branches
                .GetUnmergedCommitsAsync(repository, branch, limit: BranchOperations.MaximumListedCommits, cancellationToken: token)
                .ConfigureAwait(true);

            int count = await _branches.CountUnmergedCommitsAsync(repository, branch, cancellationToken: token).ConfigureAwait(true);

            return new RemovalProbe(changes, Math.Max(count, subjects.Count), subjects);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Reading the worktree at {Path} failed", worktree.Path);
            _infoBar.Notify(failureTitle, FirstLine(exception.StandardError), InfoBarSeverity.Error);
            return null;
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the question.
            return null;
        }
    }

    /// <summary>
    /// Asks the removal's question, in the destructive look: <em>Remove</em>, or <em>Remove anyway</em>
    /// when uncommitted work would be lost.
    /// </summary>
    private async Task<bool> ConfirmRemovalAsync(RemoveWorktreeDialogViewModel model)
    {
        RemoveWorktreeDialogView view = new() { DataContext = model };
        ContentDialog? shown = null;

        try
        {
            DialogResult result = await _dialogs.ShowAsync(dialog =>
            {
                shown = dialog;
                dialog.Classes.Add(ContentDialogServiceExtensions.DangerClass);
                dialog.Title = RemoveTitle;
                dialog.Content = view;
                dialog.PrimaryButtonText = model.ConfirmText;
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Close;
            }).ConfigureAwait(true);

            return result == DialogResult.Primary;
        }
        finally
        {
            shown?.Classes.Remove(ContentDialogServiceExtensions.DangerClass);
        }
    }

    /// <summary>
    /// Deletes the branch of a worktree just removed: <c>-d</c> when it is merged, <c>-D</c> when the
    /// question named what it would lose.
    /// </summary>
    /// <returns>Why it was not deleted, or <see langword="null"/> when it was.</returns>
    private async Task<string?> DeleteBranchAsync(RepositoryHandle repository, string branch, bool unmerged, CancellationToken token)
    {
        try
        {
            await _branches.DeleteAsync(repository, branch, force: unmerged, token).ConfigureAwait(true);
            return null;
        }
        catch (GitOperationRefusedException refusal)
        {
            return refusal.Message;
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Deleting {Branch} after removing its worktree failed", branch);
            return FirstLine(exception.StandardError);
        }
    }

    // ---------------------------------------------------------------- opening

    /// <inheritdoc />
    public Task<bool> OpenAsync(GitWorktree worktree)
    {
        ArgumentNullException.ThrowIfNull(worktree);

        if (_launcher.Launch(worktree.Path))
        {
            return Task.FromResult(true);
        }

        _infoBar.Notify(
            "No new window",
            $"Another instance of Enigma.GitClient could not be started to open '{worktree.Path}'.",
            InfoBarSeverity.Error);

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public async Task<bool> OpenFolderAsync(GitWorktree worktree)
    {
        ArgumentNullException.ThrowIfNull(worktree);

        // The toolbar's own words for the same failure (MainWindowViewModel): one action, one wording.
        if (await _interop.OpenPathAsync(worktree.Path).ConfigureAwait(true))
        {
            return true;
        }

        _infoBar.Notify(
            "The folder did not open",
            $"Nothing on this desktop opened '{worktree.Path}'.",
            InfoBarSeverity.Warning);

        return false;
    }

    /// <inheritdoc />
    public async Task<bool> OpenTerminalAsync(GitWorktree worktree)
    {
        ArgumentNullException.ThrowIfNull(worktree);

        if (await _interop.OpenTerminalAsync(worktree.Path).ConfigureAwait(true))
        {
            return true;
        }

        _infoBar.Notify(
            "No terminal started",
            $"No terminal could be started in '{worktree.Path}'.",
            InfoBarSeverity.Warning);

        return false;
    }

    private IReadOnlyCollection<string> ExistingNames()
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (GitBranch branch in _context.Refs.LocalBranches)
        {
            names.Add(branch.ShortName);
        }

        return names;
    }

    /// <summary>
    /// Runs the write under the repository's exclusive lock, turning both kinds of failure into a
    /// sentence in the info bar rather than an exception nobody catches.
    /// </summary>
    private async Task<bool> RunAsync(
        Func<RepositoryHandle, CancellationToken, Task> operation,
        string failureTitle)
    {
        if (!_context.IsRepositoryOpen)
        {
            return false;
        }

        try
        {
            await _context.RunExclusiveAsync(operation).ConfigureAwait(true);
            return true;
        }
        catch (GitOperationRefusedException refusal)
        {
            _infoBar.Notify(failureTitle, refusal.Message, InfoBarSeverity.Warning);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "{Title}", failureTitle);

            _infoBar.Notify(failureTitle, FirstLine(exception.StandardError), InfoBarSeverity.Error);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the operation.
        }

        return false;
    }

    private static string FirstLine(string text)
    {
        string trimmed = text.Trim();

        if (trimmed.Length == 0)
        {
            return "git reported no reason.";
        }

        int newline = trimmed.IndexOf('\n', StringComparison.Ordinal);

        return newline < 0 ? trimmed : trimmed[..newline].TrimEnd('\r');
    }

    /// <summary>
    /// What removing a worktree would cost, as read before the question.
    /// </summary>
    /// <param name="Changes">The uncommitted changes in it.</param>
    /// <param name="UnmergedCommits">How many commits its branch holds that the current branch does not.</param>
    /// <param name="UnmergedSubjects">The first of those commits' subjects, newest first.</param>
    private sealed record RemovalProbe(int Changes, int UnmergedCommits, IReadOnlyList<string> UnmergedSubjects);
}
