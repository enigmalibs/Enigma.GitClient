# FEATURE-3B4E — Open the folder and a terminal

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-3b4e-folder-and-terminal-buttons`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Objective

Two buttons in the repository window's top toolbar: one opens the repository's folder in the file
manager (Explorer on Windows), the other opens a terminal in that folder.

## Context & constraints

- `Views/MainWindow.axaml`: the repository strip's right-hand `StackPanel` — fetch, pull, push, a
  separator, new window, close, refresh, theme, about — `Button Classes="toolbar"` with an `ei:Icon`,
  `ToolTip.Tip` and `AutomationProperties.Name`.
- `Services/SystemInterop.cs` (`ISystemInterop`): `OpenPathAsync` (shell-execute, also a directory),
  `RevealPathAsync`, `OpenUrlAsync`; launches go through `Launch`, which never throws. Tests replace it
  with `RecordingSystemInterop` (`UiServiceDoubles.cs`).
- `ViewModels/MainWindowViewModel.cs` owns the toolbar's commands; `RepositoryContext.Repository.WorkTreePath`.
- Tests: `MainWindowShellTests`, `ToolbarButtonLookTests`, `MenuIconTests`.

## Steps

1. `ISystemInterop.OpenTerminalAsync(string directory)`: Windows Terminal (`wt.exe -d <dir>`) and, when
   it is not there, `cmd.exe` in the directory; macOS `open -a Terminal <dir>`; Linux `$TERMINAL`, then
   `x-terminal-emulator`, `gnome-terminal`, `konsole`, `xfce4-terminal`, `xterm`, started in the directory.
   Arguments through `ArgumentList`, never a shell string. The candidates are built by a pure function,
   unit-tested.
2. `MainWindowViewModel`: `OpenFolderCommand` (`OpenPathAsync(WorkTreePath)`) and `OpenTerminalCommand`
   (`OpenTerminalAsync(WorkTreePath)`), enabled while a repository is open; a launch that fails says so in
   a warning info bar.
3. `MainWindow.axaml`: the two buttons after the sync group's separator, then a separator; icons
   `FolderOpen` and `TerminalWindow`.
4. Tests: each command asks the desktop for the repository's folder; a failed launch reports; the
   commands are disabled with no repository; the candidate lists per platform.

## Acceptance criteria

- The toolbar has an "open folder" and an "open terminal" button that act on the repository's folder.
- Build clean, whole suite green, the new tests among them.

## Out of scope

- A setting to choose the terminal or the file manager.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which terminal | The platform's usual one, with fallbacks; no setting | Not asked for; covers the common setups | A terminal setting; Git Bash on Windows |
| Where | After fetch/pull/push, grouped by separators | Repository actions sit together, before the window actions | Beside the repository name |
| Failure | A warning info bar | The click must say why nothing appeared | Silent |
| Folder | `OpenPathAsync` on the work tree | Already cross-platform | `RevealPathAsync` (selects the folder in its parent) |
