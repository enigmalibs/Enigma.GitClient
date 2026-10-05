# BUG-3D1F — Column titles run under the panel

**Item:** BUG-3D1F — Column titles run under the panel
**Branch:** `bugfix/bug-3d1f-column-titles-stop-at-panel`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Summary

With the details panel open in a window too narrow for every column's minimum beside it, the rows and the
column titles are both laid out past the history's viewport. The rows were cut there by the list's scroll
viewer; the header row (`HeaderRow`, a Grid as wide as the viewport) was not clipped, so its titles and
grips were drawn on over the details panel.

`HeaderRow` is now `ClipToBounds="True"`: the titles stop exactly where the rows stop.

Reproduced first: the new test failed before the fix ("the column titles are drawn over the details
panel") and passes after it.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`: `HeaderRow` clipped, with a comment.
- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryPageTests.cs`:
  `TheColumnTitles_StopAtTheDetailsPanel_AsTheRowsDo` and a `RenderedPixels` helper.
- `docs/roadmap.md`, `docs/plan/BUG-3D1F.md`: statuses.

**Created**

- `docs/done/BUG-3D1F.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What the test compares | The panel's pixels at the titles' height, with the titles drawn and with them made transparent | It asserts what the reader sees, and does not depend on how Avalonia reports clipping |
| Proving the reproduction | The test first asserts the "Commit" title is laid out past the panel's edge | A window wide enough to fit everything would pass vacuously |

## Deviations & follow-ups

- None from the plan.
- Follow-up (outside this item): `CloneAsync_ClonesALocalRepositoryAndReportsProgress` is racy — it asserts
  on reports a `Progress<T>` posts to the thread pool; a synchronous `IProgress<T>` in the test would fix it.
- Test gate on this Windows host: as recorded in `docs/done/FEATURE-903B.md`.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

Nothing stale: no prose doc describes the column header's layout.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- Before the fix, with the verification-only teardown change: `TheColumnTitles_StopAtTheDetailsPanel_AsTheRowsDo`
  failed ("the column titles are drawn over the details panel"). After it: that test, the `Header_*` and
  the `GraphColumn_*` tests (6) all pass.
- `Enigma.GitClient.Core.UnitTests`: 1132 total, 1131 passed, 0 failed, 1 skipped.
- `Enigma.GitClient.Core.IntegrationTests`: 344 total, 341 passed, 1 failed, 2 skipped — the failure is
  `RepositoryServiceTests.CloneAsync_ClonesALocalRepositoryAndReportsProgress`, the racy test recorded in
  `docs/done/BUG-123A.md` (its `Progress<T>` posts may not have arrived when it asserts). This dev changes
  one XAML attribute; the test passed 5 times out of 5 alone right after.
- Desktop, targeted (`HistoryPageTests`, `ShellRenderTests`, `ShellSnapshotTests`): 139 total, 72 passed,
  0 real failures, 67 teardown refusals. With the verification-only teardown change (not committed): 133
  passed; 4 teardown `IOException`s and the 2 over-long `git branch xxx…` refusals seen before this run.
- Fix budget: 0 cycles used.
