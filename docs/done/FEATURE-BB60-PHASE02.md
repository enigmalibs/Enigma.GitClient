# FEATURE-BB60-PHASE02 — The unified diff on the editor

**Item:** FEATURE-BB60 — Diffs on AvaloniaEdit
**Branch:** `feature/feature-bb60-phase02-unified-editor`
**Run:** feature/2026-10-05-diff-view-avaloniaedit

## Summary

The unified rendering is now one read-only `DiffTextEditor` with its minimap beside it. The
`ListBox` of templated rows, its sideways bar and its scroll state are gone. Side by side is still the
list until PHASE03.

- **Selecting.** It is the editor's own: drag, double-click a word, triple-click a line, Shift+arrows,
  Ctrl+A. The selection is mirrored into the viewer's `DiffTextSelection` (row = line − 1, column =
  character index).
- **Copying.** Ctrl+C — the platform's copy gesture — and the *Copy* menu run the viewer's
  `CopyCommand`, so what is copied is the selected code without bands, fillers or markers, exactly as
  before.
- **Searching.** Ctrl+F opens AvaloniaEdit's search panel. Its replace cannot change anything: the
  document is read-only.
- **Bands.** A press on a hunk band widens the context, as the band button did. Over a band the
  pointer is a hand and the tooltip says "Show more of the file around this change". A press on a line
  starts a selection.
- **Scrolling.** Sideways, the editor scrolls itself and the gutter stays put: the numbers are a
  margin, outside the scrolling. Vertically, the bar is hidden, and the minimap follows and drives the
  editor's scroll viewer. A file opens with the row two above its first change at the top, to the
  pixel.
- **The rows.** `UnifiedRows` / `SideBySideRows` are now `IReadOnlyList`s, replaced once per patch
  with one notification, so the editor builds one document per patch rather than one per added row.

## Files / modules touched

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/DiffUnifiedEditorTests.cs` — 7 tests:
  - a band press widens the context, with the tooltip over the band and none off it;
  - a press on a line widens nothing;
  - Ctrl+F opens the search, whose replace leaves the text alone;
  - a long line scrolls sideways under a gutter that does not move;
  - a file opens at its first change;
  - a 5000-line patch lays out only the lines on screen;
  - a new patch takes the old selection away.
- `docs/done/FEATURE-BB60-PHASE02.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Controls/Diff/DiffTextEditor.cs`:
  - `TextSelection`, `CopyCommand`, `RowAt`, `ScrollHost`, `ScrollToRow`;
  - the band press and hover;
  - the selection mirror;
  - the copy gesture.
- `src/Enigma.GitClient.Desktop/ViewModels/Panels/DiffViewerViewModel.cs`:
  - the row lists are replaced whole;
  - `DiffRenderOptions.UnifiedScroll` is gone.
- `src/Enigma.GitClient.Desktop/Views/Panels/DiffViewerView.axaml`:
  - the unified grid is the editor and the minimap;
  - the unified row template, `UnifiedList` and `UnifiedBar` are gone.
- `src/Enigma.GitClient.Desktop/Views/Panels/DiffViewerView.axaml.cs`:
  - the minimap and the first-change opening for an editor;
  - the wheel, viewport and pointer-selection code now serve only the side-by-side list.
- `tests/Enigma.GitClient.Desktop.UnitTests/DiffViewerTests.cs`:
  - the scrollable harness finds the editor's scroll viewer;
  - the rendering and word-highlight tests read the unified editor;
  - the `UnifiedScroll` assertions are gone.
- `tests/Enigma.GitClient.Desktop.UnitTests/DiffTextSelectingTests.cs` — the unified test drags
  across the editor and copies with Ctrl+C.
- `tests/Enigma.GitClient.Desktop.UnitTests/DiffTextSelectionTests.cs` — `IReadOnlyList` rows.
- `docs/roadmap.md`, `docs/plan/FEATURE-BB60.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The mirror property's name | `TextSelection`, not `Selection` | `TextArea.Selection` is the editor's own; two `Selection`s one level apart would read as one |
| How the editor scrolls | Through its template's `ScrollViewer` (`ScrollHost`, `ScrollToRow`) | A probe showed `TextEditor.ScrollToVerticalOffset` / `ScrollToHorizontalOffset` leave the view where it is in AvaloniaEdit 12, while the viewer's `Offset` moves it |
| Where the minimap reads the scroll | The editor's scroll viewer, as it read the list's | The text area is a logical scrollable counted in pixels: offset, extent and viewport mean what they meant, so `Report` / `ScrollTo` are unchanged |
| The copy gesture | A tunnelling key handler matching `PlatformSettings.HotkeyConfiguration.Copy`, claimed whenever `CopyCommand` is set | The text area copies on the bubble. Claiming it first keeps the copy the viewer's; with nothing selected, the editor's copy would copy nothing too |
| A press on a band whose context cannot widen | Left to the editor | The command refuses it (side by side reads the whole file), so the band is plain text there, as the disabled button was |
| Clearing on an empty selection | Only when the shared selection is this pane's | A click in one pane says nothing about the other's selection; this is what PHASE03's two editors rely on |

## Deviations & follow-ups

- The plan said `Selection`; the property is `TextSelection` (see above).
- The row selection (`Selection`, `CopySelectionCommand`) is still there for the side-by-side list.
  It goes in PHASE03 as planned.
- `HistoryDragMergeTests.DroppingOnARemoteBranchOrOnItself_OffersNothing` failed once in one full
  run and passed in isolation and in the next full run. It is timing-sensitive under load and
  untouched by this dev.
- Line endings: no CRLF churn.

## Documentation sweep

The README's diff lines stay true: colour-coded, word-level, unified or side by side, a minimap.
No edit. Search and syntax highlighting go in once both renderings have them.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2809 passed**, 0 failed, 0 skipped (7 new; 6
  rewritten).
- Fix budget: 2 cycles.
  1. `ScrollToRow` through the scroll viewer, after the first run found the editor's own scroll
     methods inert.
  2. The first-change assertion compares offsets to the pixel rather than probing the line at the
     very top edge.
