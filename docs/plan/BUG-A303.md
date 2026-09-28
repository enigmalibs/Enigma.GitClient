# BUG-A303 — File line menus only open on the text

**Status:** DONE — see `docs/done/BUG-A303.md`
**Type:** BUG
**Branch:** `bugfix/bug-a303-whole-line-file-menus`
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Objective

In the changed-files lists — the Changes page's *Not staged* and *Staged* lists and the History diff
view's file list — a file line's context menu opens only when the right-click lands on the line's
text. It must open wherever the line is right-clicked: its padding above, below and beside the
columns, its right end, and — in tree mode — its indentation.

## Context & constraints

- All three lists are `Views/Panels/ChangedFilesPanelView.axaml`, as a `ListBox` (list mode) or a
  `TreeView` (tree mode) over one row template, `ChangedFileRow`: a `Grid` with the row's
  `ContextMenu`, painted transparent since FEATURE-5860 PHASE01 so the gaps *between* its columns hit.
- The row grid is the container's **content**: it sits inside the `ListBoxItem`'s padding, and inside
  the `TreeViewItem`'s header with its level indentation and chevron. A right-click there hits the
  container, which carries no menu, so nothing opens — which reads as "only over the text".
  FEATURE-5860's test checked only a gap between two columns, inside the grid.
- A `ContextMenu` on a control opens from that control's `ContextRequested` and handles the event, so a
  request that reaches the panel unhandled came from outside every row grid.

## Steps

1. Confirm the diagnosis with a headless test that right-clicks a file line in its container's padding
   (list mode) and in its indentation (tree mode).
2. `ChangedFilesPanelView.axaml.cs`: a `ContextRequested` handler on the panel. A request that no row
   opened a menu for, raised inside a `ListBoxItem`/`TreeViewItem` whose data is a file line, opens
   that line's own row menu at the pointer and is handled. The row template keeps its menu, so the
   request from the grid itself is unchanged.
3. Headless tests, on the Changes page (both lists) and on the History diff view: a right-click in a
   line's padding, at its right end and between its columns opens that line's menu; in tree mode, a
   right-click in a nested line's indentation opens that line's menu, not its folder's.

## Acceptance criteria

- Right-clicking anywhere on a file line — text, padding, right end, indentation — opens that line's
  menu, in list and in tree mode, on the Changes page and in the History diffs.
- The menu is the one it was (same items, same commands).
- Build clean with zero warnings; the whole suite green.

## Out of scope

- The menu's items; other lists (they were fixed by FEATURE-5860 PHASE01).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How to cover the container's own area | Route an unhandled `ContextRequested` from a line's container to its row menu | Covers the list padding and the tree's indentation and chevron alike, keeps one menu definition, no restyling | Zero container padding with a padded border in the template (misses the tree's indentation, changes the look); a `ContextMenu` setter per container (a second menu definition) |
| Which lists | The one panel, so both pages at once | The Changes lists and the History diff list are the same control | Separate fixes per page |
