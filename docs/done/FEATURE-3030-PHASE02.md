# FEATURE-3030-PHASE02 — Real columns with a resizable header

**Item:** FEATURE-3030 — History list: columns, menus, search
**Branch:** `feature/feature-3030-phase02-column-header`
**Run:** feature/2026-09-18-columns-selection-minimap

## Summary

The history list has a header, its columns can be dragged from it, and a long subject ellipsises in
its own column instead of making the whole list scroll sideways.

`HistoryColumnLayout` is the page's one set of widths. The rows keep `Auto` columns and give each
cell an explicit width from it — the pattern the graph cell and the badge strip already used, and
the only one available: a `ColumnDefinition` is not a `StyledElement`, so it inherits no
`DataContext` and cannot bind a `GridLength` from inside a row template.

The message column is the one that is never set. It is whatever is left, which is why the list no
longer scrolls sideways (`HorizontalScrollBarVisibility` is now `Disabled`) and why every grip makes
the boundary under the pointer move: the badge column grows to the right, the author, date and hash
columns grow to the left, and the message absorbs the difference either way. A grip stops at its
column's own minimum and at the message's, so nothing can be dragged into nothing.

The header is drawn at the width the list reports for its **viewport**, not at the page's width.
Avalonia's Fluent scrollbar hides itself over the content and costs the rows nothing, but a theme
that pins it would take its width out of the rows and not out of a header docked above them — the
header would then be a scrollbar out of step with every column. Following the viewport is what makes
that case impossible, and there is a test that pins the scrollbar to prove it.

## Files / modules touched

**Added — App**

- `ViewModels/Pages/HistoryColumnLayout.cs` — the `HistoryColumn` enum and the layout: the four
  resizable widths, the graph's content-driven width, the viewport the view reports, `MessageWidth`
  and `Slack`, `MinimumWidth`, `SeedRefsWidth` and `Resize`

**Modified — App**

- `ViewModels/Pages/HistoryPageViewModel.cs` — exposes `Columns`; `GraphColumnWidth` feeds it the
  lane width and `RefColumnWidth` offers it the measured badge width
- `Views/Pages/HistoryPageView.axaml` — the header row with its six cells and four grips; the row
  template's author, date and hash cells take their widths from `Columns`, their column definitions
  become `Auto`, and the list stops scrolling sideways
- `Views/Pages/HistoryPageView.axaml.cs` — each grip's `DragDelta` resizes its column; the list's
  scroll viewer is found on `TemplateApplied` and its viewport width is reported to the layout
- `Themes/Styles.axaml` — `TextBlock.columnheader` and `Thumb.columngrip`, the latter templated down
  to a transparent hit area with a one-pixel rule that thickens under the pointer

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — a new "the columns and their header"
  section: the defaults, the measured seed and when the reader takes the badge column over, the
  direction each grip resizes in, the minimums, and the message's minimum stopping a grip;
  `Header_LinesUpWithEveryRow` compares every header cell's right edge with every row's, before and
  after a resize, and `Header_LinesUpWithTheRowsWhenAScrollbarTakesTheirWidth` does it again with
  the scrollbar pinned. `ShowHistoryPageAsync` takes an optional commit count, and the rendering
  test asserts the column titles are drawn

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-3030.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the widths live | On the cells, with `Auto` columns | A `ColumnDefinition` inherits no `DataContext`, so a row template cannot bind one; the two columns that were already page-wide did it this way |
| The graph column's header | Left blank | It is 40–80 px wide and titled by what it draws; a word there would only be ellipsised |
| The badge column's minimum | Zero | A history with nothing decorated asks for no column at all, and a floor would spend width on an empty one |
| The measured badge width after a drag | Ignored | A column that snapped back to the widest branch name on the next refresh would undo the reader's drag in front of them; `IsRefsWidthOwnedByReader` records that it is theirs now |
| What the header is drawn at | The list's viewport | It is the one width that is right whether or not the scrollbar takes layout room |
| Finding the scroll viewer | `TemplateApplied`, with a visual-tree fallback | `PART_ScrollViewer` is the Fluent template's name; the fallback keeps the header working against a theme that names it something else |
| The grip's look | A one-pixel rule in a nine-pixel transparent hit area | The boundary has to be visible to be aimed at, and a nine-pixel target is what makes it catchable; the rule thickens under the pointer to say it is draggable |
| The grip's width cost | None — a negative margin straddles the gap | A grip inside the column would otherwise widen the column it is meant to measure |

## Deviations & follow-ups

- **One deviation from the plan.** The plan's acceptance criterion asked for alignment "with the
  vertical scrollbar shown and hidden". Avalonia's Fluent scrollbar auto-hides *over* the content,
  so in the shipped theme the two cases are the same width; the criterion is met by a test that
  pins the scrollbar (`ScrollViewer.SetAllowAutoHide(list, false)`), which is the only way the case
  exists at all.
- The header follows a viewport change one layout pass later — the pass that shrinks the viewport is
  the pass that tells the layout, and the header is measured in the next one. Invisible in the
  application; the test makes it explicit by laying out twice.
- Column widths are not persisted between sessions, and the columns cannot be sorted, reordered or
  hidden. All three are out of scope for this item and would each be a feature of their own.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1694  failed: 0  succeeded: 1694  skipped: 0
```

Twelve tests added (six of them the theory's cases), one rendering test extended. The rendered
snapshot `history-page.png` was inspected: the header sits between the toolbar and the commits, its
titles are above the columns they name, and the grip rules stand on the boundaries. Two fix cycles:
the alignment test first failed because a headless window ignores a `Height` set after `Show`, and
then because the header needs the layout pass after the one that moves the viewport.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`. The README's history bullet describes the graph, the
authors, the timestamps and the hashes — all still true, and it says nothing about how the columns
behave. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository. Nothing
edited.
