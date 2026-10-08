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
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Hosting;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Tags;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Creating and removing tags, as a user performs them.
/// </summary>
public interface ITagOperations
{
    /// <summary>
    /// Asks for a name, a target and an optional message, then creates the tag.
    /// </summary>
    /// <param name="target">The revision to offer first, or <see langword="null"/> for HEAD.</param>
    /// <param name="targetLabel">What to call that revision in the selector.</param>
    /// <returns><see langword="true"/> when a tag was created.</returns>
    Task<bool> CreateAsync(string? target = null, string? targetLabel = null);

    /// <summary>
    /// Confirms, then deletes the tag: a batch of one, asked in the words a single delete uses.
    /// </summary>
    /// <param name="name">The tag to delete.</param>
    /// <returns><see langword="true"/> when the tag was deleted.</returns>
    Task<bool> DeleteAsync(string name);

    /// <summary>
    /// Confirms once, naming every tag, then deletes them all here — going on past one that fails —
    /// reads the repository again once, and says once what went and what did not.
    /// </summary>
    /// <param name="names">The tags to delete.</param>
    /// <returns><see langword="true"/> when at least one tag was deleted.</returns>
    Task<bool> DeleteAsync(IReadOnlyList<string> names);

    /// <summary>
    /// Confirms, then deletes the tag from the remote a tag is pushed to — the one the current branch
    /// pushes to, or <c>origin</c>. The tag here is kept.
    /// </summary>
    /// <param name="name">The tag to delete from the remote.</param>
    /// <returns><see langword="true"/> when the remote no longer has the tag.</returns>
    Task<bool> DeleteRemoteAsync(string name);

    /// <summary>
    /// Confirms once, naming every tag and that the remote changes for everyone, then deletes them all
    /// from the remote a tag is pushed to, going on past one that fails. The tags here are kept.
    /// </summary>
    /// <param name="names">The tags to delete from the remote.</param>
    /// <returns><see langword="true"/> when the remote no longer has at least one of them.</returns>
    Task<bool> DeleteRemoteAsync(IReadOnlyList<string> names);
}

/// <summary>
/// Default <see cref="ITagOperations"/>.
/// </summary>
public sealed class TagOperations : ITagOperations
{
    private readonly IRepositoryContext _context;
    private readonly ITagService _tags;
    private readonly IPushGuard _pushGuard;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<TagOperations> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the application is looking at.</param>
    /// <param name="tags">Performs the tag operations.</param>
    /// <param name="pushGuard">Says whether the repository's profile may push to the remote.</param>
    /// <param name="dialogs">Raises the form and the confirmation.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public TagOperations(
        IRepositoryContext context,
        ITagService tags,
        IPushGuard pushGuard,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<TagOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(pushGuard);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _tags = tags;
        _pushGuard = pushGuard;
        _dialogs = dialogs;
        _infoBar = infoBar;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> CreateAsync(string? target = null, string? targetLabel = null)
    {
        if (_context.Repository is null)
        {
            return false;
        }

        CreateTagDialogViewModel model = new(BuildTargets(target, targetLabel), ExistingNames());
        CreateTagDialogView view = new() { DataContext = model };

        ContentDialog? shown = null;

        void OnValidationChanged(object? sender, EventArgs e)
        {
            if (shown is not null)
            {
                shown.IsPrimaryButtonEnabled = model.IsValid;
            }
        }

        model.ValidationChanged += OnValidationChanged;

        DialogResult result;

        try
        {
            result = await _dialogs.ShowAsync(dialog =>
            {
                shown = dialog;
                dialog.Title = "Create a tag";
                dialog.Content = view;
                dialog.PrimaryButtonText = "Create";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = DefaultButton.Primary;
                dialog.IsPrimaryButtonEnabled = model.IsValid;
            }).ConfigureAwait(true);
        }
        finally
        {
            model.ValidationChanged -= OnValidationChanged;
        }

        if (result != DialogResult.Primary || !model.IsValid)
        {
            return false;
        }

        string name = model.Name;
        string? revision = model.SelectedTarget?.Revision;
        string? message = model.Message.Trim().Length == 0 ? null : model.Message;

        return await RunAsync(
            (handle, token) => _tags.CreateAsync(handle, name, revision, message, false, token),
            $"Could not create \"{name}\"").ConfigureAwait(true);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return DeleteAsync([name]);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        if (names.Count == 0 || _context.Repository is null)
        {
            return false;
        }

        DeletionItem[] items = [.. names.Select(name => new DeletionItem(name))];

        DeletionPlan plan = items.Length == 1
            ? DeletionPlan.ForOne(
                items[0],
                "Delete tag",
                $"Delete the tag \"{items[0].Name}\"? The commit it points at is not affected.",
                "Delete")
            : DeletionPlan.ForMany(
                items,
                $"Delete {DeletionOutcome.Count(items.Length, "tag", "tags")}",
                $"Delete these {items.Length.ToString(CultureInfo.CurrentCulture)} tags? The commits they point at are not affected.",
                warning: null,
                "Delete");

        if (!await _dialogs.ConfirmDestructiveAsync(plan.Title, plan.Message, plan.ConfirmText).ConfigureAwait(true))
        {
            return false;
        }

        DeletionOutcome? outcome = await RunBatchAsync(
            plan,
            (handle, item, token) => _tags.DeleteAsync(handle, item.Name, token)).ConfigureAwait(true);

        if (outcome is null)
        {
            return false;
        }

        if (items.Length == 1)
        {
            ReportFailureOfOne(outcome, $"Could not delete \"{items[0].Name}\"");
        }
        else
        {
            Report(outcome.Summarise("Deleted", "tag", "tags"));
        }

        return outcome.Deleted.Count > 0;
    }

