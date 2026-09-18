# FEATURE-3030-PHASE04 — Merge nodes half a commit's size

**Item:** FEATURE-3030 — History list: columns, menus, search
**Branch:** `feature/feature-3030-phase04-smaller-merge-nodes`
**Run:** feature/2026-09-18-columns-selection-minimap

## Summary

A merge is drawn at half the radius of a commit. It was already drawn differently — a ring rather
than a filled disc — and the size now says the same thing louder: a merge is a join, and the eye
should pick the commits out of a busy graph without having to read the lanes.

`CalculateMergeNodeRadius` halves the radius `CalculateNodeRadius` already fitted to the row and the
lane, rather than halving the radius that was asked for. That ordering matters: the fitting rule is
what guarantees a visible node at the smallest row height (18) and lane width (8) the preferences
allow, and halving the wanted radius first would have thrown that guarantee away. A floor of one
pixel keeps the guarantee after the halving too.

The halving also happens before the HEAD ring is drawn, so a merge that HEAD points at gets a ring
around the node it actually has instead of one sized for a commit.

## Files / modules touched

**Modified — App**

- `Controls/Graph/CommitGraphCell.cs` — the `MergeNodeScale` constant, the public static
  `CalculateMergeNodeRadius`, and `DrawNode` halving a merge's radius before the HEAD ring

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/CommitGraphCellTests.cs` —
  `MergeNode_IsHalfTheSizeOfACommit` over the four row-height and lane-width cases the commit's own
  theory covers, `MergeNode_NeverDisappearsHoweverSmallTheRowIs`, and
  `Render_DrawsAMergeSmallerThanACommit`, which renders the same row twice — differing in nothing
  but `IsMerge` — and counts the pixels each frame painted over its background

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-3030.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What gets halved | The fitted radius | The fitting rule is what keeps a node visible at the smallest settings; halving the wanted radius first would undo it |
| Where the halving happens | Before the HEAD ring | Otherwise a merge at HEAD wears a ring sized for a node twice as big as the one inside it |
| The stroke | Unchanged (`StrokeThickness + 0.5`) | A thinner ring at half the radius would be a different change — visibility, not size — and the request asked for size |
| How to prove it on the canvas | Two frames of one row differing only in `IsMerge`, compared by painted pixels | A row from a real layout carries an extra incoming edge, whose ink would swamp the node's; this isolates the node |

## Deviations & follow-ups

- **None from the plan.** All three acceptance criteria are covered.
- The graph snapshot `graph-history.png` was inspected: the two merges are small rings, the commits
  are unchanged, and the HEAD merge keeps its ring.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1688  failed: 0  succeeded: 1688  skipped: 0
```

Six tests added (four of them the theory's cases). No fix cycle was needed.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`. The README's first bullet says the graph draws "lanes,
merges and branches so a real repository's history is readable" — still exactly what it does, and no
document states a node size. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the
repository. Nothing edited.
