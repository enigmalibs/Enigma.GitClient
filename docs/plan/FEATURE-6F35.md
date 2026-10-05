# FEATURE-6F35 — Uncommitted line joins HEAD, dashed

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Objective

As GitKraken draws it: the uncommitted line's dashed circle is joined by a **dashed line** to the
commit that is checked out (HEAD) — not by a solid line to whatever happens to be drawn below it.

## Context & constraints

- `Core/Graph/CommitGraphLayout.cs`: lanes wait for SHAs (`GraphLayoutState.Lanes`); a commit takes the
  leftmost lane waiting for it, a tip opens the leftmost free lane, the first parent continues in the
  commit's lane. Rows carry `GraphEdge(FromLane, ToLane, Kind, Colour)` (`Straight`, `MergeIn`,
  `BranchOut`), each drawn inside its own row. The state is carried across pages.
- `ViewModels/Pages/CommitRowViewModel.Uncommitted(lane, colour, commands)` builds its own `GraphRow`:
  lane 0, colour 0, one solid `BranchOut` straight down. `HistoryPageViewModel.LoadPageAsync` adds it
  before the first page is read; `AppendPage` lays the commits out. So the dashed circle sits in lane 0
  and its line runs into whatever is in lane 0 below — often not HEAD.
- `Controls/Graph/CommitGraphCell.cs` draws edges with a solid pen and the uncommitted node as a dashed
  outline (`DashStyle.Dash`).
- HEAD's commit: `RepositoryContext.Head?.Sha`; it may be several rows below (another branch has newer
  commits), on a later page, or absent (unborn branch).
- Tests: Core `CommitGraphLayoutTests`; Desktop `CommitGraphCellTests`, `HistoryPageTests`
  (`Page_ShowsTheUncommittedRowOnlyWhenThereIsSomethingUncommitted`), `ShellSnapshotTests`.

## PHASE01 — The working tree in the graph layout

**Branch:** `feature/feature-6f35-phase01-working-tree-layout`
**Status:** DONE — see `docs/done/FEATURE-6F35-PHASE01.md`

### Steps

1. `GraphCommitInput.WorkingTree(string? headSha)`: a pseudo-commit, flagged `IsWorkingTree`, whose
   only parent is HEAD's commit (none when there is no HEAD commit).
2. `GraphEdge` gains `IsDashed` (default `false`).
3. `GraphLayoutState` remembers which lanes carry a working-tree line; cloned with the state, cleared
   when the lane is released or taken over.
4. `CommitGraphLayout.BuildRow`: the working tree opens a lane like a tip and reserves it for HEAD's
   commit as a dashed line: its `BranchOut`, every `Straight` through that lane, and the `MergeIn` into
   HEAD's commit are dashed. From HEAD's commit down, the lane is an ordinary one.
5. Tests: HEAD first; HEAD below another branch's commits (the lane is kept free down to it, dashed);
   another child of HEAD merging in beside the dashed line; HEAD on a later page (carried in the state);
   no HEAD commit (no edge); a complete history without HEAD's commit (no edge); every other edge solid.

### Acceptance criteria

- The layout reserves the working tree's lane down to HEAD's commit, and only those segments are dashed.
- Build clean, whole suite green, the new tests among them.

## PHASE02 — The dashed line in the history

**Branch:** `feature/feature-6f35-phase02-dashed-line-to-head`
**Status:** TODO

### Steps

1. `HistoryPageViewModel`: the first page of a dirty working tree is laid out with
   `GraphCommitInput.WorkingTree(Head.Sha)` in front of its commits, and the uncommitted row takes the
   layout's first row; the commits take the rest. A repository with no commit yet still shows the row.
2. `CommitRowViewModel.Uncommitted` takes the `GraphRow` it is drawn from.
3. `CommitGraphCell` draws a dashed edge with the dashed pen the uncommitted circle uses.
4. Tests: the uncommitted row sits in HEAD's lane and its edges reach HEAD's row dashed when HEAD is not
   the newest commit; a clean tree lays out as before; the cell renders a dashed edge (snapshot or
   drawing assertion).

### Acceptance criteria

- With uncommitted work, the dashed circle is joined to HEAD's commit by a dashed line in HEAD's lane,
  wherever HEAD is in the loaded history.
- Build clean, whole suite green, the new tests among them.

## Out of scope

- A WIP node for stashes or other worktrees.
- Changing the uncommitted circle itself (the user likes it).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the line reaches HEAD | The working tree is laid out as a pseudo-commit whose parent is HEAD | The layout reserves its lane down to HEAD, so the dashed line never runs over another branch's line or node | Drawing a line over HEAD's lane after the layout (collides when the lane is used above HEAD) |
| Line style | The same dash as the circle | One visual language for "not a commit" | Dots; a lighter colour |
| The lanes moving when the tree becomes dirty | Accepted | HEAD's line takes the leftmost free lane, as GitKraken's WIP does | Keeping lanes fixed at the cost of overlaps |
| HEAD on a later page | The dashed lane carries on through the loaded rows, as any lane awaiting its commit | Same rule as every other lane | Hiding the line |
| No HEAD commit (unborn) | The circle alone | Nothing to join | A line to nothing |
| Split | Two phases: Core layout, then the drawing and wiring | Each is one reviewable concern; PHASE01 is pure and tested alone | One larger dev |
