using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Branches;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// The branch operations as a user performs them: the dialog, the confirmation, the write under the
/// repository lock, and the sentence in the info bar afterwards.
/// </summary>
/// <remarks>
/// Both the branches page and the graph's own context menu offer the same operations, and they must
/// ask the same questions — a delete that names the commits at risk in one place and not the other
/// is a delete that will lose work in the other. Keeping the flow in one service is what guarantees
/// they cannot drift.
/// </remarks>
public interface IBranchOperations
{
    /// <summary>
    /// Asks for a name and a start point, then creates the branch.
    /// </summary>
    /// <param name="startPoint">
    /// The revision to offer first, or <see langword="null"/> to start from the selection or HEAD.
    /// </param>
    /// <param name="startPointLabel">What to call that revision in the selector.</param>
    /// <returns><see langword="true"/> when a branch was created.</returns>
    Task<bool> CreateAsync(string? startPoint = null, string? startPointLabel = null);

    /// <summary>
    /// Asks for a new name, then renames the branch.
    /// </summary>
    /// <param name="name">The branch to rename.</param>
    /// <returns><see langword="true"/> when the branch was renamed.</returns>
    Task<bool> RenameAsync(string name);

    /// <summary>
    /// Confirms, naming whatever would be lost, then deletes the branch.
    /// </summary>
    /// <param name="name">The branch to delete, as <c>name</c> or <c>remote/name</c>.</param>
    /// <param name="isRemote">Whether the branch lives on a remote.</param>
    /// <returns><see langword="true"/> when the branch was deleted.</returns>
    Task<bool> DeleteAsync(string name, bool isRemote);

    /// <summary>
    /// Checks a branch out, creating a tracking branch first when it is a remote one.
    /// </summary>
    /// <param name="name">The branch to check out.</param>
    /// <param name="isRemote">Whether the branch lives on a remote.</param>
    /// <returns><see langword="true"/> when HEAD moved.</returns>
    /// <remarks>
    /// A remote branch whose local branch already exists checks that local branch out: at once when
    /// the two are on the same commit, and otherwise after asking whether to reset it to the remote's
    /// commit first — GitKraken's "reset local to here".
    /// </remarks>
    Task<bool> CheckoutAsync(string name, bool isRemote);

    /// <summary>
    /// Asks which remote branch to track, then sets or clears the upstream.
    /// </summary>
    /// <param name="name">The branch to change.</param>
    /// <param name="currentUpstream">The upstream it has now, if any.</param>
    /// <returns><see langword="true"/> when the upstream changed.</returns>
    Task<bool> SetUpstreamAsync(string name, string? currentUpstream);
}

/// <summary>
/// Default <see cref="IBranchOperations"/>.
/// </summary>
public sealed class BranchOperations : IBranchOperations
{
    /// <summary>What the button that resets the local branch to the remote's commit says.</summary>
    public const string ResetLocalToHere = "Reset local to here";

    /// <summary>How many of the commits a reset leaves behind its question names.</summary>
    public const int MaximumListedCommits = 10;

    private readonly IRepositoryContext _context;
    private readonly IBranchService _branches;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<BranchOperations> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the application is looking at.</param>
    /// <param name="branches">Performs the branch operations.</param>
    /// <param name="dialogs">Raises the confirmations and the forms.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public BranchOperations(
        IRepositoryContext context,
        IBranchService branches,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<BranchOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _branches = branches;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> CreateAsync(string? startPoint = null, string? startPointLabel = null)
    {
        if (_context.Repository is null)
        {
            return false;
        }

        CreateBranchDialogViewModel model = new(BuildStartPoints(startPoint, startPointLabel), ExistingNames());
        CreateBranchDialogView view = new() { DataContext = model };

        DialogResult result = await ShowFormAsync(
            "Create a branch",
            view,
            model,
            () => model.IsValid,
            handler => model.ValidationChanged += handler,
            handler => model.ValidationChanged -= handler,
            "Create").ConfigureAwait(true);

        if (result != DialogResult.Primary || !model.IsValid)
        {
            return false;
        }

        string name = model.Name;
        string? from = model.SelectedStartPoint?.Revision;
        bool checkout = model.CheckoutAfterCreate;

        return await RunAsync(
            (handle, token) => _branches.CreateAsync(handle, name, from, checkout, token),
            $"Could not create \"{name}\"").ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<bool> RenameAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_context.Repository is null)
        {
            return false;
        }

