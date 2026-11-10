# FEATURE-6693-PHASE05 — Pin changes: diff, guard, discard

**Item:** FEATURE-6693 — Submodule support
**Branch:** `feature/feature-6693-phase05-pin-changes`
**Run:** feature/2026-11-09-feature-6693

## Summary

A submodule's change now says what it does, a commit that moves one back asks first, and *Discard*
puts a submodule back.

- **Core, the patch:** `SubprojectCommit` (`Subproject commit <sha>[-dirty]`) and
  `FilePatch.OldSubproject` / `NewSubproject`, read from the hunk lines of a submodule's patch.
- **Core, the change:** `ISubmoduleService.DescribePinChangeAsync` → `PinChange`: `Forward`, `Back`,
  `Diverged`, `Unknown` (not downloaded, a commit it lacks, added or removed — with why) or `Unchanged`,
  the commits each way (title, author, date, short SHA; 10 listed, "and N more") and the one-line
  `Summary`. No fetch.
- **The diff viewer** (history and working changes): for a submodule, the summary — in the warning
  colour when it goes back — the commits marked `+` / `−`, why when unknown, and the submodule's own
  modified/untracked content with *Open submodule*, instead of the two `Subproject commit` lines.
- **Working changes:** a submodule's line says "other commit checked out", "modified content",
  "untracked content" (from `ChangedFile.Submodule`), and its menu has *Open submodule*.
- **Commit guard:** before a commit, `ReadStagedPinsAsync` (HEAD vs index gitlinks) and each change's
  direction; any *Back* raises one red question — "This commit moves libs/Common back 3 commits (to
  a1b2c3d). Is that intended?" — *Commit anyway* / Cancel. Forward and Unknown ask nothing.
- **Discard / Discard all:** `PlanDiscardAsync` names each submodule among the paths with the commit it
  goes back to and how many commits no ref contains it would leave behind (in the red question); one
  with uncommitted changes refuses the discard, naming it, with *Open submodule*. Confirmed, the paths
  are discarded and `PutBackAsync` moves the submodules back in the same write — off a commit no ref
  contains too, which the question said.
- README *Features*: a bullet for pin changes.

## Files/modules touched

- Created:
  - `src/Enigma.GitClient.Core/Submodules/PinChange.cs`
  - `tests/Enigma.GitClient.Core.UnitTests/Submodules/PinChangeTests.cs`
  - `tests/Enigma.GitClient.Core.IntegrationTests/Submodules/PinChangeTests.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/SubmodulePinGuardTests.cs`
  - `docs/done/FEATURE-6693-PHASE05.md`
- Modified:
  - `src/Enigma.GitClient.Core/Diff/DiffModel.cs`, `Submodules/SubmoduleService.cs`
  - `src/Enigma.GitClient.Desktop/ViewModels/Panels/DiffViewerViewModel.cs`,
    `ChangedFilesPanelViewModel.cs`, `WorkingTreePanelViewModel.cs`
  - `src/Enigma.GitClient.Desktop/Views/Panels/DiffViewerView.axaml`, `ChangedFilesPanelView.axaml`
  - `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/FakeSubmoduleService.cs`
  - `README.md`; `docs/roadmap.md`, `docs/plan/FEATURE-6693.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The working-changes badge for the `C` flag | "other commit checked out", not "new commits" | git's flag says the checkout differs from the index, not which way: a stale (behind) submodule would have read "new commits" — the very accident this item is about |
| Where the guard's and the discard's sentences are built | Static helpers (`DescribeGoingBack`, `DescribeSubmodules`, `PinChange.Summary`) | Testable without a window; the texts per direction run in the Core suite here |
| A discard touching a submodule with uncommitted changes | The whole discard is refused, naming it, with *Open submodule* | Never lose work; a partial discard would leave the user unsure what went |
| Moving off a commit no ref contains in a discard | Allowed (`PutBackAsync`), after the question names how many commits | The plan's explicit exception to rule 1, for an explicit discard |
| Nested submodules of one put back | They follow what its move changed (the follow's rule) | Consistent with PHASE03 |
| A guard that cannot read the staged pins | Logs and lets the commit go | The commit is the user's; a broken read must not block it |
| New dependencies of the viewer and the panel | Optional last constructor parameters | DI injects them; the tests that build them directly compile unchanged |

## Deviations & follow-ups

- The plan's badge word "new commits" became "other commit checked out" (above).
- The diff viewer's texts per direction are tested on `PinChange.Summary` / `Lines` (Core, run here),
  which the viewer shows verbatim; the viewer's own wiring is compiled, not run.
- `git diff --raw` abbreviates object names: `ReadStagedPinsAsync` passes `--no-abbrev` (found by its
  integration test before the Definition-of-Done run).
- Finishing a conflicted merge from the conflict page is not guarded (that commit is not the working
  changes panel's); PHASE07 resolves pin conflicts by picking a side, which cannot go "back" silently.
- Pre-existing, unchanged: the Windows-only `TokenStoreTests` failure (see the PHASE01 record).

## Build/test evidence

- **Baseline:** see `docs/done/FEATURE-6693-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
  - Core unit: 1498 run, 1495 passed, 1 failed (the baseline's), 2 skipped.
  - Core integration: 446 run, 444 passed, 0 failed, 2 skipped.
  - Desktop: **compiled, not run** (this machine's standing rule): `SubmodulePinGuardTests` (3).
- **New tests:** Core unit — `Subproject commit` with and without `-dirty` and what is not one; a
  submodule's patch carries both commits, a file's does not; the summary per direction (forward, back,
  diverged, unknown, added, removed); the `+`/`−` lines and "and N more"; the dirt sentence; a
  discard's sentence; the raw-diff, tree and log parsers. Core integration — forward, back, diverged,
  not downloaded and a missing commit (unknown, nothing fetched); the staged pin; a discard puts the
  submodule back; off a commit no ref contains it says how many and moves it all the same; refused
  with uncommitted changes; a plan leaves out what is not a submodule. Desktop — the guard asks only
  for *Back*; the discard question's lines; a submodule line's badge and *Open submodule*.
- **Fix budget:** 0 cycles.

## Documentation sweep

- README *Features*: the new bullet.
