# FEATURE-292D — A resizable graph column

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-292d-resizable-graph`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Objective

The history's first column, the graph, is sized by the lanes it draws and cannot be resized. Make it
a column like the others: titled in the header, with a grip that resizes it.

## Context & constraints

- `HistoryColumnLayout` owns the widths of Refs, Author, Date and Sha, each resized by a `Thumb`
  (`columngrip`) in `HistoryPageView.axaml`; the message column absorbs the difference (`Slack`).
  Refs, a left-hand column, grows with a positive delta; the right-hand ones with a negative delta.
- The graph width is measured: `HistoryPageViewModel.GraphColumnWidth` =
  `CommitGraphCell.CalculateWidth(lanes, laneWidth, padding, MaximumLanes)`, pushed into
  `Columns.GraphWidth`; the header's graph cell is an empty `Panel`.
- Refs is the precedent for a measured column the reader may take over:
  `SeedRefsWidth` / `IsRefsWidthOwnedByReader`.
- Column widths are not persisted today.

## Steps

1. `HistoryColumnLayout`: a `HistoryColumn.Graph`; the measured width is seeded
   (`SeedGraphWidth`) until the reader drags the grip (`IsGraphWidthOwnedByReader`); `Resize` treats
   Graph as a left-hand column; a minimum width (24 px).
2. `HistoryPageViewModel`: the measured `GraphColumnWidth` seeds the layout instead of setting it.
3. `HistoryPageView.axaml`: the header's graph cell gets a "Graph" title and a right-edge grip; each
   row's `CommitGraphCell` takes `Columns.GraphWidth` and clips what does not fit.
4. `HistoryPageView.axaml.cs`: the grip resizes `HistoryColumn.Graph`.
5. Tests: layout unit tests (seed, resize, minimum, owned-by-reader, slack) and a headless test that
   the header and the rows stay aligned after a graph resize.

## Acceptance criteria

- The graph column has a header title and can be widened or narrowed with its grip; every row follows.
- Narrower than its lanes, the graph is clipped at the column edge instead of overlapping the badges.
- Until it is dragged, the column still follows the lanes in view.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Persisting column widths across sessions.
- Reordering or hiding columns.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Header title | "Graph" | "A column like the others" — every other column is titled | Keeping it untitled |
| Measured vs reader width | Measured until dragged, then the reader's, like Refs | The established pattern in this layout | Always measured (not resizable); always fixed (loses auto-fit) |
| Too narrow | Clip the graph | A column must not paint over its neighbour | Scale the lanes down |
| Persisting the width | No | No other column persists; out of the ask | A new setting |
