# FEATURE-6F35-PHASE01 — The working tree in the graph layout

**Item:** FEATURE-6F35 — Uncommitted line joins HEAD, dashed (PHASE01)
**Branch:** `feature/feature-6f35-phase01-working-tree-layout`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Summary

The Core graph layout can now lay out the uncommitted work the way GitKraken draws its WIP node: as a
pseudo-commit above the history whose only parent is HEAD's commit, joined to it by a dashed line.

- `GraphCommitInput.WorkingTree(headSha)`: the pseudo-commit (`IsWorkingTree`, an empty SHA, HEAD's
  commit as its parent — none for an unborn branch).
- `GraphEdge.IsDashed` (default `false`, so every existing construction is unchanged).
- `GraphLayoutState` remembers which lanes are the working tree's line (`IsDashed(lane)`), clones it with
  the state — so the line carries on into the next page when HEAD's commit is not on this one — and
  clears it when a lane is released, reopened or taken over.
- `CommitGraphLayout`: the working tree opens a lane like any branch tip and reserves it for HEAD's
  commit as a dashed line. Its `BranchOut`, every `Straight` through that lane and the `MergeIn` into
  HEAD's commit are dashed; HEAD's commit then carries the lane on as ordinary history, in the same
  colour. Because the lane is reserved, a newer branch opens beside it, never in it.

Nothing uses the new input yet: PHASE02 wires it into the history and draws it.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Core/Graph/CommitGraphLayout.cs`
- `src/Enigma.GitClient.Core/Graph/GraphRow.cs` (`GraphEdge`)
- `src/Enigma.GitClient.Core/Graph/GraphLayoutState.cs`
- `tests/Enigma.GitClient.Core.UnitTests/Graph/CommitGraphLayoutTests.cs`: eight tests.
- `docs/roadmap.md`, `docs/plan/FEATURE-6F35.md`: statuses.

**Created**

- `docs/done/FEATURE-6F35-PHASE01.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the pseudo-commit is told apart | An `IsWorkingTree` init property on the input, not a sentinel SHA | Explicit; a SHA can never be mistaken for it |
| Its SHA | Empty | The uncommitted row already carries an empty SHA; no lane ever waits for it |
| `IsDashed` on `GraphEdge` | An optional last positional parameter | Every existing `new GraphEdge(…)` and the edges' equality stay as they were |
| `IsDashed(lane)` | Public on the state, beside `GetColour` | A paged caller (and a test) can see the line is still open |

## Deviations & follow-ups

- None from the plan.
- Test gate on this Windows host: as recorded in `docs/done/FEATURE-903B.md`.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

Nothing stale: no prose doc describes the graph layout's rules.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- `CommitGraphLayoutTests`: 46 passed (38 before, 8 new): `WorkingTree_IsAPseudoCommitWhoseOnlyParentIsHead`,
  `WorkingTree_JoinsHeadByADashedLine_WhenHeadIsTheNewestCommit`,
  `WorkingTree_KeepsItsLaneFreeDownToHead_WhenAnotherBranchHasNewerCommits`,
  `WorkingTree_SharesHeadWithAnotherChildOfIt`, `WorkingTree_CarriesItsDashedLaneIntoTheNextPage`,
  `WorkingTree_WithoutAHeadCommit_IsJoinedToNothing`, `WorkingTree_IsJoinedToNothing_WhenAWholeHistoryLacksHead`,
  `AHistoryWithoutTheWorkingTree_HasNoDashedLine`.
- `Enigma.GitClient.Core.UnitTests`: 1132 total, 1131 passed, 0 failed, 1 skipped.
- `Enigma.GitClient.Core.IntegrationTests`: 344 total, 342 passed, 0 failed, 2 skipped.
- Desktop, targeted (`CommitGraphCellTests`, `HistoryPageTests`): 133 total, 68 passed, 0 real failures,
  65 teardown refusals. With the verification-only teardown change (not committed): 125 passed; 6
  teardown `IOException`s and the 2 over-long `git branch xxx…` refusals already seen before this dev
  (`RefColumn_StillSeedsItselfAndStillStopsAtItsMaximum`, `RefColumn_GrowsWithTheLongestBadgeAndStopsAtItsMaximum`).
- Fix budget: 0 cycles used.
