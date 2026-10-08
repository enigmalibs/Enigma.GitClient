using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// One thing a batch of deletions names: a branch, a tag, a remote.
/// </summary>
/// <param name="Name">What the confirmation and the summary call it.</param>
public sealed record DeletionItem(string Name)
{
    /// <summary>
    /// Gets what the confirmation says after the name — that a branch holds commits nothing else
    /// has — or nothing.
    /// </summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>Gets the lines the confirmation shows under the item: the commits a branch would lose.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];

    /// <summary>
    /// Gets why the item is left out of the batch — the branch that is checked out — or
    /// <see langword="null"/> when it is deleted with the rest.
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>Gets a value indicating whether the item is left out and never attempted.</summary>
    public bool IsSkipped => SkipReason is not null;
}

/// <summary>
/// The one question a batch of deletions asks before it runs: what goes, what would be lost with it,
/// what is left out and why, and what it does to a remote.
/// </summary>
public sealed class DeletionPlan
{
    private DeletionPlan(IReadOnlyList<DeletionItem> items, string title, string message, string confirmText)
    {
        Items = items;
        Title = title;
        Message = message;
        ConfirmText = confirmText;
    }

    /// <summary>Gets every item the batch names, the skipped ones included, in the order they were given.</summary>
    public IReadOnlyList<DeletionItem> Items { get; }

    /// <summary>Gets the items that are deleted.</summary>
    public IReadOnlyList<DeletionItem> ToDelete => [.. Items.Where(item => !item.IsSkipped)];

    /// <summary>Gets the items that are left out.</summary>
    public IReadOnlyList<DeletionItem> Skipped => [.. Items.Where(item => item.IsSkipped)];

    /// <summary>Gets the question's title.</summary>
    public string Title { get; }

    /// <summary>Gets the question itself.</summary>
    public string Message { get; }

    /// <summary>Gets what the button that goes ahead says.</summary>
    public string ConfirmText { get; }

    /// <summary>
    /// Asks about one item, in the words its single delete has always used.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="title">The question's title.</param>
    /// <param name="message">The question.</param>
    /// <param name="confirmText">What the button that goes ahead says.</param>
    /// <returns>The plan.</returns>
    public static DeletionPlan ForOne(DeletionItem item, string title, string message, string confirmText)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new DeletionPlan([item], title, message, confirmText);
    }

    /// <summary>
    /// Asks about several items at once: a sentence, then every item on a line of its own — what it
    /// would lose, or why it is left out — then what the batch does to a remote.
    /// </summary>
    /// <param name="items">Every item, the skipped ones included.</param>
    /// <param name="title">The question's title.</param>
    /// <param name="question">The sentence above the list.</param>
    /// <param name="warning">The sentence below it, or <see langword="null"/> for none.</param>
    /// <param name="confirmText">What the button that goes ahead says.</param>
    /// <returns>The plan.</returns>
    public static DeletionPlan ForMany(
        IReadOnlyList<DeletionItem> items,
        string title,
        string question,
        string? warning,
        string confirmText)
    {
        ArgumentNullException.ThrowIfNull(items);

        StringBuilder message = new(question);
        message.Append('\n');

        foreach (DeletionItem item in items)
        {
            message.Append('\n').Append("• ").Append(item.Name);

            if (item.SkipReason is { } reason)
            {
                message.Append(" — skipped: ").Append(reason);
                continue;
            }

            if (item.Note.Length > 0)
            {
                message.Append(" — ").Append(item.Note);
            }

            foreach (string detail in item.Details)
            {
                message.Append("\n    ").Append(detail);
            }
        }

        if (warning is { Length: > 0 })
        {
            message.Append("\n\n").Append(warning);
        }

        return new DeletionPlan(items, title, message.ToString(), confirmText);
    }
}

/// <summary>
/// What a batch of deletions came to: what went, and what did not, with the reason.
/// </summary>
public sealed class DeletionOutcome
{
    private readonly List<string> _deleted = [];
    private readonly List<(string Name, string Reason)> _notDeleted = [];

    /// <summary>Gets the names of the items deleted, in the order they were deleted.</summary>
    public IReadOnlyList<string> Deleted => _deleted;

    /// <summary>Gets the items not deleted — failed or left out — with why.</summary>
    public IReadOnlyList<(string Name, string Reason)> NotDeleted => _notDeleted;

    /// <summary>Gets a value indicating whether at least one item failed for a reason git gave, as opposed to a refusal.</summary>
    public bool HasErrors { get; private set; }

