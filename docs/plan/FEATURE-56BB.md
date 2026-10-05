# FEATURE-56BB — Centred windows, repository maximised

**Status:** DONE — see `docs/done/FEATURE-56BB.md`
**Type:** FEATURE
**Branch:** `feature/feature-56bb-centred-maximised-windows`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Objective

The start window (the repositories) and the repository window (the history) both open in the centre of
the screen, and the repository window always opens maximised.

## Context & constraints

- `Views/StartWindow.axaml`: 1100 × 720, `WindowStartupLocation="CenterScreen"` already.
- `Views/MainWindow.axaml`: 1440 × 900, no startup location, no window state.
- `Services/AppWindows.Show` builds a fresh window (transient) each time one is shown.
- Tests: `AppWindowsTests` (real windows on the headless platform), `SplashScreenTests` (asserts the
  splash's `CenterScreen`).

## Steps

1. `MainWindow.axaml`: `WindowStartupLocation="CenterScreen"` and `WindowState="Maximized"` — so the
   size it restores to is centred too.
2. `StartWindow.axaml` keeps `CenterScreen`.
3. Tests: the start window shown by `AppWindows` opens centred; the repository window opens centred and
   maximised, the first time and after going back to the start window.

## Acceptance criteria

- Both windows open centred; the repository window opens maximised every time.
- Build clean, whole suite green, the new tests among them.

## Out of scope

- Remembering window positions or sizes between runs.
- Choosing the screen the previous window was on (multi-monitor placement).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How | XAML `CenterScreen` + `Maximized` on the window | The platform's own placement; one line each | Computing positions in `AppWindows` |
| The restored size | Kept 1440 × 900, centred | Un-maximising lands somewhere sensible | Restoring to a remembered size |
| Multi-monitor | Not handled | Not asked; Avalonia centres on the screen it opens on | Following the previous window's screen |
