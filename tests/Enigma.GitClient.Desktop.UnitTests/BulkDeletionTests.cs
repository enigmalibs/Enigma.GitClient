using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A batch of deletions: the one question it asks, how it runs — every item tried, one hold of the
/// repository, one refresh — and the one sentence it ends with.
/// </summary>
public sealed class BulkDeletionTests
{
    private static RepositoryHandle Handle(string name)
        => OperatingSystem.IsWindows()
            ? new RepositoryHandle($@"C:\src\{name}", $@"C:\src\{name}\.git")
            : new RepositoryHandle($"/src/{name}", $"/src/{name}/.git");

    // ---------------------------------------------------------------- the question

    [Fact]
    public void TheQuestionForSeveral_NamesEveryItem_WhatItWouldLose_WhatIsSkipped_AndTheWarning()
    {
        DeletionPlan plan = DeletionPlan.ForMany(
            [
                new DeletionItem("feature/done"),
                new DeletionItem("feature/wip")
                {
                    Note = "holds 2 commits the current branch does not; deleting it loses them",
                    Details = ["Draw the badges", "Watch the files"],
                },
                new DeletionItem("main") { SkipReason = "it is checked out" },
                new DeletionItem("origin/old"),
            ],
            "Delete 4 branches",
            "Delete these 4 branches?",
            "Deleting a branch from a remote changes the remote for everyone who uses it.",
            "Delete");

        Assert.Equal("Delete 4 branches", plan.Title);
        Assert.Equal("Delete", plan.ConfirmText);
        Assert.Equal(
            "Delete these 4 branches?\n"
            + "\n• feature/done"
            + "\n• feature/wip — holds 2 commits the current branch does not; deleting it loses them"
            + "\n    Draw the badges"
            + "\n    Watch the files"
            + "\n• main — skipped: it is checked out"
            + "\n• origin/old"
            + "\n\nDeleting a branch from a remote changes the remote for everyone who uses it.",
            plan.Message);

        Assert.Equal(["feature/done", "feature/wip", "origin/old"], [.. plan.ToDelete.Select(item => item.Name)]);
        Assert.Equal(["main"], [.. plan.Skipped.Select(item => item.Name)]);
    }

    [Fact]
    public void TheQuestionForOne_IsTheSingleDeletesOwn()
    {
        DeletionPlan plan = DeletionPlan.ForOne(new DeletionItem("v1.0"), "Delete tag", "Delete the tag \"v1.0\"?", "Delete");

        Assert.Equal("Delete tag", plan.Title);
        Assert.Equal("Delete the tag \"v1.0\"?", plan.Message);
        Assert.Single(plan.ToDelete);
    }

    // ---------------------------------------------------------------- the sentence it ends with

    [Fact]
    public void EverythingDeleted_IsASuccess_NamingThem()
    {
        DeletionOutcome outcome = new();
        outcome.Succeeded("v1.0");
        outcome.Succeeded("v2.0");

        (string title, string message, InfoBarSeverity severity) = outcome.Summarise("Deleted", "tag", "tags");

        Assert.Equal("Deleted 2 tags", title);
        Assert.Equal("v1.0, v2.0", message);
        Assert.Equal(InfoBarSeverity.Success, severity);
    }

    [Fact]
    public void APartialFailure_SaysWhatWentAndWhatDidNot_WithTheReason()
    {
        DeletionOutcome outcome = new();
        outcome.Succeeded("v1.0");
        outcome.Failed("v2.0", "error: tag 'v2.0' not found.", isError: true);
        outcome.Failed("main", "it is checked out");

        (string title, string message, InfoBarSeverity severity) = outcome.Summarise("Deleted", "tag", "tags", " from \"origin\"");

        Assert.Equal("Deleted 1 of 3 tags from \"origin\"", title);
        Assert.Equal("Not deleted:\nv2.0 — error: tag 'v2.0' not found.\nmain — it is checked out", message);
        Assert.Equal(InfoBarSeverity.Warning, severity);
        Assert.True(outcome.HasErrors);
    }

    [Fact]
    public void NothingDeleted_IsAnError()
    {
        DeletionOutcome outcome = new();
        outcome.Failed("origin", "error: No such remote: 'origin'", isError: true);
        outcome.Failed("mirror", "error: No such remote: 'mirror'", isError: true);

        (string title, string message, InfoBarSeverity severity) = outcome.Summarise("Removed", "remote", "remotes");

        Assert.Equal("No remote was removed", title);
        Assert.Equal("origin — error: No such remote: 'origin'\nmirror — error: No such remote: 'mirror'", message);
        Assert.Equal(InfoBarSeverity.Error, severity);
    }

    // ---------------------------------------------------------------- running it

    [Fact]
    public async Task EveryItemIsTried_PastAFailure_UnderOneHold_WithOneRefresh()
    {
        FakeRefReader reader = new();
        using RepositoryContext context = new(reader, NullLogger<RepositoryContext>.Instance);
        await context.OpenAsync(Handle("batch"), TestContext.Current.CancellationToken);

        int refreshes = 0;
        context.StateRefreshed += (_, _) => refreshes++;

        List<(string Name, bool Writing)> tried = [];

        DeletionOutcome outcome = await DeletionBatch.RunAsync(
            context,
            [new DeletionItem("one"), new DeletionItem("two"), new DeletionItem("three")],
            (_, item, _) =>
            {
                tried.Add((item.Name, context.IsWriting));

                return item.Name == "two"
                    ? throw new GitOperationRefusedException("\"two\" holds commits that are not on the current branch.")
                    : Task.CompletedTask;
            },
            NullLogger.Instance);

        Assert.Equal([("one", true), ("two", true), ("three", true)], tried);
        Assert.Equal(["one", "three"], outcome.Deleted);
        Assert.Equal([("two", "\"two\" holds commits that are not on the current branch.")], outcome.NotDeleted);
        Assert.False(outcome.HasErrors);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task AGitFailure_IsGitsFirstMeaningfulLine()
    {
        FakeRefReader reader = new();
        using RepositoryContext context = new(reader, NullLogger<RepositoryContext>.Instance);
        await context.OpenAsync(Handle("batch"), TestContext.Current.CancellationToken);

        DeletionOutcome outcome = await DeletionBatch.RunAsync(
            context,
            [new DeletionItem("gone")],
            (_, _, _) => throw new GitCommandException(
                new GitCommand(Handle("batch").WorkTreePath, ["tag", "-d", "gone"]),
                1,
                "hint: nothing\nerror: tag 'gone' not found.\n",
                string.Empty),
            NullLogger.Instance);

        Assert.Equal([("gone", "error: tag 'gone' not found.")], outcome.NotDeleted);
        Assert.True(outcome.HasErrors);
    }
}
