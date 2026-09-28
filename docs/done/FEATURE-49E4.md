# FEATURE-49E4 — About in the start window

**Item:** FEATURE-49E4 — About in the start window
**Branch:** `feature/feature-49e4-about-on-start`
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Summary

The start window's home, the Repositories page, now ends its header with an **Info** button, set apart
from Open, Clone and Create by a thin divider. It opens the same About dialog as the Info button at the
end of the repository window's strip, with the same tooltip ("About") and automation name ("About
Enigma git client").

`RepositoriesPageViewModel` takes `IAboutDialogService` and exposes `OpenAboutCommand`, as
`MainWindowViewModel` and `SettingsPageViewModel` already do. The Settings page's About card still
opens the dialog too.

## Files / modules touched

**Created**

- `docs/done/FEATURE-49E4.md`

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/RepositoriesPageViewModel.cs` — `IAboutDialogService`,
  `OpenAboutCommand`.
- `src/Enigma.GitClient.App/Views/Pages/RepositoriesPageView.axaml` — the divider and the Info
  `toolbar` button at the end of the header.
- `tests/Enigma.GitClient.App.UnitTests/AboutDialogTests.cs`:
  - `TheStartWindowsHome_OpensIt`;
  - `TheStartWindowsHome_HasTheAboutButtonAtTheEndOfItsHeader` — a real headless click on the
    rendered button, which must sit after Create.
- `docs/roadmap.md`, `docs/plan/FEATURE-49E4.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The button's look | `toolbar` with the `Info` icon, behind a 1 px divider | The repository strip's own About button and its group separator. The page's text buttons are the repository actions; About is not one of them. |
| How the view test clicks | Headless `MouseDown`/`MouseUp` on the button's centre | Raising `Button.ClickEvent` does not run a button's command (the first draft of the test found that). A real click proves the binding. |

## Deviations & follow-ups

- None from the plan.
- Line endings: no CRLF churn.

## Documentation sweep

The README and `docs/RELEASE.md` mention the About dialog but not where it opens from. No edit. The
release notes are FEATURE-10AA's.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2454 passed**, 0 failed, 0 skipped (2 new).
- Fix budget: no fix cycle. The view test's first draft raised `ClickEvent`, which runs no command. It
  was corrected to a real click before the first suite run.
