# FEATURE-6F35-PHASE02 — The dashed line in the history

**Item:** FEATURE-6F35 — Uncommitted line joins HEAD, dashed (PHASE02)
**Branch:** `feature/feature-6f35-phase02-dashed-line-to-head`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Summary

The history now draws the uncommitted line as GitKraken does: its dashed circle sits in the lane of the
commit that is checked out, and a **dashed line** runs down that lane to HEAD's commit — past any newer
branch, which opens beside it — instead of a solid line into whatever happened to be in lane 0 below.

- `HistoryPageViewModel`: the first page of a dirty working tree is laid out with
  `GraphCommitInput.WorkingTree(Head.Sha)` in front of its commits (PHASE01); the uncommitted row takes
  the layout's first row, the commits the rest. It is now added with the page rather than before it is
  read. With no HEAD commit yet (an unborn branch) the circle stands alone; before the context has read
  HEAD, the existing catch-up reload redraws it joined.
- `CommitRowViewModel.Uncommitted(GraphRow, commands)`: the row comes from the layout instead of being
  made up (lane 0, colour 0, a solid edge).
- `CommitGraphCell`: an `IsDashed` edge is drawn with the dashed pen the uncommitted circle uses.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`: `LoadPageAsync`,
  `AppendPage(page, withWorkingTree)`.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/CommitRowViewModel.cs`: `Uncommitted`.
- `src/Enigma.GitClient.Desktop/Controls/Graph/CommitGraphCell.cs`: the dashed pen.
- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryPageTests.cs`:
  `TheUncommittedLine_JoinsTheCheckedOutCommitByADashedLine_BelowANewerBranch`.
- `tests/Enigma.GitClient.Desktop.UnitTests/CommitGraphCellTests.cs`: `Render_DrawsADashedEdgeWithGapsInIt`.
- `docs/roadmap.md`, `docs/plan/FEATURE-6F35.md`: statuses.

**Created**

- `docs/done/FEATURE-6F35-PHASE02.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| When the uncommitted row is added | With the first page, after the log is read | It needs the page's layout; a load superseded in the meantime adds nothing, as before |
| The dashed pen | `DashStyle.Dash`, flat caps, as the circle | Round caps would fill the gaps at this thickness |
| A log that fails to read | No uncommitted row | The history reports the failure; the row was an affordance on a history that is not there |

## Deviations & follow-ups

- None from the plan.
- The dash pattern restarts in every row, so at some row heights the dashes do not line up exactly across
  a row boundary. Barely visible at the default 36 px; a follow-up could offset the pattern by row.
- Test gate on this Windows host: as recorded in `docs/done/FEATURE-903B.md`.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

Nothing stale: the README does not describe how the uncommitted line is drawn.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors (after one fix cycle: a missing
  `using Enigma.GitClient.Core.Graph;` in the new test).
- `Enigma.GitClient.Core.UnitTests`: 1132 total, 1131 passed, 0 failed, 1 skipped.
- `Enigma.GitClient.Core.IntegrationTests`: 344 total, 342 passed, 0 failed, 2 skipped.
- Desktop, targeted (`CommitGraphCellTests`, `HistoryPageTests`, `AutoRefreshTests`, `HistoryStashTests`,
  `ShellSnapshotTests`): 171 total, 82 passed, 0 real failures, 89 teardown refusals. (A first attempt
  hit the run's own 5-minute budget for these five classes; with 20 minutes they finish in 4.5.)
- With the verification-only teardown change (not committed): 161 passed; 5 teardown `IOException`s; the
  2 over-long `git branch xxx…` refusals seen before this run; 3 × `HistoryStashTests.AStashLinesAction_RunsAndRedrawsTheHistory`
  comparing `"edited\n"` / `"two\n"` with git's `"…\r\n"` — this machine's system `core.autocrlf=true`.
- The new tests pass for real (verification run): `TheUncommittedLine_JoinsTheCheckedOutCommitByADashedLine_BelowANewerBranch`,
  `Render_DrawsADashedEdgeWithGapsInIt`; so do the existing `Page_ShowsTheUncommittedRowOnlyWhenThereIsSomethingUncommitted`
  and `Page_ShowsOneUncommittedRowWhenTwoLoadsOverlap`.
- Fix budget: 1 cycle used (the missing `using`).
