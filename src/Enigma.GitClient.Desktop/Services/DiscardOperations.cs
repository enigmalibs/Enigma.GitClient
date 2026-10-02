using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Staging;
using Enigma.GitClient.Core.Status;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Throwing the uncommitted work away, from wherever it is offered: the history's uncommitted line.
/// </summary>
public interface IDiscardOperations
{
    /// <summary>
    /// Gets a value indicating whether there is anything the uncommitted work could be thrown back to:
    /// a first commit, and no merge or other operation in the middle of the working tree.
    /// </summary>
    bool CanDiscardUncommitted { get; }

    /// <summary>
    /// Throws every uncommitted change away — staged or not, untracked files included — after asking,
    /// in red.
    /// </summary>
    /// <returns><see langword="true"/> when the working tree went back to the last commit.</returns>
    Task<bool> DiscardUncommittedAsync();
}

/// <summary>
/// Default <see cref="IDiscardOperations"/>.
/// </summary>
public sealed class DiscardOperations : IDiscardOperations
{
    private readonly IRepositoryContext _context;
    private readonly IStatusService _status;
    private readonly IStagingService _staging;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<DiscardOperations> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The open repository.</param>
    /// <param name="status">Says how many files the question is about.</param>
    /// <param name="staging">Throws the work away.</param>
    /// <param name="dialogs">Asks first.</param>
    /// <param name="infoBar">Says how it went.</param>
    /// <param name="logger">Records what failed.</param>
    public DiscardOperations(
        IRepositoryContext context,
        IStatusService status,
        IStagingService staging,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<DiscardOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _status = status;
        _staging = staging;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Before the first commit there is nothing to go back to. In the middle of a merge, discarding
    /// would leave a merge that records nothing of the other side — abandoning the merge is the way
    /// out, and the banner offers it.
    /// </remarks>
    public bool CanDiscardUncommitted
        => _context.IsRepositoryOpen
           && _context.Head is not ({ IsUnborn: true } or { HasOperationInProgress: true });

    /// <inheritdoc />
    public async Task<bool> DiscardUncommittedAsync()
    {
        if (_context.Repository is not { } repository || !CanDiscardUncommitted)
        {
            return false;
        }

        int files;

        try
        {
            WorkingTreeStatus status = await _status
                .GetStatusAsync(repository, includeIgnored: false, _context.RepositoryLifetime)
                .ConfigureAwait(true);

            files = CountFiles(status);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Reading the working tree before discarding failed");
            _infoBar.Notify("Could not discard the changes", FirstLine(exception.StandardError), InfoBarSeverity.Error);
            return false;
        }

        if (files == 0)
        {
            _infoBar.Notify("Nothing to discard", "There are no uncommitted changes.", InfoBarSeverity.Info);
            return false;
        }

        bool confirmed = await _dialogs.ConfirmDestructiveAsync(
            "Discard uncommitted files",
            $"Throw away every uncommitted change — {Count(files)}, staged or not, untracked files included? This cannot be undone.",
            "Discard").ConfigureAwait(true);

        if (!confirmed)
        {
            return false;
        }

        try
        {
            await _context.RunExclusiveAsync(_staging.DiscardAllAsync).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Expected when the repository closes under the operation.
            return false;
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "Discarding the uncommitted changes failed");
            _infoBar.Notify("Could not discard the changes", FirstLine(exception.StandardError), InfoBarSeverity.Error);
            return false;
        }

        _infoBar.Notify("Changes discarded", "The working tree is back to the last commit.", InfoBarSeverity.Success);

        return true;
    }

    /// <summary>
    /// How many files the uncommitted work touches: every path the status names once, whichever halves
    /// of it changed.
    /// </summary>
    private static int CountFiles(WorkingTreeStatus status)
    {
        HashSet<string> paths = new(StringComparer.Ordinal);

        foreach (ChangedFile file in status.Staged)
        {
            paths.Add(file.Path);
        }

        foreach (ChangedFile file in status.NotStaged())
        {
            paths.Add(file.Path);
        }

        return paths.Count;
    }

    private static string Count(int files)
        => files == 1 ? "1 file" : $"{files.ToString(CultureInfo.CurrentCulture)} files";

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
