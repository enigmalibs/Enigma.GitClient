using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Core.Sync;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Sync;

/// <summary>
/// How a transfer's progress reaches the caller — not what it says, which
/// <see cref="SyncParsingTests"/> covers, but when and in what order.
/// </summary>
/// <remarks>
/// The service used to wrap the caller's <see cref="IProgress{T}"/> in a <see cref="Progress{T}"/> of
/// its own. A <c>Progress&lt;T&gt;</c> does not run its callback where <c>Report</c> was called: it
/// posts to the synchronisation context captured when it was constructed, and queues to the thread
/// pool when there is none — which is the case on the thread the process reader reports from. Reports
/// could therefore arrive after the operation had finished, and two of them could arrive the wrong way
/// round. These tests pin the two guarantees the fix restores, with a runner that reports on the
/// calling thread so neither is a race.
/// </remarks>
public sealed class SyncProgressDeliveryTests
{
    private static readonly string[] Chunks =
    [
        "Enumerating objects: 27, done.",
        "Counting objects:  50% (14/27)",
        "Counting objects: 100% (27/27), done.",
        "Receiving objects:  73% (1234/1690), 4.02 MiB | 2.01 MiB/s",
        "Receiving objects: 100% (1690/1690), done.",
    ];

    [Fact]
    public async Task Fetch_DeliversEveryReportOnTheThreadThatProducedIt()
    {
        Recorder recorder = new();
        SyncService service = new(new ReportingRunner(Chunks), new StubCommandFactory());

        int reporting = Environment.CurrentManagedThreadId;

        await service.FetchAsync(Handle(), "origin", progress: recorder, cancellationToken: TestContext.Current.CancellationToken);

        // Inline, not queued: the reports are all in by the time the fetch completes, and each one
        // arrived on the thread git was being read on. Marshalling is the caller's — it builds its own
        // Progress<T> where its updates have to land — and the service adding a hop of its own is what
        // made reports outlive the operation they describe.
        Assert.Equal(Chunks.Length, recorder.Reports.Count);
        Assert.All(recorder.Threads, thread => Assert.Equal(reporting, thread));
    }

    [Fact]
    public async Task Fetch_KeepsTheReportsInTheOrderGitWroteThem()
    {
        Recorder recorder = new();
        SyncService service = new(new ReportingRunner(Chunks), new StubCommandFactory());

        await service.FetchAsync(Handle(), "origin", progress: recorder, cancellationToken: TestContext.Current.CancellationToken);

        // A queue of independent work items has no order at all, and a progress line that arrives out
        // of order is an overlay going backwards.
        Assert.Equal(
            [SyncStage.Enumerating, SyncStage.Counting, SyncStage.Counting, SyncStage.Receiving, SyncStage.Receiving],
            recorder.Reports.ConvertAll(report => report.Stage));

        Assert.Equal([null, 50, 100, 73, 100], recorder.Reports.ConvertAll(report => report.Percent));
    }

    [Fact]
    public async Task Fetch_WithoutAProgressRunsAllTheSame()
    {
        ReportingRunner runner = new(Chunks);
        SyncService service = new(runner, new StubCommandFactory());

        await service.FetchAsync(Handle(), "origin", cancellationToken: TestContext.Current.CancellationToken);

        // Nothing to report to, so nothing is built to report with — and the command still ran.
        Assert.Null(runner.LastProgress);
        Assert.NotNull(runner.LastCommand);
    }

    private static RepositoryHandle Handle()
        => new(AppContext.BaseDirectory, System.IO.Path.Combine(AppContext.BaseDirectory, ".git"));

    /// <summary>
    /// Reports a fixed transcript on the calling thread, then completes successfully.
    /// </summary>
    private sealed class ReportingRunner : IGitProcessRunner
    {
        private readonly IReadOnlyList<string> _chunks;

        public ReportingRunner(IReadOnlyList<string> chunks) => _chunks = chunks;

        public GitCommand? LastCommand { get; private set; }

        public IProgress<string>? LastProgress { get; private set; }

        public Task<GitResult> RunStreamingAsync(
            GitCommand command,
            IProgress<string>? standardErrorChunks,
            bool throwOnError = true,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            LastProgress = standardErrorChunks;

            foreach (string chunk in _chunks)
            {
                standardErrorChunks?.Report(chunk);
            }

            return Task.FromResult(new GitResult(0, string.Empty, string.Empty, TimeSpan.Zero));
        }

        public Task<GitResult> RunAsync(GitCommand command, bool throwOnError = true, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<GitRawResult> RunRawAsync(GitCommand command, bool throwOnError = true, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> RunLinesAsync(GitCommand command, char separator = '\n', CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Builds a command without the global options, which these tests do not look at.
    /// </summary>
    private sealed class StubCommandFactory : IGitCommandFactory
    {
        public GitCommand Create(string workingDirectory, params string[] arguments)
            => new(workingDirectory, arguments);

        public GitCommand Create(string workingDirectory, IEnumerable<string> arguments)
            => new(workingDirectory, [.. arguments]);

        public GitCommand CreateWithInput(string workingDirectory, string standardInput, IEnumerable<string> arguments)
            => new(workingDirectory, [.. arguments], standardInput: standardInput);
    }

    /// <summary>
    /// Keeps every report, and the thread it arrived on.
    /// </summary>
    private sealed class Recorder : IProgress<SyncProgress>
    {
        public List<SyncProgress> Reports { get; } = [];

        public List<int> Threads { get; } = [];

        public void Report(SyncProgress value)
        {
            Reports.Add(value);
            Threads.Add(Environment.CurrentManagedThreadId);
        }
    }
}
