# FEATURE-532B — File-system watcher for instant refresh

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-08-title-watcher-refs-release

## Objective

Show working-tree and repository changes made outside the app within about 0.3–1 s:

- files added, edited, deleted or renamed;
- commits, checkouts, stashes and merges.

Today they wait for the next `AutoRefreshService` tick (default 15 s, minimum 5 s). The watcher is an
extra **trigger** on top of the periodic refresh, not a replacement: the periodic refresh stays as the
safety net and as the only source of remote changes (the fetch).

## Context & constraints

- **`AutoRefreshService`** runs a `PeriodicTimer` on `AppSettings.AutoRefreshSeconds`. Each tick:
  1. runs a network fetch (`SyncOperations.FetchQuietlyAsync` → `TryRunExclusiveAsync`, which
     re-reads the references afterwards);
  2. raises `Refreshed`, which `MainWindowViewModel` hands to `HistoryPageViewModel.RefreshInPlaceAsync`.

  It has a single-flight guard (`_refreshing`) and skips a tick while the repository is busy. Its
  loop starts on the UI thread and awaits with `ConfigureAwait(true)`, which is how everything it
  publishes lands on the UI thread.
- **`RepositoryContext.RefreshAsync`** re-reads the references and raises `StateRefreshed`:
  - the Branches, Tags, Remotes and Conflicts pages rebuild;
  - `WorkingTreePanelViewModel` runs `git status --porcelain=v2`, but only while `IsActive`.
- **`HistoryPageViewModel.RefreshInPlaceAsync`** decides whether the graph redraws:
  - it compares the reference stamp and the stash list;
  - it runs `IWorkingTreeProbe.IsDirtyAsync` (a `git status`) to decide whether the uncommitted line
    is shown.
- **The problem:** local refresh latency is tied to the fetch cadence. The interval cannot be
  shortened without hammering the remote.
- **`GitProcessRunner` sets `GIT_OPTIONAL_LOCKS=0`.** The app's `git status` never rewrites
  `.git/index`, so its own reads cannot retrigger a watcher.
- **BUG-6590** made the diff redraw only when the patch actually changed. A spurious refresh is
  visually free.
- **.NET 10's `FileSystemWatcher` on Linux** (checked by decompiling `System.IO.FileSystem.Watcher`
  10.0.3):
  - one inotify instance and one background thread per watcher;
  - the recursive walk that adds one watch per directory runs synchronously inside
    `EnableRaisingEvents = true`;
  - running out of instances (`EMFILE`) throws an `IOException`;
  - running out of watches (`ENOSPC`, during the walk or for a directory created later) is raised
    through `Error` as an `IOException`;
  - a queue overflow (`IN_Q_OVERFLOW`) is raised through `Error` as an
    `InternalBufferOverflowException`.

  On Windows, a `ReadDirectoryChangesW` overflow is that same `InternalBufferOverflowException`.
- **This machine:** `max_user_watches` 524288, `max_user_instances` 1024. Older distributions ship
  8192 watches and 128 instances, shared with IDEs and other tools.
- **The test container** already registers a fake `TimeProvider`, `ManualTimeProvider`: a clock that
  moves only when a test moves it and fires due timers in order.
- **Repository layouts:**
  - in a normal repository the git directory is `<work tree>/.git`;
  - in a linked worktree, `.git` is a file and the git directory is `<main>/.git/worktrees/<name>`.
    `HEAD` and `index` live there, while `refs/` and `packed-refs` live in the common directory, which
    git's `commondir` file names (relative to the git directory);
  - a submodule's git directory is `<super>/.git/modules/<name>`, a full git directory with no
    `commondir`.

## The prior rejection, and why it no longer holds

`FEATURE-13FE` (*Refresh trigger*) and `BUG-6590` (*Fix approach*) rejected a watcher. Each objection,
and how this design answers it:

