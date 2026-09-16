# FEATURE-0183 — Bigger graph nodes, taller rows

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-0183-phase01-node-size`, `feature/feature-0183-phase02-row-height`
**Run:** bugfix/2026-09-16-history-panel-and-graph

## Objective

Make the commit graph read at a glance: commit nodes twice the size they are drawn at today, and a
default row height of 36 for the history and the graph.

## Context & constraints

- Reported as "in the graph view, the circles are small, you can double their sizes" and "you can
  set the default row height for the history and graph to 36".
- The node is drawn by `Controls/Graph/CommitGraphCell.cs` from its `NodeRadius` styled property,
  which defaults to `4.5` and is set by nobody — so the default is the size.
- The row height is the `GraphRowHeight` preference (`Core/Configuration/AppSettings.cs`), default
  `26`, clamped to 18–48 by `Normalised()` and editable on the settings page. The history page and
  the graph cells share it: the history *is* the graph view.
- `GraphRowHeight` is persisted, so a default-only change would never reach anyone who has already
  run the client — their file already says 26.

## PHASE01 — Double the graph node size

**Status:** DONE — see `docs/done/FEATURE-0183-PHASE01.md`

### Steps

1. `CommitGraphCell.NodeRadius` default `4.5` → `9`.
2. Add a public static rule beside `CalculateWidth` that answers what radius a node is actually
   drawn at, given the row height, the lane spacing, the stroke and whether the row is HEAD; use it
   from `DrawNode`.
3. The rule keeps a node inside its row (the HEAD ring included, since it is drawn outside the node)
   and off the neighbouring lane's line, so the smallest row height and lane width a preference
   allows still draw something sane.

### Acceptance criteria

- `NodeRadius` defaults to 9 — twice its previous value.
- At the new default row height and lane width the drawn radius *is* 9: the clamp does not quietly
  undo the change.
- At the smallest row height (18) a HEAD node shrinks to fit rather than being clipped, and at the
  smallest lane width (8) the node stays clear of the next lane's line.
- The existing render tests still produce a frame with the same number of distinct colours.
- `dotnet build` clean with zero warnings; the whole suite green.

## PHASE02 — Default row height of 36

**Status:** DONE — see `docs/done/FEATURE-0183-PHASE02.md`

### Steps

1. `AppSettings.GraphRowHeight` default `26` → `36`; the 18–48 clamp and the settings page's
   18–48 spinner are unchanged (36 sits inside both).
2. Bump `AppSettings.CurrentVersion` to 2 and put the first real case in `SettingsService.Migrate`:
   a version-1 file whose `GraphRowHeight` is still the old default of 26 is raised to 36; any other
   value was chosen by the user and is kept.
3. Update the tests that assert the old default.

### Acceptance criteria

- A fresh install opens the history at 36 px rows, in the settings page and in the history page's
  `RowHeight`.
- A stored version-1 file with `graphRowHeight: 26` reads back as 36; one with any other value —
  including a hand-edited 30 — keeps what it says, and its version is brought to 2.
- A version-2 file is left alone.
- `dotnet build` clean with zero warnings; the whole suite green.

## Out of scope

- Making the node size a preference — the prompt asked for a bigger default, not a new setting.
- Changing the 18–48 row-height range or the lane-width default.
- Re-tuning the lane colours, the curve radius or the ref badges.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| What "double the size" means | The radius: 4.5 → 9 | It is the property that exists, and doubling it doubles the circle's diameter on screen | doubling the diameter only (a 50 % change, not what was asked) |
| Nodes on small rows / narrow lanes | Clamped by a public rule, unit-tested | The preferences reach down to an 18 px row and an 8 px lane, where a radius-9 node is clipped and covers its neighbour's line | leaving it unclamped and accepting clipped nodes; refusing the small preferences |
| Existing settings files | Schema v2, migrate a stored 26 to 36 | The old default is the value nobody chose; anything else is a choice, and a client that overwrites choices is worse than one that never changes | default-only change (invisible to every existing user); forcing 36 on everyone |
| History vs graph row height | One preference, as today | The history page *is* the graph view; a second number would be two ways to say the same thing | adding a separate history row height |
