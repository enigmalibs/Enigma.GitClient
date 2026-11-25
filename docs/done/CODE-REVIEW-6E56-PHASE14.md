# CODE-REVIEW-6E56-PHASE14 — [Medium] PERF-03 Validation off UI

## Summary

The clone and create dialogs worked out their validation in bound getters. `ValidationMessage`
probed the disk (`Directory.Exists`, and for a clone `EnumerateFileSystemEntries`), and
`HasValidationMessage` and `IsValid` each called it again. One keystroke cost three synchronous disk
probes on the UI thread, which is a visible stall on a network share or a sleeping disk. A target that
existed but could not be listed threw `UnauthorizedAccessException` out of a getter.

What changed:

- `DebouncedProbe<T>` (Desktop, new, internal) runs a disk check on the thread pool 250 ms after the
  last change. The next change replaces it.
  - The answer comes back on the UI thread, unless a newer change has replaced it.
  - `FlushAsync` skips the rest of the delay and waits for the answer.
  - The delay runs on a `TimeProvider`.
- `InitRepositoryDialogViewModel` and `CloneRepositoryDialogViewModel` store `ValidationMessage` and
  `IsValid`. Each change of a field they depend on works them out once, then raises
  `ValidationMessage`, `HasValidationMessage`, `IsValid`, the target path and `ValidationChanged`.
  - The field checks (URL, empty fields, invalid characters, branch name) stay synchronous and are
    said at once. A field problem drops the pending disk check.
  - When the fields are usable, the dialog is not confirmable and says nothing until the disk
    answers. A message about the previous target never lingers on the new one.
  - The clone check catches `IOException` and `UnauthorizedAccessException` and says "'…' exists but
    cannot be read."
  - `ValidateNowAsync()` makes the validation final.
  - An internal constructor takes the clock and the disk check, for the tests.
- `RepositoriesPageViewModel`'s create and clone confirm paths await `ValidateNowAsync()` before
  they trust `IsValid`, so a confirm in the same moment as the last keystroke cannot skip the check.

## Files/modules touched

- Created: `src/Enigma.GitClient.Desktop/ViewModels/Dialogs/DebouncedProbe.cs`
- Created: `tests/Enigma.GitClient.Desktop.UnitTests/DialogTargetValidationTests.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Dialogs/InitRepositoryDialogViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Dialogs/CloneRepositoryDialogViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/RepositoriesPageViewModel.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/RepositoriesPageTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE14.md`

## Deviations & follow-ups

- **The init dialog's disk check moved too, though it cannot throw.** It only calls
  `Directory.Exists`, which never throws, so it has no catch. It still runs off the UI thread with
  the same delay, for the stall the review describes.
- **The clone's URL setter fills in the suggested directory name before the check**, not after. The
  fields are then checked as they stand, and the dialog never says "Give the new directory a name"
  for the moment between the two assignments.
- **A disk check that a change replaces runs to its end**, and its answer is dropped. `Directory.*`
  calls cannot be cancelled. The plan's "cancelled by the next change" holds for the delay and the
  answer, not for a probe already running.
- Four existing tests read `IsValid` right after setting a field. They now await
  `ValidateNowAsync()` (`RepositoriesPageTests`: the URL and suggested name, a folder with content,
  the branch name, a folder that is already a repository). Their assertions are unchanged.
  `InitDialog_ValidatesTheBranchName` also asserts that a field problem is said without waiting.
- The page-flow tests that fill a dialog in `OnShown` and confirm at once (`FillInitDialog`, the clone
  tests) depend on the confirm path's flush: without it, they would create nothing.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 12 s).
  - Desktop: compiled, **not run** (this machine's standing rule).
- **Throwaway harness outside the repository** (named after the test assembly). It drives both
  ViewModels on a single-threaded synchronization context that stands in for the UI thread, with a
  hand-moved clock and the real Core `IRepositoryService`. Every check passed:
  - three keystrokes probe nothing before the delay, nor at 249 ms;
  - at 250 ms exactly one probe runs, of the last target, on another thread. Its answer comes back
    on the UI thread;
  - the flush runs a pending probe at once, and probes nothing when nothing is pending;
  - a field problem drops the pending probe;
  - the real init check: "already a git repository", and valid on a fresh folder;
  - the real clone check:
    - a missing folder is valid;
    - a folder with content gives "not empty";
    - a change clears the stale message and blocks confirming until the answer;
    - an empty folder is valid;
    - a bad URL is said at once;
    - a folder whose listing is denied by ACL gives "exists but cannot be read".
- **New tests (Desktop, compiled, not run)** in `DialogTargetValidationTests` (5):
  - keystrokes touch no disk until the delay, then one probe checks the last target, and the next
    change probes once more;
  - a field problem is said at once and runs no probe;
  - while the disk is checked the dialog cannot be confirmed and says nothing stale, with one
    `ValidationChanged` per change and one per answer;
  - a target that cannot be listed gives a message. Its listing is denied by ACL on Windows or by
    mode `000` on Unix; the test is skipped for an account that can list anything;
  - confirmed at once over a folder that is already a repository, the create dialog still waits for
    the check and creates nothing.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes how the dialogs validate.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the delay and the off-thread run live | One internal `DebouncedProbe<T>` shared by both dialogs | The same mechanism twice is the duplication the review series removes elsewhere | A copy in each ViewModel; a public service in the container (no other user) |
| What the dialog says while the disk is checked | Nothing, and it cannot be confirmed | A message names a target. Keeping the last one says something false about the new target for 250 ms | Keeping the previous message; a "Checking…" line that flickers on every keystroke |
| How tests reach the clock and the check | An internal constructor (`InternalsVisibleTo` already exists) | The public constructor and its callers stay as they were | A new `IFileSystem` abstraction (new seam for two checks); public parameters |
| The confirm path | `await ValidateNowAsync()`, then `IsValid` | A dialog confirmed in the same moment as the last change waits for the check. Skipping the delay means it does not wait 250 ms | Trusting the button's enabled state; waiting the delay out |
| A folder that cannot be listed | Said as "'…' exists but cannot be read." and not confirmable | Cloning into it would fail anyway. Saying so beats an exception from a getter | Treating it as empty (the clone then fails late) |
