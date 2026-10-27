using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Squash;
using Enigma.GitClient.Core.Status;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// One end of a squash, as the history shows it.
/// </summary>
/// <param name="Sha">The commit's full sha.</param>
/// <param name="ShortSha">What to call it.</param>
/// <param name="Subject">Its subject.</param>
public sealed record SquashEnd(string Sha, string ShortSha, string Subject);

/// <summary>
/// A squash the reader asked for from the history, before the dialog.
/// </summary>
/// <param name="Base">The commit the squash commit sits on.</param>
/// <param name="Tip">The commit whose files the squash commit records.</param>
/// <param name="TipBranches">The local branches that end at the tip, the checked-out one first.</param>
/// <param name="HeadIsAtTip">
/// Whether HEAD's commit is the tip — switching to the new branch then changes no file.
/// </param>
public sealed record SquashCandidate(
    SquashEnd Base,
    SquashEnd Tip,
    IReadOnlyList<string> TipBranches,
    bool HeadIsAtTip);

/// <summary>
/// Squashing a range of commits onto a new branch, as a user experiences it: the refusals, the
/// dialog, the write under the repository lock, and the sentence afterwards.
/// </summary>
public interface ISquashOperations
{
    /// <summary>
    /// Asks for the new branch and the message, then squashes.
    /// </summary>
    /// <param name="candidate">The base and the tip.</param>
    /// <returns>The squash commit's full sha, or <see langword="null"/> when nothing was created.</returns>
    Task<string?> SquashAsync(SquashCandidate candidate);
}

/// <summary>
/// Default <see cref="ISquashOperations"/>.
/// </summary>
/// <remarks>
/// The dialog is the only question: the squash loses nothing — the original branch keeps every one
/// of its commits — so there is no second "are you sure".
/// </remarks>
public sealed class SquashOperations : ISquashOperations
{
    private const string Failure = "Could not squash";

    private readonly IRepositoryContext _context;
    private readonly ISquashService _squash;
    private readonly IStatusService _status;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<SquashOperations> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the application is looking at.</param>
    /// <param name="squash">Reads the range and performs the squash.</param>
    /// <param name="status">Says whether conflicts are waiting to be resolved.</param>
    /// <param name="dialogs">Shows the squash dialog.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public SquashOperations(
        IRepositoryContext context,
        ISquashService squash,
        IStatusService status,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<SquashOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(squash);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _squash = squash;
        _status = status;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> SquashAsync(SquashCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (_context.Repository is not { } repository)
        {
            return null;
        }

        if (Refuse(_context.Head) is { } refusal)
        {
            Report(Failure, refusal, InfoBarSeverity.Warning);
            return null;
        }

        if (await ReadPreviewAsync(repository, candidate).ConfigureAwait(true) is not { } preview)
        {
            return null;
        }

        SquashDialogViewModel model = new(candidate, preview, ExistingNames());
        SquashDialogView view = new() { DataContext = model };

        DialogResult result = await ShowFormAsync(
            $"Squash {CountCommits(preview.CommitCount)} into a new branch",
            view,
            model).ConfigureAwait(true);

        if (result != DialogResult.Primary || !model.IsValid)
        {
            return null;
        }

        SquashRequest request = new(preview.BaseSha, preview.TipSha, model.BranchName, model.Message, model.SwitchToNewBranch);

        try
        {
            SquashOutcome outcome = await _context
                .RunExclusiveAsync((handle, token) => _squash.SquashAsync(handle, request, token))
                .ConfigureAwait(true);

            ReportSuccess(outcome, candidate, preview);

            return outcome.CommitSha;
        }
        catch (GitOperationRefusedException exception)
        {
            Report(Failure, exception.Message, InfoBarSeverity.Warning);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Squashing {Base}..{Tip} onto {Branch} failed", preview.BaseSha, preview.TipSha, request.BranchName);

            Report(Failure, FirstLine(exception.StandardError), InfoBarSeverity.Error);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the operation.
        }

        return null;
    }

