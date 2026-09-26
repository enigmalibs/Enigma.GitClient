using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.App.ViewModels.Dialogs;
using Enigma.GitClient.App.Views.Dialogs;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Tags;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

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
    /// Confirms, then deletes the tag.
    /// </summary>
    /// <param name="name">The tag to delete.</param>
    /// <returns><see langword="true"/> when the tag was deleted.</returns>
    Task<bool> DeleteAsync(string name);
}

/// <summary>
/// Default <see cref="ITagOperations"/>.
/// </summary>
public sealed class TagOperations : ITagOperations
{
    private readonly IRepositoryContext _context;
    private readonly ITagService _tags;
    private readonly IContentDialogService _dialogs;
    private readonly IInfoBarService _infoBar;
    private readonly ILogger<TagOperations> _logger;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the application is looking at.</param>
    /// <param name="tags">Performs the tag operations.</param>
    /// <param name="dialogs">Raises the form and the confirmation.</param>
    /// <param name="infoBar">Reports what happened.</param>
    /// <param name="logger">Receives failures that are reported to the user another way.</param>
    public TagOperations(
        IRepositoryContext context,
        ITagService tags,
        IContentDialogService dialogs,
        IInfoBarService infoBar,
        ILogger<TagOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(infoBar);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _tags = tags;
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
    public async Task<bool> DeleteAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        DialogResult answer = await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "Delete tag";
            dialog.Content = $"Delete the tag \"{name}\"? The commit it points at is not affected.";
            dialog.PrimaryButtonText = "Delete";
            dialog.CloseButtonText = "Cancel";

            // The harmless button is the default one, as everywhere a delete is offered.
            dialog.DefaultButton = DefaultButton.Close;
        }).ConfigureAwait(true);

        if (answer != DialogResult.Primary)
        {
            return false;
        }

        return await RunAsync(
            (handle, token) => _tags.DeleteAsync(handle, name, token),
            $"Could not delete \"{name}\"").ConfigureAwait(true);
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
            await ReportAsync(failureTitle, refusal.Message, InfoBarSeverity.Warning).ConfigureAwait(true);
        }
        catch (GitCommandException exception)
        {
            _logger.LogError(exception, "{Title}", failureTitle);

            await ReportAsync(failureTitle, FirstLine(exception.StandardError), InfoBarSeverity.Error)
                .ConfigureAwait(true);
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
