# FEATURE-A35A-PHASE01 — The commit's files are toggles

**Item:** FEATURE-A35A — A selected file lets go on a click
**Phase:** PHASE01 — The commit's files are toggles
**Branch:** `feature/feature-a35a-phase01-commit-files-toggle`
**Run:** feature/2026-10-01-polish-release-5-1

## Summary

In the history's details panel, a click on the selected file of a commit now lets go of it, as a
click on the selected line of the history does. Its diff over the graph goes with it. The line stays
selected and its panel stays open, and the next click picks the file and opens its diff again.

- **`ChangedFilesPanelViewModel`:**
  - `ToggleSelection(node)`: the selected line lets go, any other takes the selection;
  - `SelectionReleased`: raised only by that let-go, after the selection is cleared.

  It is separate from `SelectionChanged` going to nothing, which also happens on every rebuild (a
  filter, a refresh, list ↔ tree) on the way back to the same path.
- **`ChangedFilesPanelView`:** the history lines' gesture, once, for the list and the tree.
  - Tunnelled press, move and release handlers.
  - A plain left press (`ClickCount == 1`, no modifiers) on the line that is already selected is
    recorded. A press inside a `Button` before the line's container is never recorded: the row's
    *Stage*/*Unstage*, a folder's chevron.
  - A press that travels past `BranchDragGesture.IsDrag` is dropped.
  - A release over the same line, found by hit-testing the release point rather than its source,
    since the list may hold the pointer, posts the let-go so the list has finished with the gesture
    first.
  - `ContainerOf` is shared with the existing context-request handler.
- **`HistoryPageViewModel`:** `Files.SelectionReleased` sets `IsDiffViewOpen = false`. The graph comes
  back, and the line and the panel stay.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Panels/ChangedFilesPanelViewModel.cs`: `ToggleSelection`,
  `SelectionReleased`.
- `src/Enigma.GitClient.App/Views/Panels/ChangedFilesPanelView.axaml.cs`: the toggle gesture;
  `LineAt`/`ContainerOf`.
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs`: the release closes the diff.
- `docs/roadmap.md`, `docs/plan/FEATURE-A35A.md`: statuses.

**Created**

- `tests/Enigma.GitClient.App.UnitTests/FileListToggleTests.cs`: 8 tests. Seven are headless, on the
  real history page, over a commit adding `docs/guide.md` and `docs/notes.md`:
  - the selected file lets go on a click, the diff closes, and the line and the panel stay; the next
    click brings both back;
  - another file takes the selection and the diff stays open on it;
  - a double-click on the selected file leaves it selected;
  - a right-click and a Shift click leave it selected;
  - a press released on another file does not let go;
  - in the tree, a file and a folder let go, and a folder's chevron only folds it;
  - filtering the list (a rebuild through no selection) never closes the diff.

  The eighth tests `ToggleSelection` on its own: it releases only on a second click on the same line,
  and a rebuild raises nothing.
- `docs/done/FEATURE-A35A-PHASE01.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How a press on a button is recognised | Any `Button` between the press's source and the line's container | `ToggleButton` derives from `Button`, so the tree's chevron and the row's action are both covered, without naming the template's parts |
| The drag threshold | `BranchDragGesture.IsDrag` (4 px), the history's own | "The same behaviour", one number |
| Where the release is looked for | Hit-testing the release point | The list and the tree can capture the pointer on the press, so the release's source is not where it landed. The history does the same (`RowUnder`) |
| Proving the tests | Ran the class with the release handler unhooked | The two positive tests failed (the file stayed selected). The guard tests passed both ways, as they should |

## Deviations & follow-ups

- **None from the plan.**
- **Between the phases:** the uncommitted line's two lists use the same view, so a click on their
  selected file already lets go of it. Its diff is cleared but stays open until PHASE02 makes the
  working tree close it.
- Line endings: no CRLF churn.

## Documentation sweep

`README.md`'s features list does not go down to how a file is picked. The history's toggle went into
the 5.0.0 notes the same way. The change goes into the 5.1.0 notes with FEATURE-0C53. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2635 passed**, 0 failed, 0 skipped (8 new).
- Fix budget: 0 cycles used. One compile error on the first build (a missing
  `Enigma.GitClient.Core.Diff` using in the new test file), fixed before the first test run.
