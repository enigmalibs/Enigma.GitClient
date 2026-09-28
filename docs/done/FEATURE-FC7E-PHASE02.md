# FEATURE-FC7E-PHASE02 — Details from the line's menu

**Item:** FEATURE-FC7E — History: commit details, discard all
**Branch:** `feature/feature-fc7e-phase02-details-in-menu`
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Summary

A history line's context menu offers **Show commit details** (`Article` icon), right after "Show what
it changed". It opens PHASE01's dialog for that line's commit, whichever line is selected.

- Every line that is a commit offers it, stash lines included: a stash is a commit with a message, an
  author and a hash.
- The *Uncommitted changes* line has none of those, and does not offer it.

`HistoryRowCommands` gains `ShowDetails`. `HistoryPageViewModel` wires it to the handler the header
button uses, enabled only for a line with a commit. The record's undocumented `Stashes` and `Copy`
parameters got their `<param>` lines while it was being edited.

## Files / modules touched

**Created**

- `docs/done/FEATURE-FC7E-PHASE02.md`

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/CommitRowViewModel.cs` — `HistoryRowCommands.ShowDetails`;
  the menu entry.
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — the command, built over
  `OnShowDetailsAsync`.
- `tests/Enigma.GitClient.App.UnitTests/CommitDetailsDialogTests.cs`:
  - the entry is second on commit and stash lines, is absent on the uncommitted line, and has its icon;
  - it shows the right-clicked line's commit, not the selected one.
- `tests/Enigma.GitClient.App.UnitTests/HistoryStashTests.cs` — the stash line's pinned menu includes
  the new entry.
- `docs/roadmap.md`, `docs/plan/FEATURE-FC7E.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| One handler or two | The header's `OnShowDetailsAsync`, from both | One path to the dialog, so the menu and the button cannot disagree |

## Deviations & follow-ups

- None from the plan.
- Line endings: no CRLF churn.

## Documentation sweep

Nothing describes the line menu's items. No edit. The feature goes into the release notes with
FEATURE-10AA.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2483 passed**, 0 failed, 0 skipped (2 new, 1
  updated).
- Fix budget: no fix cycle.