        RenameBranchDialogViewModel model = new(name, ExistingNames());
        RenameBranchDialogView view = new() { DataContext = model };

        DialogResult result = await ShowFormAsync(
            "Rename branch",
            view,
            model,
            () => model.IsValid,
            handler => model.ValidationChanged += handler,
            handler => model.ValidationChanged -= handler,
            "Rename").ConfigureAwait(true);

        if (result != DialogResult.Primary || !model.IsValid)
        {
            return false;
        }

        string newName = model.Name;

        return await RunAsync(
            (handle, token) => _branches.RenameAsync(handle, name, newName, false, token),
            $"Could not rename \"{name}\"").ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string name, bool isRemote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        RepositoryHandle? repository = _context.Repository;

        if (repository is null)
        {
            return false;
        }

        if (isRemote)
        {
            return await DeleteRemoteAsync(name).ConfigureAwait(true);
        }

        bool merged = await _branches
            .IsMergedAsync(repository, name, cancellationToken: _context.RepositoryLifetime)
            .ConfigureAwait(true);

        string message = $"Delete the branch \"{name}\"?";

        if (!merged)
        {
            IReadOnlyList<string> unmerged = await _branches
                .GetUnmergedCommitsAsync(repository, name, cancellationToken: _context.RepositoryLifetime)
                .ConfigureAwait(true);

            // Naming the commits is the whole point of the confirmation. "Are you sure?" teaches
            // nothing; "these three commits exist nowhere else" is a decision a user can make.
            message = $"\"{name}\" holds {CountCommits(unmerged.Count)} that the current branch does not. "
                + "Deleting it loses them.\n\n"
                + string.Join('\n', unmerged);
        }

        if (!await ConfirmAsync("Delete branch", message, "Delete").ConfigureAwait(true))
        {
            return false;
        }

        return await RunAsync(
            (handle, token) => _branches.DeleteAsync(handle, name, !merged, token),
            $"Could not delete \"{name}\"").ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<bool> CheckoutAsync(string name, bool isRemote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!isRemote)
        {
            return await RunAsync(
                (handle, token) => _branches.CheckoutAsync(handle, name, token),
                $"Could not check out \"{name}\"").ConfigureAwait(true);
        }

        // The local branch of that name is there already: it is the one to check out, where it is or
        // where the remote is.
        if (Find(_context.Refs.LocalBranches, BranchService.LocalNameFor(name)) is { } existing
            && Find(_context.Refs.RemoteBranches, name) is { } remote)
        {
            return await CheckoutExistingAsync(existing, remote).ConfigureAwait(true);
        }

        string created = string.Empty;

        bool done = await RunAsync(
            async (handle, token) =>
                created = await _branches.CheckoutRemoteAsync(handle, name, cancellationToken: token)
                    .ConfigureAwait(true),
            $"Could not check out \"{name}\"").ConfigureAwait(true);

        if (done && created.Length > 0)
        {
            Report("Branch created", $"\"{created}\" now tracks \"{name}\".", InfoBarSeverity.Success);
        }

