# FEATURE-CA9D — Windows open on the screen in use

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-16-feature-c3f3-feature-ca9d-feature-c56e

## Objective

The start (home) window and the repository window open on the screen the user is working on, not always
on the primary one:

- **hand-over:** the window that replaces another opens on the screen the other was on — the repository
  window maximised there, the start window centred there after *Close repository*;
- **fresh start:** the splash and the first window open on the screen the application was last on,
  remembered in the settings; on the primary screen when that screen is gone;
- **another instance** started from a window (*Open in a new window* on a repository tile, a worktree's
  *Open in a new window*) opens on that window's screen.

Centred start window and maximised repository window stay as they are — only the screen changes.

## Context & constraints

- `Views/StartWindow.axaml`, `Views/MainWindow.axaml` and `Views/SplashWindow.axaml` all use
  `WindowStartupLocation="CenterScreen"`; `MainWindow` is `WindowState="Maximized"` with a 1440 × 900
  restore size. With no owner, Avalonia centres a window on the screen at its current position — (0, 0)
  for a new window, the primary screen — which is why every window lands there.
- `Services/AppWindows.cs` — `Show(kind, windowType, viewModelType)` builds the next window (transient),
  shows it, then closes the previous one. Callers: `StartAsync` (first window), `ShowStart` (*Close
  repository*: `MainWindowViewModel.OnCloseRepository`), `ShowRepository` (`RepositoriesPageViewModel`).
- `App.OnFrameworkInitializationCompleted` loads the settings **before** the splash, shows the splash,
  posts `IAppWindows.StartAsync(path, handOver)`; `App.FirstArgument(args)` is the repository path;
  `OnExit` disposes the host.
- `Services/InstanceLauncher.cs` — `BuildStartInfo(processPath, entryAssembly, repositoryPath)`, an
  argument list (never a command line).
- The title-bar ✕ of the repository window quits the application (`ShutdownMode.OnLastWindowClose`) and
  **stays so**; *Close repository* is the way back to the start window.
- Settings: `AppSettings` (Core) carries remembered values (`CloneParentDirectory`, `SelectedProfileId`)
  without a schema bump; `ISettingsService.Update(change)` writes debounced, `FlushAsync` forces it.
  `settings.json` stays at version 7.
- **Platforms:** Windows is the reference. Linux on X11 / XWayland is best effort (the window manager has
  the last word on maximising); native Wayland compositors ignore client positions — not a defect.
- **Tests on this machine:** Core suites run, Desktop tests compiled, **not run** (standing instruction).
  The placement rules are therefore plain integer maths in **Core**, where they run; the Avalonia wiring
  in Desktop is thin. A manual check on a two-screen Windows machine closes each phase.

### Shared definitions — `src/Enigma.GitClient.Core/Screens/`

- `ScreenArea(int X, int Y, int Width, int Height)` — a rectangle in physical pixels.
- `ScreenInfo(ScreenArea Bounds, ScreenArea WorkingArea, double Scaling, bool IsPrimary)`.
- `ScreenPlacement` (public static):
  - `Choose(IReadOnlyList<ScreenInfo> screens, ScreenArea? hint)` — the screen whose bounds equal the
    hint; else the one containing the hint's centre; else the one it overlaps most; else the primary;
    else the first.
  - `FromWindow(IReadOnlyList<ScreenInfo> screens, ScreenArea window)` — the screen holding most of a
    window.
  - `CenterIn(ScreenArea workingArea, int width, int height)` — the top-left corner that centres a frame
    of that pixel size in the working area, kept inside it when the frame is larger.

## PHASE01 — Hand-over keeps the screen

**Status:** DONE
**Branch:** `feature/feature-ca9d-phase01-hand-over-screen`

### Steps

1. Core `Screens` types above, with unit tests (`tests/Enigma.GitClient.Core.UnitTests/Screens/`):
   `Choose` (exact bounds; centre inside another screen; overlap; gone → primary; empty list), `FromWindow`
   (straddling two screens), `CenterIn` (smaller, equal and larger frames; a working area offset by a
   taskbar; negative coordinates of a screen left of the primary).
2. Desktop `Services/WindowPlacer` — Avalonia's `Screens` → `ScreenInfo`s; `ScreenOf(Window)` (via
   `FromWindow`); `PlaceOn(Window, ScreenInfo)`: `WindowStartupLocation = Manual`, `Position` from
   `CenterIn(screen.WorkingArea, width × screen.Scaling, height × screen.Scaling)` — the target screen's
   scaling, so mixed DPI centres right. The repository window keeps `WindowState = Maximized`: placed on
   the screen first, it maximises there.
