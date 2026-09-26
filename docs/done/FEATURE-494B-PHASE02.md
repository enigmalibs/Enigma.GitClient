# FEATURE-494B-PHASE02 — A roomier badge, a size bigger

**Item:** FEATURE-494B — History refs: full names, bigger badges
**Branch:** `feature/feature-494b-phase02-bigger-badges`
**Run:** feature/2026-09-21-refs-tags-and-dragging

## Summary

A ref badge in the history is drawn a size larger, with room around it.

The pill's padding goes from `4,0` to `7,2` — so it now has vertical room as well as horizontal, and
8 px a side counting its 1 px border — its corner radius from 3 to 4 so the corner still reads
against the taller shape, the gap between the icon and the label from 4 to 5, and the icon and the
label both from 11 to 13. `RefBadgeMetrics`' constants follow the template exactly, which is what
keeps the Refs column measured at the size the badges are actually drawn at.

Those constants are the one thing in this control that can silently fall out of step with the
template: they are constants rather than bindings on purpose, because a per-badge binding to a theme
resource would measure thousands of rows through the resource system to answer one number. So this
phase also adds the test that reads every one of them back off a realised badge — the padding, the
border, the spacing, the icon's size and the label's font size — so the two cannot drift apart again.

## Files / modules touched

**Modified — App**

- `Themes/Controls.axaml` — the `RefBadge` template's padding, corner radius, spacing, icon size and
  font size, with a comment pointing at the metrics that mirror them
- `Controls/RefBadgeMetrics.cs` — `FontSize`, `IconSize`, `IconSpacing` and `HorizontalPadding`
  follow the template (13, 13, 5, 8), and the class remark says where the 8 comes from

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — the constants are read back off a
  realised badge; a badge has room on every side and still fits a default history row; and the
  chrome a badge measures is the template's own numbers, with two badges still separated by the
  strip's spacing

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-494B.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the metrics test reaches the icon | `Enigma.Icons.Avalonia`'s `Icon` control, found among the pill's descendants, and its `Size` | It is the property the template sets; asserting the rendered glyph's bounds would be asserting the icon library |
| Padding versus border in `HorizontalPadding` | The constant is the sum (7 + 1), and the test asserts the sum | It is what a badge actually occupies, which is the only thing the column cares about |
| The row-height assertion | `AppSettings.Defaults.GraphRowHeight` rather than the literal 36 | The row height is a preference with a default; the badge has to fit the default, whatever it becomes |

## Deviations & follow-ups

- **None from the plan.** All five acceptance criteria are covered.
- A badge is now 13 px against the row's 12 px author, date and hash. That is deliberate — it was
  asked for — and it is worth knowing that the badge is no longer the smallest text on a history row.
- `BadgeSpacing` (4) is untouched: it is the gap *between* badges, not inside one, and nothing asked
  for the strip to loosen.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1781  failed: 0  succeeded: 1781  skipped: 0
```

Three tests added (1778 → 1781). No fix cycle: green on the first run.

## Documentation sweep

`README.md` and `RELEASENOTES.md` name the ref badges as a feature of the graph and say they sit in
a column of their own; neither describes their size. The diff made nothing in them wrong, so no
edits. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
