# BUG-3BAE — Search matches are not highlighted

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-3bae-search-highlight`
**Run:** feature/2026-09-20-ui-polish-diff-page

## Objective

Make the history search show what it found: the rows it matches are washed with the search colour,
which is what FEATURE-3030 PHASE05 set out to do and what the reader has never actually seen.

## Context & constraints

- The ViewModel is already right: `HistoryPageViewModel.MarkMatches` sets `CommitRowViewModel.IsSearchMatch`
  on every loaded row and counts them, and the toolbar's `MatchSummary` says "3 matches" beside the
  box. Nothing is filtered, by design — a filtered graph draws lanes between commits that are not
  adjacent.
- The row template binds `Classes.match="{Binding IsSearchMatch}"` and `Themes/Styles.axaml` gives
  `Grid.match` the `SearchMatchBrush` background. **The same `Grid` also sets
  `Background="Transparent"` as a local value in markup, and in Avalonia a local value outranks
  every style setter** — so the class is applied, the style matches, and the background never
  changes. The wash has been invisible since the day it was written.
- The transparent background is not decoration: a `Grid` with no brush is transparent to the
  pointer, and the row's right-click menu depends on the whole line being a hit target
  (FEATURE-3030 PHASE01). Whatever replaces the local value has to keep the row hit-testable.
- Avalonia applies styles in the order they are declared, so a base `Grid.commitrow` setter
  followed by `Grid.commitrow.match` resolves the way it reads: the match wins.
- **Baseline:** clean build, 1728 tests green.

## Steps

1. `Views/Pages/HistoryPageView.axaml`: the row `Grid` gains `Classes="commitrow"` and loses its
   local `Background`, keeping `Classes.match`.
2. `Themes/Styles.axaml`: `Grid.commitrow` sets `Background` to `Transparent` — the hit target,
   stated as a style so it can be overridden — and `Grid.commitrow.match`, declared after it, sets
   the `SearchMatchBrush`. The existing `Grid.match` rule is replaced by it.
3. Tests — `tests/.../HistoryPageTests.cs`: with a search that matches one commit, the realised row
   for that commit paints the search brush and a non-matching row paints nothing; clearing the
   search puts every row back; the matching row is still a hit target (its background is a brush,
   never null).

## Acceptance criteria

- Typing in the history search box washes every matching row in the search colour, and the count
  beside the box agrees with how many rows are washed.
- Clearing the search leaves no row washed.
- No row is filtered out, and the graph is unchanged.
- A right-click anywhere on a row — matching or not — still opens the row's menu.
- Build clean with zero warnings; the App and Core unit suites green.

## Out of scope

- Highlighting the matched substring inside the subject.
- Searching the whole history rather than the loaded rows, or asking git to search.
- Moving the view between matches with the keyboard.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the wash is made to win | The row's transparent background becomes a style too | Both values then live at the same precedence and the later, more specific rule wins — which is what the markup already reads as | Binding `Background` to a converter on `IsSearchMatch` (a binding per row, and the hit-target rule stops being stated); `!` priority tricks (Avalonia has none) |
| Row or word highlighting | The row | The request says the found lines; a subject is trimmed with an ellipsis, so a word highlight would often be off screen anyway | Word-level highlighting inside the subject (a different control, and out of scope) |
| The class name | `commitrow` | It names the row the way `branchrow` already names the branches list's | Styling the bare `Grid` inside the template (would reach every `Grid` in every row) |
