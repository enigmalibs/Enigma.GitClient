using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Core.Merging;
using Enigma.GitClient.Core.Worktrees;
using Enigma.GitClient.Desktop.Services;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// What the history's branch and tag actions need of the page they act for.
/// </summary>
/// <remarks>
/// The page implements it and hands itself over when it builds its commands. The actions are
/// singletons the page takes in its constructor, so they cannot take the page in theirs.
/// </remarks>
public interface IHistoryRefHost
{
    /// <summary>
    /// Gets or sets the branch "Merge into" merges from, which a badge's "Set as merge source" sets.
    /// </summary>
    MergeSource? MergeSource { get; set; }

    /// <summary>
    /// Reads the history again, after an action that changed what it draws.
    /// </summary>
    /// <returns>A task that completes once the first page is loaded.</returns>
    Task ReloadAsync();
}

/// <summary>
/// What a branch badge's menu does in the history, and the drop of one badge on another: checkout,
/// merge and fast-forward, merge source, delete, pull and push, rename, and the branch's worktree.
/// </summary>
/// <remarks>
/// Each action runs its operation, with the operation's own questions and reports, and reloads the
/// history when the operation changed something.
/// </remarks>
public sealed class HistoryBranchActions
{
    private readonly IBranchOperations _branchOperations;
    private readonly IWorktreeOperations _worktreeOperations;
    private readonly IBranchDropOperations _dropOperations;
    private readonly ISyncOperations _syncOperations;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="branchOperations">Checks out, renames and deletes a branch.</param>
    /// <param name="worktreeOperations">Creates, opens and removes the worktree a branch is in.</param>
    /// <param name="dropOperations">Merges one branch into another, checking the destination out first.</param>
    /// <param name="syncOperations">Pulls and pushes a branch.</param>
    public HistoryBranchActions(
        IBranchOperations branchOperations,
        IWorktreeOperations worktreeOperations,
        IBranchDropOperations dropOperations,
        ISyncOperations syncOperations)
    {
        ArgumentNullException.ThrowIfNull(branchOperations);
        ArgumentNullException.ThrowIfNull(worktreeOperations);
        ArgumentNullException.ThrowIfNull(dropOperations);
        ArgumentNullException.ThrowIfNull(syncOperations);

        _branchOperations = branchOperations;
        _worktreeOperations = worktreeOperations;
        _dropOperations = dropOperations;
        _syncOperations = syncOperations;
    }

    /// <summary>
    /// Builds the commands of a branch badge's menu, acting for one page.
    /// </summary>
    /// <param name="host">The page they act for.</param>
    /// <param name="clearMergeSource">The page's own command that forgets the merge source.</param>
    /// <param name="currentBranch">The branch checked out, or <see langword="null"/> on a detached HEAD.</param>
    /// <param name="copy">Copies a branch's name.</param>
    /// <param name="worktreeOf">The worktree a branch is checked out in, if any.</param>
    /// <returns>The commands.</returns>
    public HistoryBranchCommands CreateCommands(
        IHistoryRefHost host,
        RelayCommand clearMergeSource,
        Func<string?> currentBranch,
        AsyncRelayCommand<string> copy,
        Func<string, GitWorktree?> worktreeOf)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(clearMergeSource);
        ArgumentNullException.ThrowIfNull(currentBranch);
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(worktreeOf);

