# FEATURE-5EC4 — A minimap scrollbar for diffs

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-18-columns-selection-minimap

## Objective

Replace the diff viewer's vertical scrollbar with a minimap: a narrow strip beside the patch that
draws where the additions and the removals are, shows which part of the file is on screen, and
scrolls the patch when it is clicked or dragged.

## Context & constraints

- The viewer draws one of two `ListBox`es — `UnifiedRows` or `SideBySideRows` — each a
  `DiffRowViewModel` per row, built in `DiffViewerViewModel.Apply`. The two renderings have
  different row counts (side by side pairs a removal with its addition, and pads with fillers), so a
  map belongs to a rendering, not to the patch.
- Everything about the reader's horizontal position already lives in `DiffRenderOptions` /
  `DiffScrollState`, split as model (columns, viewport, offset) versus view (the pixels those mean).
  The vertical map follows the same split: the ViewModel says which rows changed, the control says
  where that lands in pixels.
- The horizontal bars are deliberately outside the lists (`Views/Panels/DiffViewerView.axaml`)
  because a scrolling `ListBox` would drag its line-number gutter with it. The minimap is the
  vertical axis and has no such constraint — it docks beside the list and drives the list's own
  `ScrollViewer`.
- A patch can hold thousands of rows and a minimap is a couple of hundred pixels tall, so drawing
  one tick per line is both invisible and wasteful: consecutive rows of the same kind are merged
  into runs, and a run is drawn at a minimum height so a one-line change is still findable.
- `Themes/Graph.axaml` already carries `DiffAddedMarkerColor` / `DiffRemovedMarkerColor` per theme
  variant, which are the colours the gutter markers use — the map reuses them so the two agree.
- The dialog that hosts the viewer disables its card's own scrolling
  (`HistoryPageView.OnDialogBodyAttached`), so the list's `ScrollViewer` is the one scroll the
  minimap has to talk to.
- `IsBusy`, the truncation band and the empty-patch message are all existing states the map must
  survive: no rows means no marks and nothing to draw.
- **Baseline:** `Core.IntegrationTests.Sync.SyncServiceTests.FetchAsync_ReportsItsProgress` fails on
  the branch this run started from, for an environment reason unrelated to this item (git 2.55
  narrates nothing for a fast local fetch). The gate for every phase is the App and Core unit suites
  green and no new failure anywhere.

## PHASE01 — The change map and its control

**Branch:** `feature/feature-5ec4-phase01-change-map`
**Status:** DONE — see `docs/done/FEATURE-5EC4-PHASE01.md`

### Steps

