# FEATURE-532B-PHASE02 — OS event sources

**Item:** FEATURE-532B — File-system watcher for instant refresh
**Phase:** PHASE02 — OS event sources
**Branch:** `feature/feature-532b-phase02-os-event-sources`
**Run:** feature/2026-10-08-title-watcher-refs-release

## Summary

This phase adds the operating system's side of the watcher. **It is still not wired into the
application:** PHASE03 registers it.

- **`RepositoryLayout`**, a pure record (`WorkTree`, `GitDirectory`, `CommonDirectory`).
  `Classify(path)` says what a change means, with no disk access and no allocation:
  - **References:** `HEAD`, `index`, `packed-refs`, `MERGE_HEAD`, `CHERRY_PICK_HEAD`, `REVERT_HEAD`,
    `ORIG_HEAD` and `config` directly in a git directory, and anything under the common directory's
    `refs/`.
  - **Nothing:**
    - `*.lock` inside a git directory;
    - `FETCH_HEAD`;
    - `objects/`, `logs/`, `hooks/` and the rest of git's bookkeeping;
    - `.git` (directory or file) at the work tree's root;
    - anything outside the repository.
  - **The working tree:** every other file of the work tree, `yarn.lock` included.
  - **Order:** the git directory is checked before the common one, because a linked worktree's git
    directory sits inside the main repository's.
- **`FileSystemRepositoryEventSource`** (`IRepositoryEventSource`) starts a set of
  `FileSystemWatcher`s, each with a 64 KB buffer:
  - the work tree, recursive (file names, folder names, last write);
  - the git directory, flat;
  - `<common>/refs`, recursive;
  - `<common>`, flat, only when it differs from the git directory.

  What the watchers report:
  - **Events:** created, changed and deleted events are classified. A rename reports both of its
    ends, so `HEAD.lock` renamed to `HEAD` is a change of HEAD.
  - **Errors:** a watcher's `Error` becomes `OnFaulted`, with `eventsLost` set for an
    `InternalBufferOverflowException`.
  - **A missing directory** throws a `DirectoryNotFoundException`. A start that fails disposes
    whatever had started.
  - **Disposing** unhooks and disposes every watcher; nothing is reported after.
- **`ResolveLayout`** reads git's `commondir` file, relative to the git directory, to find a linked
  worktree's common directory. A `commondir` that names nothing, or that cannot be read, leaves the
  git directory as the common one.

**The design is type-agnostic.** Every event, whatever its type, only marks a kind of change. So a
rename split into a delete and a create, or a directory delete reported once rather than per file —
the ways FSEvents on macOS differs from inotify and `ReadDirectoryChangesW` — marks the same kinds.
The coalescer never depends on event types.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Desktop/Services/RepositoryLayout.cs`
- `src/Enigma.GitClient.Desktop/Services/FileSystemRepositoryEventSource.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryLayoutTests.cs`: 38 cases covering every rule,
  for a plain repository, a linked worktree and a submodule.
- `tests/Enigma.GitClient.Desktop.UnitTests/FileSystemRepositoryEventSourceTests.cs`: 10 cases on real
  temporary directories:
  - the layout resolution, with `commondir` present, absent and invalid;
  - a work-tree file;
  - HEAD written through its lock;
  - a branch created;
  - objects and `FETCH_HEAD` not reported;
  - a disposed watch silent;
  - a missing work tree refused.
- `docs/done/FEATURE-532B-PHASE02.md`

**Modified**

- `docs/roadmap.md`, `docs/plan/FEATURE-532B.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Notify filters | File and folder names plus last write for the trees; names plus last write for the flat git directories | The draft's changes (added, edited, deleted, renamed). A mode-only change (`chmod +x`) waits for the periodic refresh, because attribute events also fire for every `touch` |
| A missing directory | `DirectoryNotFoundException` before any watcher is built for it | `FileSystemWatcher`'s own `ArgumentException` says less, and the interface promises an `IOException` |
| `ResolveLayout`'s visibility | Public and static on the source | It is the source's own I/O, and the tests reach it on real directories |
| Classifier cost | Spans, no allocation | It runs on every event of a build's storm |
| Proving inotify instances are released | Not a unit test: the measurement in PHASE03 counts them | A test counting `/proc/self/fd` would race other test classes that build a host, whose configuration watches its own folder |

## Deviations & follow-ups

- None from the plan. The planned "factory" is the source itself, through PHASE01's
  `IRepositoryEventSource.Watch`.
- **macOS:** the type-agnostic design is what answers the draft's FSEvents question. It was not run
  on a Mac.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing to change: the running application is unchanged until PHASE03.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Debug`: 2969 total, 2968 passed, 1 skipped,
  0 failed (48 more than after PHASE01).
- The two new classes ran three times in a row on their own, all green.
- Fix budget: 0 cycles.
