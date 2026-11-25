using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Desktop.Services;

namespace Enigma.GitClient.Desktop.ViewModels.Pages;

/// <summary>
/// The commands of the history's tag actions: create a tag on a line, and push, delete or delete
/// remotely a tag from its badge.
/// </summary>
/// <param name="CreateHere">Creates a tag on a line's commit.</param>
/// <param name="Push">Pushes a tag.</param>
/// <param name="Delete">Deletes a tag here.</param>
/// <param name="DeleteRemote">Deletes a tag from the remote.</param>
public sealed record HistoryTagCommands(
    AsyncRelayCommand<CommitRowViewModel> CreateHere,
    AsyncRelayCommand<string> Push,
    AsyncRelayCommand<string> Delete,
    AsyncRelayCommand<string> DeleteRemote);

/// <summary>
/// What the history does with tags: create one on a line, and push, delete or delete remotely one
/// from its badge.
/// </summary>
/// <remarks>
/// A tag created or deleted here changes what the graph draws, so the history reloads. A push or a
/// remote deletion changes only the badge's cloud, so the remote's tags are asked again instead.
/// </remarks>
public sealed class HistoryTagActions
{
    private readonly ITagOperations _tagOperations;
    private readonly ISyncOperations _syncOperations;
    private readonly IRemoteTags _remoteTags;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="tagOperations">Creates and deletes a tag, here and on the remote.</param>
    /// <param name="syncOperations">Pushes a tag.</param>
    /// <param name="remoteTags">Which tags the remote has, asked again after a push or a remote deletion.</param>
    public HistoryTagActions(ITagOperations tagOperations, ISyncOperations syncOperations, IRemoteTags remoteTags)
    {
        ArgumentNullException.ThrowIfNull(tagOperations);
        ArgumentNullException.ThrowIfNull(syncOperations);
        ArgumentNullException.ThrowIfNull(remoteTags);

        _tagOperations = tagOperations;
        _syncOperations = syncOperations;
        _remoteTags = remoteTags;
    }

    /// <summary>
    /// Builds the tag commands, acting for one page.
    /// </summary>
    /// <param name="host">The page they act for.</param>
    /// <returns>The commands.</returns>
    public HistoryTagCommands CreateCommands(IHistoryRefHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return new HistoryTagCommands(
            new AsyncRelayCommand<CommitRowViewModel>(row => CreateHereAsync(host, row), row => row?.Commit is not null),
            new AsyncRelayCommand<string>(PushAsync, tag => !string.IsNullOrEmpty(tag)),
            new AsyncRelayCommand<string>(tag => DeleteAsync(host, tag), tag => !string.IsNullOrEmpty(tag)),
            new AsyncRelayCommand<string>(DeleteRemoteAsync, tag => !string.IsNullOrEmpty(tag)));
    }

    private async Task CreateHereAsync(IHistoryRefHost host, CommitRowViewModel? row)
    {
        if (row?.Commit is null)
        {
            return;
        }

        if (await _tagOperations.CreateAsync(row.Sha, row.ShortSha).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Pushes a tag from its badge, then asks the remote again so the badge draws its cloud.
    /// </summary>
    /// <remarks>
    /// No reload afterwards: a pushed tag moves nothing the graph draws — there is no remote-tracking
    /// reference for a tag — and the operation re-reads the repository's state as every exclusive
    /// operation does.
    /// </remarks>
    private async Task PushAsync(string? tag)
    {
        if (tag is { Length: > 0 } && await _syncOperations.PushTagAsync(tag).ConfigureAwait(true))
        {
            await _remoteTags.RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Deletes a tag here, from its badge, and re-reads the history so the badge goes.
    /// </summary>
    private async Task DeleteAsync(IHistoryRefHost host, string? tag)
    {
        if (tag is { Length: > 0 } && await _tagOperations.DeleteAsync(tag).ConfigureAwait(true))
        {
            await host.ReloadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Deletes a tag from the remote, from its badge, then asks the remote again so the badge loses its
    /// cloud.
    /// </summary>
    /// <remarks>
    /// No reload, as for the push: the tag here stays.
    /// </remarks>
    private async Task DeleteRemoteAsync(string? tag)
    {
        if (tag is { Length: > 0 } && await _tagOperations.DeleteRemoteAsync(tag).ConfigureAwait(true))
        {
            await _remoteTags.RefreshAsync().ConfigureAwait(true);
        }
    }
}
