# FEATURE-70F3 — No New window button in the toolbar

**Item:** FEATURE-70F3 — No New window button in the toolbar
**Branch:** `feature/feature-70f3-drop-new-window-button`
**Run:** feature/2026-10-15-worktrees-toolbar

## Summary

The repository window's toolbar no longer has the *Open another window, for another repository*
button. To work on another repository beside this one, use the start window's *Open in a new window,
beside this one* on a listed repository, which is unchanged.

- **The toolbar.** The button sat between the folder/terminal group and *Close the repository*. The
  separator before it stays: it now separates that group from the window's own buttons.
- **The ViewModel.** `MainWindowViewModel` loses `NewWindowCommand`, its handler and its
  `IInstanceLauncher` dependency. `IInstanceLauncher` itself, its registration and its test double are
  untouched: the start window still uses them.

## Files touched

- Modified:
  - `src/Enigma.GitClient.Desktop/Views/MainWindow.axaml`: the button removed.
  - `src/Enigma.GitClient.Desktop/ViewModels/MainWindowViewModel.cs`: `NewWindowCommand`,
    `OnNewWindow`, the launcher field and constructor parameter removed; the `infoBar` parameter's
    doc now says what it still reports (a folder or a terminal that did not open).
  - `tests/Enigma.GitClient.Desktop.UnitTests/InstanceTests.cs`: the two `NewWindow_*` tests and the
    now-unused `ViewModels` using removed; the class summary names the one place left that asks for
    another instance.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| The `infoBar` parameter's doc | Rewritten to "the folder or a terminal could not be opened" | It described the removed launch failure | Leaving it stale |
| The pre-existing unused `System.Linq` using in `InstanceTests` | Left alone | It was already unused before this dev; out of scope | Removing it in passing |

## Deviations & follow-ups

- No deviation from the plan.
- **Follow-up:** the next release item's notes need a line for this change.

## Documentation sweep

- Nothing changed: no README, CLAUDE.md or prose doc mentions the toolbar button. `RELEASENOTES.md`
  describes past releases and is left to the next release item.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**.
- `Enigma.GitClient.Core.UnitTests`: **1183 passed, 1 skipped, 0 failed**.
- `Enigma.GitClient.Core.IntegrationTests`: **361 passed, 2 skipped, 0 failed** (3 min 37 s).
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run**, under the project's standing instruction
  never to run the Desktop suite on this Windows machine. Run it on Linux before tagging. No test was
  added: the compiler proves the command is gone, and the start window's three `OpenInNewWindow_*`
  tests still guard the behaviour that stays.
