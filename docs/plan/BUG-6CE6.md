# BUG-6CE6 — Details panel ignores the splitter

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-6ce6-details-panel-resize`
**Run:** bugfix/2026-09-16-history-panel-and-graph

## Objective

Make the history page's bottom panel — the commit details, the changed files and the diff — follow
the splitter above it, instead of staying at the one height it was born with.

## Context & constraints

- Reported as "the bottom panel that shows the diffs does not resize; when I resize the panel it
  keeps the same small height".
- The cause is in `Views/Pages/HistoryPageView.axaml`: the workspace grid is
  `RowDefinitions="*,Auto,Auto"` and the detail `Border` carries `Height="300"`. A `GridSplitter`
  writes the dragged size back into the row definition **keeping that definition's unit type**, so
  an `Auto` row receives a value it then ignores and measures to its content — a `Border` pinned at
  300. Nothing moves.
- `Views/Pages/ConflictResolutionPageView.axaml` already does this correctly
  (`RowDefinitions="*,Auto,180"`, no height on the child); this page should read the same way.
- The detail row cannot simply become a fixed pixel row: it is only shown when a row is selected
  (`IsVisible="{Binding HasSelection}"`), and a pixel row would reserve that space even with
  nothing selected, which is a visible regression.

## Steps

1. `HistoryPageView.axaml`: name the workspace grid, declare its rows explicitly — a star row with a
   `MinHeight` so the graph can never be squeezed to nothing, the `Auto` splitter row, and a pixel
   detail row starting collapsed with a `MinHeight` of its own — and drop `Height="300"` from the
   detail `Border`, naming it instead.
2. `HistoryPageView.axaml.cs`: follow the page's `HasSelection` (subscribe on `DataContextChanged`,
   unsubscribe from the previous ViewModel) and translate it into the detail row's height — the
   remembered height when a commit is selected, zero when none is. Capture whatever height the
   splitter left behind before collapsing, so re-selecting restores the size the reader chose.
3. Keep the default opening height at 300, the value the page opens on today.

## Acceptance criteria

- With a commit selected, the detail panel is 300 px tall; changing the detail row's height (what a
  splitter drag does) changes the panel's rendered height to match — the regression test fails on
  the current XAML.
- Deselecting collapses the row to zero — the graph gets the whole page back — and selecting again
  restores the height the splitter last left, not the 300 default.
- The graph list keeps a usable minimum height whatever the splitter is dragged to.
- `dotnet build` clean with zero warnings (`AVLN` included); the whole suite green.

## Out of scope

- Persisting the panel height across runs — a new preference is not part of this report.
- The vertical splitter inside the panel (files ↔ diff), which already resizes correctly.
- The same shape on other pages: `ChangesPageView` splits two star rows and
  `ConflictResolutionPageView` already uses a pixel row, so neither has this defect.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the row is sized | Pixel row, height off the child | The unit type is exactly what the splitter preserves, and it is the shape the conflict page already uses | keeping `Auto` and animating the `Border` height (the splitter does not touch it); star row (the panel would grow with the window instead of staying where it was put) |
| Collapsing with no selection | Code-behind, from `HasSelection` | A `RowDefinition` is not a control, so a binding on its height is fragile; the view translating a ViewModel fact into layout is where this belongs | binding `RowDefinition.Height` through a converter; leaving the row at 300 always (300 px of empty surface) |
| The remembered height | Kept in the view, for the session | The prompt asked for a panel that resizes, not for a new stored preference | adding `DetailsPanelHeight` to `AppSettings` |
| Minimum sizes | 120 px graph, 140 px details | A splitter that can erase either side is worse than one that stops | no minimums |