    /// <summary>
    /// Says why a squash cannot go ahead, or <see langword="null"/> when it can.
    /// </summary>
    /// <param name="head">Where HEAD is now.</param>
    /// <returns>The sentence to show, or <see langword="null"/>.</returns>
    /// <remarks>
    /// A detached HEAD is no reason: the squash moves no branch, and the new one is created whatever
    /// HEAD is on.
    /// </remarks>
    public static string? Refuse(HeadState? head)
    {
        if (head is null || head.IsUnborn)
        {
            return "The repository has no commit yet, so there is nothing to squash.";
        }

        return ResetOperations.DescribeOperationInProgress(head.Operation);
    }

    /// <summary>
    /// Reads what the squash would replace, refusing — with a sentence, git not asked to write — while
    /// conflicts wait to be resolved or when the range is not one the squash takes.
    /// </summary>
    private async Task<SquashPreview?> ReadPreviewAsync(RepositoryHandle repository, SquashCandidate candidate)
    {
        try
        {
            WorkingTreeStatus status = await _status
                .GetStatusAsync(repository, includeIgnored: false, _context.RepositoryLifetime)
                .ConfigureAwait(true);

            if (status.HasConflicts)
            {
                Report(Failure, "This repository has unresolved conflicts. Resolve them first.", InfoBarSeverity.Warning);
                return null;
            }

            return await _squash
                .PreviewAsync(repository, candidate.Base.Sha, candidate.Tip.Sha, _context.RepositoryLifetime)
                .ConfigureAwait(true);
        }
        catch (GitOperationRefusedException exception)
        {
            Report(Failure, exception.Message, InfoBarSeverity.Warning);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Reading the squash range {Base}..{Tip} failed", candidate.Base.Sha, candidate.Tip.Sha);

            Report(Failure, FirstLine(exception.StandardError), InfoBarSeverity.Error);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the read.
        }

        return null;
    }

    private void ReportSuccess(SquashOutcome outcome, SquashCandidate candidate, SquashPreview preview)
    {
        string commit = outcome.CommitSha[..Math.Min(7, outcome.CommitSha.Length)];
        string replaced = $"{commit} on \"{outcome.BranchName}\" replaces the {CountCommits(preview.CommitCount)} since {candidate.Base.ShortSha}.";
        string original = candidate.TipBranches.Count == 0
            ? "The original commits are untouched."
            : $"\"{candidate.TipBranches[0]}\" is untouched and still holds them; deleting it later warns about unmerged commits — that is expected.";
        string switched = outcome.CheckedOut ? $" You are on \"{outcome.BranchName}\" now." : string.Empty;

        Report($"Squashed into \"{outcome.BranchName}\"", $"{replaced} {original}{switched}", InfoBarSeverity.Success);
    }

    private List<string> ExistingNames()
    {
        List<string> names = [];

        foreach (GitBranch branch in _context.Refs.LocalBranches)
        {
            names.Add(branch.ShortName);
        }

        return names;
    }

    private async Task<DialogResult> ShowFormAsync(string title, Control content, SquashDialogViewModel model)
    {
        ContentDialog? shown = null;

        void OnValidationChanged(object? sender, EventArgs e)
        {
            if (shown is not null)
            {
                shown.IsPrimaryButtonEnabled = model.IsValid;
            }
        }

        model.ValidationChanged += OnValidationChanged;

        try
        {
            return await _dialogs.ShowAsync(dialog =>
            {
                shown = dialog;
                dialog.Title = title;
                dialog.Content = content;
                dialog.PrimaryButtonText = "Squash";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
                dialog.IsPrimaryButtonEnabled = model.IsValid;
            }).ConfigureAwait(true);
        }
        finally
        {
            model.ValidationChanged -= OnValidationChanged;
        }
    }

    private static string CountCommits(int commits)
        => commits == 1 ? "1 commit" : $"{commits.ToString(CultureInfo.CurrentCulture)} commits";

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
