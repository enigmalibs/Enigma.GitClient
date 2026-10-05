# BUG-6DB7 — A refresh reopens collapsed folders

**Item:** BUG-6DB7 — A refresh reopens collapsed folders
**Branch:** `bugfix/bug-6db7-tree-keeps-collapsed-folders`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Summary

The automatic refresh re-read the working tree and `ChangedFilesPanelViewModel` rebuilt every row, every
directory at the tree's default — so a folder the reader had closed opened again every 15 seconds.

- A refresh of the same change that finds **the same files** rebuilds nothing at all (no flicker, no
  scroll jump, the very same rows).
- When the files did change, every directory the reader opened or closed **against the tree's default**
  keeps that state, by its path, across the rebuild — a refresh, the filter, the list/tree toggle.
  Directories never touched still follow the default, so a new auto-expand limit still applies to them.
- Another change (another commit's files, `keepSelection: false`) starts from the defaults.
- The working-tree panel forgets the closed folders when another repository is opened.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Panels/ChangedFilesPanelViewModel.cs`: `SetFiles` skips equal
  files; `Rebuild(previous, sameChange)`; `_expanded` and `RememberExpansion`;
  `ChangedFileNodeViewModel.DefaultExpanded`.
- `src/Enigma.GitClient.Desktop/ViewModels/Panels/WorkingTreePanelViewModel.cs`: both halves start afresh
  on a repository change.
- `tests/Enigma.GitClient.Desktop.UnitTests/ChangedFilesPanelTests.cs`: five tests.
- `tests/Enigma.GitClient.Desktop.UnitTests/WorkingTreePanelTests.cs`: two tests over a scripted status
  (`ScriptedStatus`), with no repository on disk.
- `docs/roadmap.md`, `docs/plan/BUG-6DB7.md`: statuses.

**Created**

- `docs/done/BUG-6DB7.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What is remembered | Only directories whose state differs from the default they were built with | A snapshot of every directory froze the defaults: a new auto-expand limit was ignored (seen while writing `Panel_KeepsADirectoryOpenedPastTheAutoExpandLimitOpen`, before the first build) |
| "The same files" | `SequenceEqual` on the `ChangedFile` records | They are records: equal path, kind, staging and counts mean nothing to redraw |
| A repository change | Both halves reset with `keepSelection: false` | A closed `src` in one repository is not the next one's |
| The working-tree test | A scripted `IStatusService` and a fake handle | No repository on disk, so the Windows teardown problem cannot hide its result |

## Deviations & follow-ups

- None from the plan, apart from remembering only the reader's changes (above).
- Test gate on this Windows host: as recorded in `docs/done/FEATURE-903B.md` (the Desktop suite skipped at
  the user's request; Core suites in full; the dev's Desktop classes targeted).
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

Nothing stale: no README, `CLAUDE.md` or other prose doc describes how the file tree behaves on a refresh.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- `Enigma.GitClient.Core.UnitTests`: 1124 total, 1123 passed, 0 failed, 1 skipped.
- `Enigma.GitClient.Core.IntegrationTests`: 344 total, 342 passed, 0 failed, 2 skipped.
- Desktop, targeted (`ChangedFilesPanelTests`, `WorkingTreePanelTests`, `FileListToggleTests`,
  `HistoryPageTests`): 195 total, 81 passed, 0 real failures, 114 teardown refusals (tests that build a
  repository).
- Because a teardown refusal can hide an assertion, the same classes were run once more with a
  **verification-only, uncommitted** change to `TestServices.Dispose` clearing git's read-only flags
  first (removed afterwards; `TestServices.cs` is unchanged in this commit): 178 passed, 17 failed — all
  environmental, none in code this dev touched:
  - 13 × `IOException` "being used by another process" while deleting the test's directory;
  - 2 × `git branch xxx…` refused (an over-long ref name on Windows);
  - 2 × `Panel_Discards*`: `"one\r\ntwo\r\n"` instead of `"one\ntwo\n"` — this machine's system
    `core.autocrlf=true` converts the file git restores.
- The seven new tests pass: `Panel_KeepsACollapsedDirectoryCollapsedAcrossARefresh`,
  `Panel_RebuildsNothingWhenARefreshFindsTheSameFiles`, `Panel_KeepsADirectoryOpenedPastTheAutoExpandLimitOpen`,
  `Panel_KeepsACollapsedDirectoryAcrossTheListAndTreeToggle`,
  `Panel_OpensAnotherChangesDirectoriesAsTheTreeOpensThem`,
  `Panel_KeepsACollapsedFolderCollapsedAcrossTheAutomaticRefresh`,
  `Panel_ForgetsTheClosedFoldersOfTheRepositoryItLeaves`.
- Fix budget: 0 cycles used.
