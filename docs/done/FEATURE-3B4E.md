# FEATURE-3B4E — Open the folder and a terminal

**Item:** FEATURE-3B4E — Open the folder and a terminal
**Branch:** `feature/feature-3b4e-folder-and-terminal-buttons`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Summary

Two buttons in the repository window's top strip, after fetch / pull / push and before the window
actions, between separators:

- **Open the repository's folder** (`FolderOpen`): the work tree in the file manager — Explorer on
  Windows — through the existing `ISystemInterop.OpenPathAsync`.
- **Open a terminal in the repository's folder** (`TerminalWindow`): the new
  `ISystemInterop.OpenTerminalAsync`, which tries, in order, the first terminal that starts:
  - Windows: Windows Terminal (`wt.exe -d <folder>`), else the command prompt in the folder (started
    through the shell so it gets a console window of its own);
  - macOS: `open -a Terminal <folder>`;
  - Linux: `$TERMINAL`, then `x-terminal-emulator`, `gnome-terminal --working-directory=…`,
    `konsole --workdir …`, `xfce4-terminal --working-directory=…`, `xterm` — each started in the folder.

  The folder is always passed as an argument of its own (`ArgumentList`) or as the working directory,
  never through a shell command line.
- Both are enabled while a repository is open; a launch that fails says so in a warning info bar with
  the folder's path.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/Services/SystemInterop.cs`: `DesktopPlatform`, `OpenTerminalAsync`,
  `TerminalCandidates` (internal, tested), `Started`.
- `src/Enigma.GitClient.Desktop/ViewModels/MainWindowViewModel.cs`: `ISystemInterop` injected;
  `OpenFolderCommand`, `OpenTerminalCommand`.
- `src/Enigma.GitClient.Desktop/Views/MainWindow.axaml`: the two buttons and a separator.
- `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/UiServiceDoubles.cs`: `RecordingSystemInterop`
  records terminals (`Terminals`, `OpensTerminals`) and can fail an open (`OpensPaths`).
- `docs/roadmap.md`, `docs/plan/FEATURE-3B4E.md`: statuses.

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryFolderTests.cs`: the commands, the failures, the
  buttons in the strip, and the terminal candidates per platform.
- `docs/done/FEATURE-3B4E.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Testing the launcher | A pure `TerminalCandidates(platform, folder, $TERMINAL)` | The real launch must never happen in a test; the order and the arguments are what can be wrong |
| A terminal not installed | Its launch fails (`Win32Exception`, already swallowed by `Launch`) and the next is tried | No PATH probing of our own |
| Visibility | Shown while a repository is open, as fetch / pull / push are | The strip shows nothing that cannot act |

## Deviations & follow-ups

- None from the plan.
- Not verified by hand on a real desktop in this run (no interactive session): the launch commands are
  covered by the candidate tests only. Worth a click on Windows, Linux and macOS before the release.
- Test gate on this Windows host: as recorded in `docs/done/FEATURE-903B.md`.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

Nothing stale; the README's feature list does not enumerate the toolbar's buttons.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- `RepositoryFolderTests`: 9 passed, three runs in a row (after one fix cycle: the first run's two
  window tests ended while the history's first read was still running git in the folder, and the
  Windows teardown could not delete it; they now let it finish).
- `Enigma.GitClient.Core.UnitTests`: 1132 total, 1131 passed, 0 failed, 1 skipped.
- `Enigma.GitClient.Core.IntegrationTests`: 344 total, 342 passed, 0 failed, 2 skipped.
- Desktop, targeted (`RepositoryFolderTests`, `MainWindowShellTests`, `ToolbarButtonLookTests`,
  `ShellRenderTests`, `ShellSnapshotTests`, `MenuIconTests`, `AppWindowsTests`, `CompositionRootTests`,
  `HostLinkTests`, `ChangedFilesPanelTests`): 187 total, 164 passed, 0 real failures, 23 teardown
  refusals. With the verification-only teardown change (not committed): 182 passed, 5 teardown
  `IOException`s, 0 real failures.
- Fix budget: 1 cycle used (the tests' teardown race above).
