# FEATURE-56BB — Centred windows, repository maximised

**Item:** FEATURE-56BB — Centred windows, repository maximised
**Branch:** `feature/feature-56bb-centred-maximised-windows`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Summary

- The repository window (`MainWindow`) now opens **maximised** and **centred**
  (`WindowState="Maximized"`, `WindowStartupLocation="CenterScreen"`): every time a repository is opened,
  since each opening builds a fresh window. Un-maximised, it is 1440 × 900 in the middle of the screen.
- The start window already opened centred (`CenterScreen`, unchanged); a test now pins it.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/Views/MainWindow.axaml`: the two window properties and a comment.
- `tests/Enigma.GitClient.Desktop.UnitTests/AppWindowsTests.cs`:
  `BothWindows_OpenCentred_AndTheRepositoryWindowMaximised_EveryTime`.
- `docs/roadmap.md`, `docs/plan/FEATURE-56BB.md`: statuses.

**Created**

- `docs/done/FEATURE-56BB.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where | XAML on the window, not `AppWindows` | The window describes itself, as the start and splash windows do |
| The start window | Unchanged; covered by the new test | It already had `CenterScreen` |

## Deviations & follow-ups

- The draft says both windows should open centred; the start window already did. If it still looks
  off-centre on a multi-monitor setup, that is Avalonia centring on the screen the window opens on
  (usually the primary one) rather than the screen the previous window was on — a possible follow-up,
  out of this item's scope.
- Test gate on this Windows host: as recorded in `docs/done/FEATURE-903B.md`.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

Nothing stale: the README says a repository opens "in a window of its own", which is still true.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- `Enigma.GitClient.Core.UnitTests`: 1124 total, 1123 passed, 0 failed, 1 skipped.
- `Enigma.GitClient.Core.IntegrationTests`: 344 total, 342 passed, 0 failed, 2 skipped.
- Desktop, targeted (`AppWindowsTests`, `MainWindowShellTests`, `ShellRenderTests`, `ShellSnapshotTests`,
  `ToolDialogTests`, `ToolbarButtonLookTests`, `AboutDialogTests`, `NavigationRailTests`,
  `SplashScreenTests`, `InstanceTests`): 148 total, 143 passed, 0 real failures, 5 teardown refusals.
  With the verification-only teardown change (see `docs/done/BUG-6DB7.md`; not committed): 146 passed,
  2 teardown `IOException`s (the test's directory still in use) in
  `StartAsync_WithASplash_ClosesItOnlyOnceTheFirstWindowIsOnScreen(start: "repository")` and
  `StartAsync_WithARepositoryPath_OpensStraightIntoIt`.
- The new test passes in both runs.
- Fix budget: 0 cycles used.