        return done;
    }

    /// <inheritdoc />
    public async Task<bool> SetUpstreamAsync(string name, string? currentUpstream)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_context.Repository is null)
        {
            return false;
        }

        List<string> candidates = [];

        foreach (GitBranch branch in _context.Refs.RemoteBranches)
        {
            candidates.Add(branch.ShortName);
        }

        SetUpstreamDialogViewModel model = new(name, candidates, currentUpstream);
        SetUpstreamDialogView view = new() { DataContext = model };

        DialogResult result = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "Set upstream";
            dialog.Content = view;
            dialog.PrimaryButtonText = "Set";
            dialog.SecondaryButtonText = "Clear";
            dialog.CloseButtonText = "Cancel";
            dialog.DefaultButton = DefaultButton.Primary;
        }).ConfigureAwait(true);

        if (result is DialogResult.Close or DialogResult.None)
        {
            return false;
        }

        string? upstream = result == DialogResult.Secondary ? null : model.Selected;

        if (result == DialogResult.Primary && upstream is null)
        {
            return false;
        }

        return await RunAsync(
            (handle, token) => _branches.SetUpstreamAsync(handle, name, upstream, token),
            $"Could not set the upstream of \"{name}\"").ConfigureAwait(true);
    }

    // ---------------------------------------------------------------- reset local to here

    /// <summary>
    /// Checks out a remote branch whose local branch already exists: the local one, at once when the
    /// two are level, and otherwise after asking whether to reset it to the remote's commit.
    /// </summary>
    /// <param name="local">The local branch of the remote branch's name.</param>
    /// <param name="remote">The remote branch that was asked for.</param>
    /// <returns><see langword="true"/> when HEAD moved.</returns>
    private async Task<bool> CheckoutExistingAsync(GitBranch local, GitBranch remote)
    {
        string name = local.ShortName;

        if (string.Equals(local.TargetSha, remote.TargetSha, StringComparison.Ordinal))
        {
            if (local.IsCurrent)
            {
                Report("Already checked out", $"\"{name}\" is checked out, on the same commit as \"{remote.ShortName}\".", InfoBarSeverity.Info);
                return false;
            }

            return await RunAsync(
                (handle, token) => _branches.CheckoutAsync(handle, name, token),
                $"Could not check out \"{name}\"").ConfigureAwait(true);
        }

        if (_context.Repository is not { } repository)
        {
            return false;
        }

        // An answer from git's exit code, not from an empty list: a list that could not be read must
        // not pass for "nothing is left behind".
        bool nothingLeftBehind = await _branches
            .IsMergedAsync(repository, name, remote.ShortName, _context.RepositoryLifetime)
            .ConfigureAwait(true);

        IReadOnlyList<string> leftBehind = nothingLeftBehind
            ? []
            : await _branches
                .GetUnmergedCommitsAsync(repository, name, remote.ShortName, MaximumListedCommits + 1, _context.RepositoryLifetime)
                .ConfigureAwait(true);

        string message = DescribeReset(local, remote, nothingLeftBehind, leftBehind);

        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = $"Reset \"{name}\" to \"{remote.ShortName}\"?";
            dialog.Content = message;
            dialog.PrimaryButtonText = ResetLocalToHere;

            // Checking the branch out where it is means nothing when it is already checked out.
            if (!local.IsCurrent)
            {
                dialog.SecondaryButtonText = $"Check out \"{name}\"";
            }

            dialog.CloseButtonText = "Cancel";

            // The reset is the default only when it costs nothing: with commits to leave behind, a
            // stray Enter must not be what leaves them.
            dialog.DefaultButton = nothingLeftBehind ? DefaultButton.Primary : DefaultButton.Close;
        }).ConfigureAwait(true);

        if (answer == DialogResult.Secondary && !local.IsCurrent)
        {
            return await RunAsync(
                (handle, token) => _branches.CheckoutAsync(handle, name, token),
                $"Could not check out \"{name}\"").ConfigureAwait(true);
        }

        if (answer != DialogResult.Primary)
        {
            return false;
        }

        bool reset = await RunAsync(
            (handle, token) => _branches.ResetAndCheckoutAsync(handle, name, remote.ShortName, token),
            $"Could not reset \"{name}\"").ConfigureAwait(true);

        if (reset)
        {
            Report(
                $"\"{name}\" reset to \"{remote.ShortName}\"",
                $"\"{name}\" is checked out, on {ShortSha(remote.TargetSha)}.",
                InfoBarSeverity.Success);
        }

        return reset;
    }

    /// <summary>
    /// What the "reset local to here" question says: where the two branches are, what the reset does,
    /// and what it leaves behind.
    /// </summary>
    internal static string DescribeReset(GitBranch local, GitBranch remote, bool nothingLeftBehind, IReadOnlyList<string> leftBehind)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(leftBehind);

        string name = local.ShortName;

        string where = $"\"{name}\" is on {ShortSha(local.TargetSha)} here, and \"{remote.ShortName}\" on {ShortSha(remote.TargetSha)}. "
            + $"{ResetLocalToHere} moves \"{name}\" to \"{remote.ShortName}\" and checks it out.";

        string cost;

        if (nothingLeftBehind)
        {
            cost = $"Nothing is left behind: \"{remote.ShortName}\" already has every commit of \"{name}\".";
        }
        else
        {
            List<string> lines = [.. leftBehind];

            if (lines.Count > MaximumListedCommits)
            {
                lines.RemoveRange(MaximumListedCommits, lines.Count - MaximumListedCommits);
                lines.Add("…and more");
            }

            cost = lines.Count == 0
                ? $"The commits only \"{name}\" has are left behind."
                : $"These commits are only on \"{name}\", and are left behind:\n\n{string.Join('\n', lines)}";
        }

        return $"{where}\n\n{cost}\n\nUncommitted changes come along, as for any checkout: git stops instead of overwriting one.";
    }

    private static string ShortSha(string sha) => sha.Length > 7 ? sha[..7] : sha;

    private static GitBranch? Find(IReadOnlyList<GitBranch> branches, string shortName)
    {
        foreach (GitBranch branch in branches)
        {
            if (string.Equals(branch.ShortName, shortName, StringComparison.Ordinal))
            {
                return branch;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- plumbing

    private async Task<bool> DeleteRemoteAsync(string shortName)
    {
        int separator = shortName.IndexOf('/', StringComparison.Ordinal);

        if (separator <= 0 || separator == shortName.Length - 1)
        {
            return false;
        }

        string remote = shortName[..separator];
        string branch = shortName[(separator + 1)..];

        bool confirmed = await ConfirmAsync(
            "Delete remote branch",
            $"Delete \"{branch}\" from \"{remote}\"? This changes the remote for everyone who uses it.",
            "Delete").ConfigureAwait(true);

        if (!confirmed)
        {
            return false;
        }

        return await RunAsync(
            (handle, token) => _branches.DeleteRemoteAsync(handle, remote, branch, token),
            $"Could not delete \"{shortName}\"").ConfigureAwait(true);
    }

    /// <summary>
    /// Builds the revisions the create dialog offers, most useful first.
    /// </summary>
    private IReadOnlyList<BranchStartPoint> BuildStartPoints(string? startPoint, string? label)
    {
        List<BranchStartPoint> points = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        void Add(string revision, string caption, string detail)
        {
            if (revision.Length > 0 && seen.Add(revision))
            {
                points.Add(new BranchStartPoint(revision, caption, detail));
            }
        }

        if (startPoint is { Length: > 0 })
        {
            Add(startPoint, label is { Length: > 0 } ? label : startPoint, "the commit you picked");
        }
        else if (_context.SelectedCommit is { } commit)
        {
            Add(commit.Sha, commit.ShortSha, commit.Subject);
        }

        Add("HEAD", "HEAD", _context.Head?.BranchName ?? "the current position");

        foreach (GitBranch branch in _context.Refs.LocalBranches)
        {
            Add(branch.ShortName, branch.ShortName, branch.TipSubject);
        }

        foreach (GitBranch branch in _context.Refs.RemoteBranches)
        {
            Add(branch.ShortName, branch.ShortName, branch.TipSubject);
        }

        foreach (GitTag tag in _context.Refs.Tags)
        {
            Add(tag.ShortName, tag.ShortName, "tag");
        }

        return points;
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

    private static string CountCommits(int commits)
        => commits == 1
            ? "1 commit"
            : $"{commits.ToString(CultureInfo.CurrentCulture)} commits";

    private async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        DialogResult result = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = title;
            dialog.Content = message;
            dialog.PrimaryButtonText = confirmText;
            dialog.CloseButtonText = "Cancel";

            // The close button, not the destructive one: a stray Enter must never delete a branch.
            dialog.DefaultButton = DefaultButton.Close;
        }).ConfigureAwait(true);

        return result == DialogResult.Primary;
    }

    private async Task<DialogResult> ShowFormAsync(
        string title,
        Control content,
        object model,
        Func<bool> isValid,
        Action<EventHandler> subscribe,
        Action<EventHandler> unsubscribe,
        string primaryButtonText)
    {
        ArgumentNullException.ThrowIfNull(model);

        ContentDialog? shown = null;

        void OnValidationChanged(object? sender, EventArgs e)
        {
            if (shown is not null)
            {
                shown.IsPrimaryButtonEnabled = isValid();
            }
        }

        subscribe(OnValidationChanged);

        try
        {
            return await _dialogs.ShowAsync(dialog =>
            {
                shown = dialog;
                dialog.Title = title;
                dialog.Content = content;
                dialog.PrimaryButtonText = primaryButtonText;
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
                dialog.IsPrimaryButtonEnabled = isValid();
            }).ConfigureAwait(true);
        }
        finally
        {
            unsubscribe(OnValidationChanged);
        }
    }

    /// <summary>
    /// Runs a write under the repository's exclusive lock, turning both kinds of failure into a
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
            Report(failureTitle, refusal.Message, InfoBarSeverity.Warning);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "{Title}", failureTitle);

            Report(failureTitle, FirstLine(exception.StandardError), InfoBarSeverity.Error);
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

    private void Report(string title, string message, InfoBarSeverity severity)
        => _infoBar.Notify(title, message, severity);
}
