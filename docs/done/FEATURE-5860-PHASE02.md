# FEATURE-5860-PHASE02 — Copy names and hashes from the history

**Item:** FEATURE-5860 — Context menus that do more
**Branch:** `feature/feature-5860-phase02-copy-items`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

- **A branch badge's menu** ends with **Copy branch name**. It copies the name the badge shows:
  `feature`, `origin/main`.
- **A tag badge** now has a menu of its own, **Copy tag name**. Tags used to be plain badges.
- **A commit's line menu**, and a stash line's, end with **Copy short commit hash** (7 characters) and
  **Copy full commit hash**. The uncommitted line has no hash and offers neither.

All four go through one `HistoryPageViewModel.CopyCommand` (`AsyncRelayCommand<string>`, enabled for a
non-empty text), which calls `ISystemInterop.CopyTextAsync`. The item passes its text as the parameter.

## Files / modules touched

**Created**

- `tests/Enigma.GitClient.App.UnitTests/HistoryCopyTests.cs` — 4 tests:
  - branch names, local and remote;
  - the tag badge's menu, headless: opened, its single item, and what it copied;
  - both hashes;
  - nothing to copy on the uncommitted line.

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryBranchViewModel.cs`:
  - `HistoryBranchCommands.Copy`;
  - `HistoryBranchViewModel.CanCopyName`;
  - the new `HistoryTagViewModel`.
- `src/Enigma.GitClient.App/ViewModels/Pages/CommitRowViewModel.cs`:
  - `HistoryRowCommands.Copy`;
  - tag badges become `HistoryTagViewModel`s;
  - `AddCopyEntries` for commits and stashes.
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — keeps `ISystemInterop`;
  `CopyCommand`, handed to both command sets.
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml` — the branch badge's copy item and the tag
  badge's template and menu.
- `tests/Enigma.GitClient.App.UnitTests/HistoryStashTests.cs` — the stash menu's exact items now end with
  the two copies.
- `tests/Enigma.GitClient.App.UnitTests/HistoryMergeSourceTests.cs` — the badge-kinds test states the new
  contract: branches and tags have a menu, and only other references do not.
- `docs/roadmap.md`, `docs/plan/FEATURE-5860.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| One copy command or one per item | One, with the text as its parameter | Four identical actions, and a hash that is already on the row |
| Where the copy items sit | Last in each menu, after a separator | Copying changes nothing, so it comes after the actions that do. It is the usual place. |
| Copies on a stash line | Yes | It is a line of the history with a hash ("the standard context menu of a line") |

## Deviations & follow-ups

- Fix cycle 1: `HistoryMergeSourceTests.Badges_AreBranchesWithAMenuAndEverythingElseWithout` asserted
  that a tag is a plain `RefBadgeItem`, which is the contract this dev changes on purpose. It was renamed
  `…BranchesAndTagsWithAMenu…` and restated.
- Line endings: no CRLF churn.

## Documentation sweep

No README section lists menu items. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2436 passed**, 0 failed (4 new), after one fix cycle
  (above).