1. New `Controls/Diff/DiffChangeMark.cs` (or a record in the minimap's file): a run of rows of one
   kind — first row, row count, and whether it was added, removed or is a hunk band.
2. `ViewModels/Panels/DiffViewerViewModel.cs`: `UnifiedMap` and `SideBySideMap` — the merged runs of
   each rendering, built in `Apply` beside the rows, plus `MapRowCount` per rendering so a fraction
   can be worked out. Empty when there is no patch.
3. New `Controls/Diff/DiffMinimap.cs`: a `Control` with `Marks`, `RowCount`, `ViewportStart` and
   `ViewportEnd` (both fractions of the whole), drawing each run as a bar of at least two pixels at
   its proportional position, and the viewport as a translucent window with a border. A press or a
   drag moves the window's centre to the pointer and raises `ScrollRequested` with the new start
   fraction; the control never scrolls anything itself.
4. `Themes/Controls.axaml` / `Themes/Graph.axaml`: the map's own brushes — the added and removed
   marker colours it shares with the gutter, a quiet track, and the viewport window's fill and
   border, per theme variant.
5. Tests — `tests/.../DiffViewerTests.cs`: the marks of a patch with two separated hunks are two
   runs of the right kinds and positions; consecutive added lines merge into one run; a cleared
   viewer has none. Control tests: a map with marks renders something other than an empty frame, a
   press at the bottom asks to scroll near the end, and a drag past either edge clamps to 0 and 1.

### Acceptance criteria

- A patch's map has one run per stretch of same-kind lines, in row order, with the right kind.
- The map is empty for a cleared viewer, a binary file and a patch with no lines.
- The control draws its marks and its viewport window and raises one `ScrollRequested` per gesture
  step, clamped to [0, 1].
- Nothing about the control knows what a `ListBox` is.
- Build clean with zero warnings; the App and Core unit suites green.

## PHASE02 — The minimap replaces the diff's scrollbar

**Branch:** `feature/feature-5ec4-phase02-minimap-scrollbar`
**Status:** TODO

### Steps

1. `Views/Panels/DiffViewerView.axaml`: both diff `ListBox`es get
   `ScrollViewer.VerticalScrollBarVisibility="Hidden"` — hidden, not disabled, so the wheel and the
   keyboard still scroll — and a `DiffMinimap` is docked to the right of each rendering, bound to
   that rendering's map.
2. `Views/Panels/DiffViewerView.axaml.cs`: finds the `ScrollViewer` inside the rendering on screen,
   pushes `Offset` / `Extent` / `Viewport` into the minimap's viewport fractions as the reader
   scrolls, and answers `ScrollRequested` by setting the scroll viewer's offset. Detaches its
   handlers when the view goes away, and copes with a rendering that has not been realised yet.
3. Same file: switching rendering re-binds to the list that is now on screen, since only one of the
   two is realised at a time.
4. Tests — `tests/.../DiffViewerTests.cs`: both lists report a hidden vertical scrollbar; scrolling
   the list moves the minimap's window; asking the minimap to scroll moves the list; the map and the
   list agree after a switch between unified and side by side.

### Acceptance criteria

- Neither diff rendering shows a vertical scrollbar, and both still scroll by wheel and keyboard.
- The minimap's window tracks the visible part of the patch as it is scrolled.
- Clicking or dragging the minimap scrolls the patch to that part of the file.
- Switching rendering, loading another file and clearing the viewer all leave the map consistent
  with what is on screen.
- Build clean with zero warnings; the App and Core unit suites green.

## Out of scope

- A minimap for the history list, the changed-files panel or the conflict-resolution page.
- Rendering the file's text in miniature, à la an editor minimap — the map shows changes, not code.
- Word-level marks inside a line, or a mark for whitespace-only changes.
- Making the map's width, colours or position a preference.
- Any change to the horizontal scrollbars, which stay as they are.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the map is computed | In the ViewModel, per rendering, beside the rows | The two renderings have different row counts, and a map built from rows is testable without pixels | Computing it in the control from the item source (drags layout into a test); one map for the patch (wrong row indices for one of the two renderings) |
| Ticks or runs | Merged runs, with a minimum drawn height | Thousands of one-pixel ticks are invisible and slow; a run is what a reader actually looks for | One rectangle per row |
| What the control knows | `Marks`, `RowCount`, viewport fractions; it raises a request | Keeps the control reusable and testable, and keeps the scroll plumbing in the one place that already owns it | Handing the control the `ScrollViewer` or the `ListBox` |
| Hidden or disabled scrollbar | Hidden | Disabled would take the wheel and the keyboard with it; the request replaces the bar, not the scrolling | `Disabled`; leaving the bar beside the map |
| Colours | The gutter's own added/removed marker colours | The map and the diff must not disagree about what green means, and both follow the theme variant | New colours of its own |
| Where the map sits | Docked right, one per rendering | It replaces a right-hand scrollbar, which is where the hand already is | A single shared map (only one rendering is realised at a time, so it would have to be re-bound anyway — and per-rendering binding is what the XAML already does for everything else) |
| An empty patch | An empty map, drawn as a bare track | The viewer already shows its message over the list; a map with nothing in it must not throw or draw noise | Hiding the map (the panel would jump in width as files change) |
