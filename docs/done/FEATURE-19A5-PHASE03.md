# FEATURE-19A5-PHASE03 — Worktree items in the menus

**Item:** FEATURE-19A5 — Create a worktree from a branch or line
**Phase:** PHASE03 — Worktree items in the menus
**Branch:** `feature/feature-19a5-phase03-worktree-menus`
**Run:** feature/2026-10-15-worktrees-toolbar

## Summary

PHASE02's *Create a worktree* dialog is now offered from a branch and from a line.

- **A history line:** *Create worktree here…*, right after *Create branch here…*, with the
  folder-plus glyph.
  - The new branch starts at the line's commit; the dialog names it by its short hash.
  - Greyed on the uncommitted line, as *Create branch here…* is: there is no commit to start from.
  - Absent on a stash line, which offers only its own three actions.
- **A branch's badge in the history:** *Create worktree from "main"…*, right after *Check out
  "main"*, for local and remote branches. The new branch starts from that branch (a remote one is
  tracked, as git sets it up).
- **A branch's line in the branches dialog:** *Create worktree…*, right after *Check out*, shown for
  a single selection as *Rename…* is. The branch's name (`origin/x` for a remote one) is the start
  point.
- A created worktree reloads the history (or rebuilds the branches list), as a created branch does,
  so the new branch's badge appears.

## Files touched

- Modified:
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/CommitRowViewModel.cs`:
    `HistoryRowCommands.CreateWorktreeHere` (optional, last) and the line's menu entry.
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryBranchViewModel.cs`:
    `HistoryBranchCommands.CreateWorktree` (optional, last), `CanCreateWorktree`,
    `CreateWorktreeHeader`.
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`: takes
    `IWorktreeOperations`; both commands and their handlers.
  - `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`: the badge's menu item.
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/BranchesPageViewModel.cs`: takes
    `IWorktreeOperations`; `CreateWorktreeCommand`, exposed on `BranchRowViewModel`.
  - `src/Enigma.GitClient.Desktop/Views/Pages/BranchesPageView.axaml`: the row's menu item.
  - `tests/Enigma.GitClient.Desktop.UnitTests/WorktreeTests.cs`: four menu tests.
  - `tests/Enigma.GitClient.Desktop.UnitTests/BranchesPageTests.cs`: a single selection's branch row
    menu now has 8 visible items, not 7 (see *Deviations & follow-ups*).
  - `README.md`: a *Worktrees* bullet in the feature list (documentation sweep).

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| Where each item sits | Right after *Create branch here…* on a line, right after *Check out* on a badge and a branch row | Next to the action it is a variant of: making a branch, or starting work on one | A separate group at the bottom |
| The new record members | Optional, last (`CreateWorktreeHere`, `CreateWorktree`) | Every existing construction keeps compiling; the house's way for later additions (`Revert`, `Copy`) | Positional parameters in the middle |
| The badge's item | Shown whenever the page provides the command (`CanCreateWorktree`), enabled for any branch | A worktree from the checked-out branch is as valid as from any other; it creates a new branch | Hiding it on the current branch |
| The icon | `FolderPlus` | A new folder is what the action makes; `GitBranch` already marks *Create branch here…* | `TreeStructure`, `GitFork` |

## Deviations & follow-ups

- No deviation from the plan.
- **Fixed when the Desktop suite was first run, on Linux:**
  `BranchesPageTests.WithSeveralSelected_ALinesMenuOffersOnlyTheDelete` counted 7 visible items on a
  single selection's branch row menu; *Create worktree…* makes it 8. The test's count was stale, not
  the menu: one fix cycle.
- **Follow-up:** the next release item's notes need lines for FEATURE-19A5 and FEATURE-70F3.
- **Follow-up (outside this item):** listing and removing worktrees, and saying when a branch is
  checked out in another worktree (git refuses to check it out or delete it here, and the app reports
  git's sentence).

## Documentation sweep

- `README.md`: the feature list gains a *Worktrees* bullet (where the item is offered, what it does,
  where the folder goes, the new window). No other prose doc mentions branch creation in a way this
  changes.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**.
- `Enigma.GitClient.Core.UnitTests`: **1220 passed, 1 skipped, 0 failed**.
- `Enigma.GitClient.Core.IntegrationTests`: **372 passed, 2 skipped, 0 failed** (3 min 46 s).
- Desktop tests added to `WorktreeTests` (4), **compiled, not run**, under the project's standing
  instruction never to run the Desktop suite on this Windows machine. Run them on Linux before tagging.
  - A line's item comes right after *Create branch here…*, with its glyph. It names the line's short
    hash, and the worktree's HEAD is the line's commit.
  - Greyed on the uncommitted line, absent on a stash line.
  - A badge's item names the branch, starts from it, and opens the new window.
  - A branch row's item starts from that branch, and the list then shows the new branch.
- `Enigma.GitClient.Desktop.UnitTests` (Release, Linux, when the run was committed): **1545 passed,
  0 failed**, after the one fix above.
