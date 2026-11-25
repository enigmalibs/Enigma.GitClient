# CODE-REVIEW-6E56-PHASE28 — [Medium] SOLID-02 Operation reporter

## Summary

The twelve `*Operations` services of the review each took the content dialogs and the info bar, two
of them the progress overlay too, and carried the same private helpers: a `Report` that forwards to
the info bar (with a tuple overload in two), and an identical `FirstLine` of git's error output in
nine.

- `IOperationFeedback` / `OperationFeedback` (Desktop service, new, a singleton) bundles what an
  operation tells and asks the reader:
  - `Dialogs`, the content dialogs;
  - `Overlay`, the progress overlay;
  - `Report(title, message, severity)` and `Report(note)`, to the info bar;
  - `OperationFeedback.FirstLine`, the shared first line of what git wrote.
- Every one of the twelve takes `IOperationFeedback` in place of the services it bundles:

| Operations | Before | After |
|---|---|---|
| Sync | overlay, info bar, dialogs | feedback |
| Submodule | overlay, info bar | feedback |
| Worktree, Checkout, Tag, Squash, Reset, Discard, Branch, Stash, Revert, Merge | dialogs, info bar | feedback |

- Their `_dialogs` and `_overlay` fields now come from the feedback, so the code that asks and shows
  progress is unchanged. Every `_infoBar.Notify(…)` and every call of the removed `Report` helper is
  `_feedback.Report(…)`.
- The duplicated helpers are gone: the generic `Report` in seven classes, its tuple overload in two,
  and the identical `FirstLine` in nine, which now call `OperationFeedback.FirstLine`.
- A script made the change, checking each constructor, documentation line, null check and field
  before rewriting it. Registered in `AddGitClientApp` beside the operations.

## Files/modules touched

- Created: `src/Enigma.GitClient.Desktop/Services/OperationFeedback.cs`
- Modified (`src/Enigma.GitClient.Desktop/Services/`): `SyncOperations.cs`, `WorktreeOperations.cs`,
  `SubmoduleOperations.cs`, `CheckoutOperations.cs`, `TagOperations.cs`, `SquashOperations.cs`,
  `ResetOperations.cs`, `DiscardOperations.cs`, `BranchOperations.cs`, `StashOperations.cs`,
  `RevertOperations.cs`, `MergeOperations.cs`
- Modified: `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`
- Created: `tests/Enigma.GitClient.Desktop.UnitTests/OperationFeedbackTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE28.md`

## Deviations & follow-ups

- One Core integration test failed once and passed on the rerun (see *Build/test evidence*). A
  follow-up for the suite's stability: capture failing test names in the run logs.

- **The overlay is bundled too.** The plan named the info bar and the dialogs. `SubmoduleOperations`
  takes no dialogs, so swapping its info bar alone would not shorten its constructor, against the
  acceptance criterion. Its overlay, and `SyncOperations'`, are operation feedback as much as the
  other two, so both classes lose a parameter.
- **Two `FirstLine`s stay.** `SubmoduleOperations` redacts its line and has its own fallback ("git
  could not read the submodules."). `MergeOperations` skips blank lines first. They are not copies, and
  merging them would change what those reports say.
- **"Confirm" is not a new helper.** The questions already go through the shared
  `ContentDialogServiceExtensions` (`ConfirmDestructiveAsync`, `ShowDestructiveAsync`), which the
  operations call on `feedback.Dialogs`. Nothing was duplicated to remove.
- `MergeOperations` keeps its own `Report(MergeOutcome, …)`, which words a merge's outcome; only its
  `Notify` calls moved to the feedback.
- No test built these services by hand, so none needed adapting.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration, first run: 449 run, 446 passed, **1 failed**, 2 skipped (6 min 11 s). Only the
    summary was captured, so the failing test is not named. A rerun with nothing changed: 449 run, 447
    passed, 0 failed, 2 skipped. The suite does not reference the Desktop project, the only one this
    phase changed, so the failure is transient and not caused here (no fix cycle).
  - Desktop: compiled, **not run** (this machine's standing rule).
- **Throwaway harness outside the repository.** It ran on the headless platform with the real
  container (`ValidateOnBuild`, `ValidateScopes`), a recording info bar and a dialog service that
  answers "yes", over a real repository. Every check passed:
  - the container validates, all twelve operations resolve, and there is one shared feedback;
  - deleting two tags asks through the feedback's dialogs, deletes them, and reports through the
    feedback ("Deleted 2 tags" — "v1, v2");
  - deleting a branch works;
  - deleting the checked-out branch is refused and reported ("Could not delete "main"" — ""main" is
    the branch you have checked out. Switch to another branch first.").
- **New tests (Desktop, compiled, not run)** in `OperationFeedbackTests`:
  - `FirstLine` (5 cases);
  - a report reaches the info bar, as three parts or as one note;
  - every one of the twelve takes the feedback and none of the three services it bundles.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes the services' wiring.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| What the feedback bundles | Dialogs, overlay, info bar, and the shared `FirstLine` | Every class loses a parameter; it is everything an operation shows the reader | Dialogs and info bar only (Submodule would lose nothing) |
| How the operations use the dialogs and overlay | Fields taken from the feedback | The asking and progress code is unchanged, and the diff stays on the wiring | Rewriting every `_dialogs.` call to `_feedback.Dialogs.` |
| The logger and the repository context the review also named | Left as they are | The plan's bundle is the feedback. A logger's category is per class, and the context is not feedback | An "operation runner" holding all four |
