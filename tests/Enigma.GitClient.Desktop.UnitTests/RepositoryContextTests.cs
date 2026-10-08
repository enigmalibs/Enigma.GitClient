using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Exercises the repository context on its own. It touches no Avalonia type, so these tests run
/// off the headless collection and never block a dispatcher.
/// </summary>
public sealed class RepositoryContextTests
{
    private static RepositoryHandle Handle(string name)
        => OperatingSystem.IsWindows()
            ? new RepositoryHandle($@"C:\src\{name}", $@"C:\src\{name}\.git")
            : new RepositoryHandle($"/src/{name}", $"/src/{name}/.git");

    private static (RepositoryContext Context, FakeRefReader Reader) Build()
    {
        FakeRefReader reader = new();
        return (new RepositoryContext(reader, NullLogger<RepositoryContext>.Instance), reader);
    }

    [Fact]
    public void StartsWithNoRepositoryOpen()
    {
        (RepositoryContext context, _) = Build();
        using RepositoryContext scope = context;

        Assert.Null(context.Repository);
        Assert.False(context.IsRepositoryOpen);
        Assert.Null(context.Head);
        Assert.Empty(context.Refs.All());
        Assert.Equal(0, context.Decorations.DecoratedCommitCount);
    }

    [Fact]
    public async Task OpenAsync_PublishesTheRepositoryAndItsState()
    {
        (RepositoryContext context, FakeRefReader reader) = Build();
        using RepositoryContext scope = context;

        reader.Head = new HeadState(false, false, "main", "abc", RepositoryOperation.None);

        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);

