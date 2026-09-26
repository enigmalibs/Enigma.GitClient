# FEATURE-2FDF — More room between graph lanes

**Status:** DONE — see `docs/done/FEATURE-2FDF.md`
**Type:** FEATURE
**Branch:** `feature/feature-2fdf-lane-spacing`
**Run:** feature/2026-09-17-graph-space-diff-dialog

## Objective

Put a bit more horizontal space between the graph's parallel lines, so two branches running side by
side read as two lines rather than one thick one.

## Context & constraints

- Asked for as "in the history graph, add a bit of horizontal space between parallel lines".
- The distance between two parallel lines *is* `AppSettings.GraphLaneWidth`: `CommitGraphCell`
  places lane *n* at `LanePadding + LaneWidth / 2 + n * LaneWidth`, so every vertical line in the
  column is one lane width from its neighbour. The default is 16, and the setting is clamped to
  8–40 by `AppSettings.Normalised`.
- 16 is tight against the node: a commit is drawn at radius 9, so two neighbouring lanes leave
  16 − 9 − 1 = 6 device-independent pixels between a node's edge and the next lane's line. At 20
  that becomes 10 — visibly separated without making the column wide enough to push the subject
  column off a narrow window.
- The lane width is a preference on the settings page (`NumericUpDown`, 8–40, step 2), so whatever
  default ships must stay adjustable, and a value somebody already chose must survive the change.
- The house already moved a default exactly this way: schema version 2 raised the row height from
  26 to 36 and migrated the files that still carried 26, leaving every other value alone
  (`SettingsService.Migrate`, `AppSettings.LegacyGraphRowHeight`). Version 3 did the same for the
  diff rendering. This is the third case of the same pattern.
- The node radius is fitted to the lane by `CommitGraphCell.CalculateNodeRadius`, whose lane limit
  is `laneWidth − strokeThickness / 2 − 1`. At 20 that limit is 18 while the row limit is 13.5, so
  the node keeps its radius of 9 and the change is purely air between the lines.

## Steps

1. `AppSettings`: `GraphLaneWidth`'s default becomes 20; add
   `public const double LegacyGraphLaneWidth = 16;` beside `LegacyGraphRowHeight`, and raise
   `CurrentVersion` to 4, with the `<remarks>` line naming what version 4 does.
2. `SettingsService.Migrate`: a fourth case — `stored.Version < 4 && stored.GraphLaneWidth ==
   AppSettings.LegacyGraphLaneWidth` takes `AppSettings.Defaults.GraphLaneWidth`. Extend the method's
   `<remarks>` the way versions 2 and 3 are described there.
3. Tests — `tests/Enigma.GitClient.Core.UnitTests/Configuration/SettingsServiceTests.cs`: a version 3
   file whose lane width is 16 takes the new default; a version 3 file whose lane width is 24 keeps
   it; a version 4 file whose lane width is 16 is never migrated; and the "every migration at once"
   test grows the lane width so a version 1 file is proved to come through all three.
4. Tests — `tests/Enigma.GitClient.App.UnitTests/SettingsPageTests.cs`: the assertion that a fresh
   page starts at lane width 16 becomes `AppSettings.Defaults.GraphLaneWidth`, so the next default
   change does not have to edit it again.

## Acceptance criteria

- A fresh install draws lanes 20 apart: `AppSettings.Defaults.GraphLaneWidth` is 20 and
  `HistoryPageViewModel.LaneWidth` starts there.
- A settings file written before version 4 that still says 16 comes back as 20; one that says
  anything else comes back untouched; a version 4 file that says 16 keeps 16.
- The lane width stays a preference: the settings page still writes it, and the history page still
  follows it without a restart.
- The graph column measures `lanes * 20 + 2 * LanePadding`, and a commit node keeps its radius.
- `dotnet build` clean with zero warnings; the whole suite green.

## Out of scope

- The row height, the node radius and the lane padding — none of them is the distance between two
  parallel lines.
- A per-repository or per-window lane width.
- Any change to the settings page's 8–40 range: 20 sits inside it already.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which number to move | `AppSettings.GraphLaneWidth`, the default | It is literally the horizontal distance between two parallel lines, and it stays adjustable afterwards | hard-coding a wider lane in `CommitGraphCell` (kills the preference); widening `LanePadding` (that is the space before the first lane and after the last, not between lanes) |
| How much | 16 → 20 | "A bit" of space: a quarter more air, enough to separate two neighbouring lines at the default node radius, while 14 lanes still fit in the column budget a narrow window has | 18 (barely visible); 24 (pushes the subject column in on a small window, and reads as a different graph rather than a roomier one) |
| Files that already exist | A schema version 4 migration, moving only the value nobody chose | Exactly what versions 2 and 3 did; a client that overwrites a chosen preference is worse than one that never changes anything | bumping the default silently (existing users would never see the change); rewriting every file's lane width |
