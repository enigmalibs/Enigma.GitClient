# FEATURE-CA9D-PHASE01 — Hand-over keeps the screen

**Item:** FEATURE-CA9D — Windows open on the screen in use
**Phase:** PHASE01 — Hand-over keeps the screen
**Branch:** `feature/feature-ca9d-phase01-hand-over-screen`
**Run:** feature/2026-10-16-feature-c3f3-feature-ca9d-feature-c56e

## Summary

The window that replaces another now opens on the screen the other was on. Opening a repository
maximises its window on the screen the start window was on. *Close repository* brings the start window
back, centred on the screen the repository window was on.

- **Core `Screens`** (`src/Enigma.GitClient.Core/Screens/`), plain integer arithmetic:
  - `ScreenArea(X, Y, Width, Height)`: a `readonly record struct` in physical pixels, with `Contains`
    (left and top edges in, right and bottom out) and `OverlapWith`.
  - `ScreenInfo(Bounds, WorkingArea, Scaling, IsPrimary)`.
  - `ScreenPlacement.Choose(screens, hint)` tries, in order: exact bounds; the screen holding the
    hint's centre; the one the hint overlaps most; the primary; the first. It returns `null` only for
    no screens.
  - `ScreenPlacement.FromWindow(screens, frame)`: the screen holding most of a window.
  - `ScreenPlacement.CenterIn(workingArea, width, height)`: the corner that centres a frame, kept on
    the working area's edge along a side where the frame is larger.
- **Desktop `Services/WindowPlacer`:**
  - `IScreenReader` / `AvaloniaScreenReader` turns Avalonia's `Screens` into `ScreenInfo`s.
  - `IWindowPlacer` / `WindowPlacer` offers `ScreensOf`, `ScreenOf(window)` (through `FromWindow`, from
    the window's position and frame at its desktop scaling) and `PlaceOn(window, screen)`.
    `PlaceOn` sets `WindowStartupLocation = Manual` and `Position = CenterIn(screen.WorkingArea,
    width × screen.Scaling, height × screen.Scaling)`, so a mixed-DPI hand-over is centred on the
    screen it lands on. The repository window keeps `WindowState = Maximized`: placed first, it
    maximises there.
- **`AppWindows`:**
  - It remembers the current window's screen (`LastScreen`) each time the window opens or moves while
    not minimised.
  - `Show` places the next window, before `Show()`, on the previous window's screen, or on the
    remembered one when the previous window is minimised.
  - With no previous window, or no screen known, the platform's own placement stands, as before.
  PHASE02 makes the first window use the last screen too.

## Files touched

- Created:
  - `src/Enigma.GitClient.Core/Screens/ScreenArea.cs`, `ScreenInfo.cs`, `ScreenPlacement.cs`
  - `src/Enigma.GitClient.Desktop/Services/WindowPlacer.cs`
  - `tests/Enigma.GitClient.Core.UnitTests/Screens/ScreenPlacementTests.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/WindowScreenTests.cs`
- Modified:
  - `src/Enigma.GitClient.Desktop/Services/AppWindows.cs`: the placer, `LastScreen`, the hand-over
    placement.
  - `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`: `IScreenReader`,
    `IWindowPlacer`.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| `ScreenArea`'s kind | `readonly record struct` | A small value compared by value; `ScreenArea?` reads naturally as "no hint", and PHASE02 stores one in the settings (reflection-based System.Text.Json reads it) | A class record |
| Where the screens are read | `IScreenReader`, a seam of its own under `WindowPlacer` | The headless platform's screens are not two screens at 100 % and 150 %. The tests stand their own desktop in while every placement rule stays real | Faking the whole placer, which would test nothing |
| `Choose` with no hint | The primary screen | The plan's last two rules ("else the primary; else the first") apply to any hint that finds nothing, including none | `null` |
| Hand-over with no screen known | Placement skipped; the XAML's `CenterScreen` stands | "With no previous window, today's behaviour", and the same when the platform reports no screens | Placing at (0, 0) |
| A window's frame when it is not shown | `FrameSize`, else `ClientSize`, else the XAML's `Width` × `Height` | A window placed before `Show()` has no frame yet; its stated size is what it opens at (the restore size for the maximised repository window) | Assuming 800 × 600 |
| Rounding the scaled size | `Math.Round` | 1100 × 1.25 is 1375 exactly, but other scalings are not; rounding keeps the window off-centre by at most half a pixel | Truncating |
| `Contains` at the shared edge | Right and bottom edges excluded | Two screens side by side never both hold the same point, so the centre rule picks one | Both edges included |

## Deviations & follow-ups

- No deviation from the plan. `ScreenArea` gains `Contains`, `OverlapWith`, `Right`, `Bottom` and
  `IsEmpty`, which the rules need.
- **Manual check pending (Windows, two screens):**
  1. Start window moved to screen 2 → open a repository → it maximises on screen 2.
  2. *Close repository* → the start window comes back centred on screen 2.
  3. Repeat with one screen at 100 % and the other at 150 %.

  This headless machine cannot run it. The release runbook (FEATURE-C56E) lists it before tagging.
- Linux on X11 is best effort, because the window manager may ignore the position. Native Wayland
  ignores client positions entirely, which is not a defect.

## Documentation sweep

- Nothing changed in this phase. The README describes no window placement. The release notes
  (FEATURE-C56E) cover the whole item.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**.
- `Enigma.GitClient.Core.UnitTests`: **1274 passed, 1 skipped, 0 failed**. This includes 19 new
  `ScreenPlacementTests`:
  - `Choose`: exact bounds; a screen that changed size found by its centre; overlap; a screen that is
    gone falling back to the primary; no hint; no primary; no screens;
  - `FromWindow`: straddling two screens; a maximised window reaching past its screen's edges; on no
    screen; no screens;
  - `CenterIn`: smaller, equal and larger frames (also larger one way only); a working area offset by
    a taskbar; negative coordinates left of the primary; a 150 % target;
  - the shared edge, and an empty area.
- `Enigma.GitClient.Core.IntegrationTests`: **386 passed, 2 skipped, 0 failed**.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** (standing instruction).
  `WindowScreenTests` adds 7 tests, on a desktop of two screens with the second at 150 %:
  - the first window placed as before;
  - the repository window `Manual` at `CenterIn` for the start window's screen, at that screen's
    scaling, and still maximised;
  - the start window brought back on the repository window's screen;
  - a minimised window handing over to the screen it was last seen on;
  - one screen: centred on it;
  - no screens: nothing placed;
  - the placer finding a straddling window's screen.
