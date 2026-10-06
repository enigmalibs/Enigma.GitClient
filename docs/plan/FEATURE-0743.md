# FEATURE-0743 — Roomier ref badges, a HEAD icon

**Status:** DONE — see `docs/done/FEATURE-0743.md`
**Type:** FEATURE
**Branch:** `feature/feature-0743-ref-badge-head-icon`
**Run:** vibe/2026-10-06-diff-search-refs-release

## Objective

- The pills naming branches, tags and stashes in the history's Refs column get slightly more room
  around their content.
- The checked-out branch's pill carries a new icon, to the left of its current icons.

## Context & constraints

- `RefBadge` (`Controls/RefBadge.cs`) and its template (`Themes/Controls.axaml`): a 1 px border,
  `Padding="7,2"`, a 13 px kind icon, the upstream's cloud when grouped, 5 px spacing, a 13 px label.
  The checked-out branch has the `head` class (`IsCurrent`), drawn in `RefBadgeHeadBrush`.
- `RefBadgeMetrics` mirrors the template to size the Refs column (`HorizontalPadding` = 8 = padding +
  border, `IconSize`, `IconSpacing`, the upstream's extra icon); `HistoryPageTests` reads the numbers
  back off a realised badge so the two cannot drift. `RefBadgeItem` carries `IsCurrent`.
- Row height is a preference (18–48, default 36).

## Steps

1. Template: `Padding="9,3"`; a `Check` icon (13 px, the badge's foreground) first in the pill,
   visible only for the current branch.
2. `RefBadgeMetrics`: `HorizontalPadding` = 10; `MeasureBadge` counts the check icon and its gap for
   the current branch; `Measure` passes `RefBadgeItem.IsCurrent`.
3. Tests: the metric constants still match a realised badge; the current branch's pill shows the check
   first and others do not; the measured width of a current badge includes the extra icon and equals
   the realised width.

## Acceptance criteria

- Every ref pill has 2 px more horizontal and 1 px more vertical padding.
- The checked-out branch's pill shows a check icon left of its branch icon (and of the cloud, when
  grouped with its upstream); no other pill does.
- The Refs column is still seeded to fit its badges.
- Release build clean with zero warnings; the whole suite green.

## Out of scope

- Badge colours, fonts, the Refs column's maximum.
- The branches/tags dialogs (no `RefBadge` there).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How much padding | `7,2` → `9,3` | "Slightly": +2 horizontal, +1 vertical | `8,2`; `10,4` |
| Which icon | Phosphor `Check` | "Checked out" reads as a check mark (GitKraken's convention); a cue that does not rely on colour | `House`, `MapPin`, `Crosshair` |
| Where | First in the pill, before the branch icon | As asked: left of the current icons | After the label |
| Its colour | The badge's foreground | One brush per pill, as the other icons | An accent colour |
