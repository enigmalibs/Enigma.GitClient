using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    /// Confirms, naming whatever would be lost, then deletes the branch: a batch of one, asked in the
    /// words a single delete uses.
    /// </summary>
    /// <param name="name">The branch to delete, as <c>name</c> or <c>remote/name</c>.</param>
    /// <param name="isRemote">Whether the branch lives on a remote.</param>
    /// <returns><see langword="true"/> when the branch was deleted.</returns>
    Task<bool> DeleteAsync(string name, bool isRemote);

    /// <summary>
    /// Confirms once — every branch, the commits each unmerged one would lose, the checked-out branch
    /// left out, and what deleting from a remote does — then deletes them all, going on past one that
    /// fails, reads the repository again once, and says once what went and what did not.
    /// </summary>
    /// <param name="branches">The branches to delete, local and remote.</param>
    /// <returns><see langword="true"/> when at least one branch was deleted.</returns>
    Task<bool> DeleteAsync(IReadOnlyList<BranchToDelete> branches);

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
/// A branch a delete names.
/// </summary>
/// <param name="Name">The branch, as <c>name</c>, or <c>remote/name</c> for a remote one.</param>
/// <param name="IsRemote">Whether the branch lives on a remote.</param>
public sealed record BranchToDelete(string Name, bool IsRemote);

/// <summary>
/// Default <see cref="IBranchOperations"/>.
/// </summary>
public sealed class BranchOperations : IBranchOperations
{
    /// <summary>Why a delete leaves the checked-out branch out, after its name in the question.</summary>
    public const string CheckedOutReason = "it is checked out";

