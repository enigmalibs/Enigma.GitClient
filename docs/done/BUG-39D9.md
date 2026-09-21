# BUG-39D9 — Progress reports arrive late, and unordered

**Item:** BUG-39D9 — Progress reports arrive late, unordered
**Branch:** `bugfix/bug-39d9-sync-progress`
**Run:** feature/2026-09-21-icons-tracking-dragging

## Summary

A transfer's progress reports now reach the caller while the transfer is running, on the thread that
produced them, in the order git wrote them.

`SyncService.RunAsync` was wrapping the caller's `IProgress<SyncProgress>` in a **second** progress
object — `new Progress<string>(…)` — and handing that to the process runner. A `Progress<T>` does not
run its callback where `Report` was called: it posts to the synchronisation context captured when it
was *constructed*, and queues to the thread pool when there is none. The reader reports from a
thread-pool thread, so every chunk went through a queue, with two consequences: a report could arrive
**after** the operation it describes had completed, and two reports could arrive the **wrong way
round** — a progress overlay going backwards.

The wrapper bought nothing. Marshalling belongs to the caller, which builds its own `Progress<T>` where
its updates have to land — `SyncOperations` does exactly that, on the UI thread. What the service owes
the caller is a parsed chunk, passed on. `ParsedChunks` is that, and nothing else.

Found from the other end: `SyncServiceTests.FetchAsync_ReportsItsProgress` failed twice during this
run, on devs that touch nothing it can reach, and passed on both re-runs. The test was right and the
service could not keep the promise it was testing.

## Files / modules touched

**Modified — Core**

- `Sync/SyncService.cs` — `ParsedChunks : IProgress<string>` replaces the inner `Progress<string>`,
  with the reasoning recorded on it

**Modified / added — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Sync/SyncProgressDeliveryTests.cs` (new) — the two guarantees,
  against a runner that reports a fixed transcript on the calling thread: every report is in by the
  time the fetch completes and each arrived on the reporting thread, and the stages and percentages are
  in the order they were written. Plus a fetch with no progress at all, which builds nothing and still
  runs
- `tests/Enigma.GitClient.Core.IntegrationTests/Sync/SyncServiceTests.cs` — the flaky test now records
  through a plain `IProgress` (a `Progress<T>` built in a test captures no context and would move the
  defect into the test), reads the transcript the instant the fetch completes, and checks that within a
  stage the percentages only grow

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the regression test lives | A new unit test beside the parser's, not the integration test | The integration test cannot fail reliably against the old code: the queued callbacks usually did win the race, which is why it was flaky rather than red. A stub runner that reports on the calling thread makes both guarantees deterministic — and both new tests do fail against the old implementation |
| What "delivered in time" is asserted as | The reports all arrived, on the thread that produced them | It is the property, stated directly. A `Progress<T>` hop cannot deliver on the reporting thread, so the assertion is exactly the defect's shape rather than a timing guess |
| What the integration test keeps | Its own assertion, plus ordering within a stage | Percentages restart at each stage — receiving reaches 100% before resolving begins — so a single ordered sequence would be wrong. Within a stage, growing is the promise |
| Whether `SyncOperations` changes | No | It already builds its `Progress<SyncProgress>` on the UI thread, which is what marshals its callbacks. Removing the service's hop leaves the overlay exactly where it was |

## Deviations & follow-ups

- **One addition to the plan:** the plan put the regression in the integration test. That test cannot
  be made to fail against the old code — the race usually resolved in time, which is why it was flaky
  rather than red — so the regression went into a new unit test at the seam, where both guarantees are
  deterministic, and the integration test was hardened as planned.
- This item was not in the run's original plan. It was raised by the run itself, twice, as an unrelated
  red suite; the cause turned out to be a real defect in the service rather than a flaky test.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1819  failed: 0  succeeded: 1819  skipped: 0
```

Three tests added (1816 → 1819). No fix cycle. Against the tree with the old `Progress<string>` put
back, both delivery tests fail (`Assert.Equal() Failure: Values differ` on the reporting thread, and
`Collections differ` on the order) and the rest of the suite passes — so they test this defect and
nothing else. The suite was also run three times in a row on the fix without a single failure, where
the flake had been showing up about twice in ten runs.

## Documentation sweep

Nothing a reader of `README.md` or `RELEASENOTES.md` can see changed: the overlay shows the same
transfer, more reliably. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the
repository. No edits.
