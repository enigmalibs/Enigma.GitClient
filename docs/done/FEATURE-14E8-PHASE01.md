# FEATURE-14E8-PHASE01 — Open the diffs on demand

**Item:** FEATURE-14E8 — History: diffs, badges and dragging
**Branch:** `feature/feature-14e8-phase01-diffs-on-demand`
**Run:** feature/2026-09-18-history-and-diffs

## Summary

Selecting a history line now only selects it. What it changed is shown when it is asked for — by
double-clicking the row, or through the row's own menu — and checking anything out is the menu's
job alone.

Three small moves. `HistoryPageViewModel.SelectedRow`'s setter no longer opens the dialog; it still
closes one when the selection is cleared, because a reload after a checkout drops the selection and
a dialog describing a commit nobody has selected is describing nothing. `OnShowChanges` became the
one door into the dialog: it selects the row when it is not already selected and then opens, so the
menu entry and the double-click reach it by the same route. And `OnActivateAsync` — which used to
check out the row's branch, or detach onto its commit — became `OnActivate`, which shows the
changes; the uncommitted pseudo-row keeps its own meaning, because there is nothing there to compare
against a parent and the page that acts on that work is the working directory.

`HistoryRowCommands.Activate` is a `RelayCommand<CommitRowViewModel>` rather than an
`AsyncRelayCommand`: nothing it does is asynchronous once the checkout leaves it, and a command that
returns a completed task the moment it is invoked misstates its own contract to every caller.

Checking out lost nothing: `Check out this commit (detaches HEAD)` and `Check out "<branch>"` were
already in the row's menu and are untouched.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/HistoryPageViewModel.cs` — the selection setter no longer opens the dialog and
  closes it only when the selection is cleared; `OnShowChanges` selects-then-opens; `OnActivateAsync`
  → `OnActivate`, showing the changes instead of checking out; a stray duplicated doc comment left
  over from the previous item removed
- `ViewModels/Pages/CommitRowViewModel.cs` — `Activate` is a `RelayCommand`, and the `Activate` /
  `ShowChanges` parameter docs say what the two now mean
- `Views/Pages/HistoryPageView.axaml.cs` — the double-tap handler's remark stops saying "checks out"

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` —
  `DiffDialog_StaysClosedUntilACommitIsSelected` became
  `DiffDialog_StaysClosedWhenARowIsMerelySelected` and now asserts both halves (a selection opens
  nothing, a request opens it); a new `DiffDialog_OpensOnADoubleClick`; the remaining dialog tests
  open it the way a user does
- `tests/Enigma.GitClient.App.UnitTests/TagsAndCheckoutTests.cs` — the two activation tests became
  `History_ChecksOutARowsBranchFromItsMenu` and
  `History_ChecksOutABranchlessRowFromItsMenuAndDetaches`, plus a new
  `History_ActivatingARowChecksNothingOut` that pins the behaviour this phase exists for
- `tests/Enigma.GitClient.App.UnitTests/ChangesPageTests.cs` — the uncommitted row's activation runs
  synchronously
- `tests/Enigma.GitClient.App.UnitTests/ChangedFilesPanelTests.cs`,
  `tests/Enigma.GitClient.App.UnitTests/DiffViewerTests.cs` — the two render tests that needed the
  dialog on screen ask for it instead of selecting a row and relying on the old side effect

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-14E8.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What `ShowChanges` does when the row is already selected | Opens, rather than returning early | It used to lean on the selection setter to open the dialog for a new row; with that gone, one path has to both select and open or the menu does nothing for an unselected row |
| Whether a selection change closes an open dialog | Only when the selection is cleared | The dialog is modal, so the list cannot be clicked while it is up; the case that remains is a reload dropping the selection under it |
| The two failing render tests | Pointed at the menu command | They rendered the panels by selecting a row, which was the old side effect; asking for the dialog is what a user now does and is what the test should say |
| `InputGesture="Double-click"` on the menu entry | Dropped | `MenuItem.InputGesture` takes a `KeyGesture`; a pointer gesture is not one, and a string that does not parse is a runtime failure for a label |

## Deviations & follow-ups

- **None from the plan.** All six acceptance criteria are covered; the plan's step 5 (renaming the
  menu entry) turned out to be unnecessary — the entry already read "Show what it changed", which is
  exactly right now that it is no longer a fallback.
- **Follow-up.** `CommitRowViewModel.CanCheckoutBranch` and `HasBranch` are still what the menu's
  checkout entries are gated on, and are untouched; nothing became dead.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1641  failed: 0  succeeded: 1641  skipped: 0
```

Two tests are new (`DiffDialog_OpensOnADoubleClick`,
`History_ActivatingARowChecksNothingOut`). One fix cycle was needed: two render tests drew their
panels by way of the selection's old side effect and had to ask for the dialog instead.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`. The README's feature list says "One-click checkout of
anything in the graph, including a detached commit" — a sentence this phase made untrue, since
checkout is now reached through the row's menu. Corrected to name the menu. Nothing else in either
file describes how the history opens a diff. There is no `CLAUDE.md`, `CHANGELOG.md` or
`CONTRIBUTING.md` in the repository.