| Objection then | Answered by |
|---|---|
| **Noise:** every save, build output and `.git` write fires events | An event only sets a dirty flag. Nothing runs per event. A refresh redraws nothing that did not change (BUG-6590's changed-patch check, the history's reference stamp) |
| **No debouncing** | A trailing debounce of 300 ms of quiet, capped at 2 s, so a long build still refreshes every 2 s. At most one refresh at a time, and exactly one more afterwards if events arrived during it |
| **`.git` churn** retriggering the refresh | The working-tree watcher drops every event under `.git/`. The git-directory watcher is not recursive (plus `refs/`), so it never sees `objects/`. `*.lock` files and `FETCH_HEAD` are ignored. The app's own reads write nothing (`GIT_OPTIONAL_LOCKS=0`), and the app's own writes are suppressed while they run and for 500 ms after |
| **inotify limits** | A failure to start, or a limit reached later, is logged once and falls back to the periodic refresh, with one full refresh to catch up. No crash, no dialog. The OS watchers start off the UI thread, because their initial walk is O(directories) |
| **Buffer overflows** on large trees | 64 KB buffers. An overflow is treated as "everything changed": one full refresh, and the watcher keeps going |
| **The polling stays needed** for index, HEAD and remotes | HEAD, the index, the merge state files, `packed-refs`, `config` and `refs/` are watched. The periodic refresh stays for the remotes (the fetch) and as the safety net |
| **Ignore-awareness** (FEATURE-13FE) | Not needed: coalescing plus the changed-patch check make an event in an ignored file cost one cheap, invisible refresh per window. Filtering through `.gitignore` is a follow-up if measurements ever say otherwise |

## Design

```
OS watchers (thread pool)          RepositoryWatcher                        consumers (UI thread)
  work tree, recursive  ──┐   Mark(kind): suppressed? → OR flags          ┌─ References: AutoRefreshService
  git dir, flat         ──┼─▶ wake the loop                  debounce ───▶│   .RefreshLocallyAsync → refs →
  <common>/refs, deep   ──┤   loop (UI context): 300 ms quiet / 2 s max   │   StateRefreshed + Refreshed
  <common>, flat (wt)   ──┘   await RefreshLocallyAsync (single flight)   └─ WorkingTree only: Refreshed →
                                                                               history.RefreshWorkingTreeAsync
```

- **`RepositoryChanges`**, `[Flags]`: `None`, `WorkingTree`, `References`, `Everything`.
- **`IRepositoryEventSource`** (`IDisposable`) is the thin OS abstraction. It raises:
  - `Changed(RepositoryChanges)`, on any thread, already classified;
  - `Faulted`, with `EventsLost` set for an overflow (the source keeps going) and clear for a
    failure (the source has stopped).

  **`IRepositoryEventSourceFactory.Start(RepositoryHandle)`** creates and starts one, and throws
  when the OS refuses.
- **`IRepositoryWatcher` / `RepositoryWatcher`** (DI singleton) does three things:
  - it starts and stops with `IRepositoryContext.RepositoryChanged` and follows
    `AppSettings.WatchFileSystem` live, as `AutoRefreshService` follows its interval;
  - it owns one **session** per repository: a cancellation source, the event source, the pending
    flags and the times of the first and last events;
  - it coalesces events, suppresses the app's own and falls back on failure.
- **Suppression:**
  - `IRepositoryContext` gains `IsWriting` (an exclusive write holds the lock) and `WriteEnded`;
  - events are dropped while `IsWriting`, and for **500 ms** after `WriteEnded`;
  - those writes refresh on their own (`RunExclusiveAsync(refreshAfter: true)`).
- **`IAutoRefreshService.RefreshLocallyAsync(RepositoryChanges, CancellationToken)`** is a refresh
  with no fetch:
  - it shares the single-flight guard with the periodic refresh, but **waits** for a running one
    instead of skipping, so a local change is never dropped behind a fetch;
  - with `References`, it re-reads the references and raises `StateRefreshed`;
  - it publishes `Refreshed` with `Fetch = NotAttempted` and `Changes` set.