        return new HistoryBranchCommands(
            new AsyncRelayCommand<HistoryBranchViewModel>(branch => CheckoutAsync(host, branch), branch => branch?.CanCheckout == true),
            new RelayCommand<HistoryBranchViewModel>(branch => SetMergeSource(host, branch), branch => branch?.CanSetAsMergeSource == true),
            clearMergeSource,
            // "Merge" records a merge commit even when a fast-forward would do, which is what sets it
            // apart from the fast-forward-only item beside it.
            new AsyncRelayCommand<HistoryBranchViewModel>(
                branch => MergeIntoAsync(host, branch, FastForwardMode.Never),
                branch => branch?.CanMergeInto == true),
            new AsyncRelayCommand<HistoryBranchViewModel>(
                branch => MergeIntoAsync(host, branch, FastForwardMode.Only),
                branch => branch?.CanMergeInto == true),
            new AsyncRelayCommand<HistoryBranchViewModel>(
                branch => MergeIntoCurrentAsync(host, branch, currentBranch),
                branch => branch?.CanMergeIntoCurrent == true),
            new AsyncRelayCommand<HistoryBranchViewModel>(branch => DeleteAsync(host, branch), branch => branch?.CanDelete == true),
            new AsyncRelayCommand<HistoryBranchViewModel>(branch => PullAsync(host, branch), branch => branch?.CanSynchronise == true),
            new AsyncRelayCommand<HistoryBranchViewModel>(branch => PushAsync(host, branch), branch => branch?.CanSynchronise == true),
            () => host.MergeSource,
            currentBranch,
            copy,
            new AsyncRelayCommand<HistoryBranchViewModel>(branch => CreateWorktreeAsync(host, branch), branch => branch is not null),

            // The worktree that has the branch: opened anywhere but where its folder has gone, and
            // removed unless it is the main working tree.
            new HistoryWorktreeCommands(
                worktreeOf,
                new AsyncRelayCommand<HistoryBranchViewModel>(
                    branch => branch is { Worktree: { } worktree } ? _worktreeOperations.OpenAsync(worktree) : Task.CompletedTask,
                    branch => branch?.Worktree is { IsMissing: false }),
                new AsyncRelayCommand<HistoryBranchViewModel>(
                    branch => branch is { Worktree: { } worktree } ? _worktreeOperations.OpenFolderAsync(worktree) : Task.CompletedTask,
                    branch => branch?.Worktree is { IsMissing: false }),
                new AsyncRelayCommand<HistoryBranchViewModel>(
                    branch => branch is { Worktree: { } worktree } ? _worktreeOperations.OpenTerminalAsync(worktree) : Task.CompletedTask,
                    branch => branch?.Worktree is { IsMissing: false }),
                new AsyncRelayCommand<HistoryBranchViewModel>(
                    branch => RemoveWorktreeAsync(host, branch),
                    branch => branch?.CanRemoveWorktree == true)),
            new AsyncRelayCommand<HistoryBranchViewModel>(branch => RenameAsync(host, branch), branch => branch?.CanRename == true),

            // Both or neither: git refuses to delete the branch HEAD is on, and deleting only its
            // remote is the item above it.
            new AsyncRelayCommand<HistoryBranchViewModel>(
                branch => DeleteWithRemoteAsync(host, branch),
                branch => branch is { CanDeleteWithRemote: true, CanDelete: true }));
    }

    /// <summary>
    /// Builds the command that merges the branch dropped on another one into it, acting for one page.
    /// </summary>
    /// <param name="host">The page it acts for.</param>
    /// <param name="fastForward">Whether the merge records a merge commit, or only fast-forwards.</param>
    /// <returns>The command.</returns>
    public AsyncRelayCommand<HistoryBranchDrop> CreateDropCommand(IHistoryRefHost host, FastForwardMode fastForward)
    {
        ArgumentNullException.ThrowIfNull(host);

        return new AsyncRelayCommand<HistoryBranchDrop>(
            drop => DropAsync(host, drop, fastForward),
            drop => drop?.CanDrop == true);
    }

    private async Task CreateWorktreeAsync(IHistoryRefHost host, HistoryBranchViewModel? branch)
    {
        if (branch is null)
        {
            return;
        }

        if (await _worktreeOperations.CreateAsync(branch.Name, branch.Name).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Removes the worktree that has a branch checked out, and reloads the history as a branch
    /// deletion does: its badge loses the tree, and the branch may be gone with it.
    /// </summary>
    private async Task RemoveWorktreeAsync(IHistoryRefHost host, HistoryBranchViewModel? branch)
    {
        if (branch?.Worktree is not { } worktree)
        {
            return;
        }

        if (await _worktreeOperations.RemoveAsync(worktree).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task CheckoutAsync(IHistoryRefHost host, HistoryBranchViewModel? branch)
    {
        if (branch is null)
        {
            return;
        }

        if (await _branchOperations.CheckoutAsync(branch.Name, branch.IsRemote).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task RenameAsync(IHistoryRefHost host, HistoryBranchViewModel? branch)
    {
        if (branch is not { CanRename: true })
        {
            return;
        }

        if (await _branchOperations.RenameAsync(branch.Name).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task DeleteAsync(IHistoryRefHost host, HistoryBranchViewModel? branch)
    {
        if (branch is null)
        {
            return;
        }

        if (await _branchOperations.DeleteAsync(branch.Name, branch.IsRemote).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Deletes a branch and the upstream its badge draws with it, as one batch: one question naming both
    /// and whatever would be lost, then both deletes, one reading of the repository and one report.
    /// </summary>
    private async Task DeleteWithRemoteAsync(IHistoryRefHost host, HistoryBranchViewModel? branch)
    {
        if (branch is not { CanDeleteWithRemote: true, Remote: { } remote })
        {
            return;
        }

        BranchToDelete[] both = [new(branch.Name, IsRemote: false), new(remote.Name, IsRemote: true)];

        if (await _branchOperations.DeleteAsync(both).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task PullAsync(IHistoryRefHost host, HistoryBranchViewModel? branch)
    {
        if (branch is { CanSynchronise: true } && await _syncOperations.PullBranchAsync(branch.Name).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task PushAsync(IHistoryRefHost host, HistoryBranchViewModel? branch)
    {
        // A push moves the remote-tracking branch, which the graph draws too.
        if (branch is { CanSynchronise: true } && await _syncOperations.PushBranchAsync(branch.Name).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    private static void SetMergeSource(IHistoryRefHost host, HistoryBranchViewModel? branch)
    {
        if (branch is not null)
        {
            host.MergeSource = new MergeSource(branch.Name, branch.IsRemote);
        }
    }

    /// <summary>
    /// Merges the merge source into a branch, through the same flow as dropping one branch on another:
    /// the branch is checked out first when it is not the current one.
    /// </summary>
    private async Task MergeIntoAsync(IHistoryRefHost host, HistoryBranchViewModel? branch, FastForwardMode fastForward)
    {
        if (branch?.MergeRequest is not { } request || !BranchDropOperations.CanDrop(request))
        {
            return;
        }

        if (await _dropOperations.DropAsync(request, fastForward).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Merges a branch into the one checked out — the question the row's menu used to be able to ask of
    /// only one branch per line.
    /// </summary>
    private async Task DropAsync(IHistoryRefHost host, HistoryBranchDrop? drop, FastForwardMode fastForward)
    {
        if (drop is not { CanDrop: true })
        {
            return;
        }

        if (await _dropOperations.DropAsync(drop.Request, fastForward).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task MergeIntoCurrentAsync(IHistoryRefHost host, HistoryBranchViewModel? branch, Func<string?> currentBranch)
    {
        if (branch is null || !branch.CanMergeIntoCurrent || currentBranch() is not { } current)
        {
            return;
        }

        BranchDropRequest request = new(branch.Name, branch.IsRemote, current, false, true);

        if (await _dropOperations.DropAsync(request, FastForwardMode.Never).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }
}
