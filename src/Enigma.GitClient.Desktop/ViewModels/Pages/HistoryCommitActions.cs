using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Core.Reset;
using Enigma.GitClient.Core.Squash;
using Enigma.GitClient.Desktop.Services;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// What the history's commit actions need of the page they act for, beyond what the branch and tag
/// actions need.
/// </summary>
public interface IHistoryRowHost : IHistoryRefHost
{
    /// <summary>
    /// Gets or sets the commit a squash starts after, which a line's "Set as squash base" sets.
    /// </summary>
    SquashBase? SquashBase { get; set; }

    /// <summary>
    /// Gets the lines loaded so far, which a squash range is measured among.
    /// </summary>
    IReadOnlyList<CommitRowViewModel> Rows { get; }

    /// <summary>
    /// Selects a commit's line, loading more of the history until it is there.
    /// </summary>
    /// <param name="sha">The commit.</param>
    /// <returns>Whether the line was found.</returns>
    Task<bool> RevealAsync(string sha);
}

/// <summary>
/// The commands of a history line's own menu that act on its commit.
/// </summary>
/// <param name="CreateBranchHere">Creates a branch at the commit.</param>
/// <param name="Checkout">Checks the commit out, detached.</param>
/// <param name="ResetSoft">Moves the branch named in the menu to the commit, keeping the changes.</param>
/// <param name="ResetHard">Moves it there, throwing the uncommitted changes away.</param>
/// <param name="Revert">Records a commit undoing this one on the branch checked out.</param>
/// <param name="CreateWorktreeHere">Creates a worktree on a new branch at the commit.</param>
/// <param name="ShowDetails">Shows the commit's title, description, author, date and hash.</param>
/// <param name="Squash">The squash base and the squash itself.</param>
public sealed record HistoryCommitCommands(
    AsyncRelayCommand<CommitRowViewModel> CreateBranchHere,
    AsyncRelayCommand<CommitRowViewModel> Checkout,
    AsyncRelayCommand<HistoryResetRequest> ResetSoft,
    AsyncRelayCommand<HistoryResetRequest> ResetHard,
    AsyncRelayCommand<CommitRowViewModel> Revert,
    AsyncRelayCommand<CommitRowViewModel> CreateWorktreeHere,
    AsyncRelayCommand<CommitRowViewModel> ShowDetails,
    HistorySquashCommands Squash);

/// <summary>
/// What a history line's own menu does with its commit: create a branch or a worktree there, check it
/// out, reset to it, revert it, squash up to it, and show its details.
/// </summary>
/// <remarks>
/// Each action runs its operation, with the operation's own questions and reports, and reloads the
/// history when the operation changed something.
/// </remarks>
public sealed class HistoryCommitActions
{
    private readonly IBranchOperations _branchOperations;
    private readonly ICheckoutOperations _checkoutOperations;
    private readonly IWorktreeOperations _worktreeOperations;
    private readonly IResetOperations _resetOperations;
    private readonly IRevertOperations _revertOperations;
    private readonly ISquashOperations _squashOperations;
    private readonly ICommitDetailsDialogService _details;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="branchOperations">Creates a branch at a line's commit.</param>
    /// <param name="checkoutOperations">Checks a line's commit out.</param>
    /// <param name="worktreeOperations">Creates a worktree on a new branch from a line, dialog and all.</param>
    /// <param name="resetOperations">Moves the branch that is checked out to a line's commit.</param>
    /// <param name="revertOperations">Records a commit undoing a line's commit on the branch that is checked out.</param>
    /// <param name="squashOperations">Squashes the commits since the squash base onto a new branch.</param>
    /// <param name="details">Shows a commit's title, description, author, date and hash.</param>
    public HistoryCommitActions(
        IBranchOperations branchOperations,
        ICheckoutOperations checkoutOperations,
        IWorktreeOperations worktreeOperations,
        IResetOperations resetOperations,
        IRevertOperations revertOperations,
        ISquashOperations squashOperations,
        ICommitDetailsDialogService details)
    {
        ArgumentNullException.ThrowIfNull(branchOperations);
        ArgumentNullException.ThrowIfNull(checkoutOperations);
        ArgumentNullException.ThrowIfNull(worktreeOperations);
        ArgumentNullException.ThrowIfNull(resetOperations);
        ArgumentNullException.ThrowIfNull(revertOperations);
        ArgumentNullException.ThrowIfNull(squashOperations);
        ArgumentNullException.ThrowIfNull(details);

        _branchOperations = branchOperations;
        _checkoutOperations = checkoutOperations;
        _worktreeOperations = worktreeOperations;
        _resetOperations = resetOperations;
        _revertOperations = revertOperations;
        _squashOperations = squashOperations;
        _details = details;
    }

    /// <summary>
    /// Builds the commands, acting for one page.
    /// </summary>
    /// <param name="host">The page they act for.</param>
    /// <param name="clearSquashBase">The page's own command that forgets the squash base.</param>
    /// <returns>The commands.</returns>
    public HistoryCommitCommands CreateCommands(IHistoryRowHost host, RelayCommand clearSquashBase)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(clearSquashBase);

        // A stash is not a commit anyone builds on, so it is no base; the squash itself is offered only
        // where the base is on the line's first-parent line, which the loaded lines answer at once.
        HistorySquashCommands squash = new(
            new RelayCommand<CommitRowViewModel>(row => SetSquashBase(host, row), row => row is { Commit: not null, IsStash: false }),
            clearSquashBase,
            new AsyncRelayCommand<HistorySquashRequest>(
                request => SquashAsync(host, request),
                request => request is { Row: { Commit: not null, IsStash: false } row } && MeasureSquash(host, row, request.Base) is not null),
            () => host.SquashBase,
            row => host.SquashBase is { } squashBase ? MeasureSquash(host, row, squashBase) : null);

