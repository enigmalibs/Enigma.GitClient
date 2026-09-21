# BUG-39D9 — Progress reports arrive late, and unordered

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-39d9-sync-progress`
**Run:** feature/2026-09-21-icons-tracking-dragging

## Objective

A transfer's progress reports reach the caller while the transfer is running, in the order git wrote
them.

## Context & constraints

Found by a test that failed twice during this run — `SyncServiceTests.FetchAsync_ReportsItsProgress`,
on devs that touch nothing it can reach — and passed on both re-runs. The flake is the symptom; the
cause is in the service.

- `SyncService.RunAsync` wraps the caller's `IProgress<SyncProgress>` in a **second** progress object:
  `chunks = new Progress<string>(chunk => …)`, handed to `IGitProcessRunner.RunStreamingAsync`.
- `Progress<T>` does not invoke its callback where `Report` was called. It posts to the
  `SynchronizationContext` captured **when the `Progress<T>` was constructed**, and when there is none
  it queues each callback to the **thread pool**. Two consequences:
  - *Late.* `GitProcessRunner.ReadChunksAsync` reports from a thread-pool thread after its first
    `ConfigureAwait(false)`, so with no captured context the parse and the caller's `Report` are queued
    and can run **after `FetchAsync` has already completed**. That is precisely what the test races: it
    asserts on a list that the queued callbacks have not filled yet.
  - *Unordered.* Independent thread-pool work items have no ordering, so two chunks can reach the
    caller the wrong way round — a progress overlay showing 40% after 60%.
- The wrapper buys nothing. The caller already owns the marshalling: `SyncOperations` builds its
  `Progress<SyncProgress>` on the UI thread, so the report reaches the overlay on the UI thread whether
  or not the service adds a hop of its own. What the service has to do is parse a chunk and pass it on.
- Parsing a progress line is `SyncProgressParser.Parse` — a pure function on a short string, called
  once per chunk. Doing it on the reader's thread costs nothing and is where it belongs.
- **Baseline:** clean build, 1816 tests green (with this test's flake, twice in ~10 runs of the suite).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the defect is | In `SyncService`, not in the test | The test asserts something true — that the plumbing reports while the transfer runs. It is the service that cannot promise it | Loosening the test to "eventually" with a wait (hiding a real ordering defect behind a sleep) |
| The fix | A synchronous `IProgress<string>` adapter that parses and forwards inline | It is what the service actually needs: translate a chunk and pass it on. Order is preserved because there is no queue, and a report cannot outlive the operation | Keeping `Progress<T>` and flushing at the end (the ordering defect stays); reporting raw chunks and parsing in the caller (every caller would repeat the parse) |
| Whether callers change | No | `SyncOperations` already creates its `Progress<SyncProgress>` on the UI thread, which is what marshals to it. Removing the service's hop leaves that exactly as it is | Making every caller responsible for thread-hopping |
| Whether the test changes | One line: what it asserts about ordering | The existing assertion was right; a test that also pins the order is what stops this being reintroduced | Leaving the test as it was |

## Steps

1. `Core/Sync/SyncService.cs`: replace the inner `Progress<string>` with a private sealed
   `ParsedChunks : IProgress<string>` that parses each chunk and reports it to the caller inline, with
   a comment recording why a second `Progress<T>` was wrong here.
2. Tests — `tests/Enigma.GitClient.Core.IntegrationTests/Sync/SyncServiceTests.cs`: the fetch test
   keeps its assertion and gains the two the defect broke — every report has arrived by the time the
   fetch completes, and the reports are in the order git wrote them. The list is guarded, since the
   reports now arrive on the reader's thread.

## Acceptance criteria

- Progress reports are delivered before the operation's task completes.
- Reports reach the caller in the order git produced them.
- A caller that marshals with its own `Progress<T>` still gets its callbacks where it always did.
- Build clean with zero warnings; the whole suite green, including the test that was flaky.

## Out of scope

- The progress parser itself, and what the overlay does with a report.
- Cancellation, error mapping, and the rest of the sync service.
- `GitProcessRunner`'s own chunking.
