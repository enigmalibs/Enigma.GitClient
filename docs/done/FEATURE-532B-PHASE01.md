# FEATURE-532B-PHASE01 — Coalescer and suppression

**Item:** FEATURE-532B — File-system watcher for instant refresh
**Phase:** PHASE01 — Coalescer and suppression
**Branch:** `feature/feature-532b-phase01-coalescer`
**Run:** feature/2026-10-08-title-watcher-refs-release

## Summary

This phase builds the watcher's decision-making: when a change on disk becomes a refresh, which
changes are the application's own, and what a failing watch falls back on. The operating system's
side sits behind a small interface. **Nothing is wired into the running application yet:** PHASE02
adds the operating-system watch and PHASE03 registers it.

- **`RepositoryWatcher`** (`IRepositoryWatcher`, `IsWatching`) follows the open repository and
  `AppSettings.WatchFileSystem` live. It keeps one *session* per repository; a new repository means a
  new session, so nothing seen in the previous one is ever refreshed against it.
  - **Coalescing:** an event only marks what changed (`RepositoryChanges`: `WorkingTree`,
    `References`, `Everything`). The refresh waits for 300 ms without a change, or 2 s after the first
    one, whichever comes first. Refreshes are awaited one at a time, and changes during one make
    exactly one more afterwards.
  - **The application's own writes:** events are dropped while `IRepositoryContext.IsWriting`, and for
    500 ms after `WriteEnded`.
  - **Faults:**
    - an overflow marks `Everything`, and the watch goes on;
    - a failed watch runs one `Everything` refresh, then lets go, and the periodic refresh carries on
      alone;
    - a watch the system refuses at start is logged, and nothing is refreshed, since nothing was
      missed.

    The first failure of the process is a warning naming the inotify limits; later ones are debug. No
    exception ever reaches the UI.
  - **Threads:** the operating system's start runs on the thread pool, because the inotify walk is
    O(directories). Events touch only interlocked state. The loop runs on the context the session was
    started from, the UI thread in the application.
- **`IRepositoryContext.IsWriting` and `WriteEnded`:** a write is counted from taking the lock to
  releasing it, its refresh included. `WriteEnded` is raised for a failed write too, never for a
  `TryRunExclusiveAsync` that did not run.
- **`IAutoRefreshService.RefreshLocallyAsync(changes)`:** a refresh with no fetch.
  - It reads the references only when `References` is set.
  - It publishes `Refreshed` with `Fetch = QuietFetchResult.NotAttempted` and the new
    `AutoRefreshResult.Changes`.
  - **The guard:** the single-flight guard became a `SemaphoreSlim`. The periodic and requested
    refreshes still skip while it is taken; a local refresh waits for it.
  - **A switched repository:** a local refresh cancelled while it waits, or whose repository changed
    while it read, publishes nothing.
- **`AppSettings.WatchFileSystem`**, `true` by default. A file without the key reads as the default.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Desktop/Services/RepositoryChanges.cs`
- `src/Enigma.GitClient.Desktop/Services/RepositoryEventSource.cs`: `IRepositoryEventSource`,
  `IRepositoryEventSink`.
- `src/Enigma.GitClient.Desktop/Services/RepositoryWatcher.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryWatcherTests.cs`: 13 tests.
- `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/ScriptedRepositoryEventSource.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/RecordingLogger.cs`
- `docs/done/FEATURE-532B-PHASE01.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Services/IRepositoryContext.cs`, `RepositoryContext.cs`: `IsWriting`,
  `WriteEnded`.
- `src/Enigma.GitClient.Desktop/Services/AutoRefreshService.cs`: `RefreshLocallyAsync`,
  `AutoRefreshResult.Changes`, the semaphore guard.
- `src/Enigma.GitClient.Desktop/Services/SyncOperations.cs`: `QuietFetchResult.NotAttempted`.
- `src/Enigma.GitClient.Core/Configuration/AppSettings.cs`: `WatchFileSystem`.
- `tests/Enigma.GitClient.Desktop.UnitTests/AutoRefreshTests.cs`: 6 tests for the local refresh.
- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryContextTests.cs`: 3 tests for the write
  tracking.
- `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/ManualTimeProvider.cs`: `GetTimestamp` and
  `TimestampFrequency` follow the manual clock.
- `docs/roadmap.md`, `docs/plan/FEATURE-532B.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The operating system's interface | `IRepositoryEventSource.Watch(repository, sink)` returning an `IDisposable`, with an `IRepositoryEventSink` receiving changes and faults | A sink handed over at the start cannot miss what the system reports before a subscriber attaches. One interface replaces the plan's source and factory pair |
| The clock the waits are measured on | `TimeProvider.GetTimestamp()` (monotonic), not `GetUtcNow()` | A change of the system's time cannot stall or skip the debounce. The test clock's `GetTimestamp` now follows its manual time |
| How the loop sleeps | A `TaskCompletionSource` completed by the first change, without `RunContinuationsAsynchronously` | Completed on an OS thread, the loop is posted straight to the UI context. A `SemaphoreSlim` wake-up hopped through the thread pool first, which made the timing tests order-dependent |
| An overflow during the application's own write | Dropped like any of that write's events | The write caused it and refreshes by itself |
| A failure during the application's own write | One `Everything` refresh all the same | After a failure nothing more comes from the watch, and what it missed is unknown |
| A refresh that throws | Logged as a warning; the loop goes on | Nothing awaits the loop, and the next change deserves its refresh |

## Deviations & follow-ups

- **The interface shape**, as above: a sink instead of an event-raising source plus a factory. The
  plan's `RepositoryWatchFaultEventArgs` became `IRepositoryEventSink.OnFaulted(error, eventsLost)`.
- **Not wired yet, as planned.** The running application is unchanged until PHASE03.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing to change: no user-facing behaviour moved in this phase. The README's features and the
Settings text change with PHASE03 and PHASE04.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Debug`: 2921 total, 2920 passed, 1 skipped,
  0 failed (22 more than the baseline's 2899).
- `RepositoryWatcherTests` ran five times in a row on its own, all green. With the auto-refresh and
  context tests it also ran three times, all green.
- **Fix budget:** 0 cycles. The development iterations before the first full run were:
  - two compile errors (an `xUnit1051` token and an unused field);
  - the wake-up change found by the targeted tests.

  The first full build and suite run was green.
