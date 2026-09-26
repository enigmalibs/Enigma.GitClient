# FEATURE-5EC4-PHASE01 — The change map and its control

**Item:** FEATURE-5EC4 — A minimap scrollbar for diffs
**Branch:** `feature/feature-5ec4-phase01-change-map`
**Run:** feature/2026-09-18-columns-selection-minimap

## Summary

The pieces a minimap is made of: the map itself, and the control that draws it.

The map is computed in the ViewModel, beside the rows and at the same moment, as **runs** of
consecutive rows of one kind — added, removed, or a hunk band. One per rendering, because the two
renderings are not the same rows: side by side pairs a removal with the addition that replaced it
and pads the shorter side with fillers, so a run's indices mean nothing in the other one. Context
lines produce no run at all, which matters more than it sounds: side by side asks git for the whole
file, so a map that marked context would be one solid bar with the changes invisible inside it.

`DiffMinimap` draws those runs and a window over the part of the patch on screen, and knows nothing
about lists or scroll viewers. It is given the runs, the row count and the viewport as two
fractions, and it raises `ScrollRequested` with where the viewport should start — the pointer taken
as the *middle* of the window, because pointing at a change is asking to look at it, not to put it
on the first visible line. Whoever owns the scrolling answers that, which is PHASE02's job.

It is not an editor's minimap: it draws the changes, not the code, and a run is drawn at a minimum
of two pixels so a one-line change in a four-thousand-line file is still findable.

## Files / modules touched

**Added — App**

- `Controls/Diff/DiffMinimap.cs` — `DiffMarkKind`, the `DiffChangeMark` run, and the control: its
  ten styled properties, `StartFor` (the pointer-to-start arithmetic, static and testable),
  `Render`, and the press/drag/release handlers that raise `ScrollRequested`

**Modified — App**

- `ViewModels/Panels/DiffViewerViewModel.cs` — `UnifiedMap` and `SideBySideMap`, built in `Apply`
  by `BuildMap` from `KindOf`
- `Themes/Controls.axaml` — the `DiffMinimap` control theme: 14 px wide, the gutter's brush for its
  strip, the gutter's own added and removed marker brushes for its runs
- `Themes/Graph.axaml` — `DiffMinimapViewportColor` and `DiffMinimapViewportBorderColor` per theme
  variant, and their brushes

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/DiffViewerTests.cs` — a "the change map" section: the runs
  of the sample patch with their kinds and positions, both renderings mapped and a side-by-side
  change reading as an addition, an empty map after `Clear`, twelve consecutive additions merging
  into one run, the `StartFor` theory including both edges and a patch that fits on screen, a press
  at the bottom asking for the end of the patch, a rendered map holding its track, its marks and its
  window, and an empty map that draws nothing without throwing

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-5EC4.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Context lines on the map | Not marked | Side by side shows the whole file, so marking context would draw one solid bar and hide the changes inside it |
| A side-by-side row that is both | Marked as an addition | One colour per row, and the reader is looking at the file as it will be |
| Hunk bands on the map | Marked, in the band's own foreground colour | It is where a change begins, and it is what a reader aims at when the change itself is one line |
| Where the pointer lands | The middle of the window | Pointing at a change asks to read it; starting the view there would put it on the top line with its context off screen |
| The strip's colour | The gutter's | The map is the strip beside the text, which is what a gutter is; a colour of its own would be a third thing to keep in step with the theme |
| The window over the visible rows | A translucent wash with a one-pixel outline | The marks have to read through it, or the map stops being a map exactly where the reader is |
| The whole patch on screen | No window drawn | A window around everything says nothing and only dims the marks |
| `StartFor` as a static | Yes | It is the whole of the arithmetic, and a theory over six cases is worth more than a pointer-driven test of the same thing |

## Deviations & follow-ups

- **None from the plan.** All five acceptance criteria are covered; nothing in the control names a
  `ListBox` or a `ScrollViewer`.
- The rendered snapshot `diff-minimap.png` was inspected: the band, the removals and the additions
  are drawn in order and the window washes the rows it covers.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1706  failed: 0  succeeded: 1706  skipped: 0
```

Thirteen tests added (six of them the theory's cases). No fix cycle was needed; the one change after
the first green run was cosmetic — the strip took the gutter's brush instead of a colour of its own,
after the snapshot showed a track indistinguishable from the page behind it.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`. Neither describes the diff's scrollbars, and nothing user
facing changes until PHASE02 puts the map on screen — the control exists but no view uses it yet.
There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository. Nothing edited.
