# FEATURE-B14C — A wider minimap that finds the change

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-20-ui-polish-diff-page

## Objective

Make the diff's minimap wide enough to grab with a trackpad, and make a newly opened file start
where its first change is rather than wherever the last file was scrolled to.

## Context & constraints

- `Controls/Diff/DiffMinimap.cs` is a plain `Control` that draws a track, one rectangle per run of
  changed rows and a window over the visible rows; it raises `ScrollRequested` with a fraction and
  `Views/Panels/DiffViewerView.axaml.cs` answers it against the list's own `ScrollViewer`.
  `Themes/Controls.axaml` gives it `Width="14"` and a two-pixel side padding.
- It replaced the patch's vertical scrollbar (FEATURE-5EC4 PHASE02): the lists are
  `ScrollViewer.VerticalScrollBarVisibility="Hidden"`, so the map is the only thing left to drag.
  Fourteen pixels is a thin target for a trackpad.
- `DiffViewerViewModel.Apply` rebuilds both renderings' rows and both maps for every file, and
  resets the **sideways** panes (`DiffScrollState.Reset`). Nothing resets the vertical scroll: the
  `ListBox` keeps the offset it had, and where that lands in the next file depends on how long the
  last one was — which is the "a bit random" the request describes.
- The maps are already exactly what "where the first change is" means: `UnifiedMap` and
  `SideBySideMap` are runs in row order, the first of which is the hunk band that introduces the
  first change. A file with no change at all (a rename, a mode change) has no runs, and starts at
  the top.
- Rows are realised by a `VirtualizingStackPanel`, so a scroll target is an offset in the extent
  rather than a realised control; the view already converts between the two in `Report` and
  `ScrollTo`, in fractions of the extent.
- The viewer is shown in two places — the history page's diff view and the changes page — and both
  reach it through the same `DiffViewerView`.
- **Baseline:** clean build, 1728 tests green.

## PHASE01 — A minimap wide enough to grab

**Branch:** `feature/feature-b14c-phase01-wider-minimap`
**Status:** TODO

### Steps

1. `Themes/Controls.axaml`: the `DiffMinimap` theme's `Width` goes from 14 to 36 — about two and a
   half times, the step the request asks for — with the comment saying what the number is for.
2. `Controls/Diff/DiffMinimap.cs`: the side padding and the least-visible mark height are stated as
   a proportion of the strip rather than a constant where the constant no longer suits it, so the
   marks grow with the strip instead of becoming three hairlines in a wide gutter.
3. Same file: the viewport window keeps a visible outline at the new width.
4. Tests — `tests/.../DiffViewerTests.cs`: the theme's width is the new one; a map laid out at that
   width draws its marks across it; `StartFor` is unchanged by the width (it already works in
   fractions).

### Acceptance criteria

- The strip beside a patch is between two and three times as wide as it was, in both renderings and
  on both pages that show a diff.
- Its marks and its viewport window still read at the new width.
- Pointing anywhere on it still scrolls the patch to that place, and dragging still follows the
  pointer.
- Build clean with zero warnings; the App and Core unit suites green.

## PHASE02 — Opening at the first change

**Branch:** `feature/feature-b14c-phase02-open-at-first-change`
**Status:** TODO

### Steps

1. `ViewModels/Panels/DiffViewerViewModel.cs`: `UnifiedFirstChangeRow` and
   `SideBySideFirstChangeRow`, each the first row of the rendering's first run — 0 when the file has
   no run at all — and a `PatchChanged` event raised at the end of `Apply`, which is the one place a
   new patch reaches the screen.
2. `Views/Panels/DiffViewerView.axaml.cs`: on `PatchChanged`, both renderings are scrolled to their
   first change with two rows of context above it, using the same extent arithmetic the minimap
   already drives; the offset is posted after the rows are realised, so the extent it divides is the
   new patch's.
3. Same file: a patch with no changes — and a cleared viewer — goes to the top.
4. Tests — `tests/.../DiffViewerTests.cs`: the first-change row of each rendering for a patch whose
   change is in the middle of the file, for one whose change is the first line, and for one with no
   change at all; `tests/.../HistoryPageTests.cs`: showing a second file after scrolling the first
   one leaves the view at the second file's first change rather than where the first was left.

### Acceptance criteria

- Selecting a file in the changed-files list shows its diff scrolled to its first change, with a
  little context above it, whichever rendering is on.
- A file with no textual change — a rename, a mode change — opens at the top.
- The minimap's window agrees with where the patch actually is, immediately.
- Switching back and forth between two files lands on each one's first change every time, not on
  the offset the other left behind.
- Build clean with zero warnings; the App and Core unit suites green.

## Out of scope

- A minimap width preference, or a draggable divider for it.
- Drawing the code in the minimap the way an editor's does.
- Moving between changes with the keyboard, or a "next change" button.
- Remembering where a file was scrolled to when it is opened again.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How wide | 36 px | The request asks for two to three times 14; 36 is a comfortable trackpad target and still reads as a gutter rather than a pane | 28 (barely a change); 48 (starts competing with the patch for width) |
| Where the marks go at that width | Grown with the strip | Three 10 px marks in a 36 px gutter would look like a mistake | Keeping the 2 px inset and leaving the marks thin |
| What "the first change" is | The first run of the rendering's map | It is already computed, per rendering, and it is where the hunk band that introduces the change sits | Scanning the patch again in the view (a second definition of the same thing, in the wrong layer) |
| How the scroll is done | The extent fraction the minimap already uses | The rows are virtualised, so there is no control to bring into view; the view already owns this arithmetic | `ScrollIntoView` (realises the row at the bottom edge, which is the wrong end) |
| Context above the change | Two rows | Enough that the change does not sit on the very first pixel; little enough that it is still the first thing seen | None (the change touches the top edge); a screenful (the change is no longer where the eye lands) |
| Who says a new patch arrived | An event from the ViewModel, raised in `Apply` | `Apply` is the single place every path — a new file, a reload, a rendering switch, a clear — ends in | Watching the rows collection from the view (fires per row, and says nothing about which patch) |
