# FEATURE-6DB0-PHASE01 — Commit graph lane layout

**Item:** FEATURE-6DB0 — Core graph, diff & tree algorithms
**Branch:** `feature/feature-6db0-phase01-graph-layout`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

Implemented the algorithm the headline feature rests on: assigning every commit a lane, working out
the segments drawn around it, and keeping a long-lived branch on a stable column and colour. It is a
pure function over `(sha, parents)` — no Avalonia, no git, no I/O — which is what makes it testable
to the level a graph this important deserves.

The pass is incremental by construction: laying out a history in pages and carrying the lane table
forward produces byte-identical rows to laying the whole thing out at once, asserted at **every**
possible page boundary.

## Files / modules touched

**Created — `src/Enigma.GitClient.Core/Graph/`**

- `GraphRow.cs` — `GraphEdgeKind` (`Straight` / `MergeIn` / `BranchOut`), `GraphEdge`
  (from-lane, to-lane, kind, palette index) and `GraphRow` (sha, lane, colour, merge/root flags,
  edges, width)
- `GraphLayoutState.cs` — the lane table, its colour cursor, `Clone`, and the colour picker
- `CommitGraphLayout.cs` — `GraphCommitInput`, `GraphLayoutOptions`, `GraphLayoutResult` and the
  layout pass itself

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Graph/CommitGraphLayoutTests.cs` — 38 cases

**Modified**

- `docs/roadmap.md`, `docs/plan/FEATURE-6DB0.md` — status updates

## Design notes

Each row describes only the segments drawn **inside itself**, which is precisely what lets the
history view virtualise: a row can be rendered knowing nothing about its neighbours.

- `Straight` — a lane passing through the row untouched, top edge to bottom edge.
- `MergeIn` — a line arriving from the top edge and ending at the node. Same lane means the commit's
  own line continuing upwards; a different lane means a branch merging in, and that lane ends here.
- `BranchOut` — a line leaving the node for the bottom edge on its way to a parent. Same lane means
  the line continuing downwards; a different lane means a fork.

Edges are emitted straight-first so the renderer paints through-lines behind the curves.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `GraphLayoutOptions` as a record struct | Changed to a sealed record **class** | A record struct's implicit parameterless constructor ignores the primary constructor's defaults, so `new()` and `default` silently meant "zero colours". This was caught by the tests, and a type whose default value is wrong is a trap for every future caller — the class makes the default correct by construction |
| Where a segment's colour comes from | The lane the segment belongs to, not the row | A fork is drawn in the colour of the lane it heads for, and a merged-in line keeps its own colour all the way into the node. That is what makes a branch readable as one continuous coloured line |
| Colour is an index, never a value | Palette index | Core stays free of UI types, and Dark and Light can use genuinely different colours for the same lane |
| Colour assignment | A rotating cursor that skips colours currently in use by an open lane | Deterministic, allocation-free, and it keeps adjacent lanes distinct until the palette genuinely runs out — at which point it repeats rather than failing |
| Parents missing from the history | `HistoryIsComplete` option, default off | While paging, a parent not yet seen must keep its lane open — the line genuinely continues below the page. Only when the caller states the history is complete (a filtered or shallow walk) does the lane close instead |
| A merge whose *first* parent is filtered out | The first parent that **is** present continues in the commit's lane | Otherwise the lane would be abandoned and the surviving branch would fork sideways for no reason |
| Guarding against a leaked lane | A single `laneCarriedOn` flag drives the release | Roots, fully filtered commits and ordinary commits all go through one rule, instead of three special cases that can drift apart |
| Mutating the caller's carry state | `Build` clones it | A caller that re-lays-out the same page (a re-render, a retry) must get the same answer; a test asserts the carried state is untouched |

## Deviations & follow-ups

- **Deviation (stronger than planned):** the plan asked for an incremental test comparing a one-shot
  layout against two halves. The suite asserts it at **every** split point of a nine-commit history
  with merges, plus a three-page case, because a single boundary would not have caught an
  off-by-one in the carry.
- **Deviation (additive):** `BuildComplete` was added as the obvious entry point for a caller that
  already has the whole history, so `HistoryIsComplete` is not something each caller has to remember.
- **Follow-up:** the pass scans the lane table per commit, so it is O(rows × open lanes). The
  performance test lays out 100 000 commits with a merge every ten and asserts under two seconds.
  If a pathological repository ever keeps hundreds of lanes open at once, a sha-to-lane dictionary
  would make it O(rows); not worth the complexity until such a repository shows up.
- **Follow-up:** the layout does not yet know about the "uncommitted changes" pseudo-row — that is a
  view concern and lands with FEATURE-2326 PHASE02.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 286  failed: 0  succeeded: 286  skipped: 0
```

38 tests are new in this dev. Two fix cycles were used:

1. `GraphLayoutOptions.Default.ColourCount` came back as 0 — a real defect in the product code (see
   the decision table), fixed by making the options a record class.
2. `Build_ReusesALaneFreedByAnEarlierBranch` asserted a two-lane width for a history that genuinely
   needs three, because the earlier branch's root had not been laid out yet when the later branch
   opened. The fixture was corrected to two independent histories, which is what the test was
   actually trying to express; the layout was right.

The warning count was read explicitly (0).
