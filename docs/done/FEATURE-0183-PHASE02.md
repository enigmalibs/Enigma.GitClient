# FEATURE-0183-PHASE02 — Default row height of 36

**Item:** FEATURE-0183 — Bigger graph nodes, taller rows (PHASE02)
**Branch:** `feature/feature-0183-phase02-row-height`
**Run:** bugfix/2026-09-16-history-panel-and-graph

## Summary

The history and the graph — which are the same view — open at 36 px rows instead of 26.
`AppSettings.GraphRowHeight` is the one preference both read, the 18–48 range and the settings
page's spinner are unchanged, and 36 sits comfortably inside them.

A default is only half the change, though. `GraphRowHeight` is written to `settings.json`, and
anyone who has run this client already has a file that says 26 — so a new default alone would have
reached nobody who could notice it. `AppSettings.CurrentVersion` therefore goes to 2 and
`SettingsService.Migrate` gets its first real case, the one its own comment was waiting for: a
version-1 file whose row height is still 26 — version 1's own default, and so a number nobody chose
— is raised to 36. Any other value was typed on the settings page and is left exactly as it is, and
a version-2 file is not touched at all, because at version 2 26 is a preference like any other.

## Files / modules touched

**Modified — Core**

- `Configuration/AppSettings.cs` — `GraphRowHeight` default 26 → 36, `CurrentVersion` 1 → 2, and
  `LegacyGraphRowHeight` naming the old default the migration looks for
- `Configuration/SettingsService.cs` — the version 2 case in `Migrate`

**Modified — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Configuration/SettingsServiceTests.cs` — the default, three
  migration cases, and the version assertion now following the constant
- `tests/Enigma.GitClient.App.UnitTests/SettingsPageTests.cs` — the history page's opening row height

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-0183.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Existing settings files | Migrate the old default, keep everything else | A default-only change would be invisible to every existing user, and overwriting a chosen value would be worse than changing nothing |
| How "nobody chose it" is recognised | The stored value equals version 1's default | It is the only evidence the file carries, and it is exactly the evidence the schema version exists to interpret |
| Where the old default lives | `AppSettings.LegacyGraphRowHeight` | A migration keyed on a bare `26` is unreadable a year from now, and the test can name the same constant |
| The version assertion in the file test | Formatted from `CurrentVersion` | A hard-coded `1` made a schema bump fail a test about JSON formatting, which is not what that test is for |
| The 18–48 range | Unchanged | 36 fits, and widening a range nobody complained about is scope nobody asked for |

## Deviations & follow-ups

- **Observation:** a version-1 file that never mentions `graphRowHeight` deserialises straight to the
  new default and needs no migration, which the migration case leaves alone by construction.
- **Follow-up:** the history and the graph still share one row height, as they always have. They are
  the same view, so a second number would be two ways of saying the same thing.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1579  failed: 0  succeeded: 1579  skipped: 0
```

Three tests are new — a version-1 row height nobody changed takes the new default, one somebody
chose is kept, and a version-2 file is never migrated — and three existing ones were updated to the
new default and to the current schema version. One fix cycle was needed: the test asserting the
JSON names its version had the number `1` written into it.