    /// <inheritdoc />
    public Task<bool> DeleteRemoteAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return DeleteRemoteAsync([name]);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A deletion on a remote is a push, so it goes where the tag's push goes and asks the push guard
    /// first — once for the whole batch: a profile that never pushes there never deletes there either.
    /// No overlay, as for a remote branch: each is one short round trip.
    /// </remarks>
    public async Task<bool> DeleteRemoteAsync(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        if (names.Count == 0 || _context.Repository is not { } repository)
        {
            return false;
        }

        string remote = SyncOperations.TagRemote(_context.Refs);

        if (!await MayPushAsync(repository, remote).ConfigureAwait(true))
        {
            return false;
        }

        DeletionItem[] items = [.. names.Select(name => new DeletionItem(name))];

        DeletionPlan plan = items.Length == 1
            ? DeletionPlan.ForOne(
                items[0],
                "Delete remote tag",
                $"Delete the tag \"{items[0].Name}\" from \"{remote}\"? This changes the remote for everyone who uses it. "
                + "The tag here is kept.",
                "Delete")
            : DeletionPlan.ForMany(
                items,
                $"Delete {DeletionOutcome.Count(items.Length, "remote tag", "remote tags")}",
                $"Delete these {items.Length.ToString(CultureInfo.CurrentCulture)} tags from \"{remote}\"? The tags here are kept.",
                $"This changes \"{remote}\" for everyone who uses it.",
                "Delete");

        if (!await _dialogs.ConfirmDestructiveAsync(plan.Title, plan.Message, plan.ConfirmText).ConfigureAwait(true))
        {
            return false;
        }

        DeletionOutcome? outcome = await RunBatchAsync(
            plan,
            (handle, item, token) => _tags.DeleteRemoteAsync(handle, remote, item.Name, token)).ConfigureAwait(true);

        if (outcome is null)
        {
            return false;
        }

        if (items.Length > 1)
        {
            Report(outcome.Summarise("Deleted", "tag", "tags", $" from \"{remote}\""));
        }
        else if (outcome.Deleted.Count == 1)
        {
            // Nothing on screen changes — a remote's tags are not drawn — so the sentence is the proof.
            Report("Deleted from the remote", $"\"{remote}\" no longer has the tag \"{items[0].Name}\".", InfoBarSeverity.Success);
        }
        else
        {
            ReportFailureOfOne(outcome, $"Could not delete \"{items[0].Name}\" from \"{remote}\"");
        }

        return outcome.Deleted.Count > 0;
    }

    /// <summary>
    /// Asks whether the repository's profile may push to a remote, and says why not when it may not.
    /// </summary>
    private async Task<bool> MayPushAsync(RepositoryHandle repository, string remote)
    {
        PushPermission permission;

        try
        {
            permission = await _pushGuard.CheckAsync(repository, remote, _context.RepositoryLifetime).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (permission.IsAllowed)
        {
            return true;
        }

        (string title, string message) = SyncOperations.DescribeRefusal(permission);
        Report(title, message, InfoBarSeverity.Warning);

        return false;
    }

    private IReadOnlyList<BranchStartPoint> BuildTargets(string? target, string? label)
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

        if (target is { Length: > 0 })
        {
            Add(target, label is { Length: > 0 } ? label : target, "the commit you picked");
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

        return points;
    }

    private IReadOnlyCollection<string> ExistingNames()
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (GitTag tag in _context.Refs.Tags)
        {
            names.Add(tag.ShortName);
        }

        return names;
    }

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
    /// Runs a batch under the repository's lock; <see langword="null"/> when the repository closed
    /// under it.
    /// </summary>
    private async Task<DeletionOutcome?> RunBatchAsync(
        DeletionPlan plan,
        Func<RepositoryHandle, DeletionItem, CancellationToken, Task> delete)
    {
        try
        {
            return await DeletionBatch.RunAsync(_context, plan.ToDelete, delete, _logger).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Says why the one item of a batch of one did not go, as a single delete always has.
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
}
