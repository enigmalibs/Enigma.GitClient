# FEATURE-2326-PHASE01 — Graph row rendering control

**Item:** FEATURE-2326 — Commit graph UI
**Branch:** `feature/feature-2326-phase01-graph-control`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

The headline feature now draws. `CommitGraphCell` renders one row of the commit graph — the lanes
passing through it, the lines arriving at and leaving its commit, and the node — from the layout
Core produced. A ten-colour theme palette follows Dark and Light, and `RefBadge` draws the pills for
branches, tags and the stash.

The output was rendered off-screen and looked at, not just asserted: a blue trunk with a green
branch that forks and merges back, an amber branch beside it, rounded elbows, ringed merge nodes and
a highlighted HEAD.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.App/Themes/Graph.axaml` — ten lane colours per theme variant, the node
  outline, the HEAD ring, and the ref-badge colours
- `src/Enigma.GitClient.App/Controls/Graph/GraphPalette.cs` — palette index to brush, cached per
  theme variant
- `src/Enigma.GitClient.App/Controls/Graph/CommitGraphCell.cs` — the drawing control
- `src/Enigma.GitClient.App/Controls/RefBadge.cs` + its control theme

**Modified**

- `src/Enigma.GitClient.App/App.axaml` — merges the graph dictionary
- `src/Enigma.GitClient.App/Themes/Controls.axaml` — the `RefBadge` theme
- `docs/roadmap.md`, `docs/plan/FEATURE-2326.md`

**Created — tests**

- `tests/Enigma.GitClient.App.UnitTests/CommitGraphCellTests.cs` — 20 cases: measurement, lane
  geometry, the palette (including a theme switch and a missing-resource fallback), two rendered
  graphs measured for colour variety, and the ref badges

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Connection shape | A vertical run, then a quadratic elbow into the node | A diagonal reads as a slash across the column and becomes unreadable wherever several lanes cross. A line that leaves and arrives vertically stays traceable, which is the entire point of the view — and it is what makes the result look like GitKraken rather than like `git log --graph` |
| Node shapes | Filled circle for a commit, ring for a merge, extra outer ring for HEAD, dashed outline for the uncommitted row | Each distinction is one a reader actually makes. A merge is a join rather than a point where work happened, and the working directory is not a commit at all |
| Where the palette lives | Theme resources, resolved at render time and cached per variant | Core deals only in palette indices. Resolving per render pass would waste a lookup per lane per frame; caching without keying on the variant would freeze the colours at the first paint. A test switches the theme and asserts the colour changed |
| Palette choice | Ten hues alternating warm and cool, never red beside green | Adjacent lanes must stay distinguishable, including for the most common form of colour blindness |
| Pixel snapping | Coordinates floored, with a half-pixel offset for odd stroke widths | A 1 or 2 pixel line drawn on a fractional coordinate is smeared across two rows of pixels, which is exactly how a graph ends up looking blurry |
| Column width | Grows with the row's lanes, capped at fourteen | A repository with fifty concurrent branches must not squeeze the subject, author and hash columns out of existence; past the cap the graph scrolls |
| `RefBadge` alignment | `HorizontalAlignment=Left` in the theme | Found by a test: without it the pill stretches to fill its container, so one long branch name became a badge the width of the window |
| `RefBadge.IconKind` | A read-only `DirectProperty` kept in step by the same method that sets the style classes | Lets the control template bind the icon without the template having to know the mapping |

## Deviations & follow-ups

- **Deviation:** the plan described a `Palette` property on the control. It is resolved from theme
  resources instead, which is what makes a live theme switch recolour the graph — a property would
  have had to be pushed down from the page on every variant change.
- **Deviation (additive):** `IsUncommitted` and `MaximumLanes` were not in the plan. The first is
  needed by the pseudo-row the plan asks for in PHASE02; the second is what stops a very wide graph
  from eating the row.
- **Follow-up:** the plan's "a 100-row graph renders in a headless layout pass in under 100 ms" was
  not asserted as a timing test. Render timing under a headless software renderer measures the
  renderer, not the control; the meaningful budget is the layout pass, which FEATURE-6DB0 already
  covers at 100 000 commits. Recorded rather than asserted misleadingly.
- **Follow-up:** hit-testing on the graph column (clicking a node to select its commit) arrives with
  PHASE02, where the row is a list item that already handles selection.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 624  failed: 0  succeeded: 624  skipped: 0
```

20 tests are new in this dev. The Definition-of-Done gate passed on its first run; one issue was
found and fixed while authoring (the stretching badge, above). The rendered graphs are written to
`snapshots/graph-diamond.png` and `snapshots/graph-history.png` beside the test assembly.