3. `AppWindows`: remembers the current window's screen whenever it is opened or moved while not minimised;
   `Show` places the next window on the previous window's screen — the remembered one when the previous
   window is minimised — before `Show()`. With no previous window, today's behaviour (PHASE02 changes it).
4. Desktop tests (compiled): a window handed over gets `Manual` and the position `CenterIn` gives for the
   previous window's screen.
5. Manual check, two screens on Windows: start window moved to screen 2 → open a repository → maximised
   on screen 2; *Close repository* → start window centred on screen 2; one screen at 100 %, the other at
   150 %.

### Acceptance criteria

- The repository window maximises on the screen the start window was on; *Close repository* brings the
  start window back centred on the screen the repository window was on.
- On one screen, nothing changes.
- Build clean; Core suites green; Desktop tests compile.

## PHASE02 — Fresh start on the last screen

**Status:** DONE
**Branch:** `feature/feature-ca9d-phase02-last-screen`

### Steps

1. `AppSettings.LastScreen` (`ScreenArea?`, `null` by default) — a remembered value, no schema bump;
   `Normalised` drops one with a non-positive width or height.
2. Recorded through `ISettingsService.Update` each time a window is placed or handed over and when the
   current window closes (its screen); `App.OnExit` awaits `FlushAsync` before the host is disposed, so a
   quit by ✕ keeps it.
3. A fresh start places the **splash** (in `App`, before `splash.Show()`) and the **first window** on
   `ScreenPlacement.Choose(screens, settings.LastScreen)`: the primary screen when it is gone or was never
   recorded.
4. Tests: Core — the settings round trip with and without `LastScreen`, `Normalised`, `Choose` for a
   screen that has gone; Desktop (compiled) — the first window placed on the remembered screen.
5. Manual check: quit on screen 2, start again from the taskbar → splash and start window on screen 2;
   unplug screen 2, start → primary.

### Acceptance criteria

- A fresh start opens where the application was last used, and on the primary screen when that screen is
  gone.
- Build clean; Core suites green; Desktop tests compile.

## PHASE03 — New instances on the caller's screen

**Status:** TODO
**Branch:** `feature/feature-ca9d-phase03-instance-screen`

### Steps

1. Core `Screens/StartupArguments` (public static): `Parse(string[] args)` → the repository path and an
   optional `ScreenArea` from `--screen=x,y,w,h` (invariant-culture integers); the first argument that is
   not an option is the path; `--` ends the options; a malformed or unknown option is ignored.
   `FormatScreen(ScreenArea)` writes the option. Unit tests: path alone; screen then path; a path with
   spaces or `=`; a malformed screen; `--` before a path starting with `-`; nothing at all.
2. `InstanceLauncher`: adds `--screen=…` for the current window's screen (`WindowPlacer.ScreenOf` on
   `IAppWindows.CurrentWindow`) ahead of the path; `BuildStartInfo` takes the screen. Desktop tests
   (compiled): the argument vector with and without a screen, through `dotnet` too.
3. `App`: `StartupArguments.Parse` replaces `FirstArgument`; the hint goes before `LastScreen` for the
   splash and the first window (`IAppWindows.StartAsync(path, splash, screen)`).
4. Manual check: a tile's *Open in a new window* from screen 2 → the new instance's splash and window on
   screen 2; the same from a worktree (FEATURE-19A5, FEATURE-C3F3).

### Acceptance criteria

- An instance started from a window opens on that window's screen.
- A command line without `--screen` behaves as before (the path still opens).
- Build clean; Core suites green; Desktop tests compile.

## Out of scope

- Remembering a window's size or position within a screen (the start window stays centred, the repository
  window maximised).
- The ✕ button's behaviour.
- The screen under the mouse pointer.

## Decisions

| Question | Chosen | Source |
|---|---|---|
| The repository window's ✕ | Still quits; *Close repository* returns to the start window | User (recommendation) |
| Fresh start | The last screen used, remembered; primary when gone | User (recommendation) |
| Another instance | The launching window's screen, through `--screen=x,y,w,h` | User (recommendation) |
| Where the rules live | Core `Screens` (pure integer maths), thin Desktop `WindowPlacer` | Default: they run in the Core suites here |
| Identifying the remembered screen | Exact bounds, then centre, then overlap, then primary | Default |
| A minimised window handing over | The last screen it was seen on | Default |
| When the screen is recorded | Each placement and hand-over, and on close; flushed at exit | Default |
| Linux | X11 best effort; native Wayland ignores positions | Default (platform limit) |
| Phases | Hand-over → fresh start → instances | Default (each builds on the last) |
