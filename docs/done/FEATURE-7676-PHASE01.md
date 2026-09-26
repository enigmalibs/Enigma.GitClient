# FEATURE-7676-PHASE01 — Side by side by default

**Item:** FEATURE-7676 — Diff panel defaults and typography (PHASE01)
**Branch:** `feature/feature-7676-phase01-side-by-side`
**Run:** feature/2026-09-16-diff-panel-layout

## Summary

The diff panel opens side by side. `AppSettings.DiffView` defaults to `DiffView.SideBySide`, and
because `DiffViewerViewModel.Apply(AppSettings)` already takes the stored shape on at construction
and on every settings change, that one default is the whole visible change — the toolbar's two
toggles, the rendering itself and the selection behaviour are untouched.

A default alone would have reached nobody, though: `DiffView` is written to `settings.json`, and
every file that exists today says `Unified`. So `AppSettings.CurrentVersion` goes to 3 and
`SettingsService.Migrate` gains its second case, written to read the same way as the first — the
value an older build shipped as *its* default is a value nobody chose, so it moves; anything else
was picked on the settings page and is kept. A file written before version 3 that still says
`Unified` becomes `SideBySide`; one that says `SideBySide` is already where version 3 would put it;
and a version 3 file is not touched at all, because from version 3 on `Unified` is a preference like
any other. The two cases compose, which a version 1 file carrying both old defaults now proves.

One smaller thing came with it: `Normalised()`'s fallback for an out-of-range `DiffView` was the
literal `Unified`, which after this change would have meant "an unreadable value silently opts out
of the new default". It now falls back to `Defaults.DiffView`, so there is one answer to what the
default shape is.

## Files / modules touched

**Modified — Core**

- `Configuration/AppSettings.cs` — `DiffView` default `Unified` → `SideBySide`, `CurrentVersion`
  2 → 3, the new `LegacyDiffView` constant naming what the migration looks for, and the
  `Normalised()` fallback following `Defaults`
- `Configuration/SettingsService.cs` — the version 3 case in `Migrate`, and a remark rewritten to
  state the rule the cases share rather than re-describing each one

**Modified — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Configuration/SettingsServiceTests.cs` — the default, the
  round trip (which now stores `Unified`, the value that is no longer the default), and three new
  migration cases
- `tests/Enigma.GitClient.App.UnitTests/DiffViewerTests.cs` — a new test for the opening shape, and
  five tests that were implicitly relying on the viewer opening unified now ask for that shape
- `tests/Enigma.GitClient.App.UnitTests/SettingsPageTests.cs` — the two diff-preference tests now
  move the preference *away* from its default, so they still prove something

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-7676.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Reaching files that already say `Unified` | Schema v3 with a migration, exactly as v2 did for the row height | A default-only change is invisible to every existing user, and the house already has the answer to this question |
| The `Normalised()` fallback for an undefined value | `Defaults.DiffView` rather than a repeated literal | Two places stating the default shape is one place too many, and the literal would have quietly kept the old behaviour |
| The round-trip test | Stores `Unified` now | Storing what is already the default proves nothing about persistence |
| Tests that opened unified by accident | Made to ask for the shape they assert on | They were testing the unified rendering, not the default; saying so keeps them honest through the next default change |
| `FilesView`'s similar fallback mismatch (`List` where the default is `Tree`) | Left alone | Not this phase's change, and touching it would move a behaviour nobody asked about |

## Deviations & follow-ups

- **None from the plan.** All three acceptance criteria are covered by tests, including the two
  migrations composing on a single version 1 file.
- **Observation:** `Normalised()` still falls back to `FilesView.List` while the default is
  `FilesView.Tree`. Harmless — it only applies to a hand-edited out-of-range value — but it is the
  same shape of inconsistency this phase fixed for `DiffView`, and worth a line in a future tidy-up.
- **Line endings (recommendation only):** no CRLF churn observed in the touched files. No action
  taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1583  failed: 0  succeeded: 1583  skipped: 0
```

Four tests are new — the viewer's opening shape, a version 2 file whose `Unified` nobody chose, a
version 3 file whose `Unified` somebody did choose, and a version 1 file going through both
migrations at once. Green on the first run; no fix cycle was needed.

## Documentation sweep

The README and the release notes both describe the diff viewer as "unified or side by side" without
claiming which one opens first, so neither was made stale by this change. Nothing edited.
