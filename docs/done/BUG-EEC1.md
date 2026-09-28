# BUG-EEC1 — Start page empty state not centred

**Item:** BUG-EEC1 — Start page empty state not centred
**Branch:** `bugfix/bug-eec1-start-empty-state`
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Summary

With no recent repository, the start window's "No repositories yet" sat against the left edge of the
page. `RepositoriesPageView` is a `DockPanel`, in which only the last child fills. The empty state
came before the list of recent repositories, so it was docked left at the width of its text: 477 px of
an 1100 px page. The empty state and the list now share one `Grid` cell, the fix
`ChangedFilesPanelView` already uses. The empty state takes the whole area under the header, and its
own theme centres what it says.

The page was the only instance of this layout. Every other `EmptyState` already shares a `Grid` or a
`Panel` cell with what it stands in for.

## Files / modules touched

**Created**

- `docs/done/BUG-EEC1.md`

**Modified**

- `src/Enigma.GitClient.App/Views/Pages/RepositoriesPageView.axaml` — the empty state and the list in
  one grid cell.
- `tests/Enigma.GitClient.App.UnitTests/RepositoriesPageTests.cs` — `EmptyPage_CentresItsEmptyStateOnThePage`.
- `docs/roadmap.md`, `docs/plan/BUG-EEC1.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Other pages | None changed | Checked every `EmptyState`. The conflicts, branches, tags, remotes and history pages, the diff viewer and the host repositories dialog all put it in a shared `Grid`/`Panel` cell. |
| How to prove it | Headless geometry: the empty state's width equals the page's, and its content's centre is its centre, within a pixel | It measures the defect itself. The test was run against the old XAML and failed with *Expected: 1100, Actual: 477*. |

## Deviations & follow-ups

- None from the plan.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

Nothing user-facing describes where the empty state sits. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2452 passed**, 0 failed, 0 skipped (1 new).
- Fix budget: no fix cycle. The new test's first draft compared centres to one decimal place, which a
  half-pixel layout rounding broke. That was corrected to a 1 px tolerance before the first suite run.
