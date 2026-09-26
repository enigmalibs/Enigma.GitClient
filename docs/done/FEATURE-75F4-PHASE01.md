# FEATURE-75F4-PHASE01 — The splash screen

**Item:** FEATURE-75F4 — A splash screen and an About box
**Branch:** `feature/feature-75f4-phase01-splash-screen`
**Run:** feature/2026-09-26-release-1-0-0

## Summary

The application now starts behind a splash screen built like Enigma.MarkdownEditor's: a borderless
420 × 260 window centred on the screen, off the taskbar and on top, on the `EnigmaSurfaceBrush`
surface with a subtle border, showing the 96-pixel icon, **Enigma git client** at 18 SemiBold and
"Version 1.0.0" in the secondary foreground.

- **When it appears.** In `App.OnFrameworkInitializationCompleted`, right after the settings are read
  and the theme applied, so it is painted in the user's theme from its first frame. The start itself
  is then posted at `DispatcherPriority.Background`, so the message loop paints the splash before the
  first window is built.
- **How long it stays.** A floor of one second (`SplashTiming.MinimumDisplay`), measured from when it
  was shown: a start slower than that waits for nothing; a faster one waits out the rest.
- **How it goes.** `IAppWindows.StartAsync` takes a `SplashHandOver`. After the slow part of the start
  (opening the repository named on the command line, if any) it waits out the floor, shows the start
  or repository window, and only then closes the splash — in a `finally`, so a start that throws still
  takes the splash away. The application ends when its last window closes, so the order is what keeps
  the process alive across the hand-over.
- `ProductInformation.DisplayName` ("Enigma git client") is the human name the splash — and, next, the
  About dialog and the Linux launcher — presents; `Name` stays "Enigma.GitClient" because it is also the
  user agent's product token, which cannot carry a space. `ProductInformation.Version` exposes the
  version as a property `x:Static` can read.

## Files / modules touched

**Created — App**

- `Assets/app-256.png` — the 256-pixel frame of `app.ico`, copied byte for byte (the frame was already
  PNG-encoded; its header was checked to say 256 × 256)
- `Views/SplashWindow.axaml`, `Views/SplashWindow.axaml.cs`
- `Views/SplashTiming.cs`
- `Services/SplashHandOver.cs`

**Modified**

- `Core/Diagnostics/ProductInformation.cs` — `DisplayName`, `Version`
- `App/Services/AppWindows.cs` — `StartAsync(path, splash)`: the floor, then the first window, then the
  splash closed
- `App/App.axaml.cs` — the splash shown after the theme, the start posted at Background priority

**Tests**

- `App.UnitTests/SplashScreenTests.cs` (new) — the remaining delay before, at and after the floor and
  never negative; the floor is one second; the splash shows the name, "Version" and the version; its
  icon asset resolves at 96 px; its size and window flags; its surface and border resolve in either
  theme; a rendered frame is drawn (saved as `snapshots/splash.png`); the hand-over holds the splash
  on a stopped clock until its floor, and closes it only once
- `App.UnitTests/AppWindowsTests.cs` — with a splash, for no path, a folder that is no repository and a
  real repository, the splash closes only when the replacing window is already visible; with a
  one-second floor on a stopped clock, nothing is shown until the clock moves
- `App.UnitTests/Infrastructure/UiServiceDoubles.cs` — `RecordingAppWindows.StartAsync` follows the new
  signature (and closes a splash it is given, as the real one would)

## Deviations & follow-ups

- **A splash hand-over object instead of a posted timer.** MarkdownEditor's `App` holds the splash and
  swaps the windows with a one-shot `DispatcherTimer`; here the first window is shown by
  `AppWindows.StartAsync`, which is where the hand-over has to happen, so the splash is given to it as a
  `SplashHandOver` measured on a `TimeProvider` — which is also what lets a test stop the clock.
- **Follow-up, not caused by this phase:** the process does not end on `SIGTERM`. The generic host's
  console lifetime catches the signal, stops the host ("Application is shutting down…"), and cancels
  the default termination, while Avalonia's loop carries on. Seen while checking a real launch; worth a
  bug item of its own (disable the console lifetime's signal handling, or shut the desktop lifetime
  down from `ApplicationStopping`).
- The captured frame was looked at: icon centred, name and version below it, on the dark surface
  with its border — as MarkdownEditor's splash.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx` — 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx` — 2123 passed, 0 failed (18 new).
- A real launch on this desktop (`DISPLAY=:1`): the host started, the first window opened after the
  splash (the git probe, which runs when that window opens, logged `Using git 2.55.0`), and no error
  or exception was written.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the hand-over lives | `Services/SplashHandOver.cs`, given to `IAppWindows.StartAsync` as an optional argument | The window service is the one that shows the first window; an optional argument keeps every other caller unchanged |
| How the floor is measured | Differences of `TimeProvider.GetUtcNow()` | The tests' `ManualTimeProvider` moves the wall clock, not the timestamp counter |
| The window title | `DisplayName` | Never shown (no decorations), but it is what a window list or an accessibility tool reads |
| Foreground of the name | `EnigmaForegroundBrush`, set explicitly | The window is not themed text by default; explicit brushes keep it legible in both themes |
