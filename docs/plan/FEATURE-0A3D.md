# FEATURE-0A3D — Focus a dialog's first field or button

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-11-06-dialog-focus-badge-menus

## Objective

Every content dialog opens with the keyboard focus where the reader's next keystroke belongs:

- a dialog that asks for text (create a branch, a tag, a worktree, a remote, clone, a profile…) puts
  the caret in its first text field;
- a confirmation puts the focus on its main button, so Enter confirms it and Tab goes on to the next
  buttons (Cancel…);
- a question whose red button removes or throws work away puts the focus on Cancel, so an Enter
  pressed by mistake right away cancels.

## Context & constraints

- **The library does not move the focus.** `Enigma.Avalonia.Desktop` 1.3.0's `ContentDialog` sets
  `IsOpen` and nothing else; its `DefaultButton` is documented as "which button is styled as the
  default (keyboard-focused) action" but is not acted on. The focus stays on whatever opened the dialog
  — a toolbar button, a menu item — behind it, so Enter and Escape do not reach the dialog at all.
- **The template** (`Themes/Controls/ContentDialog.axaml` in the library): `PART_PrimaryButton`,
  `PART_SecondaryButton`, `PART_CloseButton`, in that order, each hidden when its text is empty. An
  Avalonia `Button` clicks on Enter when focused; Tab follows the template's order.
- **Hosts:** `MainWindow` has `HostDialog` (questions) and `ToolDialog` (the branches, tags, remotes and
  worktrees pages, `ToolDialogService`); `StartWindow` has `HostDialog`. A question can open on
  `HostDialog` over an open tool dialog.
- **Per-view focus today:** `CreateTagDialogView` and `RenameBranchDialogView` focus their name box
  (rename also selects it) on `Loaded` + `Dispatcher.Post`; `SquashDialogView` deliberately focuses the
  message box (not the branch name above it), caret at 0.
- **Red questions:** `ContentDialogServiceExtensions.ConfirmDestructiveAsync` (adds the `danger` class,
  `DefaultButton.Close`, takes the class off afterwards) and `WorktreeOperations` remove (the same by
  hand). Eight other questions that throw work away are not red but default to Close:
  `StashOperations.DropAsync`, `ProfilesPageViewModel` delete profile and disconnect account,
  `SettingsPageViewModel` reset preferences, `MergeOperations.AbortAsync`, `ResetOperations` hard reset,
  `BranchOperations` reset to the remote when commits would be left behind, and
  `ConflictResolutionPageViewModel.ConfirmAsync` (keep ours/theirs everywhere).
- **House precedent for a view helper attached from code-behind:** `LineMenus.Attach(this)`,
  `InfoBarPlacement.Place(...)` in the window constructors.
- Tests: headless (`HeadlessAvaloniaFixture`, the real `App` and its styles); a bare `ContentDialog` in
  a `Window` with a real `ContentDialogService` is the existing pattern
  (`TagsAndCheckoutTests.CreateTagDialog_OpensWithTheCaretInTheName`).

## PHASE01 — The focus rule on every dialog host

**Branch:** `feature/feature-0a3d-phase01-dialog-focus`
**Status:** TODO

### Steps

1. `src/Enigma.GitClient.Desktop/Views/DialogFocus.cs` — `internal static class DialogFocus`:
   - `Attach(ContentDialog host)`: idempotent; listens to the host's `IsOpen`.
   - On open: remember the element that has the focus (unless it is inside the host); once the host
     has been laid out (its next `LayoutUpdated`), post a focus pass at `DispatcherPriority.Background`,
     so the views' own `Loaded` focus (squash) has run first.
   - The focus pass, when the host is still open:
     1. the focus is already inside the dialog's content (a view took it) → leave it;
     2. else the first `TextBox` of the content, in visual order, that is effectively visible, effectively
        enabled, focusable and not read-only → `Focus(NavigationMethod.Tab)` (a filled single-line box
        gets its text selected, as when tabbed into);
     3. else a button: the `danger` class → `PART_CloseButton` only; otherwise the `DefaultButton`'s part,
        then Primary, Secondary, Close — the first that is shown and enabled →
        `Focus(NavigationMethod.Tab)` (its focus ring shows which button Enter presses).
   - On close: give the focus back to the remembered element when the focus is inside the host (or
     nowhere) and the element is still attached, visible, enabled and focusable; otherwise clear the
     focus if it is still on the hidden dialog. Forget the element.
   - `internal` entry points (`FocusInside(host)`) so tests can run the pass without timing.
2. `MainWindow` constructor: `DialogFocus.Attach(HostDialog)`, `DialogFocus.Attach(ToolDialog)`;
   `StartWindow`: `DialogFocus.Attach(HostDialog)`.
3. Remove the now-redundant own focus code of `CreateTagDialogView` and `RenameBranchDialogView` (the
   rule does the same: first field, text selected). `SquashDialogView` keeps its own (rule 1).
4. Existing focus tests (`TagsAndCheckoutTests.CreateTagDialog_OpensWithTheCaretInTheName`,
   `HistoryRenameBranchTests` rename focus) attach `DialogFocus` to their bare host.
5. Tests (`tests/Enigma.GitClient.Desktop.UnitTests/DialogFocusTests.cs`, headless, a real host and
   `ContentDialogService`, an opener button focused first):
   - a form (create branch view) → its name box is focused, the opener is not;
   - a read-only box first (the unhandled-error view) is skipped → the Close button;
   - a plain confirmation (`DefaultButton.Primary`) → the primary button; Enter on it answers `Primary`;
   - `DefaultButton.None` with a primary → the primary; with only a Close button → Close;
   - `DefaultButton.Secondary` → the secondary;
   - `ConfirmDestructiveAsync` → the Cancel button, not the red one; Enter on it answers not-confirmed;
   - a disabled primary → the next shown button;
   - the squash view keeps the focus on its message box;
   - on close, the opener has the focus back;
   - a question over an open tool dialog: the question's button has the focus, then the tool dialog's
     element again when it closes;
   - `Attach` twice does not focus twice / throw.