        return new HistoryCommitCommands(
            new AsyncRelayCommand<CommitRowViewModel>(row => CreateBranchHereAsync(host, row), HasCommit),
            new AsyncRelayCommand<CommitRowViewModel>(row => CheckoutAsync(host, row), HasCommit),

            // Soft back onto the commit HEAD is already at would do nothing; hard there is how the
            // uncommitted work is thrown away, so it stays.
            new AsyncRelayCommand<HistoryResetRequest>(
                request => ResetAsync(host, request, ResetMode.Soft),
                request => request is { Row: { Commit: not null, IsHead: false } }),
            new AsyncRelayCommand<HistoryResetRequest>(
                request => ResetAsync(host, request, ResetMode.Hard),
                request => request?.Row.Commit is not null),
            new AsyncRelayCommand<CommitRowViewModel>(row => RevertAsync(host, row), HasCommit),
            new AsyncRelayCommand<CommitRowViewModel>(row => CreateWorktreeHereAsync(host, row), HasCommit),
            new AsyncRelayCommand<CommitRowViewModel>(ShowDetailsAsync, row => row?.Commit is not null),
            squash);
    }

    private static bool HasCommit(CommitRowViewModel? row) => row?.Commit is not null;

    private async Task CreateBranchHereAsync(IHistoryRowHost host, CommitRowViewModel? row)
    {
        if (row?.Commit is null)
        {
            return;
        }

        if (await _branchOperations.CreateAsync(row.Sha, row.ShortSha).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task CreateWorktreeHereAsync(IHistoryRowHost host, CommitRowViewModel? row)
    {
        if (row?.Commit is null)
        {
            return;
        }

        if (await _worktreeOperations.CreateAsync(row.Sha, row.ShortSha).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task CheckoutAsync(IHistoryRowHost host, CommitRowViewModel? row)
    {
        if (row?.Commit is null)
        {
            return;
        }

        if (await _checkoutOperations.CheckoutAsync(row.Sha, row.ShortSha, detach: true).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Moves the branch the menu named to a line's commit, and re-reads the history when it moved.
    /// </summary>
    private async Task ResetAsync(IHistoryRowHost host, HistoryResetRequest? request, ResetMode mode)
    {
        if (request?.Row is not { Commit: not null } row)
        {
            return;
        }

        if (await _resetOperations.ResetAsync(row.Sha, row.ShortSha, request.Branch, mode).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Records a commit undoing a line's commit on the branch that is checked out, and re-reads the
    /// history when one was recorded.
    /// </summary>
    private async Task RevertAsync(IHistoryRowHost host, CommitRowViewModel? row)
    {
        if (row?.Commit is not { } commit)
        {
            return;
        }

        if (await _revertOperations.RevertAsync(row.Sha, row.ShortSha, row.Subject, commit.IsMerge).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    private static void SetSquashBase(IHistoryRowHost host, CommitRowViewModel? row)
    {
        if (row is { Commit: not null, IsStash: false })
        {
            host.SquashBase = new SquashBase(row.Sha, row.ShortSha, row.Subject);
        }
    }

    /// <summary>
    /// Squashes everything since the base up to a line onto a new branch, then shows the result: the
    /// history is read again and the squash commit selected. The base has served its squash and is
    /// forgotten.
    /// </summary>
    private async Task SquashAsync(IHistoryRowHost host, HistorySquashRequest? request)
    {
        if (request is not { Row: { Commit: not null } row, Base: { } squashBase })
        {
            return;
        }

        SquashCandidate candidate = new(
            new SquashEnd(squashBase.Sha, squashBase.ShortSha, squashBase.Subject),
            new SquashEnd(row.Sha, row.ShortSha, row.Subject),
            LocalBranchesAt(row),
            row.IsHead);

        if (await _squashOperations.SquashAsync(candidate).ConfigureAwait(true) is not { } commit)
        {
            return;
        }

        host.SquashBase = null;

        await host.ReloadAsync().ConfigureAwait(true);
        await host.RevealAsync(commit).ConfigureAwait(true);
    }

    /// <summary>
    /// Measures the range from a base to a line among the loaded lines, as the menu counts it.
    /// </summary>
    private static SquashRangeMeasure? MeasureSquash(IHistoryRowHost host, CommitRowViewModel row, SquashBase squashBase)
    {
        if (row.Commit is null)
        {
            return null;
        }

        IReadOnlyList<CommitRowViewModel> rows = host.Rows;
        List<GitCommit> loaded = new(rows.Count);

        foreach (CommitRowViewModel line in rows)
        {
            if (line.Commit is { } commit)
            {
                loaded.Add(commit);
            }
        }

        return SquashRange.Measure(loaded, squashBase.Sha, row.Sha);
    }

    /// <summary>
    /// The local branches that end at a line, the checked-out one first: the squash dialog names the
    /// new branch after the first, and says it keeps its commits.
    /// </summary>
    private static List<string> LocalBranchesAt(CommitRowViewModel row)
    {
        List<string> names = [];

        foreach (HistoryBranchViewModel branch in row.Branches)
        {
            if (branch.IsRemote)
            {
                continue;
            }

            if (branch.IsCurrent)
            {
                names.Insert(0, branch.Name);
            }
            else
            {
                names.Add(branch.Name);
            }
        }

        return names;
    }

    /// <summary>
    /// Shows a line's commit in the details dialog. The uncommitted line has none, and shows nothing.
    /// </summary>
    private async Task ShowDetailsAsync(CommitRowViewModel? row)
    {
        if (row?.Commit is { } commit)
        {
            await _details.ShowAsync(commit).ConfigureAwait(true);
        }
    }
}