        Assert.True(context.IsRepositoryOpen);
        Assert.Equal("one", context.Repository!.Name);
        Assert.Equal("main", context.Head!.BranchName);
        Assert.Equal(1, reader.ReadCount);
    }

    [Fact]
    public async Task OpenAsync_OnTheSameRepositoryJustRefreshes()
    {
        (RepositoryContext context, FakeRefReader reader) = Build();
        using RepositoryContext scope = context;

        List<RepositoryChangedEventArgs> events = [];
        context.RepositoryChanged += (_, e) => events.Add(e);

        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);
        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);

        Assert.Single(events);
        Assert.Equal(2, reader.ReadCount);
    }

    [Fact]
    public async Task RepositoryChanged_CarriesBothSides()
    {
        (RepositoryContext context, _) = Build();
        using RepositoryContext scope = context;

        List<RepositoryChangedEventArgs> events = [];
        context.RepositoryChanged += (_, e) => events.Add(e);

        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);
        await context.OpenAsync(Handle("two"), TestContext.Current.CancellationToken);
        context.Close();

        Assert.Equal(3, events.Count);
        Assert.Null(events[0].Previous);
        Assert.Equal("one", events[0].Current!.Name);
        Assert.Equal("one", events[1].Previous!.Name);
        Assert.Equal("two", events[1].Current!.Name);
        Assert.Equal("two", events[2].Previous!.Name);
        Assert.Null(events[2].Current);
    }

    [Fact]
    public async Task RepositoryLifetime_IsCancelledWheneverTheRepositoryChanges()
    {
        (RepositoryContext context, _) = Build();
        using RepositoryContext scope = context;

        CancellationToken first = context.RepositoryLifetime;
        Assert.False(first.IsCancellationRequested);

        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);

        Assert.True(first.IsCancellationRequested);

        CancellationToken second = context.RepositoryLifetime;
        Assert.False(second.IsCancellationRequested);

        context.Close();

        Assert.True(second.IsCancellationRequested);
    }

    [Fact]
    public void Close_OnAnAlreadyClosedContextDoesNothing()
    {
        (RepositoryContext context, _) = Build();
        using RepositoryContext scope = context;

        bool raised = false;
        context.RepositoryChanged += (_, _) => raised = true;

        context.Close();

        Assert.False(raised);
    }

    [Fact]
    public async Task Close_ClearsEverythingTheShellBindsTo()
    {
        (RepositoryContext context, FakeRefReader reader) = Build();
        using RepositoryContext scope = context;

        reader.Head = new HeadState(false, false, "main", "abc", RepositoryOperation.None);
        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);

        context.Close();

        Assert.Null(context.Repository);
        Assert.Null(context.Head);
        Assert.Null(context.SelectedCommit);
        Assert.Empty(context.Refs.All());
    }

    [Fact]
    public async Task RefreshAsync_DoesNothingWithNoRepositoryOpen()
    {
        (RepositoryContext context, FakeRefReader reader) = Build();
        using RepositoryContext scope = context;

        await context.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task RefreshAsync_RaisesStateRefreshed()
    {
        (RepositoryContext context, _) = Build();
        using RepositoryContext scope = context;

        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);

        int refreshes = 0;
        context.StateRefreshed += (_, _) => refreshes++;

        await context.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task RefreshAsync_SwallowsAFailingReadRatherThanTakingTheShellDown()
    {
        ThrowingRefReader reader = new();
        using RepositoryContext context = new(reader, NullLogger<RepositoryContext>.Instance);

        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);

        Assert.True(context.IsRepositoryOpen);
        Assert.Null(context.Head);
    }

    [Fact]
    public async Task RunExclusiveAsync_ThrowsWithNoRepositoryOpen()
    {
        (RepositoryContext context, _) = Build();
        using RepositoryContext scope = context;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.RunExclusiveAsync(
                (_, _) => Task.CompletedTask,
                true,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunExclusiveAsync_SerialisesEveryWrite()
    {
        (RepositoryContext context, _) = Build();
        using RepositoryContext scope = context;

        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);

        int concurrent = 0;
        int peak = 0;

        Task[] operations =
        [
            .. Enumerable.Range(0, 8).Select(_ => context.RunExclusiveAsync(
                async (_, token) =>
                {
                    int current = Interlocked.Increment(ref concurrent);
                    peak = Math.Max(peak, current);
                    await Task.Delay(5, token);
                    Interlocked.Decrement(ref concurrent);
                },
                refreshAfter: false,
                TestContext.Current.CancellationToken)),
        ];

        await Task.WhenAll(operations);

        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task RunExclusiveAsync_RefreshesAfterwardsWhenAsked()
    {
        (RepositoryContext context, FakeRefReader reader) = Build();
        using RepositoryContext scope = context;

        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);
        int before = reader.ReadCount;

        await context.RunExclusiveAsync(
            (_, _) => Task.CompletedTask,
            refreshAfter: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(before + 1, reader.ReadCount);

        await context.RunExclusiveAsync(
            (_, _) => Task.CompletedTask,
            refreshAfter: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(before + 1, reader.ReadCount);
    }

    [Fact]
    public async Task RunExclusiveAsync_HandsTheOperationTheOpenRepositoryAndAReturnValue()
    {
        (RepositoryContext context, _) = Build();
        using RepositoryContext scope = context;

        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);

        string name = await context.RunExclusiveAsync(
            (repository, _) => Task.FromResult(repository.Name),
            refreshAfter: false,
            TestContext.Current.CancellationToken);

        Assert.Equal("one", name);
    }

    [Fact]
    public async Task RunExclusiveAsync_ReleasesTheLockWhenTheOperationThrows()
    {
        (RepositoryContext context, _) = Build();
        using RepositoryContext scope = context;

        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidTimeZoneException>(
            () => context.RunExclusiveAsync(
                (_, _) => throw new InvalidTimeZoneException("boom"),
                refreshAfter: false,
                TestContext.Current.CancellationToken));

        // A leaked lock would hang this second call forever.
        await context.RunExclusiveAsync(
            (_, _) => Task.CompletedTask,
            refreshAfter: false,
            TestContext.Current.CancellationToken);
    }

    // ---------------------------------------------------------------- telling the watcher about writes

    [Fact]
    public async Task IsWriting_HoldsForTheWriteAndItsRefresh_AndWriteEndedFollows()
    {
        (RepositoryContext context, FakeRefReader reader) = Build();
        using RepositoryContext scope = context;

        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);

        List<bool> writingDuringRefresh = [];
        context.StateRefreshed += (_, _) => writingDuringRefresh.Add(context.IsWriting);

        int ended = 0;
        context.WriteEnded += (_, _) => ended++;

        bool writingDuringOperation = false;

        await context.RunExclusiveAsync(
            (_, _) =>
            {
                writingDuringOperation = context.IsWriting;
                return Task.CompletedTask;
            },
            refreshAfter: true,
            TestContext.Current.CancellationToken);

        Assert.True(writingDuringOperation);
        Assert.Equal([true], writingDuringRefresh);
        Assert.False(context.IsWriting);
        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task WriteEnded_IsRaisedForAFailedWriteToo()
    {
        (RepositoryContext context, _) = Build();
        using RepositoryContext scope = context;

        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);

        int ended = 0;
        context.WriteEnded += (_, _) => ended++;

        await Assert.ThrowsAsync<InvalidTimeZoneException>(
            () => context.RunExclusiveAsync(
                (_, _) => throw new InvalidTimeZoneException("boom"),
                refreshAfter: false,
                TestContext.Current.CancellationToken));

        Assert.False(context.IsWriting);
        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task TryRunExclusiveAsync_CountsAsAWrite_AndOneThatNeverRanRaisesNothing()
    {
        (RepositoryContext context, _) = Build();
        using RepositoryContext scope = context;

        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);

        int ended = 0;
        context.WriteEnded += (_, _) => ended++;

        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> holding = context.TryRunExclusiveAsync(
            (_, _) => release.Task,
            refreshAfter: false,
            TestContext.Current.CancellationToken);

        Assert.True(context.IsWriting);

        // Busy: this one never runs, so there is no write of its to end.
        Assert.False(await context.TryRunExclusiveAsync(
            (_, _) => Task.CompletedTask,
            refreshAfter: false,
            TestContext.Current.CancellationToken));
        Assert.Equal(0, ended);

        release.SetResult();
        Assert.True(await holding);

        Assert.False(context.IsWriting);
        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task PropertyChanged_FiresForEverythingTheShellBindsTo()
    {
        (RepositoryContext context, FakeRefReader reader) = Build();
        using RepositoryContext scope = context;

        reader.Head = new HeadState(false, false, "main", "abc", RepositoryOperation.None);

        // A non-empty collection on purpose: RefCollection is a record, so publishing an empty one
        // over an empty one is genuinely not a change and correctly raises nothing.
        reader.Refs = new RefCollection(
            [
                new GitBranch(
                    "refs/heads/main",
                    "abc",
                    isCurrent: true,
                    upstreamShortName: null,
                    BranchTracking.None,
                    Core.History.GitSignature.Empty,
                    DateTimeOffset.UnixEpoch,
                    "Subject"),
            ],
            [],
            [],
            []);

        List<string> changed = [];
        context.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        await context.OpenAsync(Handle("one"), TestContext.Current.CancellationToken);

        Assert.Contains(nameof(IRepositoryContext.Repository), changed);
        Assert.Contains(nameof(IRepositoryContext.IsRepositoryOpen), changed);
        Assert.Contains(nameof(IRepositoryContext.Head), changed);
        Assert.Contains(nameof(IRepositoryContext.Refs), changed);
        Assert.Contains(nameof(IRepositoryContext.Decorations), changed);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        (RepositoryContext context, _) = Build();

        context.Dispose();
        context.Dispose();
    }

    private sealed class ThrowingRefReader : IRefReader
    {
        public Task<RefCollection> GetRefsAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the repository cannot be read");

        public Task<HeadState> GetHeadStateAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the repository cannot be read");

        public Task<RepositoryRefState> GetStateAsync(RepositoryHandle repository, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the repository cannot be read");
    }
}
