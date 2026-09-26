# FEATURE-2FDF — More room between graph lanes

**Item:** FEATURE-2FDF — More room between graph lanes
**Branch:** `feature/feature-2fdf-lane-spacing`
**Run:** feature/2026-09-17-graph-space-diff-dialog

## Summary

The graph's parallel lines are now 20 device-independent pixels apart instead of 16.

`AppSettings.GraphLaneWidth` is the distance between two lanes — `CommitGraphCell` draws lane *n* at
`LanePadding + LaneWidth / 2 + n * LaneWidth` — so raising its default is the whole change as far as
the rendering is concerned. At 16 a commit node, drawn at radius 9, left six pixels between its edge
and the neighbouring lane's line; at 20 it leaves ten, and two branches running side by side read as
two lines. The node itself does not move: `CalculateNodeRadius` fits it to the smaller of the row and
the lane, and at this row height the row is still what limits it.

The width stays a preference. What a settings file already holds is therefore the interesting half of
the dev, and it is handled the way the house has handled it twice before: the schema version goes to
4, `AppSettings.LegacyGraphLaneWidth` records the 16 that versions 1 to 3 shipped as *their* default,
and `SettingsService.Migrate` moves a pre-version-4 file to 20 only when it still says 16 — a value
nobody chose. A file that says anything else is left alone, and from version 4 on, 16 is a preference
like any other.

## Files / modules touched

**Modified — Core**

- `Configuration/AppSettings.cs` — `GraphLaneWidth`'s default is 20, `CurrentVersion` is 4, and
  `LegacyGraphLaneWidth` joins the other two legacy-default constants
- `Configuration/SettingsService.cs` — the fourth migration case, and the `<remarks>` describing it

**Modified — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Configuration/SettingsServiceTests.cs` — the pinned default
  is 20; three new cases (a version 3 file that still says 16 takes 20, one that says 24 keeps it, a
  version 4 file that says 16 is never migrated) and the "every migration at once" case now carries a
  lane width too
- `tests/Enigma.GitClient.App.UnitTests/SettingsPageTests.cs` — the history page's starting metrics
  are asserted against `AppSettings.Defaults` rather than against literals

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-2FDF.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Whether the pinned-defaults test keeps its literal | It keeps it, updated to 20 | That test exists to state what a fresh install runs on; asserting it against `AppSettings.Defaults` would have made it assert that a value equals itself |
| Whether the page test keeps its literals | It moves to `AppSettings.Defaults` | It is testing that a preference reaches the history page, not what the preference is, so it should not have to be edited by the next default change |

## Deviations & follow-ups

- **None from the plan.** All five acceptance criteria are covered.
- **Follow-up.** The settings page still offers 8–40 in steps of 2, which brackets the new default
  comfortably; nothing there needed to move.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build
  0 Warning(s)
  0 Error(s)

dotnet test
  Test run summary: Passed!
  total: 1629  failed: 0  succeeded: 1629  skipped: 0
```

Three tests are new. One fix cycle: `EveryPreferenceHasADefault` pins the shipped defaults literally
and had to be told the new one.

## Documentation sweep

Scanned the README and the release notes. Both mention the lane width only as a preference that
exists ("graph row height and lane width"), which this dev leaves true, and neither states a default
value. Nothing edited.
