using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Reset;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// Resetting the branch that is checked out, as a user experiences it: the refusals, the question
/// before work is thrown away, the write under the repository lock, and the sentence afterwards.
/// </summary>
public interface IResetOperations
{
    /// <summary>
    /// Moves the branch that is checked out to a commit.
    /// </summary>
    /// <param name="sha">The commit to move to, in full.</param>
    /// <param name="shortSha">What to call it in the questions and the report.</param>
    /// <param name="branch">
    /// The branch the reader was shown. The reset is refused when HEAD is no longer on it: a branch
    /// nobody named is never the one that moves.
    /// </param>
    /// <param name="mode">Whether the work is kept, staged, or thrown away.</param>
    /// <returns><see langword="true"/> when the branch moved.</returns>
    Task<bool> ResetAsync(string sha, string shortSha, string branch, ResetMode mode);
}

/// <summary>
/// Default <see cref="IResetOperations"/>.
/// </summary>
/// <remarks>
/// A soft reset asks nothing: every change is kept, staged, and the commits it steps back over are
/// still in the reflog. A hard reset always asks, and names the files whose uncommitted changes it
/// would take — those are gone for good, and a slip of the mouse in a menu must not be how it happens.
/// </remarks>
public sealed class ResetOperations : IResetOperations
{
    private readonly IRepositoryContext _context;
    private readonly IResetService _reset;
    private readonly IDiffService _diffs;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<ResetOperations> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the application is looking at.</param>
    /// <param name="reset">Performs the reset.</param>
    /// <param name="diffs">Lists the files a hard reset would take.</param>
    /// <param name="dialogs">Raises the question before a hard reset.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public ResetOperations(
        IRepositoryContext context,
        IResetService reset,
        IDiffService diffs,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<ResetOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reset);
        ArgumentNullException.ThrowIfNull(diffs);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _reset = reset;
        _diffs = diffs;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> ResetAsync(string sha, string shortSha, string branch, ResetMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        RepositoryHandle? repository = _context.Repository;

        if (repository is null)
        {
            return false;
        }

        string target = shortSha is { Length: > 0 } ? shortSha : sha[..Math.Min(7, sha.Length)];

        if (Refuse(_context.Head, branch) is { } refusal)
        {
            await ReportAsync($"Could not reset \"{branch}\"", refusal, InfoBarSeverity.Warning).ConfigureAwait(true);
            return false;
        }

        if (mode == ResetMode.Hard && !await ConfirmDiscardAsync(repository, branch, target).ConfigureAwait(true))
        {
            return false;
        }

        try
        {
            await _context
                .RunExclusiveAsync((handle, token) => _reset.ResetAsync(handle, sha, mode, token))
                .ConfigureAwait(true);

            await ReportAsync(
                $"\"{branch}\" reset to {target}",
                mode == ResetMode.Soft
                    ? "Every change is kept, and staged: commit it again whenever you are ready."
                    : "The tracked files are as that commit left them.",
                InfoBarSeverity.Success).ConfigureAwait(true);

            return true;
        }
        catch (GitOperationRefusedException exception)
        {
            await ReportAsync($"Could not reset \"{branch}\"", exception.Message, InfoBarSeverity.Warning)
                .ConfigureAwait(true);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Resetting {Branch} to {Revision} failed", branch, sha);

            await ReportAsync(
                $"Could not reset \"{branch}\"",
                FirstLine(exception.StandardError),
                InfoBarSeverity.Error).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the operation.
        }

        return false;
    }

    /// <summary>
    /// Says why a reset cannot go ahead, or <see langword="null"/> when it can.
    /// </summary>
    /// <param name="head">Where HEAD is now.</param>
    /// <param name="branch">The branch the reader was shown.</param>
    /// <returns>The sentence to show, or <see langword="null"/>.</returns>
    public static string? Refuse(HeadState? head, string branch)
    {
        if (head is null || head.IsUnborn)
        {
            return "The repository has no commit yet, so there is nothing to reset to.";
        }

        if (head.IsDetached || head.BranchName is not { Length: > 0 } current)
        {
            return "HEAD is detached, so there is no branch to move. Check a branch out first.";
        }

        if (!string.Equals(current, branch, StringComparison.Ordinal))
        {
            return $"The repository is on \"{current}\" now, not \"{branch}\". Open the menu again to reset the branch you are on.";
        }

        return head.Operation switch
        {
            RepositoryOperation.None => null,
            RepositoryOperation.Merge => "A merge is in progress. Finish it or abandon it first.",
            RepositoryOperation.CherryPick => "A cherry-pick is in progress. Finish it or abandon it first.",
            RepositoryOperation.Revert => "A revert is in progress. Finish it or abandon it first.",
            RepositoryOperation.Bisect => "A bisect session is running. End it first.",
            RepositoryOperation.Rebase => "Another tool left a rebase in progress. Finish or abort it with git first.",
            RepositoryOperation.ApplyMailbox => "An 'am' session is in progress. Finish or abort it first.",
            _ => "An operation is in progress. Finish it first.",
        };
    }

    /// <summary>
    /// Asks before a hard reset, naming the files whose uncommitted changes it throws away.
    /// </summary>
    /// <returns><see langword="true"/> when the reset should go ahead.</returns>
    private async Task<bool> ConfirmDiscardAsync(RepositoryHandle repository, string branch, string target)
    {
        List<ChangedFile> tracked = [];

        foreach (ChangedFile file in await ReadUncommittedAsync(repository).ConfigureAwait(true))
        {
            // git reset --hard does not touch a file git has never been told about, so listing one
            // here would be claiming a loss that does not happen.
            if (!file.IsUntracked)
            {
                tracked.Add(file);
            }
        }

        string lost = tracked.Count == 0
            ? "There are no uncommitted changes to lose."
            : $"The uncommitted changes to {CountFiles(tracked.Count)} are thrown away and cannot be brought back:\n\n"
                + string.Join('\n', Describe(tracked));

        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "Discard all changes?";
            dialog.Content =
                $"\"{branch}\" will point at {target}, and every tracked file will be as that commit left it.\n\n"
                + lost
                + "\n\nUntracked files are left where they are.";
            dialog.PrimaryButtonText = "Reset and discard";
            dialog.CloseButtonText = "Cancel";
            dialog.DefaultButton = DefaultButton.Close;
        }).ConfigureAwait(true);

        return answer == DialogResult.Primary;
    }

    private async Task<IReadOnlyList<ChangedFile>> ReadUncommittedAsync(RepositoryHandle repository)
    {
        try
        {
            return await _diffs
                .GetChangedFilesAsync(repository, DiffTarget.Uncommitted(), _context.RepositoryLifetime)
                .ConfigureAwait(true);
        }
        catch (GitCommandException exception)
        {
            // The list is a courtesy; failing to read it must not stop the question being asked.
            _logger.LogWarning(exception, "Listing the uncommitted files failed");
            return [];
        }
    }

    private static IEnumerable<string> Describe(IReadOnlyList<ChangedFile> files)
    {
        int shown = Math.Min(files.Count, CheckoutOperations.MaximumListedFiles);

        for (int index = 0; index < shown; index++)
        {
            yield return files[index].Path;
        }

        if (files.Count > shown)
        {
            yield return $"…and {(files.Count - shown).ToString(CultureInfo.CurrentCulture)} more";
        }
    }

    private static string CountFiles(int files)
        => files == 1
            ? "1 file"
            : $"{files.ToString(CultureInfo.CurrentCulture)} files";

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

    private Task ReportAsync(string title, string message, InfoBarSeverity severity)
        => _infoBar.ShowAsync(bar =>
        {
            bar.Title = title;
            bar.Message = message;
            bar.Severity = severity;
        });
}
