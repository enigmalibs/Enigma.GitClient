# BUG-29C8 — History column titles lack a margin

**Status:** DONE — see `docs/done/BUG-29C8.md`
**Type:** BUG
**Branch:** `bugfix/bug-29c8-column-title-margins`
**Run:** bugfix/2026-09-28-history-tag-push-release

## Objective

In the History page's column header, the *Graph*, *Author*, *Date* and *Commit* titles have no space
on their left, while *Refs* and *Message* do. Every title must sit the same distance after the
separator before it, and *Graph* must not touch the page's edge.

## Context & constraints

- **The header** is `HeaderRow` in `Views/Pages/HistoryPageView.axaml`: six `Panel` cells at the rows'
  widths, 10 px apart (`ColumnSpacing`). Each cell holds a `TextBlock.columnheader` (`Themes/Styles.axaml`,
  margin `0,0,4,0`) and, except *Message*, a `Thumb.columngrip`. A grip is 9 px wide and draws a
  1 px rule down its middle.
- **Why the titles differ.**
  - *Graph* and *Refs* hang their grip off their **right** edge (`HorizontalAlignment="Right"`,
    `Margin="0,0,-5,0"`). The rule sits at the start of the 10 px gap, so the next title (*Refs*,
    *Message*) starts about 9.5 px after it.
  - *Author*, *Date* and *Commit* hang theirs off their **left** edge (`Margin="-5,0,0,0"`). The rule
    ends up half a pixel before the column's own title, which it touches.
  - *Graph* is the first column: its title starts at x = 0, against the page. The rows draw the lanes
    from `HistoryPageViewModel.LanePadding` (8).
- **What must not move.** The header cells line up with the row cells (`AssertColumnsLineUp` in
  `HistoryPageTests`), and the row content starts at each cell's left edge. The grips resize through
  `Thumb.DragDelta`, which reports a delta, so where a grip sits does not change what it does.

## Steps

1. `HistoryPageView.axaml`, the header:
   - the *Author*, *Date* and *Commit* grips move into the gap before their column,
     `Margin="-14,0,0,0"` (the 10 px gap plus half the 9 px grip), with a comment saying why. Their
     rule then sits where the *Graph* and *Refs* rules do: at the start of the gap, about 9.5 px
     before the title;
   - the *Graph* title gets a left margin equal to the lane padding (`Margin="8,0,4,0"`), so it starts
     where the lanes do.
2. Tests (`HistoryPageTests`):
   - every title from *Refs* to *Commit* starts the same distance (±1 px) after the nearest grip rule
     on its left, and that distance is at least 8 px;
   - the *Graph* title starts at least 8 px from the header's left edge;
   - the header cells still line up with the rows (the existing assertion, after a layout pass).

## Acceptance criteria

- *Graph*, *Refs*, *Message*, *Author*, *Date* and *Commit* each have the same breathing room on their
  left; none touches a separator or the page's edge.
- The header cells still line up with the row cells, and every grip still resizes its column.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Moving the row content or changing the column widths.
- Restyling the grips or the titles (size, weight, colour).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How to even out the titles | Move the three left-edge grips into the gap before their column | Only the rule moves. The titles stay aligned with the row content under them, and every title ends up the same distance after its separator, as *Refs* and *Message* already are | Padding the three titles (they would drift right of the values under them, or every row cell would have to move with them) |
| *Graph*'s left space | The lane padding, 8 px | The title then starts where the lanes it names start | 10 px, the column gap (off the lanes by 2 px); the toolbar's 12 px |
| Where the fix lives | Local values in the header, not the shared `columnheader` style | Only the first column needs an indent; the style is right for the others | A style change for every title |
