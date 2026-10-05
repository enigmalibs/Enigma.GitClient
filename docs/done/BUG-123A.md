# BUG-123A — A repository opens on its history

**Item:** BUG-123A — A repository opens on its history
**Branch:** `bugfix/bug-123a-repository-opens-on-history`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Summary

The rail's selection lives in the singleton navigation service and outlives the repository window, and
`MainWindowViewModel.InitialiseAsync` selects the History page only the first time. So a page chosen on
the rail (Profiles, Settings) before *Close this repository* was still selected when the next repository
opened.

`MainWindowViewModel` now sends the rail to **History** whenever a repository is opened — the context's
`Repository` becoming a repository. That covers both ways a repository reaches the window:

- closed, then another one opened from the start window;
- a clone started from the repository window's own Profiles page, opened in the same window.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/MainWindowViewModel.cs`: `OnRepositoryContextPropertyChanged`.
- `tests/Enigma.GitClient.Desktop.UnitTests/AppWindowsTests.cs`:
  `OpeningAnotherRepository_ShowsItsHistory_WhateverPageTheRailWasLeftOn`,
  `ARepositoryOpenedFromTheRepositoryWindowsProfilesPage_ShowsItsHistory`, and their helpers.
- `docs/roadmap.md`, `docs/plan/BUG-123A.md`: statuses.

**Created**

- `docs/done/BUG-123A.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Which notification | `Repository` only, not `IsRepositoryOpen` as well | Both are raised on an open; one navigation is enough |
| How the tests pick a page | Through the navigation's `SelectedItem`, as a click on the rail does | `GoTo` also sets `Current`; a click does not, and the bug lived exactly there |
| The tests' repositories | `git init` with no commit | No read-only objects, so the Windows teardown cannot hide the verdict |
| The clone test | Waits for the history's first read before ending | Otherwise git is still running in the folder the teardown deletes |

## Deviations & follow-ups

- None from the plan.
- `ShellNavigation.Current` is not updated by a click on the rail, only by `GoTo`. Nothing reads it in a
  way that matters today; a follow-up could keep it in step with the navigation's selection.
- Test gate on this Windows host: as recorded in `docs/done/FEATURE-903B.md`.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

Nothing stale: no prose doc says which page a repository opens on.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- `Enigma.GitClient.Core.UnitTests`: 1132 total, 1131 passed, 0 failed, 1 skipped.
- `Enigma.GitClient.Core.IntegrationTests`: first run 341 passed, 1 failed —
  `RepositoryServiceTests.CloneAsync_ClonesALocalRepositoryAndReportsProgress` ("Collection was empty":
  the progress `Progress<T>` posts had not arrived under load). This dev changes no Core code; the test
  passed 5 times out of 5 alone, and the whole suite re-run gave 344 total, 342 passed, 0 failed,
  2 skipped. Recorded as a pre-existing timing flake.
- Desktop, targeted (`AppWindowsTests`, `MainWindowShellTests`, `NavigationRailTests`,
  `ProfileIntegrationsTests`, `ConflictResolutionPageTests`): 85 total, 59 passed, 0 real failures,
  26 teardown refusals. With the verification-only teardown change (not committed): 81 passed; 2 teardown
  `IOException`s; 2 × `ConflictResolutionPageTests` comparing `"one\nour version\nthree\n"` with git's
  `"…\r\n"` (this machine's `core.autocrlf=true`).
- The two new tests pass, with no teardown problem (their repositories have no commit).
- Fix budget: 0 cycles used.
