# FEATURE-F452 — The line's items on badge menus

**Item:** FEATURE-F452 — The line's items on badge menus
**Branch:** `feature/feature-f452-badge-menus-line-items`
**Run:** feature/2026-11-05-history-badges-release

## Summary

A right-click on a branch or a tag badge in the History opens the badge's own menu, followed by a
separator and everything the line's own menu offers: show what it changed, commit details, create
branch / worktree / tag here, check out this commit, reset, revert, the squash items, the line's
branch merge items, open on the host, copy the hash.

- `CommitRowViewModel.BadgeMenuEntries(badge)` gives the line's entries for a badge:
  - it leaves out those whose parameter is the badge's own item. Those are "set it as the merge
    source" and "merge the source into it", which the branch badge already offers under the same
    conditions;
  - it starts with a separator and never has two in a row or one last.
- `Views/BadgeMenus.AppendLineEntries` (new) turns those entries into `MenuItem`s / `Separator`s, with
  the drop menu's glyphs. They are appended after the badge's XAML items, each marked so that the next
  opening removes them first.
- `HistoryPageView.OnContextRequested`, the page's tunnelling handler that already rebuilt the line's
  menu, now also appends the entries when the right-click is on a branch or tag badge.

## Files/modules touched

- Created:
  - `src/Enigma.GitClient.Desktop/Views/BadgeMenus.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/BadgeLineMenuTests.cs`
  - `docs/done/FEATURE-F452.md`
- Modified:
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/CommitRowViewModel.cs`
  - `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml.cs`
  - `docs/roadmap.md`, `docs/plan/FEATURE-F452.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How a duplicate is recognised | A line entry whose parameter is the badge's own item | The plan said "same command and parameter as a shown badge item". Before the menu opens, the XAML items' bindings (`IsVisible`, `Command`) are not evaluated yet, so they cannot be compared. The line entries whose parameter is the badge's own branch are exactly the two the badge mirrors, under the same conditions |
| Where the filtering lives | In the ViewModel (`BadgeMenuEntries`) | Testable without a window; the view only turns entries into controls |

## Deviations & follow-ups

- The duplicate rule is the narrower, equivalent one above, not a comparison with the realised badge
  items (see the decisions).
- The menu opened from the keyboard (the menu key, Shift+F10) goes through the same `ContextRequested`,
  so it gets the line's items too.

## Build/test evidence

- Baseline: see `docs/done/FEATURE-D7D5-PHASE01.md`.
- `dotnet build Enigma.GitClient.slnx`: **0 warnings, 0 errors**.
- `dotnet test --solution Enigma.GitClient.slnx`: 3,601 total, 3,598 passed, 3 skipped, **0 failed**.
- New tests (`BadgeLineMenuTests`, 7, real git):
  - the entries start with a separator, leave out the badge's own, and never double a separator;
  - a branch badge's menu ends with the line's items after "Copy branch name" and a separator, bound
    to the line's commands, the line and the hashes as parameters;
  - a tag badge's menu, the same after "Copy tag name";
  - "Set "topic" as merge source" appears once on topic's badge;
  - opening twice does not duplicate;
  - every appended item has a glyph;
  - the line's own menu is unchanged.
- Fix budget: 0 cycles (a missing `using` in the new test file was fixed before the first test run).

## Documentation sweep

- README: the badge lines (49–89) say what a badge's menu offers. None says the line's items are
  missing from it, so nothing became wrong; no edit.
