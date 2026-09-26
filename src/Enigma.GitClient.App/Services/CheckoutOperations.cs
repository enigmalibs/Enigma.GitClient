using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Checkout;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Status;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// Checking anything out, as a user experiences it: the warning before HEAD detaches, the question
/// about local changes, the write under the repository lock, and the sentence afterwards.
/// </summary>
public interface ICheckoutOperations
{
    /// <summary>
    /// Checks a revision out, asking whatever has to be asked first.
    /// </summary>
    /// <param name="revision">What to check out — a branch, a tag, a remote branch or a sha.</param>
    /// <param name="label">What to call it in the questions. Defaults to the revision itself.</param>
    /// <param name="detach">Whether to detach even when the revision names a local branch.</param>
    /// <returns><see langword="true"/> when HEAD moved.</returns>
    Task<bool> CheckoutAsync(string revision, string? label = null, bool detach = false);
}

/// <summary>
/// Default <see cref="ICheckoutOperations"/>.
/// </summary>
/// <remarks>
/// git refuses a checkout that would overwrite local changes, which is right but unhelpful on its
/// own. The three real answers — put them on the stash, throw them away, or stop — are offered
/// here, and the one that throws work away says exactly which files it would take.
/// </remarks>
public sealed class CheckoutOperations : ICheckoutOperations
{
    /// <summary>
    /// The most files named in the discard confirmation before it stops listing them.
    /// </summary>
    public const int MaximumListedFiles = 12;

    private readonly IRepositoryContext _context;
    private readonly ICheckoutService _checkout;
    private readonly IDiffService _diffs;
    private readonly IWorkingTreeProbe _probe;
    private readonly IBranchOperations _branches;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<CheckoutOperations> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the application is looking at.</param>
    /// <param name="checkout">Performs the checkout.</param>
    /// <param name="diffs">Lists the files a discard would take.</param>
    /// <param name="probe">Answers whether there is anything uncommitted at all.</param>
    /// <param name="branches">Offers "create a branch here" instead of detaching.</param>
    /// <param name="dialogs">Raises the warnings and the questions.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public CheckoutOperations(
        IRepositoryContext context,
        ICheckoutService checkout,
        IDiffService diffs,
        IWorkingTreeProbe probe,
        IBranchOperations branches,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<CheckoutOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentNullException.ThrowIfNull(diffs);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _checkout = checkout;
        _diffs = diffs;
        _probe = probe;
        _branches = branches;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> CheckoutAsync(string revision, string? label = null, bool detach = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        RepositoryHandle? repository = _context.Repository;

        if (repository is null)
        {
            return false;
        }

        string name = label is { Length: > 0 } ? label : revision;

        bool detaching = detach ||
            await _checkout.WouldDetachAsync(repository, revision, _context.RepositoryLifetime).ConfigureAwait(true);

        if (detaching && !await ConfirmDetachAsync(revision, name).ConfigureAwait(true))
        {
            return false;
        }

        DirtyTreeResolution resolution = DirtyTreeResolution.Keep;

        if (await _probe.IsDirtyAsync(repository, true, _context.RepositoryLifetime).ConfigureAwait(true))
        {
            resolution = await AskAboutLocalChangesAsync(repository, name).ConfigureAwait(true);

            if (resolution == DirtyTreeResolution.Cancel)
            {
                return false;
            }
        }

        CheckoutOptions options = new()
        {
            Dirty = resolution,
            Detach = detach,
            StashMessage = $"Before checking out {name}",
        };

        return await RunAsync(repository, revision, name, options).ConfigureAwait(true);
    }

    /// <summary>
    /// Warns that HEAD is about to detach, and offers the branch that would avoid it.
    /// </summary>
    /// <returns><see langword="true"/> when the checkout should go ahead.</returns>
    private async Task<bool> ConfirmDetachAsync(string revision, string name)
    {
        DialogResult result = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "This will detach HEAD";
            dialog.Content =
                $"\"{name}\" is not a local branch, so the repository will sit on the commit itself. "
                + "Commits made from there belong to no branch and are easy to lose.\n\n"
                + "You can create a branch here instead.";
            dialog.PrimaryButtonText = "Check out anyway";
            dialog.SecondaryButtonText = "Create a branch here…";
            dialog.CloseButtonText = "Cancel";
            dialog.DefaultButton = DefaultButton.Secondary;
        }).ConfigureAwait(true);

        if (result == DialogResult.Secondary)
        {
            // The branch dialog does the checkout itself when the user asks it to, so this path
            // ends here whatever they choose.
            await _branches.CreateAsync(revision, name).ConfigureAwait(true);
            return false;
        }

        return result == DialogResult.Primary;
    }

    /// <summary>
    /// Asks what to do with the work in the tree, naming the files a discard would take.
    /// </summary>
    private async Task<DirtyTreeResolution> AskAboutLocalChangesAsync(RepositoryHandle repository, string name)
    {
        IReadOnlyList<ChangedFile> files = await ReadUncommittedAsync(repository).ConfigureAwait(true);

        string listed = string.Join('\n', Describe(files));

        DialogResult result = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "You have uncommitted changes";
            dialog.Content =
                $"Checking out \"{name}\" may overwrite {CountFiles(files.Count)}.\n\n"
                + listed
                + "\n\nStashing keeps them; discarding does not.";
            dialog.PrimaryButtonText = "Stash and check out";
            dialog.SecondaryButtonText = "Discard and check out";
            dialog.CloseButtonText = "Cancel";
            dialog.DefaultButton = DefaultButton.Primary;
        }).ConfigureAwait(true);

        return result switch
        {
            DialogResult.Primary => DirtyTreeResolution.Stash,
            DialogResult.Secondary => DirtyTreeResolution.Discard,
            _ => DirtyTreeResolution.Cancel,
        };
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
        int shown = Math.Min(files.Count, MaximumListedFiles);

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
            ? "1 changed file"
            : $"{files.ToString(CultureInfo.CurrentCulture)} changed files";

    private async Task<bool> RunAsync(
        RepositoryHandle repository,
        string revision,
        string name,
        CheckoutOptions options)
    {
        try
        {
            CheckoutResult result = await _context.RunExclusiveAsync(
                (handle, token) => _checkout.CheckoutAsync(handle, revision, options, token))
                .ConfigureAwait(true);

            if (result.Stashed)
            {
                await ReportAsync(
                    "Changes stashed",
                    "Your uncommitted work is on the stash and can be restored from there.",
                    InfoBarSeverity.Info).ConfigureAwait(true);
            }
            else if (result.IsDetached)
            {
                await ReportAsync(
                    "HEAD is detached",
                    $"The repository is on {result.Head.DisplayName}. Create a branch before committing.",
                    InfoBarSeverity.Warning).ConfigureAwait(true);
            }

            return true;
        }
        catch (GitOperationRefusedException refusal)
        {
            await ReportAsync($"Could not check out \"{name}\"", refusal.Message, InfoBarSeverity.Warning)
                .ConfigureAwait(true);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Checking out {Revision} failed", revision);

            await ReportAsync(
                $"Could not check out \"{name}\"",
                FirstLine(exception.StandardError),
                InfoBarSeverity.Error).ConfigureAwait(true);
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

    private Task ReportAsync(string title, string message, InfoBarSeverity severity)
        => _infoBar.ShowAsync(bar =>
        {
            bar.Title = title;
            bar.Message = message;
            bar.Severity = severity;
        });
}
