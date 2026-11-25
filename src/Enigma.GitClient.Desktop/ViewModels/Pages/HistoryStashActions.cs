using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Core.Stashes;
using Enigma.GitClient.Desktop.Services;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// The history's commands for the uncommitted work and the stash.
/// </summary>
/// <param name="Lines">Stash, apply, pop and drop, as the lines' menus offer them.</param>
/// <param name="DiscardUncommitted">Throws the uncommitted work away, from the uncommitted line.</param>
/// <param name="Stash">Stashes the uncommitted work, from the history's toolbar.</param>
public sealed record HistoryStashActionCommands(
    HistoryStashCommands Lines,
    AsyncRelayCommand<CommitRowViewModel> DiscardUncommitted,
    AsyncRelayCommand Stash);

/// <summary>
/// What the history does with the uncommitted work and the stash: stash it, apply, pop or drop a stash
/// line, and throw the uncommitted work away.
/// </summary>
/// <remarks>
/// Each action runs its operation, with the operation's own questions and reports, and reloads the
/// history when the operation changed something.
/// </remarks>
public sealed class HistoryStashActions
{
    private readonly IStashOperations _stashOperations;
    private readonly IDiscardOperations _discards;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="stashOperations">Stashes, and applies, pops and drops a stash entry.</param>
    /// <param name="discards">Throws the uncommitted work away.</param>
    public HistoryStashActions(IStashOperations stashOperations, IDiscardOperations discards)
    {
        ArgumentNullException.ThrowIfNull(stashOperations);
        ArgumentNullException.ThrowIfNull(discards);

        _stashOperations = stashOperations;
        _discards = discards;
    }

    /// <summary>
    /// Builds the commands, acting for one page.
    /// </summary>
    /// <param name="host">The page they act for.</param>
    /// <param name="hasUncommittedChanges">Whether the page shows uncommitted work, which the toolbar's stash needs.</param>
    /// <returns>The commands.</returns>
    public HistoryStashActionCommands CreateCommands(IHistoryRefHost host, Func<bool> hasUncommittedChanges)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(hasUncommittedChanges);

        return new HistoryStashActionCommands(
            new HistoryStashCommands(
                new AsyncRelayCommand<CommitRowViewModel>(_ => StashAsync(host), row => row is { IsUncommitted: true }),
                new AsyncRelayCommand<CommitRowViewModel>(row => StashLineAsync(host, row, _stashOperations.ApplyAsync), IsStashLine),
                new AsyncRelayCommand<CommitRowViewModel>(row => StashLineAsync(host, row, _stashOperations.PopAsync), IsStashLine),
                new AsyncRelayCommand<CommitRowViewModel>(row => StashLineAsync(host, row, _stashOperations.DropAsync), IsStashLine)),
            new AsyncRelayCommand<CommitRowViewModel>(
                _ => DiscardUncommittedAsync(host),
                row => row is { IsUncommitted: true } && _discards.CanDiscardUncommitted),
            new AsyncRelayCommand(() => StashAsync(host), hasUncommittedChanges));
    }

    private static bool IsStashLine(CommitRowViewModel? row) => row?.Stash is not null;

    private async Task StashAsync(IHistoryRefHost host)
    {
        if (await _stashOperations.StashAsync().ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    private async Task DiscardUncommittedAsync(IHistoryRefHost host)
    {
        if (await _discards.DiscardUncommittedAsync().ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    private static async Task StashLineAsync(IHistoryRefHost host, CommitRowViewModel? row, Func<StashEntry, Task<bool>> operation)
    {
        if (row?.Stash is { } entry && await operation(entry).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }
}
