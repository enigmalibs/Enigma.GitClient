# FEATURE-532B-PHASE03 — Wire the watcher into the app

**Item:** FEATURE-532B — File-system watcher for instant refresh
**Phase:** PHASE03 — Wire the watcher into the app
**Branch:** `feature/feature-532b-phase03-wire-watcher`
**Run:** feature/2026-10-08-title-watcher-refs-release

## Summary

The watcher is live. A file saved, added or deleted outside the application, or a commit, checkout,
stash or merge made in a terminal, shows about 0.3 s after the last change, without waiting for the
automatic refresh.

- **Registration:**
  - `IRepositoryEventSource` → `FileSystemRepositoryEventSource` and `IRepositoryWatcher` →
    `RepositoryWatcher`, both singletons;
  - `App` resolves the watcher once at start-up, and from then on it follows the repository context;
  - the host's disposal at exit disposes it, and with it every inotify instance.
- **`MainWindowViewModel`:** a refresh whose `Changes` is `WorkingTree` alone goes to the history's
  new `RefreshWorkingTreeAsync()`. Every other refresh goes to `RefreshInPlaceAsync`, as before.
- **`HistoryPageViewModel.RefreshWorkingTreeAsync()`:**
  - **One `git status`:** while the working-tree panel is on screen, the panel's own read answers
    both questions, what is changed and whether there is anything uncommitted. Otherwise the probe
    answers.
  - **The uncommitted line** appears or goes with a reload that keeps the reader's place. While the
    diffs have the page, the reload waits for them to close, as it already did.
  - **One reading at a time:** changes during a reading make exactly one more after it.
- **`RefreshInPlaceAsync(referencesMoved, workingTreeRead)`:** the new flag says the panel has just
  read the tree, or is reading it, for the same refresh.
  - The auto refresh passes it: its re-read of the references set the panel reading.
  - So does the panel's own operation: it read the tree before it said so.
  - Every other caller still gets a fresh read.
  - `WorkingTreePanelViewModel.Reading` exposes the panel's latest reading for this.
- **A reload decided by a reading reuses it.** `_uncommittedJustRead` hands the deciding answer to
  the reload's first page, which used to probe again.

## Measurements

The measurement ran on this machine with a throwaway harness (a temporary explicit test, not
committed). It used the real watcher, the real automatic refresh (its interval off), the real
history and panel, and the system clock.

- **Machine:** 16 CPUs, Arch Linux, git 2.56.0.
- **The repository:** 50,000 files in 2,000 directories (+20 parents), plus a branch changing 10,000
  of them.
- **Counting:** a git status is counted at the panel's status service and at the probe.

| What | Events | Refreshes | `git status` runs | Last refresh after the storm ended |
|---|---|---|---|---|
| 10,000 files rewritten in 48 ms, panel hidden | 17,138 | 1 (WorkingTree, at 347 ms) | 1 (probe) | 299 ms |
| 10,000 files rewritten in 45 ms, panel shown | 14,092 | 1 (WorkingTree, at 342 ms) | 1 (panel) | 297 ms |
| `git switch` in a terminal changing 10,000 files (117 ms) | 30,004 | 1 (Everything, at 418 ms) | 1 (probe) | 301 ms |
| A build writing 100 files every 100 ms for 5.2 s | 14,082 | 3 (at 2.0 s, 4.1 s, 5.4 s) | 3 (probe), one per window | 196 ms |

- **Start:** the watch alone took 61 ms. Opening, reading the history and starting the watch took
  254 ms. The repository took 3 inotify instances and 2,293 inotify watches (the work tree's 2,020
  directories, the git directory's, and `refs/`).
- **Idle:** 0 events over 10 s. The whole test host used 102 ms of CPU in 10 s with the watcher on
  and 114 ms with it off. That is noise: its own threads block in inotify reads.
- **Disposed:** 0 inotify instances and 0 watches left.
- **Before reusing the deciding answer:** the panel-hidden storm and the checkout ran 2 probes, one
  more from the reload. With `_uncommittedJustRead`, every storm runs exactly one `git status` per
  window.
- **No overflow or fault** was reported at any point. A 64 KB buffer absorbed 30,000 events in
  117 ms.
- **`.gitignore` filtering stays a follow-up that is not needed:** with one refresh per window, events
  in ignored files cost nothing measurable.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`: the two
  registrations.
- `src/Enigma.GitClient.Desktop/App.axaml.cs`: the watcher resolved at start-up.
- `src/Enigma.GitClient.Desktop/ViewModels/MainWindowViewModel.cs`: the routing of a working-tree-only
  refresh.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`:
  - `RefreshWorkingTreeAsync`;
  - `HasUncommittedWorkAsync`;
  - `RefreshInPlaceAsync`'s `workingTreeRead`;
  - `_uncommittedJustRead`.
- `src/Enigma.GitClient.Desktop/ViewModels/Panels/WorkingTreePanelViewModel.cs`: `Reading`.
- `docs/roadmap.md`, `docs/plan/FEATURE-532B.md`: statuses.

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryWatcherIntegrationTests.cs`: 4 tests on real
  repositories, all with the real OS watcher and a test clock moved only through the quiet period:
  - a file written shows in the working-tree panel;
  - a file written in a clean repository brings the uncommitted line;
  - a commit made in a terminal shows in the history;
  - the working tree is read one reading at a time, with exactly one more after changes during one.

  The first three assert that no periodic refresh ran: every published refresh is
  `QuietFetchResult.NotAttempted`.
- `docs/done/FEATURE-532B-PHASE03.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| When the in-place refresh may take the panel's reading | Only when the caller says the panel read for this refresh (`workingTreeRead`) | A caller without a re-read of the references just before (a test, a sync that failed) would get a stale answer. The existing test `ThePanel_GoesWhenItsLineDoes` caught that |
| Where the watcher is started | Resolved once in `App`, inside the desktop branch | Nothing else asks for it. The headless tests' application never reaches that branch |
| The first page of a load | The probe, unless the refresh that decided the reload has just read the answer | A plain reload (opening a repository) has no reading to reuse. A decided reload has one, and reusing it keeps one `git status` per window |
| How the measurement was run | A temporary explicit test, deleted before the commit | It needs the real pipeline and the dispatcher, which the test project already hosts; nothing of it ships |

## Deviations & follow-ups

- **Added beyond the plan:** `_uncommittedJustRead`. Without it, a reload decided by a working-tree
  change probed a second time, which the measurement showed.
- **`.gitignore` filtering:** not needed, per the measurements above.
- **Each open repository costs 3 inotify instances** (4 for a linked worktree), and one watch per
  directory of the work tree and the git directory. That is the draft's design. On a distribution
  that still ships 128 instances, about 40 open repositories across all applications would reach
  the limit, and the watcher falls back with one warning.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing to change: the README describes neither the refresh nor its timing. Its features gain the
watcher, and the Settings page its toggle, in PHASE04 as planned.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Debug`: 2973 total, 2972 passed, 1 skipped,
  0 failed.
- `RepositoryWatcherIntegrationTests` ran three times in a row on its own, all green.
- **Fix budget:** 0 cycles. Development iterations before the first full run:
  - `ThePanel_GoesWhenItsLineDoes` failed on the first wiring, which took the panel's reading
    unconditionally. The `workingTreeRead` flag fixed it before any full run.
  - The second probe found by the measurement.
