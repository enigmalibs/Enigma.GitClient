# BUG-3D1F — Column titles run under the panel

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-3d1f-column-titles-stop-at-panel`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Objective

With the history's details panel open (a commit's files or the uncommitted files), the column titles
stop at the panel, as the rows do, instead of running under or over it.

## Context & constraints

- `Views/Pages/HistoryPageView.axaml`: `PageRoot` docks `DetailsPanel` right; `HistoryArea` holds the
  toolbar, the `ColumnHeader` border with `HeaderRow` (a Grid, `Width="{Binding Columns.HeaderWidth}"` —
  the list's viewport — six columns, the message one `*` with `MinWidth="120"`, the others fixed), and the
  `CommitList`.
- When the panel narrows the area below the fixed columns + the message minimum, the rows overflow too,
  but the list's `ScrollViewer` clips them; the header Grid is not clipped, so its titles and grips are
  drawn past its right edge — over the panel.
- Tests: `HistoryPageTests`, `ShellRenderTests`, `ShellSnapshotTests` (headless rendering).

## Steps

1. Reproduce headlessly: a history with the details panel open in a window too narrow for every column's
   minimum; the header's last titles are arranged past the header's right edge.
2. Clip the header row to its bounds (`ClipToBounds`), so what does not fit is cut where the rows are cut.
3. Test: with the panel open in a narrow window, nothing of the header is drawn over the panel (render
   assertion), and the header still lines up with the rows.

## Acceptance criteria

- The column titles never draw over the details panel.
- Build clean, whole suite green, the new test among them.

## Out of scope

- Horizontal scrolling of the history; changing the columns' minimum widths.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Fix | Clip the header to its bounds | Exactly what the rows do; no layout change | Shrinking the fixed columns; hiding columns |
| Where | The header row (its width is the viewport's, as the rows') | The cut lands where the rows' cut lands | Clipping the whole history area |
