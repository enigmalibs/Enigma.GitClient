# FEATURE-BB60-PHASE03 — Side by side on two editors

**Item:** FEATURE-BB60 — Diffs on AvaloniaEdit
**Branch:** `feature/feature-bb60-phase03-side-by-side-editors`
**Run:** feature/2026-10-05-diff-view-avaloniaedit

## Summary

The side-by-side rendering is two read-only `DiffTextEditor`s either side of the divider: the old
file (`LeftEditor`) and the new (`RightEditor`), with the minimap beside them. No `ListBox`, no
`DiffScrollState` and no hand-built pointer selection is left in the diff viewer.

- **Scrolling together.** `DiffScrollLink` keeps the two scrolled together, down and sideways.
  - It remembers where the reader last asked to be and puts each side there as far as that side can
    go.
  - Each side therefore scrolls to the end of its own longest line, and the shorter side never drags
    the longer one back to its end.
  - It tells its own moves from the reader's by comparing with where it would have put the side,
    within a pixel. A scroll viewer reports changes after layout, so a flag around the setter could
    not.
  - The minimap follows and drives the old side, and the link takes the new side along.
  - A file opens with both sides level, two rows above the first change.
- **Wrapping** is the unified view's only.
  - The side-by-side editors never wrap: two editors wrap independently, so the rows would fall out
    of line.
  - The toolbar toggle is disabled side by side, with the tooltip "Wrap long lines (unified view)"
    shown even when disabled.
  - The setting reads "Wrap long lines in the unified view".
- **Selecting.** Each side selects on its own, with the editor's own drag, Shift+click, keyboard and
  Ctrl+A. One selection at a time: taking it in one side clears the other. Ctrl+C and each editor's
  *Copy* menu copy the code of that side. Ctrl+F searches the side that has the keyboard.
- **What went.**
  - The side-by-side row template and `SideBySideList`.
  - The `LeftBar` / `RightBar` scrollbars and `DiffScrollState`.
  - The extent measuring, the viewport, wheel and pointer-selection code-behind (about 400 lines).
  - The row selection (`DiffViewerViewModel.Selection`, `CopySelectionCommand`,
    `DiffRowViewModel.Lines`).
  - The list-only styles and the row-selection colour.

  `CopyCommand` copies the selected text. `DiffLineText` keeps what the conflict page uses: no
  selection, no horizontal offset, no hit-testing.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Desktop/Controls/Diff/DiffScrollLink.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/DiffSideBySideEditorTests.cs` — 8 tests:
  - the sides scroll down together whichever one is scrolled;
  - they scroll sideways together, each as far as its own longest line, with the gutters fixed;
  - a long line stays inside its own side;
  - neither side wraps, and the wrap toggle is the unified view's;
  - a new file starts both sides at their left edge;
  - a file opens with both sides level at its first change;
  - the minimap scrolls both sides;
  - `Reachable` clamps.
- `docs/done/FEATURE-BB60-PHASE03.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Views/Panels/DiffViewerView.axaml` — the side-by-side grid is two
  editors and the minimap; the wrap toggle is unified-only; the resources block is gone.
- `src/Enigma.GitClient.Desktop/Views/Panels/DiffViewerView.axaml.cs` — rewritten: the minimaps,
  the first-change opening, the link (639 → 203 lines).
- `src/Enigma.GitClient.Desktop/ViewModels/Panels/DiffViewerViewModel.cs`:
  - `DiffScrollState`, `SideBySideScroll`, `Panes()`, `MeasureExtents`, `LongestLine` are gone;
  - the row selection and its command are gone;
  - `WrapLines` is a plain property.
- `src/Enigma.GitClient.Desktop/Controls/Diff/DiffLineText.cs` — only what the conflict page uses.
- `src/Enigma.GitClient.Desktop/Themes/Styles.axaml` — the list-only styles are gone; `diffnumber`
  stays for the conflict page.
- `src/Enigma.GitClient.Desktop/Themes/Graph.axaml` — `DiffSelectionColor` / `DiffSelectionBrush`
  (the row-selection bar) are gone.
- `src/Enigma.GitClient.Desktop/Views/Pages/SettingsPageView.axaml` — the wrap setting's label.
- `tests/Enigma.GitClient.Desktop.UnitTests/DiffViewerTests.cs`:
  - the copy tests use text selection;
  - the rendering, word-highlight, layout and end-to-end tests read the editors;
  - the scroll-state, extent and line-offset tests are gone.
- `tests/Enigma.GitClient.Desktop.UnitTests/DiffTextSelectingTests.cs` — every drag, click,
  Shift+click, gutter press, menu and edge-scroll test drives the editors.
- `tests/Enigma.GitClient.Desktop.UnitTests/DiffTextSelectionTests.cs` — the line-control hit-test
  and tint tests are replaced by two editor tests: the mirror in rows and characters, and one
  selection at a time.
- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryPageTests.cs` — `ScrollOf` finds a diff editor's
  scroll as well as a list's.
- `docs/roadmap.md`, `docs/plan/FEATURE-BB60.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the scroll sync lives | `DiffScrollLink`, a class of its own in `Controls/Diff` | It holds state (where the reader asked to be) and has a clamping rule worth testing on its own (`Reachable`) |
| How the link tells its own moves from the reader's | Compare the side's offset with where the link would have put it, within one pixel | A probe showed the scroll viewer raises `ScrollChanged` after layout, and rounds an offset set at 1423.5 to 1423. A half-pixel tolerance took that for a reader's move and dragged the longer side back |
| What the minimap follows | The old side | Both sides have the same lines and height, so either is the patch's position; the link keeps the other level |
| The wrap setting's label | "Wrap long lines in the unified view" | Side by side no longer wraps, and a setting that silently did nothing there would read as broken |
| The row-selection colour | Removed from both themes | Nothing draws a selected row any more; text selection has its own colour |
| `DiffRowViewModel.Options` / `DiffCellViewModel.Options` | Kept | Many tests build rows through these constructors; the property costs nothing |
| `DiffRowBuilder.CopyText` (Core) | Kept | Public Core API with its own tests; only the App stopped calling it |

## Deviations & follow-ups

- The plan said the drag-selection tests would be rewritten. All seven were, and the edge-scroll
  test now drives AvaloniaEdit's own scrolling-while-selecting.
- `SettingsServiceTests.ChangingSomethingRaisesTheEventWithTheNewSettings` (Core) failed once in a
  full run. It passed in three reruns of the Core suite and in the next full run. Core is untouched
  by this run, so it is a timing flake.
- Line endings: no CRLF churn.

## Documentation sweep

The README's "Side by side shows the whole file on both sides, scrolling as one" stays true. No doc
mentions wrapping. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2792 passed**, 0 failed, 0 skipped.
  - 10 new: 8 side by side, 2 selection mirror.
  - 27 removed: tests of the deleted scroll state, line offsets, extents and row selection.
  - The rest rewritten for the editors.
- Fix budget: 3 cycles.
  1. A namespace for `ScrollContentPresenter`, after the first run's compile error.
  2. The link's tolerance widened to a pixel, and two history tests updated for the editors. The
     build stopped on one leftover reference.
  3. That reference, and pixel-tolerant offset assertions — green.
