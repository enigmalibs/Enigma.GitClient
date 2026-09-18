# FEATURE-3030-PHASE01 — Right-click anywhere on the row

**Item:** FEATURE-3030 — History list: columns, menus, search
**Branch:** `feature/feature-3030-phase01-row-hit-area`
**Run:** feature/2026-09-18-columns-selection-minimap

## Summary

A right-click anywhere on a history line now opens that line's menu.

The menu was never missing: it lives on the row template's `Grid`, and a `Grid` with no `Background`
is transparent to the pointer everywhere one of its children is not. The row has five gaps of ten
points between its six columns, plus whatever room the last column does not fill, and a press
landing in any of them fell through to the `ListBox` — which has no menu of its own, so nothing
opened. `Background="Transparent"` makes the whole line a hit target; a transparent brush is hit
tested, the absence of a brush is not.

The `ListBoxItem` style also states `HorizontalContentAlignment="Stretch"` rather than inheriting it
from the theme, because "the whole line" is only the whole line if the row's content is given it.

## Files / modules touched

**Modified — App**

- `Views/Pages/HistoryPageView.axaml` — the row grid takes `Background="Transparent"`; the page's
  `ListBoxItem` style states the stretch its hit area depends on

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — a new "the row's hit area" section:
  `Row_OpensItsMenuFromAnywhereOnTheLine` picks a point in the spacing between two columns, asserts
  no child of the row covers it, and asserts a hit test there reaches the control carrying the row's
  menu — then asserts the same for a point over a cell. Two helpers came with it: `RowGrids`, and
  `Render`, which forces the headless frames a hit test reads

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-3030.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the hit area goes | On the row grid, not on the `ListBoxItem` | The menu is on the grid; putting the brush anywhere else would make the hit test pass while the menu still did not open |
| How to prove it | A hit test at a point that is provably inside no cell | Asserting `Background is not null` would restate the edit rather than test the behaviour; the test also asserts the chosen point is in a gap, so it cannot quietly stop testing anything |
| Headless hit testing | Force render ticks first | `InputHitTest` reads the composed frame, and a headless window that never drew one returns `null` for every point — verified: without the ticks the test fails on the fixed code too |
| The stretch setter | Stated explicitly | The theme happens to stretch a `ListBoxItem`'s content today; the row's hit area should not depend on that continuing |

## Deviations & follow-ups

- **None from the plan.** Both acceptance criteria are covered.
- The suite's `Core.IntegrationTests.Sync.SyncServiceTests.FetchAsync_ReportsItsProgress` failed on
  the run's starting commit and passed here, unchanged: git narrates a local fetch only when it
  takes long enough to narrate, so the test is timing-dependent rather than broken. Worth a
  follow-up making it assert the plumbing without depending on git's pacing; untouched by this dev.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1682  failed: 0  succeeded: 1682  skipped: 0
```

One test added. It was checked against the unfixed view — with the `Background` removed it fails
with `Assert.Same() Failure`, so it guards the behaviour rather than describing it. No fix cycle
was needed.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`; neither describes the history row's menu or how it is
opened, so neither was made wrong by this change. There is no `CLAUDE.md`, `CHANGELOG.md` or
`CONTRIBUTING.md` in the repository. Nothing edited.
