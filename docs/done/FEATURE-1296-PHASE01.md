# FEATURE-1296-PHASE01 — The interval setting

**Item:** FEATURE-1296 — Automatic fetch and refresh
**Branch:** `feature/feature-1296-phase01-interval-setting`
**Run:** feature/2026-09-22-home-window-merges-refresh

## Summary

A new preference, `AppSettings.AutoRefreshSeconds`, holds how often an open repository is fetched and
refreshed on its own. It defaults to **15 seconds**, and **0 turns it off**. `Normalised()` keeps any
other value between 5 and 3600 seconds, so a hand-edited 1 cannot hammer a remote. A settings file
written before the key existed simply reads the default, which is why the schema version stays at 4.

The Settings page's Git card has a *Fetch and refresh automatically* row with a number editor (0 to
3600, in steps of 5). The card's description now mentions it. This phase only adds the setting;
PHASE02 is what acts on it.

## Files / modules touched

- `src/Enigma.GitClient.Core/Configuration/AppSettings.cs` — `AutoRefreshSeconds`, its bounds as
  constants, and its clamp
- `src/Enigma.GitClient.App/ViewModels/Pages/SettingsPageViewModel.cs` — `AutoRefreshSeconds` (and
  its change notification when the store changes), the bounds for the editor
- `src/Enigma.GitClient.App/Views/Pages/SettingsPageView.axaml` — the editor row in the Git card
- `tests/Enigma.GitClient.Core.UnitTests/Configuration/SettingsServiceTests.cs` — the default; the
  clamp (off stays off, 1 → 5, 99 999 → 3600); an older file reads the default; the value survives a
  restart
- `tests/Enigma.GitClient.App.UnitTests/SettingsPageTests.cs` — the page shows and writes the value
  and clamps it; the editor is bound once its card is open

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Negative values | Treated as off | "Less than never" has only one sensible reading |
| The editor's step | 5 seconds | The floor is 5, and a step of 1 between 5 and 3600 is a lot of clicking |
| Where the row sits | The Git card, above the path to git | It is about how the client talks to the repository and its remotes |

## Deviations & follow-ups

- Two fix cycles, both on the new view test: the view was not in a laid-out tree; then the Git card
  was collapsed, and a collapsed card has not realised its content. The test now opens the cards, as a
  user would. No production code changed in either cycle.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 1907 passed, 0 failed (10 new).