    /// <summary>What the question about several branches ends with when any of them is on a remote.</summary>
    public const string RemoteWarning = "Deleting a branch from a remote changes the remote for everyone who uses it.";

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
    public Task<bool> DeleteAsync(string name, bool isRemote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return DeleteAsync([new BranchToDelete(name, isRemote)]);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// What would be lost is read before the question, branch by branch, because naming it is the
    /// whole point of the question: "are you sure?" teaches nothing, "these three commits exist nowhere
    /// else" is a decision a reader can make. A branch named as unmerged is deleted with <c>-D</c>, and
    /// only such a branch: one that gained commits since is refused by <c>-d</c>, and the batch goes on.
    /// </para>
    /// <para>
    /// No push guard and no overlay for a remote branch, as before: each is one short round trip.
    /// </para>
    /// </remarks>
    public async Task<bool> DeleteAsync(IReadOnlyList<BranchToDelete> branches)
    {
        ArgumentNullException.ThrowIfNull(branches);

        if (branches.Count == 0 || _context.Repository is not { } repository)
        {
            return false;
        }

        // The question reads the items; the batch needs what each one is, and whether it was named as
        // unmerged.
        Dictionary<DeletionItem, Described> byItem = new(ReferenceEqualityComparer.Instance);
        List<DeletionItem> items = [];

        foreach (BranchToDelete branch in branches)
        {
            Described described = await DescribeAsync(repository, branch).ConfigureAwait(true);

            items.Add(described.Item);
            byItem[described.Item] = described;
        }

        DeletionPlan plan = items.Count == 1
            ? PlanForOne(byItem[items[0]])
            : PlanForMany(items, anyRemote: items.Any(item => !item.IsSkipped && byItem[item].Branch.IsRemote));

        if (plan.ToDelete.Count == 0)
        {
            // The checked-out branch alone: nothing to ask about, and git would refuse it anyway.
            string name = plan.Skipped[0].Name;
            Report($"Could not delete \"{name}\"", $"\"{name}\" is the branch you have checked out. Switch to another branch first.", InfoBarSeverity.Warning);
            return false;
        }

        if (!await _dialogs.ConfirmDestructiveAsync(plan.Title, plan.Message, plan.ConfirmText).ConfigureAwait(true))
        {
            return false;
        }

        DeletionOutcome outcome;

        try
        {
            outcome = await DeletionBatch.RunAsync(
                _context,
                plan.ToDelete,
                (handle, item, token) => DeleteOneAsync(handle, byItem[item], token),
                _logger).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the batch.
            return false;
        }

        foreach (DeletionItem skipped in plan.Skipped)
        {
            outcome.Failed(skipped.Name, skipped.SkipReason!);
        }

        if (items.Count == 1)
        {
            ReportFailureOfOne(outcome, $"Could not delete \"{items[0].Name}\"");
        }
        else
        {
            Report(outcome.Summarise("Deleted", "branch", "branches"));
        }

        return outcome.Deleted.Count > 0;
    }

    /// <summary>
    /// What the question says about one branch: what it would lose, or why it is left out — and
    /// whether deleting it needs the force only a branch named as unmerged gets.
    /// </summary>
    private async Task<Described> DescribeAsync(RepositoryHandle repository, BranchToDelete branch)
    {
        if (branch.IsRemote)
        {
            return new Described(new DeletionItem(branch.Name), branch, Force: false, Unmerged: 0);
        }

        if (string.Equals(_context.Head?.BranchName, branch.Name, StringComparison.Ordinal))
        {
            return new Described(new DeletionItem(branch.Name) { SkipReason = CheckedOutReason }, branch, Force: false, Unmerged: 0);
        }

        bool merged = await _branches
            .IsMergedAsync(repository, branch.Name, cancellationToken: _context.RepositoryLifetime)
            .ConfigureAwait(true);

        if (merged)
        {
            return new Described(new DeletionItem(branch.Name), branch, Force: false, Unmerged: 0);
        }

        IReadOnlyList<string> subjects = await _branches
            .GetUnmergedCommitsAsync(repository, branch.Name, limit: MaximumListedCommits, cancellationToken: _context.RepositoryLifetime)
            .ConfigureAwait(true);

        int count = Math.Max(
            subjects.Count,
            await _branches.CountUnmergedCommitsAsync(repository, branch.Name, cancellationToken: _context.RepositoryLifetime)
                .ConfigureAwait(true));

        List<string> details = [.. subjects];

        if (count > subjects.Count)
        {
            details.Add($"…and {(count - subjects.Count).ToString(CultureInfo.CurrentCulture)} more");
        }

        DeletionItem item = count == 0
            ? new DeletionItem(branch.Name)
            : new DeletionItem(branch.Name)
            {
                Note = $"holds {CountCommits(count)} the current branch does not; deleting it loses them",
                Details = details,
            };

        return new Described(item, branch, Force: true, Unmerged: count);
    }

    /// <summary>
    /// The question about one branch, in the words its delete has always used.
    /// </summary>
    private static DeletionPlan PlanForOne(Described described)
    {
        (DeletionItem item, BranchToDelete branch, _, int unmerged) = described;

        if (branch.IsRemote)
        {
            string message = SplitRemote(branch.Name) is (string remote, string name)
                ? $"Delete \"{name}\" from \"{remote}\"? This changes the remote for everyone who uses it."
                : $"Delete \"{branch.Name}\"? This changes the remote for everyone who uses it.";

            return DeletionPlan.ForOne(item, "Delete remote branch", message, "Delete");
        }

        if (unmerged == 0)
        {
            return DeletionPlan.ForOne(item, "Delete branch", $"Delete the branch \"{item.Name}\"?", "Delete");
        }

        return DeletionPlan.ForOne(
            item,
            "Delete branch",
            $"\"{item.Name}\" holds {CountCommits(unmerged)} that the current branch does not. "
            + "Deleting it loses them.\n\n"
            + string.Join('\n', item.Details),
            "Delete");
    }

    /// <summary>
    /// The question about several branches: every one on a line of its own, then what deleting from a
    /// remote does when any is a remote branch.
    /// </summary>
    private static DeletionPlan PlanForMany(IReadOnlyList<DeletionItem> items, bool anyRemote)
    {
        int deleted = items.Count(item => !item.IsSkipped);

        return DeletionPlan.ForMany(
            items,
            $"Delete {DeletionOutcome.Count(deleted, "branch", "branches")}",
            deleted == 1 ? "Delete this branch?" : $"Delete these {deleted.ToString(CultureInfo.CurrentCulture)} branches?",
            anyRemote ? RemoteWarning : null,
            "Delete");
    }

    /// <summary>
    /// Deletes one branch of a batch: <c>git branch -d</c>, or <c>-D</c> for one named as unmerged, or
    /// <c>git push --delete</c> for a remote one.
    /// </summary>
    private Task DeleteOneAsync(RepositoryHandle handle, Described described, CancellationToken token)
    {
        BranchToDelete branch = described.Branch;

        if (!branch.IsRemote)
        {
            return _branches.DeleteAsync(handle, branch.Name, described.Force, token);
        }

        return SplitRemote(branch.Name) is (string remote, string name)
            ? _branches.DeleteRemoteAsync(handle, remote, name, token)
            : throw new GitOperationRefusedException($"\"{branch.Name}\" does not name a branch on a remote.");
    }

    /// <summary>
    /// Splits <c>origin/feature/x</c> into the remote, <c>origin</c>, and the branch on it,
    /// <c>feature/x</c>; <see langword="null"/> when there is no such split.
    /// </summary>
    private static (string Remote, string Name)? SplitRemote(string shortName)
    {
        int separator = shortName.IndexOf('/', StringComparison.Ordinal);

        return separator <= 0 || separator == shortName.Length - 1
            ? null
            : (shortName[..separator], shortName[(separator + 1)..]);
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

    /// <summary>
    /// Says why the one branch of a batch of one did not go, as a single delete always has.
    /// </summary>
    private void ReportFailureOfOne(DeletionOutcome outcome, string title)
    {
        if (outcome.NotDeleted.Count == 1)
        {
            Report(title, outcome.NotDeleted[0].Reason, outcome.HasErrors ? InfoBarSeverity.Error : InfoBarSeverity.Warning);
        }
    }

    private void Report((string Title, string Message, InfoBarSeverity Severity) note)
        => Report(note.Title, note.Message, note.Severity);

    private void Report(string title, string message, InfoBarSeverity severity)
        => _infoBar.Notify(title, message, severity);

    /// <summary>
    /// One branch of a delete, as its question describes it and its batch deletes it.
    /// </summary>
    /// <param name="Item">What the question and the summary say about it.</param>
    /// <param name="Branch">The branch.</param>
    /// <param name="Force">Whether it is deleted with <c>-D</c>: only a branch the question named as unmerged.</param>
    /// <param name="Unmerged">How many commits the question said it would lose.</param>
    private sealed record Described(DeletionItem Item, BranchToDelete Branch, bool Force, int Unmerged);
}
