# FEATURE-0DED — Remove the splash screen

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-0ded-remove-splash-screen`
**Run:** feature/2026-10-29-uniform-enigma-apps

## Objective

The application starts right away: no splash screen, no one-second floor. The first window (start window
or repository window) is the first thing on screen. Part of the uniform baseline for every Enigma app.

## Context & constraints

- The splash came with FEATURE-75F4 (PHASE01), modelled on Enigma.MarkdownEditor's:
  - `Views/SplashWindow.axaml(.cs)` — the borderless 420 × 260 window;
  - `Views/SplashTiming.cs` — the 1 s floor and `RemainingDelay`;
  - `Services/SplashHandOver.cs` — holds the splash to its floor, closes it once the first window is up;
  - `App.OnFrameworkInitializationCompleted` shows it, places it with `AppWindows.PlaceForStart`, and
    posts `IAppWindows.StartAsync(path, handOver, screen)` at `DispatcherPriority.Background` so the
    splash paints first;
  - `IAppWindows.StartAsync(string? path, SplashHandOver? splash = null, ScreenArea? screen = null)`
    waits out the floor and closes the splash in a `finally`.
- Before FEATURE-75F4, `App` fired `StartAsync(path)` directly. `ShutdownMode` is `OnLastWindowClose`;
  with no splash there is no window to hand over from, so nothing can end the lifetime early.
- `Assets/app-256.png` stays: the About dialog shows it (`AppIconTests` pins it to the `.ico`'s 256 frame).
- `AppSettings.LastScreen` stays: the first window opens on it.
- Comments naming the splash: `Directory.Build.props`, `Core/Diagnostics/ProductInformation.cs`,
  `Core/Configuration/AppSettings.cs`, `Services/UnhandledErrorReporter.cs`, `Services/AppWindows.cs`.
- Tests: `SplashScreenTests.cs`; two `AppWindowsTests.StartAsync_WithASplash_*`; the `IAppWindows`
  double in `Infrastructure/UiServiceDoubles.cs`; `AppIconTests.TheSplashImage_*`;
  `UnhandledErrorReporterTests` wording.
- Desktop tests are compiled, never run, on this Windows machine (user rule); the Core suites run.

## Steps

1. Delete `Views/SplashWindow.axaml`, `Views/SplashWindow.axaml.cs`, `Views/SplashTiming.cs`,
   `Services/SplashHandOver.cs`.
2. `IAppWindows.StartAsync(string? path, ScreenArea? screen = null)` — drop the splash parameter, its
   floor wait and its `finally` close; keep the startup failure handling as it is.
3. `App.OnFrameworkInitializationCompleted`: no splash; call
   `windows.StartAsync(startup.RepositoryPath, startup.Screen).Forget()` directly (no dispatcher post).
4. Reword the comments that name the splash (list above); `PlaceForStart`'s summary names the first
   window only.
5. Tests: delete `SplashScreenTests.cs` and the two splash `AppWindowsTests`; update the double;
   rename `AppIconTests.TheSplashImage_IsTheIconsLargestFrameByteForByte` to the About image; drop a
   test helper only the splash tests used (e.g. `ManualTimeProvider`), if any.
6. `docs/RELEASE.md`: the version checks name the About dialog only.

## Acceptance criteria

- No `Splash*` type, file or test remains under `src/` or `tests/`; `grep -ri splash src tests` finds
  nothing but history-neutral wording, if any.
- `App` shows the first window directly; `StartAsync` has no splash parameter.
- The Release build has zero warnings (Desktop tests compiled); the Core unit and integration suites
  are green.

## Out of scope

- The About dialog (this app is the reference).
- `RELEASENOTES.md` (FEATURE-530E).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Hide or delete the splash | Delete every splash type and test | Code that is never shown is dead weight | A setting to turn it off; leaving the types unused |
| How the first window starts | `StartAsync(...).Forget()` called directly, as before FEATURE-75F4 | The Background post existed only to let the splash paint first; "starts right away" | Keeping the post at Background or Normal priority |
| A repository on the command line | Opens before the first window shows, as before the splash | Opening is one `git rev-parse`-class call; a placeholder window would be a new feature | Showing the start window first, then switching |
| `PlaceForStart` | Kept public, comment updated | The first window still uses it | Making it private (churn in tests for no gain) |
| Tests | Delete the splash tests; no new test | Behaviour removed; `StartAsync_WithNoPath_OpensTheStartWindow` and the repository-start tests already pin the start | A test asserting the absence of a type |
