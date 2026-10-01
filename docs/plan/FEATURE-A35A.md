# FEATURE-A35A — A selected file lets go on a click

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-01-polish-release-5-1

## Objective

A click on the selected line of the history already deselects it, which closes the details panel.
The file lists in that panel should behave the same way, both a commit's files and the uncommitted
line's *Not staged* and *Staged* lists: a click on the selected file lets go of it. Since the selected
file is what the diff over the graph shows, letting go of it closes that diff and gives the graph
back. Closing the diff already lets go of the file.

## Context & constraints

- **The history's toggle** (FEATURE-5261) is the model to follow, in `HistoryPageView.axaml.cs`:
  - tunnelled `PointerPressed`/`PointerReleased` handlers on the page;
  - a plain left press (`ClickCount == 1`, no modifiers) on the line that is already selected records
    a pending toggle;
  - a release on the same line that has not become a drag posts the let-go, so the list has finished
    with the gesture before the selection moves;
  - `HistoryPageViewModel.ToggleSelection(row)` does the deselecting;
  - tests: `ALine_IsTheDetailsPanelsToggle`, `ADoubleClick_NeverLeavesThePanelClosed` (a double-click
    on the selected line ends selected), `ARightClickOnTheSelectedLine_LeavesItSelected`.
- **One view for all three lists:** `ChangedFilesPanelView` (a `ListBox` and a `TreeView` bound to
  `ChangedFilesPanelViewModel.SelectedNode`) is the commit's files (`HistoryPageView`, `DetailsFiles`)
  and both halves of the working tree (`WorkingTreePanelView`, `UnstagedFiles`/`StagedFiles`).
- **The diff follows the selection:**
  - `HistoryPageViewModel.OnFileSelectionChanged` opens the diff for a commit's file;
  - `OnWorkingTreeSelectionChanged` opens it for a working-tree change;
  - `IsDiffViewOpen = false` clears both selections.
  - A selection that merely becomes `null` must not close the diff. Rebuilding the list (a filter, a
    refresh, `Nodes.Clear()`) passes through `null` and selects the path again. Closing therefore
    follows the explicit let-go only.
- **What a click must not toggle:**
  - the row's own action button (*Stage*, *Unstage*);
  - a tree item's chevron;
  - a right-click (the menu);
  - a Ctrl/Shift click;
  - the second press of a double-click;
  - a press released on another line.

## PHASE01 — The commit's files are toggles

**Branch:** `feature/feature-a35a-phase01-commit-files-toggle`
**Status:** TODO

### Steps

1. `ChangedFilesPanelViewModel`:
   - `ToggleSelection(ChangedFileNodeViewModel node)`: the selected node lets go, any other takes the
     selection;
   - a `SelectionReleased` event raised only when a toggle let go.
2. `ChangedFilesPanelView`:
   - tunnelled press/release handlers, as the history's: a plain left press on the selected line
     (list item or tree item) records it, unless the press is inside a `Button` (row action, tree
     chevron);
   - a release on the same line without a drag posts the toggle;
   - the drag threshold is the one the history uses (`BranchDragGesture.IsDrag`).
3. `HistoryPageViewModel`: `Files.SelectionReleased` closes the diff (`IsDiffViewOpen = false`), so the
   graph comes back and the line stays selected with its panel.
4. Tests (headless, on the real history page):
   - a click on the selected file of a commit lets go of it, the diff closes, the line and the panel
     stay. The next click selects it again and reopens the diff;
   - a double-click on the selected file leaves it selected;
   - a right-click on the selected file leaves it selected;
   - a click on another file moves the selection and the diff stays open on it;
   - in the tree view, the same toggle works on a file, and the chevron of a selected folder only
     expands or collapses it;
   - `ToggleSelection` unit behaviour: it raises `SelectionReleased` only on a let-go, and a rebuild
     through `null` raises nothing.

### Acceptance criteria

- In a commit's file list (list and tree), a plain click on the selected file deselects it and
  closes its diff. Any other click behaves as before.
- Filtering or refreshing the list never closes the diff.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — The working tree's files too

**Branch:** `feature/feature-a35a-phase02-working-tree-toggle`
**Status:** TODO

### Steps

1. `WorkingTreePanelViewModel`: a `SelectionReleased` event raised when *Not staged* or *Staged* lets
   go of its file by a toggle.
2. `HistoryPageViewModel`: `WorkingTree.SelectionReleased` closes the diff, as for a commit's files.
3. Tests (headless, on the uncommitted line):
   - a click on the selected *Not staged* file lets go of it and the diff closes; the uncommitted line
     and its panel stay;
   - the same for a *Staged* file;
   - the row's *Stage* / *Unstage* button on the selected file stages or unstages it and does not
     toggle;
   - picking a file in the other half moves the selection there (unchanged).

### Acceptance criteria

- On the uncommitted line, a plain click on the selected file of either list deselects it and closes
  its diff; the row buttons and every other click behave as before.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Keyboard deselection (Space, Ctrl+click semantics). The history lines' toggle is pointer-only
  too.
- Multi-selection in the file lists.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| What deselecting a file does to its diff | Closes it: the graph comes back, the line and the panel stay | Closing the diff already lets go of the file. The reverse keeps "diff open ⇔ a file picked", with no empty diff over the graph | Leaving an empty diff open |
| What closes the diff | The explicit let-go only, through an event | The selection passes through `null` on every rebuild. Closing on `null` would close the diff on a filter or a refresh | Closing whenever `SelectedFile` becomes `null` |
| Where the gesture lives | `ChangedFilesPanelView`, once, for all three lists | It is the one view all three use | A handler in each page |
| When it fires | On a plain left release on the same line, posted | The same gesture as the history lines, and the posting is what keeps the list from handing the selection back | On the press |
| Folders in the tree | They toggle too; the chevron never does | "The same behaviour". The chevron is a button | Files only |
| Phasing | Two phases: a commit's files, then the working tree | One reviewable commit each. PHASE01 already makes all three lists deselect, and PHASE02 makes the working tree close the diff | One dev |