- **Consumers:** `MainWindowViewModel` hands a working-tree-only result to
  `HistoryPageViewModel.RefreshWorkingTreeAsync`, and everything else to `RefreshInPlaceAsync`, as
  today.
- **One `git status` per window:**
  - a working-tree change is read once: by the panel's own read while the panel is shown, otherwise
    by the history's probe;
  - the history's uncommitted line follows that one answer;
  - `RefreshInPlaceAsync` also reuses the panel's read while the panel is shown, instead of probing a
    second time.

## PHASE01 — Coalescer and suppression

**Branch:** `feature/feature-532b-phase01-coalescer`
**Status:** DONE — see `docs/done/FEATURE-532B-PHASE01.md`

### Steps

1. `Services/RepositoryChanges.cs`: the flags.
2. `Services/RepositoryEventSource.cs`:
   - `IRepositoryEventSource`;
   - `RepositoryWatchFaultEventArgs` (`Error`, `EventsLost`);
   - `IRepositoryEventSourceFactory`.
3. `IRepositoryContext` / `RepositoryContext`:
   - `IsWriting`, counted with `Interlocked` around `RunExclusiveAsync<T>` and `TryRunExclusiveAsync`
     (the operation and its refresh);
   - `WriteEnded`, raised once the lock is released.

   Update every test double that implements the interface.
4. `AutoRefreshService`:
   - the guard becomes a `SemaphoreSlim`: the periodic and requested refreshes take it with `Wait(0)`
     (skip when busy, as today), and `RefreshLocallyAsync` takes it with `WaitAsync`;
   - `AutoRefreshResult.Changes` (`init`, default `Everything`);
   - `QuietFetchResult.NotAttempted`.
5. `AppSettings.WatchFileSystem` (`init`, default `true`). A file without the key reads as the
   default, so no migration is needed.
6. `Services/RepositoryWatcher.cs`: `IRepositoryWatcher` (`IsWatching`) and `RepositoryWatcher`.
   - Constants: `QuietPeriod` 300 ms, `MaximumWait` 2 s, `WriteGracePeriod` 500 ms.
   - The session's loop runs on the context it was started from (the UI thread). It waits for a
     change, then waits for 300 ms of quiet or 2 s since the first event, whichever comes first,
     with `Task.Delay(…, TimeProvider, token)`. It takes the flags, awaits
     `RefreshLocallyAsync(changes)`, and goes round again: events that arrived meanwhile make
     exactly one more refresh.
   - The event source is started with `Task.Run`. A session cancelled while it starts disposes it.
   - `Mark(changes)` runs on any thread and only touches `Interlocked` state:
     - it drops the change while suppressed;
     - it records the first and last event times from `TimeProvider.GetUtcNow()`;
     - it wakes the loop.
   - `Faulted` with `EventsLost`: `Mark(Everything)`, and the watcher keeps going.
   - `Faulted` without it: a forced `Everything` refresh, the source disposed, `IsWatching = false`.
     The periodic refresh is the only trigger until the repository or the setting changes.
   - A factory that throws: no watching, logged; nothing was missed, so no refresh.
   - Logging: the first failure of the process is a warning, naming the inotify limits on Linux;
     later ones are debug. Start, stop and overflow are debug.
   - A failing refresh is logged and the loop goes on. Nothing is ever thrown to the UI.
   - Stopping (repository closed or changed, setting off, `Dispose`): cancel the session, so a
     pending debounce publishes nothing, and dispose its source.
