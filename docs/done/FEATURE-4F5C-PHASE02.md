# FEATURE-4F5C-PHASE02 — The tile's context menu

**Item:** FEATURE-4F5C — Repository tiles: menu and icon
**Branch:** `feature/feature-4f5c-phase02-tile-menu`
**Run:** feature/2026-11-06-repo-tiles-history-search

## Summary

A repository tile on the start window now has a menu, opened by a right-click anywhere on it or by
the keyboard's menu request (Shift+F10, the menu key) from one of its buttons. Every item has its
glyph:

- Open (`ArrowSquareIn`) · Open in a new window (`ArrowSquareOut`)
- Open the repository's folder (`FolderOpen`) · Open a terminal in the repository's folder
  (`TerminalWindow`) · Copy repository path (`Copy`)
- Forget this repository (`X`)

Each item runs the page's command for the tile's repository, the same one the tile's button runs.
Copy repository path is new: `RepositoriesPageViewModel.CopyPathCommand` copies the full path through
`ISystemInterop`. It works for a repository that has moved too: the list still knows where it was.

A right-click never starts a reorder drag; the gesture already needed the left button, and a test now
holds it to that.

## Files/modules touched

- Created:
  - `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryTileMenuTests.cs`
  - `docs/done/FEATURE-4F5C-PHASE02.md`
- Modified:
  - `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml`
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/RepositoriesPageViewModel.cs`
  - `docs/roadmap.md`, `docs/plan/FEATURE-4F5C.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the menu's items reach the page | `$parent[UserControl]` (the page view, whose `DataContext` is the page) | The buttons' `$parent[ItemsControl]` finds the `ContextMenu` itself, which is an `ItemsControl` too, so every command came out `null`. The menu's logical parent is the tile, so the page view is reachable |
| The drag copy's menu | Left on the shared template, unused | The copy is never hit-tested (`IsHitTestVisible="False"`), so its menu never opens; stripping it would need a second template |
| The keyboard path | The menu request bubbles from the tile's focused button to the tile | The tile (`Border`) is not focusable, but its buttons are, and Avalonia opens a control's menu from a `ContextRequested` raised on a descendant |

## Deviations & follow-ups

- None.

## Build/test evidence

- **Baseline:** see `docs/done/FEATURE-4F5C-PHASE01.md` (clean: 0 warnings, 0 failures).
- **This dev:** build 0 warnings, 0 errors; tests 3652 run, 3649 passed, 0 failed, 3 skipped.
- **New tests** (`RepositoryTileMenuTests`, 5):
  - the items and separators in order, each with its `menu` glyph;
  - each item's command is the page's, with the tile's repository as parameter;
  - copying the path, also after the folder is gone, with no warning;
  - a right press and move never drags, and the release opens the menu;
  - the keyboard's menu request on the tile's Open button opens the tile's menu.
- **Fix budget:** 0 cycles. The binding problem was caught by the new tests during implementation,
  before the first full run.

## Documentation sweep

- README's start-window bullet lists what the window does and is not made wrong by a menu; no edit.
