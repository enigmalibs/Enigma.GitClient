using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Merging;
using Enigma.GitClient.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// Merging as a user performs it: the merge itself, the sentence afterwards, and getting out of one
/// that conflicted.
/// </summary>
public interface IMergeOperations
{
    /// <summary>
    /// Merges a revision into the current branch.
    /// </summary>
    /// <param name="source">The branch, tag or commit to merge in.</param>
    /// <param name="fastForward">How to treat a branch that is simply ahead.</param>
    /// <returns>What happened.</returns>
    Task<MergeOutcome> MergeAsync(string source, FastForwardMode fastForward = FastForwardMode.WhenPossible);

    /// <summary>
    /// Abandons a merge in progress, after confirming.
    /// </summary>
    /// <returns><see langword="true"/> when the merge was abandoned.</returns>
    Task<bool> AbortAsync();

    /// <summary>
    /// Records the merge commit once everything is resolved.
    /// </summary>
    /// <param name="message">The commit message, or <see langword="null"/> for git's own.</param>
    /// <returns><see langword="true"/> when the merge was committed.</returns>
    Task<bool> ContinueAsync(string? message = null);
}

/// <summary>
/// Default <see cref="IMergeOperations"/>.
/// </summary>
public sealed class MergeOperations : IMergeOperations
{
    private readonly IRepositoryContext _context;
    private readonly IMergeService _merges;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<MergeOperations> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the application is looking at.</param>
    /// <param name="merges">Performs the merge.</param>
    /// <param name="dialogs">Raises the confirmation.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public MergeOperations(
        IRepositoryContext context,
        IMergeService merges,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<MergeOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(merges);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _merges = merges;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MergeOutcome> MergeAsync(
        string source,
        FastForwardMode fastForward = FastForwardMode.WhenPossible)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        MergeOutcome none = new(MergeResultKind.Failed, [], "No repository is open.", string.Empty);

        if (!_context.IsRepositoryOpen)
        {
            return none;
        }

        MergeRequest request = new() { Source = source, FastForward = fastForward };

        try
        {
            MergeOutcome outcome = await _context
                .RunExclusiveAsync((handle, token) => _merges.MergeAsync(handle, request, token))
                .ConfigureAwait(true);

            await ReportAsync(outcome, source).ConfigureAwait(true);

            return outcome;
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Merging {Source} failed", source);

            MergeOutcome failure = new(
                MergeResultKind.Failed,
                [],
                FirstLine(exception.StandardError),
                exception.StandardError);

            await ReportAsync(failure, source).ConfigureAwait(true);

            return failure;
        }
        catch (OperationCanceledException)
        {
            return none;
        }
    }

    /// <inheritdoc />
    public async Task<bool> AbortAsync()
    {
        if (!_context.IsRepositoryOpen)
        {
            return false;
        }

        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "Abandon the merge";
            dialog.Content =
                "Put the repository back exactly as it was before the merge started?\n\n"
                + "Anything you have resolved so far is thrown away. Commits on either branch are not "
                + "affected.";
            dialog.PrimaryButtonText = "Abandon";
            dialog.CloseButtonText = "Keep going";
            dialog.DefaultButton = DefaultButton.Close;
        }).ConfigureAwait(true);

        if (answer != DialogResult.Primary)
        {
            return false;
        }

        try
        {
            await _context
                .RunExclusiveAsync((handle, token) => _merges.AbortAsync(handle, token))
                .ConfigureAwait(true);

            await ShowAsync("Merge abandoned", "The repository is back as it was.", InfoBarSeverity.Info)
                .ConfigureAwait(true);

            return true;
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Abandoning the merge failed");

            await ShowAsync("Could not abandon the merge", FirstLine(exception.StandardError), InfoBarSeverity.Error)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the operation.
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<bool> ContinueAsync(string? message = null)
    {
        if (!_context.IsRepositoryOpen)
        {
            return false;
        }

        try
        {
            string sha = await _context
                .RunExclusiveAsync((handle, token) => _merges.ContinueAsync(handle, message, token))
                .ConfigureAwait(true);

            await ShowAsync(
                "Merge committed",
                $"{(sha.Length >= 7 ? sha[..7] : sha)} records the merge.",
                InfoBarSeverity.Success).ConfigureAwait(true);

            return true;
        }
        catch (GitOperationRefusedException refusal)
        {
            await ShowAsync("Not ready to commit", refusal.Message, InfoBarSeverity.Warning).ConfigureAwait(true);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Committing the merge failed");

            await ShowAsync("Could not commit the merge", FirstLine(exception.StandardError), InfoBarSeverity.Error)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the operation.
        }

        return false;
    }

    private Task ReportAsync(MergeOutcome outcome, string source)
        => outcome.Kind switch
        {
            MergeResultKind.Conflicted => ShowAsync(
                "The merge stopped on conflicts",
                $"{CountFiles(outcome.ConflictedPaths.Count)} to resolve before the merge can be committed.",
                InfoBarSeverity.Warning),

            MergeResultKind.Failed => ShowAsync($"Could not merge \"{source}\"", outcome.Message, InfoBarSeverity.Error),

            MergeResultKind.AlreadyUpToDate => ShowAsync("Nothing to merge", outcome.Message, InfoBarSeverity.Info),

            _ => ShowAsync("Merged", outcome.Message, InfoBarSeverity.Success),
        };

    private static string CountFiles(int files)
        => files == 1 ? "1 file" : $"{files.ToString(CultureInfo.CurrentCulture)} files";

    private static string FirstLine(string text)
    {
        foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return "git reported no reason.";
    }

    private Task ShowAsync(string title, string message, InfoBarSeverity severity)
        => _infoBar.ShowAsync(bar =>
        {
            bar.Title = title;
            bar.Message = message;
            bar.Severity = severity;
        });
}