    /// <summary>Records an item that went.</summary>
    /// <param name="name">The item.</param>
    public void Succeeded(string name) => _deleted.Add(name);

    /// <summary>Records an item that did not go.</summary>
    /// <param name="name">The item.</param>
    /// <param name="reason">Why.</param>
    /// <param name="isError">Whether git failed, rather than the client refusing or leaving it out.</param>
    public void Failed(string name, string reason, bool isError = false)
    {
        _notDeleted.Add((name, reason));
        HasErrors |= isError;
    }

    /// <summary>
    /// Says, in one notification, what a batch of several did: everything went, some did, or none.
    /// </summary>
    /// <param name="done">The verb in the past, capitalised: "Deleted", "Removed".</param>
    /// <param name="one">What one item is called: "tag".</param>
    /// <param name="many">What several are called: "tags".</param>
    /// <param name="place">Where they went from, " from \"origin\"", or nothing.</param>
    /// <returns>The notification's title, message and severity.</returns>
    public (string Title, string Message, InfoBarSeverity Severity) Summarise(string done, string one, string many, string place = "")
    {
        int total = _deleted.Count + _notDeleted.Count;

        string notDeleted = string.Join('\n', _notDeleted.Select(item => $"{item.Name} — {item.Reason}"));

        if (_notDeleted.Count == 0)
        {
            return ($"{done} {Count(total, one, many)}{place}", string.Join(", ", _deleted), InfoBarSeverity.Success);
        }

        if (_deleted.Count == 0)
        {
            return ($"No {one} was {done.ToLowerInvariant()}{place}", notDeleted, InfoBarSeverity.Error);
        }

        return (
            $"{done} {_deleted.Count.ToString(CultureInfo.CurrentCulture)} of {Count(total, one, many)}{place}",
            $"Not {done.ToLowerInvariant()}:\n{notDeleted}",
            InfoBarSeverity.Warning);
    }

    /// <summary>Says how many, without "1 tags".</summary>
    /// <param name="count">How many.</param>
    /// <param name="one">What one is called.</param>
    /// <param name="many">What several are called.</param>
    /// <returns>The words.</returns>
    public static string Count(int count, string one, string many)
        => count == 1 ? $"1 {one}" : $"{count.ToString(CultureInfo.CurrentCulture)} {many}";
}

/// <summary>
/// Runs a batch of deletions: every item tried, one after another, under one hold of the repository.
/// </summary>
public static class DeletionBatch
{
    /// <summary>
    /// Deletes every item, going on past the ones that fail, and reads the repository again once, at
    /// the end.
    /// </summary>
    /// <param name="context">The repository, whose lock the whole batch holds.</param>
    /// <param name="items">The items to delete; skipped ones are the caller's to leave out.</param>
    /// <param name="delete">Deletes one item.</param>
    /// <param name="logger">Receives what git said when it failed.</param>
    /// <returns>What happened to each item.</returns>
    /// <exception cref="OperationCanceledException">The repository closed under the batch.</exception>
    /// <remarks>
    /// One hold rather than one per item: the automatic refresh cannot slip in between two items and
    /// show a half-deleted list, and the reference state is read once rather than once per item.
    /// </remarks>
    public static async Task<DeletionOutcome> RunAsync(
        IRepositoryContext context,
        IReadOnlyList<DeletionItem> items,
        Func<RepositoryHandle, DeletionItem, CancellationToken, Task> delete,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(delete);
        ArgumentNullException.ThrowIfNull(logger);

        DeletionOutcome outcome = new();

        if (items.Count == 0)
        {
            return outcome;
        }

        await context.RunExclusiveAsync(
            async (handle, token) =>
            {
                foreach (DeletionItem item in items)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        await delete(handle, item, token).ConfigureAwait(true);
                        outcome.Succeeded(item.Name);
                    }
                    catch (GitOperationRefusedException refusal)
                    {
                        outcome.Failed(item.Name, refusal.Message);
                    }
                    catch (SyncException exception)
                    {
                        outcome.Failed(item.Name, exception.Failure.Message, isError: true);
                    }
                    catch (GitCommandException exception)
                    {
                        logger.LogError(exception, "Deleting {Item} failed", item.Name);
                        outcome.Failed(item.Name, SyncErrorMapper.FirstMeaningfulLine(exception.StandardError), isError: true);
                    }
                }
            },
            refreshAfter: true).ConfigureAwait(true);

        return outcome;
    }
}