6. Documentation sweep (§7).

### Acceptance criteria

- Every dialog opened on a window's hosts focuses its first editable text field, else its default
  button; a red question focuses Cancel.
- Enter on the focused button answers with that button; Tab goes to the next button.
- Closing a dialog gives the focus back to where it was.
- The squash dialog still opens with the caret on the message's title.
- The build has zero warnings; the whole suite is green against the baseline.

## PHASE02 — Red questions for every destructive one

**Branch:** `feature/feature-0a3d-phase02-red-questions`
**Status:** TODO

### Steps

1. `ContentDialogServiceExtensions`:
   - `ShowDestructiveAsync(Action<ContentDialog> configure)`: the `danger` class on, `configure`, then
     `DefaultButton.Close` (enforced), the long-question height when the content is a long string; the
     class and the height given back in `finally`. Returns the `DialogResult`.
   - `ConfirmDestructiveAsync(title, message, confirmText, cancelText = "Cancel")` built on it.
2. Red, Cancel focused (each keeps its texts, its cancel label included):
   - `StashOperations.DropAsync` (Delete / Cancel);
   - `ProfilesPageViewModel` delete profile (Delete / Keep it) and disconnect account
     (Disconnect / Keep it);
   - `SettingsPageViewModel` reset every preference (Reset / Keep them);
   - `MergeOperations.AbortAsync` (Abandon / Keep going);
   - `ResetOperations` hard reset (Reset and discard / Cancel);
   - `BranchOperations` reset to the remote, only when commits would be left behind (it keeps its
     secondary "Check out" button: `ShowDestructiveAsync`); with nothing left behind it stays a plain
     question, its main button focused;
   - `ConflictResolutionPageViewModel.ConfirmAsync` (keep ours/theirs everywhere: Continue / Cancel).
3. `WorktreeOperations` remove: `ShowDestructiveAsync` instead of adding and removing the class by hand.
4. Plain confirmations that defaulted to Close → `DefaultButton.Primary`: `RepositoriesPageViewModel`
   "Use your identity in this repository" and "Use <profile> in every repository".
5. Left as they are, with the reason in the done doc: informational dialogs with only Close (About,
   commit details, host repositories, worktree folder missing), the unhandled-error report (Close; its
   token reset asks its own red question), the detach warning (`DefaultButton.Secondary`: "Create a
   branch here…" is the dialog's recommendation).
6. Tests: update the assertions on these questions' `DefaultButton`; assert, while each converted
   question is asked (`ScriptedContentDialogService.OnShown`), that it carries the `danger` class and
   `DefaultButton.Close`, and that the class is gone afterwards; the reset to the remote is red only with
   commits left behind; the two identity questions default to Primary; `ShowDestructiveAsync` enforces
   Close even when `configure` set Primary.
7. Documentation sweep (§7).

### Acceptance criteria

- Every question that removes something or throws work away has a red confirm button and opens with
  Cancel focused.
- Every other confirmation opens with its main button focused.
- No question's texts change.
- The build has zero warnings; the whole suite is green against the baseline.

## Out of scope

- Enter in a text field submitting the dialog (`IsDefault`): not asked.
- Trapping Tab inside the dialog (cycling from the last button back to the first).
- Changing the library (`Enigma.Avalonia.Desktop`) to honour `DefaultButton` itself — a library change;
  noted as a follow-up.
- A release item.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the focus logic lives | One `DialogFocus.Attach(host)` helper, attached from the window constructors | One place for every dialog, string questions included; the house's `LineMenus.Attach` / `InfoBarPlacement` precedent | Focus code in every dialog view (duplicated, misses string questions); a global style setting an attached property (no attached-property precedent in the house); changing the library |
| Which button | Red → Close; else the dialog's `DefaultButton`, then Primary, Secondary, Close, the first shown and enabled | `DefaultButton` is the library's declared "keyboard-focused action", already set per question by the code | Ignoring `DefaultButton` and always focusing Primary: the detach warning's recommendation and the error report's Close would be lost |
| Non-red questions that throw work away (8) | Made red, Cancel focused (PHASE02) | The request names red remove questions as the only Cancel-first case, and the house rule is already "throws work away → red"; focusing their main button would let a stray Enter delete a stash or reset a branch | Main button focused (unsafe); keep them non-red with Cancel (breaks the stated rule) |
| What a "text field" is | The first `TextBox` in visual order: visible, enabled, focusable, not read-only | Covers every form; a read-only box (error details) is not an input | Any focusable control (a combo box first would win over the name) |
| How the focus is given | `NavigationMethod.Tab` | As if tabbed in: a prefilled single-line field is selected (rename keeps its behaviour), a button shows its focus ring | `Unspecified`: no ring, no selection |
| A view that focuses itself | Respected when the focus is already inside the content | The squash dialog deliberately puts the caret on the message's title | Overriding it with the branch name |
| On close | The focus goes back to the element that had it, when it still can take it | Otherwise the keyboard focus is left on a hidden button after every dialog (worse than today) | Leaving the focus where it is |
| Tool dialogs | Included | They are content dialogs; their filter box takes the focus (type to filter), Escape now closes them | Excluding the tool host |
| Phasing | Two phases: the rule, then aligning the questions | Each a reviewable commit; the rule stands on its own | One phase |
