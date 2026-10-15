# FEATURE-70F3 — No New window button in the toolbar

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-70f3-drop-new-window-button`
**Run:** feature/2026-10-15-worktrees-toolbar

## Objective

Take the *Open another window, for another repository* button out of the repository window's
toolbar. The start window keeps its own way to open a repository beside the current one: a listed
repository's *Open in a new window, beside this one*.

## Context & constraints

- The button is in `src/Enigma.GitClient.Desktop/Views/MainWindow.axaml` (`AppWindow` icon,
  `AutomationProperties.Name="Open a new window"`). It is bound to `MainWindowViewModel.NewWindowCommand`.
  That command calls `IInstanceLauncher.Launch()` and says *No new window* in the info bar when the
  launch fails.
- `IInstanceLauncher` is also used by `RepositoriesPageViewModel.OpenInNewWindowCommand` (the start
  window's tile), which stays — the service, its registration and its test double are untouched.
- `MainWindowViewModel` uses the info bar for other things (the terminal), so only the launcher
  dependency goes.
- Tests: `InstanceTests.NewWindow_StartsAnotherInstanceOnItsStartWindow` and
  `NewWindow_WhenTheInstanceCannotStart_SaysSo` cover the removed command.
- Desktop tests are written and compiled, **not run** on this Windows machine; Core suites are run.

## Steps

1. `MainWindow.axaml`: remove the button. The separator before it now separates the folder/terminal
   group from *Close the repository*, and stays.
2. `MainWindowViewModel`: remove `NewWindowCommand`, `OnNewWindow`, the `_launcher` field and the
   `launcher` constructor parameter (and its XML doc).
3. `InstanceTests`: remove the two `NewWindow_*` tests; the class summary no longer speaks of "the two
   places that ask for one". The three `OpenInNewWindow_*` tests stay, guarding the start window's
   item.

## Acceptance criteria

- The repository window's toolbar has no *Open another window* button, and `MainWindowViewModel` no
  longer depends on `IInstanceLauncher`.
- The start window's *Open in a new window, beside this one* is unchanged.
- Build clean (0 warnings); Core suites green; Desktop tests compile.

## Out of scope

- A menu item or keyboard shortcut replacing the button.
- Any change to `IInstanceLauncher` or to the start window.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| How deep the removal goes | The button, its command, its handler and the ViewModel's launcher dependency | Dead code left behind would be one more thing to keep compiling and explaining | Hiding the button (`IsVisible="False"`), keeping the command |
| Its tests | Deleted; the start window's tests stay | They test a command that no longer exists; the kept behaviour has its own tests | Rewriting them as "absence" tests (the compiler already proves the command is gone) |
| The separator before the button | Kept | It still separates the folder/terminal group from the window's own buttons | Removing it too |
