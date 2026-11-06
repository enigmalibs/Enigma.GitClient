# FEATURE-0A3D-PHASE02 — Red questions for every destructive one

**Item:** FEATURE-0A3D — Focus a dialog's first field or button
**Branch:** `feature/feature-0a3d-phase02-red-questions`
**Run:** feature/2026-11-06-dialog-focus-badge-menus

## Summary

Every question whose main button removes something or throws work away is now asked in red, with
Cancel as the default button. Since PHASE01 that means Cancel has the focus as the question opens, so
an Enter pressed by mistake cancels. Every other confirmation opens with its main button focused.

- `ContentDialogServiceExtensions.ShowDestructiveAsync(configure)` (new):
  - puts the red class on, runs `configure`, then sets `DefaultButton.Close` whatever `configure` said;
  - bounds a long string question's height;
  - takes the class and the height off again afterwards.

  `ConfirmDestructiveAsync` is built on it and takes an optional cancel label.
- **Now red, Cancel focused**, each keeping its texts:
  - delete a stash;
  - delete a profile; disconnect an account;
  - reset every preference;
  - abandon the merge;
  - the hard reset;
  - keep ours/theirs everywhere;
  - reset a branch to its remote, only when commits would be left behind. With nothing left behind
    it stays a plain question, its main button focused.
- **Remove worktree** goes through the shared question instead of adding and removing the class by
  hand.
- **Main button focused now** (they defaulted to Cancel): "Use your identity in this repository" and
  "Use <profile> in every repository".

## Files/modules touched

- Created: `docs/done/FEATURE-0A3D-PHASE02.md`
- Modified:
  - `src/Enigma.GitClient.Desktop/Services/ContentDialogServiceExtensions.cs`
  - `src/Enigma.GitClient.Desktop/Services/BranchOperations.cs`, `MergeOperations.cs`,
    `ResetOperations.cs`, `StashOperations.cs`, `WorktreeOperations.cs`
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/ConflictResolutionPageViewModel.cs`,
    `ProfilesPageViewModel.cs`, `RepositoriesPageViewModel.cs`, `SettingsPageViewModel.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/DestructiveConfirmationTests.cs`,
    `StashOperationsTests.cs`, `MergeOperationTests.cs`, `HistoryResetTests.cs`,
    `ResetLocalToHereTests.cs`, `ProfilesPageTests.cs`, `ProfileIntegrationsTests.cs`,
    `SettingsPageTests.cs`, `ConflictResolutionPageTests.cs`, `WorktreeRemoveTests.cs`,
    `RepositoriesPageTests.cs`
  - `docs/roadmap.md`, `docs/plan/FEATURE-0A3D.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How a call site becomes red | `ShowAsync` → `ShowDestructiveAsync`, the explicit `DefaultButton.Close` line dropped | Each question keeps its shape, its texts and its own cancel label ("Keep it", "Keep going", "Keep them") |
| A red question asking for another default | The helper enforces Close | The rule is "red → Cancel first", whatever the caller wrote |
| The reset to the remote | One `Ask` configuration, shown plain or red depending on what is left behind | The red is exactly when a stray Enter would lose commits |
| Left as they were | Informational dialogs with only a close button (About, commit details, host repositories, worktree folder missing); the error report (Close; its token reset asks its own red question); the detach warning (`Secondary`: "Create a branch here…" is its recommendation) | None is a plain confirmation whose main button was not the default |

## Deviations & follow-ups

- None from the plan.
- The test infrastructure on this machine is unchanged: see `docs/done/FEATURE-0A3D-PHASE01.md`.

## Build/test evidence

- **Baseline:** see `docs/done/FEATURE-0A3D-PHASE01.md`.
- **This dev**, `dotnet build Enigma.GitClient.slnx`: **0 warnings, 0 errors**.
- `dotnet test --solution Enigma.GitClient.slnx`: 2215 run, 2177 passed, 34 failed, 4 skipped. Every
  failure is in the baseline's list; **none is new**.
- **Test bodies, per class.** Run on scratch copies of the baseline and of this dev, with only the
  copies' test cleanup patched to clear read-only attributes; the repository is untouched.
  - Classes: `DestructiveConfirmationTests`, `StashOperationsTests`, `MergeOperationTests`,
    `HistoryResetTests`, `ResetLocalToHereTests`, `ProfilesPageTests`, `ProfileIntegrationsTests`,
    `SettingsPageTests`, `ConflictResolutionPageTests`, `WorktreeRemoveTests`,
    `WorktreesDialogTests`, `RepositoriesPageTests`, `HistoryDiscardTests`, `WorkingTreePanelTests`,
    `RemotesAndSyncTests`, `BranchesPageTests`, `TokenResetTests`, `DialogFocusTests`.
  - Result: 528 tests; the same 11 failures on both, none new.
  - Those 11 are environmental:
    - 9 compare a file's content and get CRLF endings (`core.autocrlf=true` here);
    - 1 `FormatException` in the font list;
    - 1 DPAPI token test.
  - The edited tests that fail there do so on the same content comparison as in the baseline, after
    the new assertions.
- **Tests added or extended:**
  - `DestructiveConfirmationTests` (+3): the cancel label is kept; a red question's default is Cancel
    whatever it asks for, and its secondary answer comes back; a long red question is bounded, then
    the height is given back.
  - A red-while-asked check, Cancel as the default and the class gone afterwards, for each converted
    question: stash, merge, hard reset, reset to the remote (red only with commits left behind),
    profile, account, preferences, keep everywhere, worktree.
  - The two identity questions now expect `Primary`.
- **Fix budget:** 0 cycles.

## Documentation sweep

- README: line 106 ("every discard confirms with a red button") and line 109 (the force push's red
  warning) stay true; nothing else describes these questions. No edit.
