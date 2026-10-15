using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Worktrees;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Creating a worktree as a user does it: the dialog, the write under the repository lock, the
/// sentence in the info bar afterwards, and the new window it opens in.
/// </summary>
/// <remarks>
/// A history line, a branch on one and a row of the branches page all offer it, and must ask the same
/// question — which is why the flow is a service of its own rather than three copies in three pages.
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
}

/// <summary>
/// Default <see cref="IWorktreeOperations"/>.
/// </summary>
public sealed class WorktreeOperations : IWorktreeOperations
{
    private readonly IRepositoryContext _context;
    private readonly IWorktreeService _worktrees;
    private readonly IContentDialogService _dialogs;
    private readonly IFolderDialogService _folderDialogs;
    private readonly IInstanceLauncher _launcher;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<WorktreeOperations> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the application is looking at.</param>
    /// <param name="worktrees">Creates the worktree.</param>
    /// <param name="dialogs">Raises the form.</param>
    /// <param name="folderDialogs">Raises the form's folder picker.</param>
    /// <param name="launcher">Opens the new worktree in another instance.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public WorktreeOperations(
        IRepositoryContext context,
        IWorktreeService worktrees,
        IContentDialogService dialogs,
        IFolderDialogService folderDialogs,
        IInstanceLauncher launcher,
        IInfoBarService infoBar,
        ILogger<WorktreeOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(worktrees);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(folderDialogs);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _worktrees = worktrees;
        _dialogs = dialogs;
        _folderDialogs = folderDialogs;
        _launcher = launcher;
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
}
