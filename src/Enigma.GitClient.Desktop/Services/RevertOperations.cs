using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Revert;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Reverting a commit, as a user experiences it: the refusals, the question, the write under the
/// repository lock, and the sentence afterwards.
/// </summary>
public interface IRevertOperations
{
    /// <summary>
    /// Records a commit on the branch HEAD is on that undoes another one.
    /// </summary>
    /// <param name="sha">The commit to undo, in full.</param>
    /// <param name="shortSha">What to call it in the question and the report.</param>
    /// <param name="subject">Its subject, which the question quotes.</param>
    /// <param name="isMerge">
    /// Whether it is a merge — undone against its first parent, the branch it was merged into.
    /// </param>
    /// <returns><see langword="true"/> when a commit was recorded.</returns>
    Task<bool> RevertAsync(string sha, string shortSha, string subject, bool isMerge);
}

/// <summary>
/// Default <see cref="IRevertOperations"/>.
/// </summary>
/// <remarks>
/// A revert always asks, because it writes a commit onto the branch; but it loses nothing — the commit
/// it undoes stays in the history, and the revert can itself be reverted or reset away — so the
/// question's default answer is to go ahead.
/// </remarks>
public sealed class RevertOperations : IRevertOperations
{
    private readonly IRepositoryContext _context;
    private readonly IRevertService _revert;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<RevertOperations> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the application is looking at.</param>
    /// <param name="revert">Performs the revert.</param>
    /// <param name="dialogs">Raises the question before the revert.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public RevertOperations(
        IRepositoryContext context,
        IRevertService revert,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<RevertOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(revert);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _revert = revert;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> RevertAsync(string sha, string shortSha, string subject, bool isMerge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);

        if (_context.Repository is null)
        {
            return false;
        }

        string target = shortSha is { Length: > 0 } ? shortSha : sha[..Math.Min(7, sha.Length)];

        if (Refuse(_context.Head) is { } refusal)
        {
            Report($"Could not revert {target}", refusal, InfoBarSeverity.Warning);
            return false;
        }

        // Refuse has made sure HEAD is on a branch; it is the one named in the question, read now
        // rather than when the menu opened, because it is where the commit is about to land.
        string branch = _context.Head!.BranchName!;

        if (!await ConfirmAsync(branch, target, subject ?? string.Empty, isMerge).ConfigureAwait(true))
        {
            return false;
        }

        try
        {
            RevertOutcome outcome = await _context
                .RunExclusiveAsync((handle, token) => _revert.RevertAsync(handle, sha, isMerge ? 1 : null, token))
                .ConfigureAwait(true);

            return ReportOutcome(outcome, branch, target);
        }
        catch (GitOperationRefusedException exception)
        {
            Report($"Could not revert {target}", exception.Message, InfoBarSeverity.Warning);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Reverting {Revision} failed", sha);

            Report($"Could not revert {target}", FirstLine(exception.StandardError), InfoBarSeverity.Error);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the operation.
        }

        return false;
    }

    /// <summary>
    /// Says why a revert cannot go ahead, or <see langword="null"/> when it can.
    /// </summary>
    /// <param name="head">Where HEAD is now.</param>
    /// <returns>The sentence to show, or <see langword="null"/>.</returns>
    public static string? Refuse(HeadState? head)
    {
        if (head is null || head.IsUnborn)
        {
            return "The repository has no commit yet, so there is nothing to revert.";
        }

        if (head.IsDetached || head.BranchName is not { Length: > 0 })
        {
            return "HEAD is detached, so the revert would belong to no branch. Check a branch out first.";
        }

        return ResetOperations.DescribeOperationInProgress(head.Operation);
    }

    private async Task<bool> ConfirmAsync(string branch, string target, string subject, bool isMerge)
    {
        string merge = isMerge
            ? "\n\nIt is a merge: what it brought in is undone, and the branch it was merged into is kept."
            : string.Empty;

        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = $"Revert {target}?";
            dialog.Content =
                $"A new commit on \"{branch}\" will undo what {target} changed:\n\n\"{subject}\""
                + merge
                + "\n\nThe commit itself stays in the history.";
            dialog.PrimaryButtonText = "Revert";
            dialog.CloseButtonText = "Cancel";
            dialog.DefaultButton = DefaultButton.Primary;
        }).ConfigureAwait(true);

        return answer == DialogResult.Primary;
    }

    private bool ReportOutcome(RevertOutcome outcome, string branch, string target)
    {
        switch (outcome.Kind)
        {
            case RevertResultKind.Reverted:
                string commit = outcome.CommitSha[..Math.Min(7, outcome.CommitSha.Length)];

                Report(
                    $"{target} reverted",
                    $"{commit} on \"{branch}\" undoes what it changed.",
                    InfoBarSeverity.Success);
                return true;

            case RevertResultKind.NothingToRevert:
                Report($"Nothing to revert in {target}", outcome.Message, InfoBarSeverity.Info);
                return false;

            case RevertResultKind.Conflicted:
                Report(
                    $"Could not revert {target}",
                    outcome.ConflictedPaths.Count switch
                    {
                        0 => outcome.Message,
                        1 => $"{outcome.Message} The conflict is in {outcome.ConflictedPaths[0]}.",
                        _ => $"{outcome.Message} The conflicts are in {string.Join(", ", Describe(outcome.ConflictedPaths))}.",
                    },
                    InfoBarSeverity.Warning);
                return false;

            default:
                _logger.LogWarning("Reverting {Revision} was refused: {Detail}", target, outcome.Detail);

                Report($"Could not revert {target}", outcome.Message, InfoBarSeverity.Warning);
                return false;
        }
    }

    private static IEnumerable<string> Describe(IReadOnlyList<string> paths)
    {
        int shown = Math.Min(paths.Count, CheckoutOperations.MaximumListedFiles);

        for (int index = 0; index < shown; index++)
        {
            yield return paths[index];
        }

        if (paths.Count > shown)
        {
            yield return $"…and {(paths.Count - shown).ToString(CultureInfo.CurrentCulture)} more";
        }
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

    private void Report(string title, string message, InfoBarSeverity severity)
        => _infoBar.Notify(title, message, severity);
}
