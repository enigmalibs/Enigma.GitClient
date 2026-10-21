# FEATURE-1585 — Info bars at the bottom left

**Item:** FEATURE-1585 — Info bars at the bottom left
**Branch:** `feature/feature-1585-infobar-bottom-left`
**Run:** feature/2026-10-21-ui-polish-release-650

## Summary

The info bar was a band across the top of the window. It now sits in the bottom-left corner of
both windows (start and repository), starting right of the navigation rail.

**Width:**

- a quarter of the window;
- never less than 360 px;
- never more than the room right of the rail;
- recomputed on every resize.

**What it no longer covers:**

- the repository strip, the page headers, and the rail's *Profiles* / *Settings* footer.

**How:**

- `HostInfoBar` overrides the library theme's `Stretch`/`Top` alignment with `Left`/`Bottom` in both
  windows' markup.
- A new `InfoBarPlacement` helper sets the bar's `Width` and its left margin (the rail's `PaneSize`).
  Both windows call it at construction and on `SizeChanged`, as `MainWindow` already does for its
  tool dialog.
- The bar stays the root panel's last child, so it still draws over the dialogs.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Desktop/Views/InfoBarPlacement.cs`: `WidthFor` and `Place`.
- `tests/Enigma.GitClient.Desktop.UnitTests/InfoBarPlacementTests.cs`:
  - the width at five window sizes (a quarter, the minimum, the cap, a tiny window);
  - a theory over both windows: left and bottom aligned, offset by the rail, 400 px in a 1600 px
    window, 500 px after a resize to 2000 px.
- `docs/done/FEATURE-1585.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Views/MainWindow.axaml` and `StartWindow.axaml`: the rail is named
  `NavigationRail`; the bar is aligned left and bottom.
- `src/Enigma.GitClient.Desktop/Views/MainWindow.axaml.cs` and `StartWindow.axaml.cs`: the bar is
  placed at construction and on every resize.
- `docs/roadmap.md`, `docs/plan/FEATURE-1585.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the rail width comes from | The named `NavigationView`'s `PaneSize` | One source of truth: the same value the rail is drawn with (`RepositoryRailWidth` / `StartRailWidth`) |
| The minimum versus the window | The cap wins: never wider than the room right of the rail | A bar wider than the window would be clipped |
| Helper's home | `Views`, `internal static` | Used only by the two windows' code-behind; tested through `InternalsVisibleTo`, like `ToolDialogSizeFor` |

## Deviations & follow-ups

- **The Desktop suite is not run** on this machine (standing instruction). The new tests are written
  and compiled.
  - The resize assertion relies on the headless platform honouring `Width` on a shown, restored
    window.
  - Worth a manual check: a warning in the repository window, then resize the window.
- The template's own 24 px margin still surrounds the card, so the card is inset 24 px from the rail
  and the window's bottom edge. That looks deliberate in the library and is left alone.
- Line endings: the touched files are LF; no CRLF churn.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: succeeded, **0 warnings, 0 errors** (AVLN included).
- `Enigma.GitClient.Core.UnitTests`: 1307 total, 1306 passed, 0 failed, 1 skipped (pre-existing).
- `Enigma.GitClient.Core.IntegrationTests`: 388 total, 386 passed, 0 failed, 2 skipped (pre-existing).
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** (standing instruction). Seven new test
  cases in `InfoBarPlacementTests`. `MainWindowShellTests`' "hosts last" test still holds by
  construction.

## Documentation sweep

Nothing stale: no README, `CLAUDE.md` or `docs/` prose describes where the info bar appears. The
6.5.0 notes (FEATURE-6E21) will announce it.
