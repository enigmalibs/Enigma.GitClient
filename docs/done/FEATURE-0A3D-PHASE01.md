# FEATURE-0A3D-PHASE01 — The focus rule on every dialog host

**Item:** FEATURE-0A3D — Focus a dialog's first field or button
**Branch:** `feature/feature-0a3d-phase01-dialog-focus`
**Run:** feature/2026-11-06-dialog-focus-badge-menus

## Summary

Every dialog now opens with the keyboard focus inside it, where the next keystroke belongs. Before, the
focus stayed on whatever opened it (a toolbar button, a menu item), so Enter and Escape never reached
the dialog. Once the dialog is laid out, the focus goes to:

1. nowhere new, when a view has already taken it (the squash dialog puts the caret on the message's
   title);
2. else the first text field that can be typed in, in layout order; read-only boxes are skipped;
3. else a button:
   - a red question (the `danger` class): Cancel;
   - otherwise the dialog's `DefaultButton`, then the first shown and enabled of Primary, Secondary
     and Close.

The focus is given as Tab gives it:
- a filled single-line field has its text selected (rename keeps its select-all);
- a button shows its focus ring.

Enter answers with the focused button, and Tab goes on to the next button. When the dialog closes, the
focus goes back to where it was, the tool dialog included when a question opened over it.

- `Views/DialogFocus.cs` (new): `Attach(host)` watches the host's `IsOpen`. Its focus pass is
  `MoveFocusIn`, with the pieces `FirstTextField` and `ButtonToFocus`.
- `MainWindow` attaches `HostDialog` and `ToolDialog`; `StartWindow` attaches `HostDialog`.
- `CreateTagDialogView` and `RenameBranchDialogView` lose their own focus code, which the rule now
  does. `SquashDialogView` keeps its own.

## Files/modules touched

- Created:
  - `src/Enigma.GitClient.Desktop/Views/DialogFocus.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/DialogFocusTests.cs`
  - `docs/done/FEATURE-0A3D-PHASE01.md`
- Modified:
  - `src/Enigma.GitClient.Desktop/Views/MainWindow.axaml.cs`, `Views/StartWindow.axaml.cs`
  - `src/Enigma.GitClient.Desktop/Views/Dialogs/CreateTagDialogView.axaml.cs`,
    `RenameBranchDialogView.axaml.cs`, `SquashDialogView.axaml.cs` (comment only)
  - `tests/Enigma.GitClient.Desktop.UnitTests/TagsAndCheckoutTests.cs`, `HistoryRenameBranchTests.cs`:
    their bare hosts are attached
  - `docs/roadmap.md`, `docs/plan/FEATURE-0A3D.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| When the pass runs | After the host's next layout, posted at `Background` priority | Before layout, the view is not in the tree and the buttons may not exist. At `Background`, the views' own `Loaded` work (squash) has run first |
| Where to return on close | The focused element at opening, or at the pass if it is outside the dialog and in the same window | A menu item that opened the dialog is in a popup that has closed by then. The menu has given the focus back to what had it before, so that is noted at the pass |
| A focus already moved elsewhere at close | Left alone | Another dialog or window took it; not ours to move |
| A view's own focus | Respected when the focus is inside the dialog, not on one of its own buttons | A previous question may have left the focus on a button of the same host |

## Deviations & follow-ups

- **Test infrastructure on this machine (pre-existing, not touched):**
  - The Desktop test host crashes after about 395 of its 1825 tests, in the baseline as now. Exit code
    `0xE0434352`: an `ObjectDisposedException` from `RepositoryContext.EndWrite`, in a checkout still
    running after its test's cleanup failed.
  - That cleanup fails on Windows because `TestServices.Dispose` cannot delete git's read-only object
    files (`UnauthorizedAccessException`, 592 of the 601 failures of a per-class run).
  - The fix BUG-6EAA carried was abandoned at your request; making `TestServices.Dispose` clear
    read-only attributes, as the integration tests' `DirectoryCleanup` does, is a follow-up.
- The library could honour `DefaultButton` itself (a change to `Enigma.Avalonia.Desktop`): follow-up.

## Build/test evidence

- **Baseline**, the run branch at `7355811`, taken once for the whole run:
  - `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
  - `dotnet test --solution Enigma.GitClient.slnx`: 2215 run (the Desktop host crashes, see above),
    2176 passed, 35 failed, 4 skipped. All 35 are environmental:
    - 33 cleanup `UnauthorizedAccessException`s, in `HistoryDragMergeTests` (5), `MenuIconTests` (4),
      `StashOperationsTests` (9) and `WorktreeRemoveTests` (15);
    - `ProfileSelectionTests.SelectingTheSameProfileAgainChangesNothing`: a file in use;
    - `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`: DPAPI.
- **This dev**, `dotnet build`: **0 warnings, 0 errors**.
- `dotnet test`: 2215 run, 2176 passed, 35 failed, 4 skipped. The 35 failures are the baseline's own;
  **none is new**.
- **Test bodies, per class.** Run on scratch copies of the baseline and of this dev, with only the
  copies' test cleanup patched to clear read-only attributes; the repository is untouched.
  - Classes: `TagsAndCheckoutTests`, `HistoryRenameBranchTests`, `DestructiveConfirmationTests`,
    `DialogQuestionWrapTests`, `ToolDialogTests`, `AppWindowsTests`, `MainWindowShellTests`,
    `HistorySquashTests`, `SquashDialogTests`, `AboutDialogTests`, `CommitDetailsDialogTests`,
    `HostRepositoriesDialogTests`, `TokenResetTests`.
  - Result: the same on both. All pass but
    `TokenResetTests.ConnectingAgain_AfterThePasswordReset_OffersTheReset_ThenStoresTheTypedToken`
    (DPAPI, fails in both).
  - `DialogFocusTests`: **19/19 passed**.
- **New tests** (`DialogFocusTests`, 19):
  - a form focuses its first field; a read-only box is skipped; only a read-only box → Close;
  - a confirmation focuses its main button, Enter confirms, Tab goes to Cancel;
  - each `DefaultButton` value; a message's only button; a disabled main button passes the focus on;
  - a red question focuses Cancel and a stray Enter cancels, whatever its `DefaultButton` says;
  - the squash dialog keeps the caret on the message;
  - closing gives the focus back; a question over a tool dialog gives it back to the tool dialog;
  - attaching twice; a host not attached; both windows attach their hosts.
- **Fix budget:** 1 cycle (a test helper looked for a closed host's button on the other host).

## Documentation sweep

- README: nothing says how a dialog takes the focus; no edit.
