# FEATURE-2074-PHASE03 — Stashing from the history

**Item:** FEATURE-2074 — Stashes like GitKraken
**Branch:** `feature/feature-2074-phase03-stash-in-history`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

The stash is now handled where it is drawn, in the history:

- **Stash all changes** has a toolbar button (Archive icon), enabled while the history shows uncommitted
  changes. The uncommitted line's menu also offers **Stash all changes…**. Both use PHASE02's dialog and
  operation.
- **A stash line's menu** offers **Apply stash**, **Pop stash** and **Delete stash…**, with "Show what
  it changed" above them. It offers nothing else: branching, tagging, checking out or resetting to git's
  record of a stash is a trap, and GitKraken does not offer those either.
- Every operation that changed something redraws the history. After a stash, the uncommitted line is
  gone and the new stash line is there. After a pop, the stash line is gone and the uncommitted line is
  back.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/CommitRowViewModel.cs`:
  - `HistoryStashCommands` and `HistoryRowCommands.Stashes`;
  - `MenuEntries`: the stash line's own menu, and the uncommitted line's stash item.
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs`:
  - takes `IStashOperations`;
  - `StashCommand` and `HasUncommittedChanges` (notified with the empty state);
  - `OnStashAsync` and `OnStashLineAsync`.
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml` — the toolbar's Stash button.
- `tests/Enigma.GitClient.App.UnitTests/HistoryStashTests.cs` — 6 tests:
  - the toolbar;
  - the uncommitted line's menu;
  - the stash menu's exact items;
  - apply, pop and delete from a stash line (theory ×3).
- `docs/roadmap.md`, `docs/plan/FEATURE-2074.md` — statuses. The item is done.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The uncommitted line's other items | Kept as they were, with "Stash all changes…" added after "Show what it changed" | Removing the (disabled) commit items there was not asked for, and existing tests describe that menu |
| Toolbar placement | Leftmost in the right-hand group, set apart by a small margin | It acts on the repository, while the three buttons beside it open dialogs |
| Toolbar tooltip when disabled | Shown (`ShowOnDisabled`) | A greyed button says what it would do |

## Deviations & follow-ups

- None from the plan.
- Follow-up: the history's diff of a stash line (`git show` against its first parent) does not list the
  stash's untracked files. The Changes page's stash list does. This was out of scope.
- Line endings: no CRLF churn.

## Documentation sweep

No README statement became inaccurate. The release notes describe the stash's new home. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2401 passed**, 0 failed (6 new), with no fix cycle.
