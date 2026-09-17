# FEATURE-2288-PHASE02 — Reopen it from the row menu

**Item:** FEATURE-2288 — Diffs in a dialog, not a panel (PHASE02)
**Branch:** `feature/feature-2288-phase02-row-menu`
**Run:** feature/2026-09-17-graph-space-diff-dialog

## Summary

"Show what it changed" now sits at the top of a line's context menu, above "Create branch here…".

`PHASE01` opens the dialog from a selection *change*, which leaves one gap: close the dialog and the
same line is still selected, so nothing announces it again. A gesture cannot fill that gap — any rule
that opens the dialog on a tap hands the second click of a double-click to the modal, and
double-clicking a line is how this client checks it out. A menu entry conflicts with nothing and is
the more discoverable half of the feature besides.

The command is deliberately not a second way to open the dialog. On a row that is not the selected
one it simply selects it and lets `SelectedRow`'s setter do the opening, so there is still exactly one
place that decides what "a line is selected" means; only when the row *is* already selected does it
set `IsDiffDialogOpen` itself, which is the case the entry exists for. It is offered on every row,
including the uncommitted-changes one, where the dialog shows the working tree's own diff.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/CommitRowViewModel.cs` — `HistoryRowCommands` carries `ShowChanges`, a
  `RelayCommand<CommitRowViewModel>`
- `ViewModels/Pages/HistoryPageViewModel.cs` — `OnShowChanges`, and the command's registration
- `Views/Pages/HistoryPageView.axaml` — the menu entry and the separator under it

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — two cases: the entry brings the dialog
  back for the row that is already selected without moving the selection, and on another row it
  selects it and opens the dialog there

**Modified — docs**

- `RELEASENOTES.md` — the graph's dialog line names the menu entry that brings it back
- `docs/roadmap.md`, `docs/plan/FEATURE-2288.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the entry sits in the menu | First, with a separator under it | It is what a reader wants most often from a line, and it is a read rather than a write, so it belongs above the group that changes the repository |
| Whether the command opens the dialog itself in every case | Only when the row is already selected | Otherwise it selects the row and lets its setter open the dialog, which keeps one rule for what a selection means instead of two |
| Which object the tests run the command from | The page's `RowCommands` | A row's own `Commands` is nullable, and a test asserting behaviour should not be asserting that |

## Deviations & follow-ups

- **None from the plan.** All four acceptance criteria are covered.
- **Follow-up.** With the entry in place, the only route a reader can no longer take is
  double-clicking a line that is *not* yet selected, whose first click now opens the dialog. Checkout
  stays available by double-clicking the selected line, and from "Check out this commit" and "Check
  out <branch>" in the same menu.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build
  0 Warning(s)
  0 Error(s)

dotnet test
  Test run summary: Passed!
  total: 1637  failed: 0  succeeded: 1637  skipped: 0
```

Two tests are new. One build fix during implementation, in the tests: a row's `Commands` is nullable,
so the cases go through the page's own `RowCommands`.

## Documentation sweep

Scanned the README and the release notes. The README lists features without naming the menus they are
reached from, which this dev leaves true. The release notes' line about the dialog — written by
`PHASE01` — described only how it closes, so it now also names what brings it back.
