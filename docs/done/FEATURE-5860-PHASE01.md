# FEATURE-5860-PHASE01 — A line's menu opens anywhere on it

**Item:** FEATURE-5860 — Context menus that do more
**Branch:** `feature/feature-5860-phase01-whole-line-menus`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

In the branches, tags and remotes dialogs, a line's menu opened only when the right-click landed on its
text. The menu sat on an inner `Grid` with no background, and a panel without a brush is not
hit-testable between its children. The line's `Border`, which paints a transparent background over the
whole line padding included, carried no menu.

- **Branches, tags, remotes:** the menu moved from the inner grid to the line's border. It now opens in
  the padding, between the columns and over the text.
- **The same defect elsewhere, fixed the same way:** two grids that carry a menu got
  `Background="Transparent"`:
  - the rows of the changed-files panel (Changes page, history diffs);
  - the Changes page's stash rows.

## Files / modules touched

**Created**

- `tests/Enigma.GitClient.App.UnitTests/WholeLineMenuTests.cs` — 4 headless hit-test tests:
  - branch, tag and remote lines: padding, the gap after the first column, and the text;
  - a changed-file line: the gap after the first column.

**Modified**

- `src/Enigma.GitClient.App/Views/Pages/BranchesPageView.axaml`, `TagsPageView.axaml`,
  `RemotesPageView.axaml` — the menu is on the row's border.
- `src/Enigma.GitClient.App/Views/Pages/ChangesPageView.axaml` — the stash row's grid is painted.
- `src/Enigma.GitClient.App/Views/Panels/ChangedFilesPanelView.axaml` — the file row's grid is painted.
- `docs/roadmap.md`, `docs/plan/FEATURE-5860.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Rows whose menu is on a grid with no surrounding border | Paint the grid transparent | The same fix FEATURE-3030 used in the history. There is no border to move the menu to. |

## Deviations & follow-ups

- The plan named branches, tags and remotes. The file and stash rows had the same defect and got the
  same one-attribute fix, as the plan allowed.
- Line endings: no CRLF churn.

## Documentation sweep

Nothing user-facing describes where a menu opens. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors. A missing `using Avalonia.Input` in the new
  test was fixed before the first test run.
- `dotnet test --solution Enigma.GitClient.slnx`: **2432 passed**, 0 failed (4 new).
