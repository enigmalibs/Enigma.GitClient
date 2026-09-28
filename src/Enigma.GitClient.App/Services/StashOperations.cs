using System;
using System.Threading;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.Views.Dialogs;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Stashes;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// The stash, as every page offers it: put the uncommitted work aside, and get it back — the way
/// GitKraken does.
/// </summary>
/// <remarks>
/// <para>
/// One place for the four operations, so the history and the Changes page behave alike: the same
/// questions, the same messages, and the same answer to the one case that is neither a success nor a
/// failure — a stash that comes back with conflicts.
/// </para>
/// <para>
/// Applying keeps the entry, always. Popping removes it once its changes are uncommitted changes
/// again; when some of them conflict, git keeps it, and so the reader is told the work is still
/// there. A refusal — uncommitted work the entry would overwrite — changes nothing at all.
/// </para>
/// </remarks>
public interface IStashOperations
{
    /// <summary>
    /// Puts every uncommitted change on the stash, untracked files included, after asking what to call
    /// it.
    /// </summary>
    /// <returns><see langword="true"/> when something was stashed.</returns>
    Task<bool> StashAsync();

    /// <summary>
    /// Brings an entry's changes back as uncommitted changes and keeps the entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns><see langword="true"/> when the work tree changed, conflicts included.</returns>
    Task<bool> ApplyAsync(StashEntry entry);

    /// <summary>
    /// Brings an entry's changes back as uncommitted changes and removes the entry — unless they
    /// conflict, when it is kept.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns><see langword="true"/> when the work tree changed, conflicts included.</returns>
    Task<bool> PopAsync(StashEntry entry);

    /// <summary>
    /// Deletes an entry, after asking: the work it holds cannot be recovered afterwards.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns><see langword="true"/> when it was deleted.</returns>
    Task<bool> DropAsync(StashEntry entry);
}

/// <summary>
/// Default <see cref="IStashOperations"/>.
/// </summary>
public sealed class StashOperations : IStashOperations
{
    private readonly IRepositoryContext _context;
    private readonly IStashService _stashes;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<StashOperations> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The open repository.</param>
    /// <param name="stashes">The stash engine.</param>
    /// <param name="dialogs">Asks the questions.</param>
    /// <param name="infoBar">Says how it went.</param>
    /// <param name="logger">Records what failed.</param>
    public StashOperations(
        IRepositoryContext context,
        IStashService stashes,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<StashOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(stashes);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _stashes = stashes;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> StashAsync()
    {
        if (!_context.IsRepositoryOpen)
        {
            return false;
        }

        string where = _context.Head is { IsDetached: false, BranchName: { Length: > 0 } branch }
            ? $" on \"{branch}\""
            : string.Empty;

        StashDialogViewModel model = new(
            $"Every uncommitted change{where}, untracked files included, goes on the stash, and the working tree is left clean.");

        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "Stash the uncommitted changes";
            dialog.Content = new StashDialogView { DataContext = model };
            dialog.PrimaryButtonText = "Stash";
            dialog.CloseButtonText = "Cancel";
            dialog.DefaultButton = DefaultButton.Primary;
        }).ConfigureAwait(true);

        if (answer != DialogResult.Primary)
        {
            return false;
        }

        string? message = model.EffectiveMessage;
        bool stashed = false;

        bool ran = await RunAsync(
            async (handle, token) => stashed = await _stashes
                .PushAsync(handle, message, includeUntracked: true, keepIndex: false, paths: null, token)
                .ConfigureAwait(true),
            "Could not stash the changes").ConfigureAwait(true);

        if (!ran)
        {
            return false;
        }

        if (!stashed)
        {
            _infoBar.Notify("Nothing to stash", "There are no uncommitted changes to put aside.", InfoBarSeverity.Warning);
            return false;
        }

        _infoBar.Notify(
            "Changes stashed",
            "Your uncommitted work is on the stash as \"stash@{0}\", and the working tree is clean.",
            InfoBarSeverity.Success);

        return true;
    }

    /// <inheritdoc />
    public Task<bool> ApplyAsync(StashEntry entry)
        => BringBackAsync(entry, pop: false);

    /// <inheritdoc />
    public Task<bool> PopAsync(StashEntry entry)
        => BringBackAsync(entry, pop: true);

    /// <inheritdoc />
    public async Task<bool> DropAsync(StashEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!_context.IsRepositoryOpen)
        {
            return false;
        }

        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "Delete stash";
            dialog.Content = $"Delete \"{Describe(entry)}\" ({entry.Reference})? The changes it holds cannot be recovered.";
            dialog.PrimaryButtonText = "Delete";
            dialog.CloseButtonText = "Cancel";

            // The harmless button is the default one, as everywhere a delete is offered.
            dialog.DefaultButton = DefaultButton.Close;
        }).ConfigureAwait(true);

        if (answer != DialogResult.Primary)
        {
            return false;
        }

        int index = entry.Index;

        return await RunAsync(
            (handle, token) => _stashes.DropAsync(handle, index, token),
            "Could not delete the stash").ConfigureAwait(true);
    }

    private async Task<bool> BringBackAsync(StashEntry entry, bool pop)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!_context.IsRepositoryOpen)
        {
            return false;
        }

        int index = entry.Index;
        StashApplyResult result = StashApplyResult.Applied;

        bool ran = await RunAsync(
            async (handle, token) => result = pop
                ? await _stashes.PopAsync(handle, index, token).ConfigureAwait(true)
                : await _stashes.ApplyAsync(handle, index, token).ConfigureAwait(true),
            pop ? "Could not pop the stash" : "Could not apply the stash").ConfigureAwait(true);

        if (!ran)
        {
            return false;
        }

        string name = Describe(entry);

        if (result == StashApplyResult.Conflicted)
        {
            _infoBar.Notify(
                "The stash applied with conflicts",
                $"Some changes of \"{name}\" conflict with the files as they are. Resolve them on the Changes page; the stash is kept, so none of its work is lost.",
                InfoBarSeverity.Warning);

            return true;
        }

        _infoBar.Notify(
            pop ? "Stash popped" : "Stash applied",
            pop
                ? $"The changes of \"{name}\" are uncommitted changes again, and the stash is gone."
                : $"The changes of \"{name}\" are uncommitted changes again; the stash is kept.",
            InfoBarSeverity.Success);

        return true;
    }

    /// <summary>
    /// What an entry is called in a message: its own message, or its name when it has none.
    /// </summary>
    private static string Describe(StashEntry entry)
        => entry.Message.Length > 0 ? entry.Message : entry.Reference;

    private async Task<bool> RunAsync(
        Func<RepositoryHandle, CancellationToken, Task> operation,
        string failureTitle)
    {
        try
        {
            await _context.RunExclusiveAsync(operation).ConfigureAwait(true);
            return true;
        }
        catch (GitOperationRefusedException refusal)
        {
            _infoBar.Notify(failureTitle, refusal.Message, InfoBarSeverity.Warning);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "{Title}", failureTitle);

            _infoBar.Notify(failureTitle, FirstLine(exception.StandardError), InfoBarSeverity.Error);
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
}
