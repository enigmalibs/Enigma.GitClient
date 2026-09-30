# BUG-6787-PHASE02 — Selecting and copying with the pointer

**Item:** BUG-6787 — Diff text cannot be selected
**Branch:** `bugfix/bug-6787-phase02-pointer-selection`
**Run:** feature/2026-09-30-profile-lists-readme-diffs

## Summary

The diff's text can now be selected with the pointer and copied, for committed and uncommitted files
alike (one viewer serves both). It works in both panes of the side-by-side rendering and in the
unified one. It stays read-only: no caret, nothing accepts typing.

- **Press.** A left press on a line's text column (not its numbers or marker) starts an empty
  selection in that pane. Shift+press extends the current one instead. The press is not handled, so
  the list still selects the row as before, and a plain click clears the text selection.
- **Drag.** Past the drag threshold (the house `BranchDragGesture.IsDrag`), the pointer is captured
  by the list and the selection follows it: to the row at the pointer's height (the nearest realised
  row above or below the list), and the character under it in the selection's own pane. A drag that
  wanders over the other side keeps selecting the side it started in.
- **Edges.** Held near the list's top or bottom, the list scrolls (the house `ScrollFor` and a
  30 ms timer) and the selection extends to the rows coming into view.
- **Copy.** Ctrl+C (a `KeyBinding` on the view) and a new "Copy" item on both lists' context menu run
  `DiffViewerViewModel.CopyCommand`. It copies the selected text when there is some, and otherwise
  the selected rows (the existing `CopySelectionCommand`, which nothing reached until now). Its
  enabled state follows both selections.
- Release or a lost capture ends the gesture, and so does the view leaving the visual tree.

## Files / modules touched

**Modified — App**

- `Views/Panels/DiffViewerView.axaml.cs` — the tunnelled press, move and release handlers, capture,
  `ExtendSelection`, `RowNearest`, `LineAt` / `LineIn`, the auto-scroll, `IsSelectingText`; the class
  comment says why the view has this code
- `Views/Panels/DiffViewerView.axaml` — the Ctrl+C `KeyBinding`; a "Copy" context menu (with the copy
  glyph and the gesture shown) on each list
- `ViewModels/Panels/DiffViewerViewModel.cs` — `CopyCommand` and `CopyAsync`

**Created — tests**

- `DiffTextSelectingTests.cs` (headless pointer and keyboard):
  - a drag across the old file selects its lines and Ctrl+C copies them;
  - the new file selects on its own even when the drag crosses to the other side;
  - the unified rendering selects the same way;
  - a click clears and Shift+click extends;
  - a press on the line numbers selects nothing;
  - both lists carry the "Copy" item, bound to `CopyCommand` once open, copying the text or else the
    rows;
  - holding the selection below a long list scrolls it and extends the selection

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/BUG-6787.md` (the phase and the item are `DONE`)

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Which area starts a selection | The pane's text column, from the line's left edge; not the gutter or the marker | A press on the numbers is how a row is picked, as before. A press anywhere right of the text (the line's stretched width) is still in the text column |
| The row selection | Left as it is, and still made by the same press | It is what "Copy" falls back to, and the gutter bar shows it |
| Where the drag's row comes from | The realised row at the pointer's height, else the nearest one | With the pointer captured the event source says nothing about where the pointer is. Rows off screen are reached by the auto-scroll |
| A drag over the other pane | Keeps the starting pane, on the row under the pointer | Two files are never mixed. The column clamps to that pane's line |
| One context menu or two | One inline menu per list | A menu takes its DataContext from the control it opens on. A shared resource on two controls is a subtlety not worth having |
| A drag ending left of a line's text | Ends at its start, so the copied text ends with a newline | What text editors do; the reader dragged into that line |

## Deviations & follow-ups

- **None from the plan.**
- Out of scope, as planned: keyboard selection (Shift+arrows), Ctrl+A, double-click word selection,
  and the conflict page's panes.
- The README does not describe the diff's copying, and nothing in it became wrong, so the sweep changed
  nothing. A release note could mention it at the next release.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2620 passed, 0 failed, 0 skipped (+7). The new
  pointer classes (this one and `RepositoryReorderTests`) were run three more times: stable.
- One fix cycle: the menu test read `MenuItem.Command` before the menu had ever opened. A context
  menu takes its DataContext on opening, so the test now opens it on the visible list before
  asserting.
