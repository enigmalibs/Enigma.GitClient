# FEATURE-4F5C-PHASE01 — Legible path, icon at 24

**Item:** FEATURE-4F5C — Repository tiles: menu and icon
**Branch:** `feature/feature-4f5c-phase01-tile-path-icon-size`
**Run:** feature/2026-11-06-repo-tiles-history-search

## Summary

On the start window's repository tiles:
- the path under the name is drawn in the secondary grey (`dim`) instead of the tertiary one
  (`faint`), which looked like disabled text — the same move the ref details made earlier;
- the leading folder glyph is 24 px instead of 20, through a new `tile` role in the icon scale, so the
  size lives in the one place every icon size does.

The drag copy uses the same template, so it follows.

## Files/modules touched

- Created:
  - `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryTileLookTests.cs`
  - `docs/done/FEATURE-4F5C-PHASE01.md`
- Modified:
  - `src/Enigma.GitClient.Desktop/Themes/Styles.axaml` (the `tile` role, and its line in the scale's comment)
  - `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml`
  - `docs/roadmap.md`, `docs/plan/FEATURE-4F5C.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the tests find the glyph and the path | `Name`s on them (`TileGlyph`, `TilePath`) | Names inside a `DataTemplate` are scoped per item; the next phases' tests use them too |

## Deviations & follow-ups

- None.

## Build/test evidence

- **Baseline** for the whole run — the run branch at `6ed9d93` (the starting `develop` @ `53f627f`
  plus the planning commit), on Linux:
  - `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
  - `dotnet test --solution Enigma.GitClient.slnx`: 3645 run, 3642 passed, 0 failed, 3 skipped.
  - Clean: the strict rule applies to every dev of the run (zero warnings, every test passing).
- **This dev:** build 0 warnings, 0 errors; tests 3647 run, 3644 passed, 0 failed, 3 skipped.
- **New tests** (`RepositoryTileLookTests`, 2): the glyph is a `Folder` of the `tile` class, 24 × 24;
  the path is drawn with `EnigmaForegroundSecondaryBrush`, not the tertiary one, in the dark and the
  light theme.
- **Fix budget:** 0 cycles.

## Documentation sweep

- README's start-window bullet says nothing about the path's colour or the glyph's size; no edit.
