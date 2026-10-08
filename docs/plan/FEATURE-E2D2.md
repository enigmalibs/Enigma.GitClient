# FEATURE-E2D2 — Expand all / collapse all in the tree

**Status:** DONE — see `docs/done/FEATURE-E2D2.md`
**Type:** FEATURE
**Branch:** `feature/feature-e2d2-expand-collapse-all`
**Run:** vibe/2026-10-08-file-panel-about-release

## Objective

While the files panel shows a tree, its header offers two more buttons beside the list/tree toggles:
**Expand all** opens every folder, and **Collapse all** closes every folder.

## Context & constraints

- The header of `ChangedFilesPanelView` holds the summary on the left, and two segment toggles on the
  right ("Show the files as a flat list" / "…as a tree").
- The same panel serves a commit's files and both working-tree halves. Each panel gets its own
  buttons.
- A folder's open/closed state lives on `ChangedFileNodeViewModel.IsExpanded`.
  - `RememberExpansion` keeps the state the reader left across a rebuild (a refresh, the filter, the
    toggle).
  - Folders set by these buttons are kept the same way.
- The tree opens itself only up to `AutoExpandLimit` files (500 by default), to keep a huge commit
  cheap. *Expand all* is an explicit request and opens everything.
- After BUG-1B14, a folder can never hold the selection, so closing the folder around the selected
  file cannot move the selection onto the folder.

## Steps

1. `ChangedFilesPanelViewModel`: `ExpandAllCommand` and `CollapseAllCommand`.
   - Each sets `IsExpanded` on every folder row, nested ones included.
   - Each can run only in tree mode, with at least one folder shown.
   - The can-execute state is re-evaluated on every rebuild and view-mode change.
2. `ChangedFilesPanelView.axaml`: two `toolbar` buttons before the segment toggles, visible only in
   tree mode.
   - Icons `CaretDoubleDown` (expand all) and `CaretDoubleUp` (collapse all).
   - Tooltips and automation names "Expand all folders" and "Collapse all folders".
3. Tests:
   - `ChangedFilesPanelTests`:
     - expand all opens every folder, including past the auto-expand limit;
     - collapse all closes every folder;
     - both are disabled in list mode and when no folder is shown;
     - the state survives a refresh of the same change;
     - the selected file stays selected through a collapse.
   - A repository-free headless view test:
     - the buttons are visible only in tree mode;
     - a click on *Collapse all* closes the tree's folders and keeps the selected file.

## Acceptance criteria

- In tree mode the header shows *Expand all* and *Collapse all*; in list mode it does not.
- *Expand all* opens every folder; *Collapse all* closes every folder.
- The selected file stays selected, and its diff stays open, through either.
- The state is kept across a refresh of the same change.
- Build clean with zero warnings; the affected suites are green.

## Out of scope

- Remembering the state across commits, or in the settings.
- A keyboard shortcut.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Placement | Before the list/tree toggles, in the same header | Where you asked; the toggles stay at the far right where they are now | A second header row |
| Icons | `CaretDoubleDown` / `CaretDoubleUp` | They echo the tree's own carets; `ArrowsOut/InLineVertical` already mean "more context" in the diff toolbar | `PlusSquare` / `MinusSquare`; `FolderPlus` / `FolderMinus` |
| Hidden or disabled in list mode | Hidden | You asked for them only when the tree is chosen | Disabled |
| Enabled with no folder | Disabled | Nothing to open or close | Always enabled |
| *Expand all* on a huge change | Opens everything, whatever the auto-expand limit | An explicit request; the limit governs only what the tree does on its own | Capping it at the limit |
| Which panels | Every files panel: a commit's and both working-tree halves | One control | The commit's files only |