7. Tests, xUnit v3 on `ManualTimeProvider` with a scripted event source and a scripted auto
   refresh:
   - **Debounce:** nothing before 300 ms of quiet, one refresh at 300 ms with the changes.
   - **Maximum wait:** a change every 100 ms refreshes at 2 s, then every 2 s while it lasts, and
     once more 300 ms after the last.
   - **Burst:** 1,000 changes produce a single refresh carrying their union.
   - **Follow-up:** changes during a running refresh produce exactly one more, after it.
   - **Suppression:**
     - nothing while a write runs;
     - nothing within 500 ms of its end;
     - counted again after that.
   - **Overflow:** one `Everything` refresh, still watching.
   - **Failure:** one `Everything` refresh, the source disposed, not watching, one warning for two
     failures.
   - **A factory that throws:** not watching, no refresh, no exception.
   - **The setting toggled live:** off disposes the source, on starts a new one.
   - **The repository switched mid-burst:** no refresh for the old one. Closing disposes the source.
   - **`AutoRefreshService`:**
     - a local refresh waits for a running periodic one, then runs once;
     - `References` re-reads the references, `WorkingTree` does not;
     - `Refreshed` carries `Changes`;
     - the periodic refresh still skips while busy.
   - **`RepositoryContext`:** `IsWriting` during a write, and `WriteEnded` after it, including a
     failed one and a skipped `TryRunExclusiveAsync`, which raises nothing.

### Acceptance criteria

- Every behaviour listed in step 7 is covered by a test and passes.
- OS callbacks touch only thread-safe state. Every refresh and every event the watcher publishes
  runs on the context the watcher was started from.
- Not wired into the application yet: nothing in the running app changes in this phase.
- Build: zero warnings. The whole suite is green.

## PHASE02 — OS event sources

**Branch:** `feature/feature-532b-phase02-os-event-sources`
**Status:** DONE — see `docs/done/FEATURE-532B-PHASE02.md`

### Steps

