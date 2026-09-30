# BUG-6787-PHASE01 — The text selection and its drawing

**Item:** BUG-6787 — Diff text cannot be selected
**Branch:** `bugfix/bug-6787-phase01-selection-model`
**Run:** feature/2026-09-30-profile-lists-readme-diffs

## Summary

The diff viewer now has a text selection model, and every line draws its share of it. PHASE02 adds
the pointer gesture and the copy.

- **`DiffTextSelection`** (`Controls/Diff/DiffTextSelection.cs`), with `DiffPane` (`Unified`, `Left`,
  `Right`) and `DiffTextPosition(Row, Column)`, where the column is an index in the raw text, before
  tab expansion.
  - `Begin`, `ExtendTo`, `Clear`; `Start` / `End` are ordered, so a backward drag covers the same
    text as a forward one; `IsEmpty`.
  - `Changed` is raised only when something actually changes.
  - `RangeOn(row, pane, length)` gives a row's selected range, clamped to its text; an empty range for
    a text-less row inside the selection; nothing for another pane.
  - `Text(lineAt)` gives the selected text, one line per row, skipping rows that have no line in the
    pane.
- **One selection per viewer**, on `DiffRenderOptions.Selection`: the object every row already
  reaches, and one that survives the virtualised list recycling rows. `DiffRowViewModel.Index` is set
  as the rows are built, and `CellFor(pane)` finds a row's cell.
- **`DiffViewerViewModel`** clears the selection on every new patch (new file, re-read, context
  change, which all go through `Apply`) and when the rendering switches. `SelectedText()` returns what
  is on screen: no line numbers, no markers, no hunk bands, no fillers, no "No newline at end of file"
  marker.
- **`DiffLineText`** gains `Selection`, `Row`, `Pane` and `SelectionBrush`.
  - It draws its selected range behind the glyphs and over the word-level tint, mapped through the tab
    expansion (a selected tab is tinted over its whole width), at the scrolled origin.
  - `IndexAt(point)` gives the raw index nearest a point, allowing for the scroll, the tabs (a point
    inside a tab lands before or after it, by half) and the wrapping.
  - It subscribes to the selection only while attached, so the viewer's long-lived selection never
    holds a recycled row alive.
  - Without a `Selection` (the conflict page) nothing changes.
- **`DiffViewerView.axaml`** binds `Selection`, `Row` and `Pane` on its three `DiffLineText`s. A
  `DiffTextSelectionBrush` (the selection blue, translucent: `#70…` dark, `#50…` light) is set through
  the `diff|DiffLineText` style.

## Files / modules touched

**Created — App**

- `Controls/Diff/DiffTextSelection.cs`

**Modified — App**

- `Controls/Diff/DiffLineText.cs` — the four properties, `IndexAt`, `DrawSelection` / `Drawn`,
  subscription on attach and detach, the hit-testing layout
- `ViewModels/Panels/DiffViewerViewModel.cs` — `DiffRenderOptions.Selection`, `DiffRowViewModel.Index`
  and `CellFor`, clearing in `Apply` and the `ViewMode` setter, `SelectedText()`
- `Views/Panels/DiffViewerView.axaml` — the bindings on the three lines
- `Themes/Styles.axaml` — `SelectionBrush` on `diff|DiffLineText`
- `Themes/Graph.axaml` — `DiffTextSelectionColor` (both themes) and `DiffTextSelectionBrush`

**Created — tests**

- `DiffTextSelectionTests.cs`:
  - the model: empty until extended, backward drags, text-less rows and other panes, clamping,
    `Changed` only on real changes, `Text` skipping rows;
  - the viewer: rows numbered in order; unified text without the band, numbers or markers; the two
    sides kept apart; the missing-newline marker left out; a new file and another rendering clear
    the selection;
  - the line: `IndexAt` with a tab and a scroll offset, `IndexAt` on an empty line, and a rendered
    frame whose tint covers exactly the selected characters, in the right pane and row, and is gone
    once cleared

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/BUG-6787.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How to hit-test | A `TextLayout` of the same drawn text, built lazily after each measure and disposed on the next or on detach | Avalonia 12's `FormattedText` draws and builds highlight geometry but has no `HitTestPoint`. Only a line that is asked builds one |
| Where the model lives | `Controls/Diff`, next to the control that draws it | The ViewModel already references that namespace (`DiffTypography`); the control must not reference ViewModels |
| The selected text's line separator | `\n` | What the patch and git use; the existing row copy does the same |
| The tint | The selection blue at about 44% (dark) and 31% (light) opacity | The line's own green or red and the word-level tint still read through it |

## Deviations & follow-ups

- **None from the plan.** Nothing sets a selection yet: the pointer and the copy are PHASE02.
- A selected empty line draws nothing. Editors often draw a sliver for its newline; it could be added
  if the reader misses it.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2613 passed, 0 failed, 0 skipped (+13).
- Two fix cycles:
  1. The first build failed on the `Enigma.Avalonia` / `Avalonia` namespace collision (`Avalonia.Layout`
     written inline in a test).
  2. Two tests failed. One expected a clamped column of 2 on a one-character line (it is 1). The
     pixel check read the frame as BGRA, and the headless frame is RGBA; a saved frame showed the tint
     exactly on the selected characters. The check now reads the byte order from `Bitmap.Format`.
