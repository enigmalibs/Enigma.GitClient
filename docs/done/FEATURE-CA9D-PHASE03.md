# FEATURE-CA9D-PHASE03 — New instances on the caller's screen

**Item:** FEATURE-CA9D — Windows open on the screen in use
**Phase:** PHASE03 — New instances on the caller's screen
**Branch:** `feature/feature-ca9d-phase03-instance-screen`
**Run:** feature/2026-10-16-feature-c3f3-feature-ca9d-feature-c56e

## Summary

An instance started from a window now opens on that window's screen. This covers *Open in a new
window* on a repository tile, a worktree's *Open in a new window* and a refused checkout's *Open*. The
window's screen travels on the command line as `--screen=x,y,w,h`.

- **Core `Screens/StartupArguments`** (a public sealed record `RepositoryPath`, `Screen`):
  - `Parse(args)`: the first argument that is not an option is the path, trimmed as the old first
    argument was. Any arguments after it are left alone, and `--` ends the options. An unknown option,
    or a `--screen` that is not four invariant-culture integers with a positive width and height, is
    ignored. Blank arguments are skipped.
  - `FormatScreen(ScreenArea)` writes the option in the invariant culture.
- **`InstanceLauncher`:**
  - It takes `IAppWindows` and `IWindowPlacer`, and adds `--screen=…` for the current window's screen
    (`WindowPlacer.ScreenOf(CurrentWindow)`) ahead of the path.
  - `BuildStartInfo(processPath, entryAssembly, repositoryPath, screen)` puts a path that starts with a
    dash after `--`, and leaves out a screen with no area.
  - With no window, or no screen known, the command line is the one it always was.
- **`App`:** `StartupArguments.Parse(desktop.Args)` replaces `FirstArgument`, which is removed. The
  splash is placed on `startup.Screen ?? settings.LastScreen`. `IAppWindows.StartAsync(path, splash,
  screen)` gains the screen, and the first window opens on it before the remembered one.

## Files touched

- Created:
  - `src/Enigma.GitClient.Core/Screens/StartupArguments.cs`
  - `tests/Enigma.GitClient.Core.UnitTests/Screens/StartupArgumentsTests.cs`
- Modified:
  - `src/Enigma.GitClient.Desktop/Services/InstanceLauncher.cs`: the caller's screen, and
    `BuildStartInfo`'s new parameter.
  - `src/Enigma.GitClient.Desktop/Services/AppWindows.cs`: `StartAsync`'s screen, ahead of the
    remembered one.
  - `src/Enigma.GitClient.Desktop/App.axaml.cs`: `StartupArguments`; `FirstArgument` removed.
  - `tests/Enigma.GitClient.Desktop.UnitTests/InstanceTests.cs`: the argument vectors with a screen.
  - `tests/Enigma.GitClient.Desktop.UnitTests/AppWindowsTests.cs`: the old `FirstArgument` cases, now
    read through `StartupArguments.Parse`, plus a screen-first one.
  - `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/UiServiceDoubles.cs`:
    `RecordingAppWindows.StartAsync`'s signature.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| `--` before the path | Only when the path starts with a dash | A full path never does, so the usual command line stays `[--screen=…] path`, and an odd folder name still opens | Always writing `--` |
| A screen with no area on the way out | Left out of the command line | The receiving side would ignore it anyway (`Parse` drops it) | Writing it |
| Two `--screen` options | The last one that parses wins | The usual command-line convention, and a hand-typed one after a generated one takes effect | The first wins |
| Blank arguments | Skipped | The old `FirstArgument` treated a blank first argument as none; the parser keeps that | Treating a blank as the path |
| Whose screen the launcher sends | `IAppWindows.CurrentWindow`'s, read when it launches | Every *Open in a new window* is clicked in the window on screen | Passing the window through every caller |
| `FirstArgument` | Removed; its cases kept as a theory over `StartupArguments.Parse` | "`StartupArguments.Parse` replaces `FirstArgument`", and one way of reading the command line is enough | Keeping both |

## Deviations & follow-ups

- No deviation from the plan.
- **Manual check pending (Windows, two screens):**
  1. A tile's *Open in a new window* from screen 2 → the new instance's splash and window on screen 2.
  2. The same from a worktree (FEATURE-19A5's create, FEATURE-C3F3's *Open in a new window*).
- An instance started by hand (`Enigma.GitClient.Desktop /src/repo`) has no `--screen`, and opens on
  the screen last used, as PHASE02 made it.

## Documentation sweep

- Nothing changed. The README does not document the command line. The release notes (FEATURE-C56E)
  mention `--screen` under the item.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**.
- `Enigma.GitClient.Core.UnitTests`: **1306 passed, 1 skipped, 0 failed**. This includes 26 new
  `StartupArgumentsTests`:
  - a path alone; a screen then a path; negative coordinates;
  - a path with spaces, with `=`, and one that looks like the option;
  - malformed screens (7 cases);
  - unknown options (3);
  - `--` before a path starting with a dash, and a screen after `--` no longer read;
  - only the first path; no path (3 + 1); nothing at all; trimming;
  - the option written and read back under another culture.
- `Enigma.GitClient.Core.IntegrationTests`: **386 passed, 2 skipped, 0 failed**.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** (standing instruction).
  - `InstanceTests` adds 6 tests:
    - the screen ahead of the path, read back by the parser;
    - a screen with no repository;
    - under the `dotnet` host, the assembly first;
    - no screen: the old vector;
    - a dash path after `--`;
    - a screen with no area left out.
  - `AppWindowsTests`: the command line's first path, 6 cases.
