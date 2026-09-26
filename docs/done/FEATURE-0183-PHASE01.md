# FEATURE-0183-PHASE01 — Double the graph node size

**Item:** FEATURE-0183 — Bigger graph nodes, taller rows (PHASE01)
**Branch:** `feature/feature-0183-phase01-node-size`
**Run:** bugfix/2026-09-16-history-panel-and-graph

## Summary

Commit nodes are drawn twice the size they were: `CommitGraphCell.NodeRadius` goes from 4.5 to 9.
Nobody sets that property, so its default *is* the size of every circle in the graph.

Doubling it needed one guard. The radius is not a free number — it has to live inside a row whose
height is a preference (18 to 48) and beside a lane whose spacing is another (8 to 40), and at the
small end of either a radius-9 node is sliced off by the rows above and below, or drawn straight
over the neighbouring lane's line. `CalculateNodeRadius` is the rule that fits it: a public static
function beside `CalculateWidth`, taking the row height, the lane width, the stroke and whether the
row carries the HEAD ring — which is drawn *outside* the node and so decides its real extent. At the
new defaults it changes nothing and the node is drawn at 9; at an 18 px row a HEAD node comes back
down to 4.5, and at an 8 px lane every node stops one pixel short of where its neighbour's line is
drawn.

The HEAD ring's gap and thickness, which were two literals inside the drawing code, are now the
named constants the rule reasons about.

## Files / modules touched

**Modified — App**

- `Controls/Graph/CommitGraphCell.cs` — `NodeRadius` default 4.5 → 9, `HeadRingGap` /
  `HeadRingThickness`, the new `CalculateNodeRadius`, and `DrawNode` drawing at the fitted radius

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/CommitGraphCellTests.cs` — a "the node" section, 8 cases

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-0183.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What doubles | The radius | It is the property the control exposes, and doubling it doubles the circle on screen |
| Where the fitting rule lives | A public static function beside `CalculateWidth` | The house already tests the width rule that way; a private branch inside `Render` would only be reachable by counting pixels in a snapshot |
| What the row limit allows for | The HEAD ring, not just the node | The ring is drawn outside the node, so it is the ring that gets clipped first |
| What the lane limit allows for | The neighbour's line, not its centre | A node that reaches the next lane's centre has already covered its line |
| The floor | 1 px | Absurd inputs should still draw a dot; refusing to draw is never the better answer |

## Deviations & follow-ups

- **Follow-up:** the node size is not a preference. The report asked for bigger circles, and a
  setting for it would be a fourth number on the graph card.
- **Observation:** at the default lane width of 16 a radius-9 node clears the neighbouring lane's
  line with 6 px to spare, so the doubling is untouched by the fitting rule where it matters.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1576  failed: 0  succeeded: 1576  skipped: 0
```

8 tests are new: the default radius is 9; the fitting rule at the defaults, at the smallest row
height with and without the HEAD ring, and at the smallest lane width; a node that never disappears;
and a small radius that is never grown to fill the room it is given. The existing render tests, which
count distinct colours in a real frame, still pass at their 26 px rows.