1. `Services/RepositoryLayout.cs`, a pure record (`WorkTree`, `GitDirectory`, `CommonDirectory`).
   `Classify(string path)` returns a `RepositoryChanges`:
   - **Under the git or common directory:**
     - `*.lock` → `None`;
     - `FETCH_HEAD` → `None`;
     - `refs/**` of the common directory → `References`;
     - a direct child named `HEAD`, `index`, `packed-refs`, `MERGE_HEAD`, `CHERRY_PICK_HEAD`,
       `REVERT_HEAD`, `ORIG_HEAD` or `config` → `References`;
     - anything else (`objects/`, `logs/`, `hooks/`, `COMMIT_EDITMSG`…) → `None`.
   - **Under the work tree:**
     - a first segment of `.git` (the directory, or a linked worktree's `.git` file) → `None`;
     - everything else, `*.lock` included (`yarn.lock` is a real file) → `WorkingTree`.
   - **Elsewhere** → `None`.
   - Paths compare ordinally, ignoring case on Windows, as `RepositoryHandle` does.
2. `Services/FileSystemRepositoryEventSource.cs`:
   - **The factory** resolves the layout. The common directory comes from `<git dir>/commondir` when
     that file exists and names an existing directory, otherwise it is the git directory itself.
   - **The source** starts:
     - the work tree, recursive (`FileName | DirectoryName | LastWrite`);
     - the git directory, flat (`FileName | LastWrite`);
     - `<common>/refs`, recursive;
     - `<common>`, flat, only when it differs from the git directory.

     Every watcher gets `InternalBufferSize = 64 KB`.
   - `Created`, `Changed`, `Deleted` and `Renamed` (both of a rename's paths) are classified, and a
     non-`None` result is raised.
   - `Error` is raised as `Faulted`, with `EventsLost` for an `InternalBufferOverflowException`.
   - A start that fails disposes the watchers already started and throws. `Dispose` stops them all,
     so no inotify instance is leaked.
3. Tests:
   - `RepositoryLayout` on every rule above, and on a linked-worktree and a submodule layout;
   - the layout resolution, on real temporary directories: `commondir` present, absent and invalid;
   - the source, on a real temporary directory:
     - a work-tree file raises `WorkingTree`;
     - `.git/HEAD` raises `References`;
     - `HEAD.lock` renamed to `HEAD` raises `References`;
     - `.git/objects/…` raises nothing;
     - a disposed source raises nothing;
     - a missing work tree throws from `Start`.

### Acceptance criteria

- Every classification rule and every source behaviour in step 3 is covered by a passing test.
- The event semantics the coalescer relies on are type-agnostic. A rename split into a delete and a
  create, or a directory delete reported once rather than per file, marks the same kinds, which is
  what makes macOS's FSEvents differences irrelevant to the design.
- Build: zero warnings. The whole suite is green.

## PHASE03 — Wire the watcher into the app

**Branch:** `feature/feature-532b-phase03-wire-watcher`
**Status:** TODO

### Steps

1. DI: `IRepositoryEventSourceFactory` → `FileSystemRepositoryEventSourceFactory` and
   `IRepositoryWatcher` → `RepositoryWatcher`, as singletons.
   - `App` resolves the watcher once at start-up, so it follows the first repository opened.
   - The host's disposal at exit disposes it.
2. `MainWindowViewModel`: a `Refreshed` whose `Changes` is `WorkingTree` alone goes to
   `HistoryPageViewModel.RefreshWorkingTreeAsync()`. Everything else goes to `RefreshInPlaceAsync`,
   as today.
3. `HistoryPageViewModel.RefreshWorkingTreeAsync()`:
   - **One read:**
     - while the panel is shown, `WorkingTree.RefreshAsync()`, and the line follows
       `!WorkingTree.IsClean`;
     - otherwise the probe.
   - **The uncommitted line** appears or disappears with the same reload-keeping-place as an
     in-place refresh. While the diffs have the page, the redraw is held, as it already is.
   - **Single flight:** a call during a running one runs exactly one more afterwards.
4. `WorkingTreePanelViewModel` remembers its latest read. While the panel is shown,
   `RefreshInPlaceAsync` awaits that read and uses its answer instead of probing a second time.
5. An integration test on a real temporary repository:
   - open it, show the panel, write a file;
   - advance the manual clock past the quiet period only;
   - the file appears in the panel, and the periodic refresh never ran.
6. A measurement on a large generated repository, with a throwaway harness in the scratchpad (not
   committed):
   - the watcher's start time and inotify watch count;
   - events and refreshes for a storm of file writes and for a checkout of thousands of files;
   - idle CPU.

   The numbers go in the completion doc.

### Acceptance criteria

- In the running app, a file written outside the app shows in the working-tree panel, and a commit
  made in a terminal shows in the history, within about a second, without waiting for the periodic
  refresh.
- A working-tree change runs exactly one `git status` per debounce window.
- The integration test passes. The measured numbers are recorded, including how many refreshes each
  storm produced.
- Build: zero warnings. The whole suite is green.

## PHASE04 — Setting and 60 s default

**Branch:** `feature/feature-532b-phase04-setting-default`
**Status:** TODO

### Steps

1. Settings, in the *Git* card: a *Watch the repository for changes* toggle bound to
   `SettingsPageViewModel.WatchFileSystem`, followed live by the watcher. Its description:
   - what it does;
   - that the automatic refresh still fetches;
   - when to turn it off (network shares, WSL `/mnt` drives, a tree too large for the inotify
     limits).
2. `AppSettings.AutoRefreshSeconds` defaults to **60**.
   - Schema **version 7**: `LegacyAutoRefreshSeconds = 15`.
   - `SettingsService.Migrate` moves a pre-7 file whose interval is exactly 15 (a value nobody chose)
     to 60. Any other value is kept.
   - The documentation comments explain it.
3. The interval's sentence on the Settings page says local changes no longer wait for it while the
   repository is watched.
4. Tests:
   - the toggle writes the setting and the watcher follows it;
   - the migration: 15 moves to 60, 30 stays, a version 7 file keeps 15, a file without the key gets
     60;
   - every test that assumed 15.
5. README: the watcher in *Features*, and the refresh interval where it is described.

### Acceptance criteria

- The toggle shows in Settings with its description, and switching it starts or stops the watcher at
  once.
- A fresh install refreshes every 60 s. A 5.9 file with 15 becomes 60, and any other value is kept.
- Build: zero warnings. The whole suite is green.

## Out of scope

- Git's own `core.fsmonitor` integration.
- Filtering events through `.gitignore` (`git check-ignore`): a follow-up if measurements ever say
  otherwise.
- Changing what the periodic fetch does.
- A custom inotify binding that could exclude `node_modules`, `bin` or `obj`.
- A status line in Settings saying whether the watcher is running.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the watcher refreshes | Through `IAutoRefreshService.RefreshLocallyAsync`, awaited | Reuses the existing single-flight guard as asked. One owner of "refresh", and `Refreshed` already reaches the history | A second refresh path beside `AutoRefreshService` (two guards, two publications) |
| A local refresh while the periodic one runs | Wait for it, then run | A periodic refresh that started before the change may have read before it. Waiting is what "exactly one more afterwards" means | Skip, as the periodic refresh does (a change could wait 60 s) |
| The working-tree path | The history's `RefreshWorkingTreeAsync`: the panel's read when shown, else the probe | Exactly one `git status` per window, and the graph's uncommitted line appears and disappears within a second too, which the goal asks for | Only the panel's read (the uncommitted line would wait for the tick); the full in-place refresh (stash list and probe on every save) |
| The in-place refresh while the panel is shown | Reuse the panel's read | Avoids a second `git status` in the same window: a checkout storm also changes HEAD | Probing again (two reads per window) |
| Suppressing the app's own writes | `IsWriting` + `WriteEnded` on the context; drop events then and for 500 ms after | Every app write goes through `RunExclusiveAsync`/`TryRunExclusiveAsync` and refreshes itself. 500 ms covers events delivered after the write | Deferring them (one redundant refresh after every operation); no grace window (stragglers retrigger) |
| Overflow vs failure | Overflow: full refresh, keep watching. Failure: full refresh, stop, periodic refresh only | An overflow lost events but the watcher still works. A failed watcher would only lose more | Restarting the watcher on failure (a full walk again, likely to fail again at the same limit) |
| Logging a failure | Warning once per process, debug afterwards | "Log once": a machine at its inotify limit fails on every repository | A warning per repository; a dialog |
| Fake clock in the tests | The existing `ManualTimeProvider` | It is the container's fake `TimeProvider` already, used by the auto-refresh tests, with the same semantics as `FakeTimeProvider` for what these tests need. No new package | Adding `Microsoft.Extensions.TimeProvider.Testing` (a second fake clock in one test project) |
| Debounce and maximum wait | 300 ms and 2 s, constants | The draft's numbers. Nobody tunes them per machine | Settings for them |
| Watched git files | The draft's list plus `config` | A remote added, or an upstream set, in a terminal is a repository change the Remotes and Branches pages show. `config` is written rarely | The list alone (those changes wait for the tick) |
| `*.lock` | Ignored inside the git directories only | `yarn.lock`, `Cargo.lock` and `Gemfile.lock` are real work-tree files whose changes matter | Ignoring `*.lock` everywhere |
| Linked worktrees | Read git's `commondir` file | git's documented layout, no process needed; an invalid one falls back to the git directory | `git rev-parse --git-common-dir` (a process per open) |
| Starting the OS watchers | `Task.Run`, off the UI thread | The inotify walk is synchronous and O(directories) | Starting on the UI thread (freezes on a large tree) |
| Buffers | 64 KB everywhere | The draft's Windows value, and harmless elsewhere | The 8 KB default |
| Default interval | 60 s, migrated from an untouched 15 (schema 7) | Local changes no longer depend on it, and it governs the fetch: 4× fewer fetches. The house migrates changed defaults (versions 2–6) | Keeping 15 (the fetch cadence the watcher exists to relax); 60 for new installs only |
| Where the setting lives | The *Git* card, beside the interval | It is about how the repository is refreshed | A card of its own |
| Breakdown | Four phases: coalescer, OS sources, wiring, setting | Each one a reviewable commit; the first two are unit-tested in isolation | One dev (far over 30 minutes of review) |
