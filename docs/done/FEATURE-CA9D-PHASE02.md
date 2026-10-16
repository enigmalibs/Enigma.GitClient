# FEATURE-CA9D-PHASE02 — Fresh start on the last screen

**Item:** FEATURE-CA9D — Windows open on the screen in use
**Phase:** PHASE02 — Fresh start on the last screen
**Branch:** `feature/feature-ca9d-phase02-last-screen`
**Run:** feature/2026-10-16-feature-c3f3-feature-ca9d-feature-c56e

## Summary

A fresh start now opens the splash and the first window on the screen the application was last used
on, which is remembered in the settings. If that screen is gone, or none was ever recorded, it uses
the primary screen.

- **`AppSettings.LastScreen`** (`ScreenArea?`, `null` by default) is a remembered value, with no
  schema bump; `settings.json` stays at version 7. A file written before it existed reads as none.
  `Normalised` drops a screen with a non-positive width or height. In the file, the screen is its
  four numbers: `ScreenArea`'s computed `Right`, `Bottom` and `IsEmpty` are `[JsonIgnore]`.
- **Recorded** through `ISettingsService.Update` by `AppWindows.Remember`:
  - each time a window is placed or handed over;
  - each time the current window opens or moves while not minimised;
  - when the current window closes (`Closing`), with its screen. A minimised window closing leaves the
    last screen it was seen on.

  Remembering the same screen again changes nothing and writes nothing (`Update` compares records).
  `App.OnExit` now awaits `ISettingsService.FlushAsync` before the repository context and the host
  are disposed, so a quit by ✕ inside the 400 ms debounce still keeps the screen.
- **Placed:**
  - The **first window** goes on `ScreenPlacement.Choose(screens, settings.LastScreen)`, in
    `AppWindows.Show` when there is no previous window.
  - The **splash** goes there too, in `App`, before `splash.Show()`, through the new public static
    `AppWindows.PlaceForStart(placer, window, hint)`.

  With no screen known to the platform, nothing is placed and the XAML's `CenterScreen` stands.

## Files touched

- Modified:
  - `src/Enigma.GitClient.Core/Configuration/AppSettings.cs`: `LastScreen`, and its `Normalised` rule.
  - `src/Enigma.GitClient.Core/Screens/ScreenArea.cs`: `[JsonIgnore]` on the computed properties.
  - `src/Enigma.GitClient.Desktop/Services/AppWindows.cs`: `ISettingsService`, `PlaceForStart`,
    `Remember`, and the `Closing` record.
  - `src/Enigma.GitClient.Desktop/App.axaml.cs`: the splash placed; the settings flushed on exit.
  - `tests/Enigma.GitClient.Core.UnitTests/Configuration/SettingsServiceTests.cs`: the `LastScreen`
    tests.
  - `tests/Enigma.GitClient.Desktop.UnitTests/WindowScreenTests.cs`: the fresh-start tests; the reader
    moved to the infrastructure.
  - `tests/Enigma.GitClient.Desktop.UnitTests/AppWindowsTests.cs`: see *Deviations*.
- Created:
  - `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/FixedScreenReader.cs`

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| Where the first-window placement lives | `AppWindows.Show` with no previous window, through `PlaceForStart` | The first window may come from `StartAsync` or straight from `ShowStart`, and both go through `Show` | Only in `StartAsync` |
| How the splash is placed | `App` calls the same `PlaceForStart`, before `splash.Show()` | One rule for both windows of a fresh start. PHASE03 swaps the hint for the caller's screen in the same place | A second copy of the rule in `App` |
| How often the screen is written | On every placement, hand-over, move and close, through `Update` | `Update` returns early for an equal record, so a drag within one screen costs nothing, and the debounce gathers the rest | Writing only on close (lost on a crash) |
| Flushing at exit | `FlushAsync().GetAwaiter().GetResult()` in `OnExit`, before the context and host go | The process edge where blocking is allowed (house async rule), next to the existing `StopAsync` | No flush (the last move would be lost inside the debounce) |
| The settings file's shape | `[JsonIgnore]` on `Right`, `Bottom`, `IsEmpty` | The file is read by people; `lastScreen` shows its four numbers and nothing derived | Writing the derived values |

## Deviations & follow-ups

- **A PHASE01 test miss, fixed here.** PHASE01 made the hand-over place the next window `Manual`
  whenever the platform reports screens, and the headless platform does report some
  (`HeadlessScreensStub`). So `AppWindowsTests.BothWindows_OpenCentred_AndTheRepositoryWindowMaximised_EveryTime`,
  which asserts `CenterScreen` throughout, would have failed from PHASE01 on. The Desktop suite is
  compiled, not run, here, so it went unseen. That test now runs on a desktop that reports no screens
  (`FixedScreenReader()`), where the platform centres the windows as it asserts. Centring on a known
  screen is covered by `WindowScreenTests`.
- **Pre-empted flakiness:** the new `RememberingTheSameScreenAgain_WritesNothing` awaits its writes
  before the test folder is deleted. It is the same teardown race fixed in FEATURE-C3F3-PHASE03.
- **Manual check pending (Windows):** quit on screen 2, start again from the taskbar → the splash and
  the start window on screen 2; unplug screen 2, start → the primary screen.

## Documentation sweep

- Nothing changed. The README does not describe the settings file's remembered values.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**.
- `Enigma.GitClient.Core.UnitTests`: **1280 passed, 1 skipped, 0 failed**. This includes 6 new tests:
  - the `LastScreen` round trip, written as four numbers;
  - an older file having none;
  - `Normalised` dropping a screen with no area (3 cases) and keeping one with an area;
  - remembering the same screen twice raising one change and costing no write.

  `Choose` for a screen that has gone is covered by PHASE01's `ScreenPlacementTests`.
- `Enigma.GitClient.Core.IntegrationTests`: **386 passed, 2 skipped, 0 failed**.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** (standing instruction).
  `WindowScreenTests` now covers, for a fresh start:
  - the first window on the primary screen when none is remembered;
  - the first window on the remembered screen;
  - the primary screen when the remembered one is gone;
  - the screen recorded as windows are placed, moved and closed;
  - the screen surviving a restart through the flush.

  It also keeps PHASE01's hand-over tests.
