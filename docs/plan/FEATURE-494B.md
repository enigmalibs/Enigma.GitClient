# FEATURE-494B — History refs: full names, bigger badges

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-21-refs-tags-and-dragging

## Objective

The badges in the history's Refs column show the whole reference name and are drawn a size bigger,
with room around them.

## Context & constraints

- `Themes/Controls.axaml` holds the `RefBadge` control theme: a `Border` with `Padding="4,0"`, a
  1 px border and `CornerRadius="3"`, wrapping an 11 px `ei:Icon` and an 11 px `TextBlock` with
  `Spacing="4"`. The label carries `MaxWidth="{TemplateBinding MaximumTextWidth}"` and
  `TextTrimming="CharacterEllipsis"` — that ellipsis is what the reader sees on a long branch name.
- `Controls/RefBadgeMetrics.cs` mirrors those numbers as constants (`FontSize`, `IconSize`,
  `IconSpacing`, `HorizontalPadding`, `BadgeSpacing`, `MaximumLabelWidth`) and measures the strip a
  row draws, because the Refs column is one width for the whole page rather than an `Auto` column
  measured per row. Its doc comment states that the constants mirror the template; they must keep
  doing so.
- `MeasureBadge` caps each label at `MaximumLabelWidth` (180), which is `RefBadge.MaximumTextWidth`'s
  default — the measurement side of the same ellipsis.
- The column's seeded width is capped separately by `HistoryPageViewModel.MaximumRefColumnWidth`
  (280) and, once the reader drags the Refs grip, `HistoryColumnLayout.IsRefsWidthOwnedByReader`
  hands the width to them for good. The strip itself is `ClipToBounds="True"`, so a badge wider than
  the column is clipped rather than pushed over the subject.
- The badge already carries `ToolTip.Tip="{TemplateBinding Text}"`, so the full name is reachable
  even when the column is narrower than the badge.
- A history row is `AppSettings.Defaults.GraphRowHeight` tall (36), so a badge may grow vertically a
  little without touching the row height.
- **Baseline:** clean build, 1775 tests green.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How to stop the trimming | Remove `MaxWidth` and `TextTrimming` from the template, and the 180 px cap from `MeasureBadge` | The reader asked for the whole name and gave the reason: the column is theirs to resize | Raising the cap (the same bug at a larger number); a per-badge tooltip only (it is already there and is not what was asked) |
| What a badge wider than its column does | It is clipped by the strip, and the tooltip still names it in full | Honest — the column edge is visible and draggable — and it is what `ClipToBounds` already does | An ellipsis (the reported problem); letting the strip overflow onto the subject |
| The column's seed cap | `MaximumRefColumnWidth` stays at 280 | One absurd branch name must not take the subject's room on first paint; the grip is the way past it | Removing the cap (a 200-character branch name would seize the page) |
| Badge padding | `7,2` around the content, so 8 px a side counting the border | Two notches of horizontal room and a little vertical, which is what reads as a pill rather than a label | A bigger jump (the Refs column is a scarce resource on this page) |
| Badge type size | Icon and label both at 13 px, gap 5 | One step up from 11, still smaller than the row's 12 px body text so the badge stays a badge | 14 px (taller than the 36 px row is comfortable with, once padding is added) |
| Where the numbers live | Still constants in `RefBadgeMetrics`, still checked against the template by a test | A per-badge binding to a theme resource would measure thousands of rows through the resource system | Binding the template to the constants (the same cost, in the hot path) |

## PHASE01 — Branch names in full, never trimmed

**Branch:** `feature/feature-494b-phase01-untrimmed-badges`
**Status:** DONE — see `docs/done/FEATURE-494B-PHASE01.md`

### Steps

1. `Themes/Controls.axaml`: the `RefBadge` template's label loses `MaxWidth` and
   `TextTrimming` — it draws whatever it is given, and the strip's clipping is what bounds it.
2. `Controls/RefBadge.cs`: `MaximumTextWidth` is gone, with its `StyledProperty`, since nothing
   bounds the label any more. Its doc comment goes with it.
3. `Controls/RefBadgeMetrics.cs`: `MeasureBadge` no longer caps the measured label, and
   `MaximumLabelWidth` is removed. The class comment says the strip clips and the column seeds.
4. Tests — `tests/.../HistoryPageTests.cs`: a badge measures its whole label, so two names that
   differ only past 180 characters measure differently; the history page still seeds the Refs column
   from the badges and still stops at `MaximumRefColumnWidth`; a realised badge's label carries no
   `TextTrimming` and no finite `MaxWidth`.

### Acceptance criteria

- A branch name in the Refs column is drawn in full, with no three dots, whatever its length.
- Widening the Refs column with its grip reveals more of a badge that the column was clipping.
- The Refs column still seeds itself from the badges and still stops at its maximum on first paint.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — A roomier badge, a size bigger

**Branch:** `feature/feature-494b-phase02-bigger-badges`
**Status:** TODO

### Steps

1. `Themes/Controls.axaml`: the `RefBadge` template's `Border` takes `Padding="7,2"`, its
   `StackPanel` `Spacing="5"`, its `ei:Icon` `Size="13"` and its `TextBlock` `FontSize="13"`.
   `CornerRadius` grows to 4 so the corner still reads against the taller pill.
2. `Controls/RefBadgeMetrics.cs`: `FontSize`, `IconSize`, `IconSpacing` and `HorizontalPadding`
   follow the template exactly (13, 13, 5, 8).
3. Tests — `tests/.../HistoryPageTests.cs`: the constants match what the realised template draws —
   the label's font size, the icon's size, the panel's spacing and the border's padding read off a
   built `RefBadge` — so the two cannot drift apart again; and the Refs column grows with the bigger
   type for the same reference.

### Acceptance criteria

- A badge is visibly roomier: more space between its edge and its content, on every side.
- The branch icon and the branch name are drawn a size larger than the rest of the row's small text.
- `RefBadgeMetrics`' constants and the control template agree, and a test says so.
- A badge still fits inside a 36 px history row without changing the row's height.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- The badges' colours, or which icon a kind of reference gets.
- The Refs column's maximum, its grip, or how the header resizes.
- Badges anywhere other than the history list.
