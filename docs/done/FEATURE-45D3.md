# FEATURE-45D3 — Info bars close after 2.5 seconds

**Item:** FEATURE-45D3 — Info bars close after 2.5 seconds
**Branch:** `feature/feature-45d3-info-bars-2-5-seconds`
**Run:** feature/2026-10-01-polish-release-5-1

## Summary

A success or an informational info bar now closes itself after **2.5 seconds** instead of 5. A
warning or an error still stays until it is closed.

- `InfoBarServiceExtensions.TransientDisplayDuration` is `TimeSpan.FromSeconds(2.5)`. It is the one
  value `Notify` hands the bar for `Success` and `Info`. Every report in the app goes through
  `Notify`, so this covers both windows.
- Its documentation already refers to the constant, not to a number, so it needed no change.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.App/Services/InfoBarServiceExtensions.cs`: the value.
- `tests/Enigma.GitClient.App.UnitTests/InfoBarNotificationTests.cs`: the spec value pinned
  literally (`TwoAndAHalfSeconds`), for the recorded notifications and for the real bar.
- `tests/Enigma.GitClient.App.UnitTests/SettingsPageTests.cs`,
  `tests/Enigma.GitClient.App.UnitTests/MergeOperationTests.cs`,
  `tests/Enigma.GitClient.App.UnitTests/ProfilesPageTests.cs`: compare against
  `InfoBarServiceExtensions.TransientDisplayDuration`. What they check is that the report is timed.
  `SettingsPageTests` gains the `Enigma.GitClient.App.Services` using.
- `docs/roadmap.md`, `docs/plan/FEATURE-45D3.md`: statuses.

**Created**

- `docs/done/FEATURE-45D3.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `FromSeconds(2.5)` or `FromMilliseconds(2500)` | `FromSeconds(2.5)` | It reads as the spec says it. `TimeSpan.FromSeconds(double)` is exact for 2.5 |

## Deviations & follow-ups

- **None from the plan.**
- Line endings: no CRLF churn.

## Documentation sweep

`README.md`, `docs/RELEASE.md` and `SECURITY.md` do not give the info bar's duration. The "5
seconds" in `RELEASENOTES.md` belongs to 1.1.0's section and stays as history. The change goes into
the 5.1.0 notes with FEATURE-0C53. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2625 passed**, 0 failed, 0 skipped.
- Fix budget: 0 cycles used.
