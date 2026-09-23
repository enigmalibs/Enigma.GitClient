# FEATURE-3507-PHASE01 — Merge source and merge into

**Item:** FEATURE-3507 — Branch actions in the history
**Branch:** `feature/feature-3507-phase01-merge-source`
**Run:** feature/2026-09-22-home-window-merges-refresh

## Summary

Merges now happen in the history, as the prompt describes: set a branch as the **merge source**, then
merge it into another branch.

- **Every branch badge has its own menu.** Check out "X"; *Set "X" as merge source* (or *Clear the
  merge source* on the source itself); *Merge "S" into "X"* and its fast-forward-only twin while a
  source is set; *Merge "X" into "current"*; *Delete "X"…*. On a line carrying several branches, the
  badge is the one place that says without ambiguity which branch an action is about.
- **The line's menu** keeps the commit's actions: show what it changed, create a branch or a tag here,
  check out this commit, open it on the host. It also lists, for **every** branch on the line,
  *Set "X" as merge source* and, once a source is set, *Merge "S" into "X"* for every valid
  destination. How many items that is depends on the branches on the line, so the menu is built as
  data (`CommitRowViewModel.MenuEntries`, a list of `HistoryMenuEntry`) and bound through an
  `ItemContainerTheme`. The view asks the line to rebuild it on every right-click.
- **The merge source** is page state, `HistoryPageViewModel.MergeSource`. The toolbar shows it as a
  "Merge source: X" chip with a ✕, and its badge is ringed in the accent colour. It lasts until it is
  cleared, replaced, or its branch disappears (checked on every refresh), or until the repository
  changes. A merge does not use it up.
- **The merges** go through `IBranchDropOperations`: the destination is checked out first when it is
  not the current branch, then the source is merged into it. This is the same flow and the same policy
  as dropping one branch on another: a remote branch can be a source but never a destination, and a
  branch is never merged into itself.

The line menu's old single-branch items — check out, merge into the current branch and delete "the"
branch, which was whichever came first — are gone. Each branch now offers them from its own badge.

## Files / modules touched

**Added — App**

- `ViewModels/Pages/HistoryBranchViewModel.cs` — `MergeSource`, `HistoryBranchCommands`,
  `HistoryBranchViewModel` (the badge's menu state and headers), `HistoryMenuEntry`

**Modified — App**

- `ViewModels/Pages/CommitRowViewModel.cs` — `HistoryRowCommands` loses the three single-branch
  commands and gains `Branches`; the row builds `Branches` and `Badges` (branch view models and plain
  badges), `MenuEntries`, `NotifyMergeSourceChanged`, `RefreshMenu`; `BranchName`, `IsBranchRemote`,
  `CheckoutHeader`, `DeleteBranchHeader`, `MergeHeader`, `CanMergeBranch` and `CanCheckoutBranch`
  removed. A misplaced doc comment (the badge's, sitting above the commands record) is back on its
  own type
- `ViewModels/Pages/HistoryPageViewModel.cs` — `BranchCommands`, `MergeSource`, `HasMergeSource`,
  `MergeSourceSummary`, `ClearMergeSourceCommand`, the branch handlers, forgetting a source that is
  gone; `IMergeOperations` replaced by `IBranchDropOperations`, which covers "merge into the current
  branch" too, so the page's dependency count does not grow
- `Views/Pages/HistoryPageView.axaml` — the line menu from `MenuEntries`; the badge strip from `Badges`
  with a template (and menu) for branches and one for everything else; the merge-source chip
- `Views/Pages/HistoryPageView.axaml.cs` — rebuilds the line's menu on a right-click
- `Themes/Controls.axaml` — `RefBadge.mergesource`: an accent ring on the border every badge already
  draws, so the badge's measured size does not change
- `README.md`, `RELEASENOTES.md` — checkout is no longer "a line's own menu: its branch" (sweep)

**Tests**

- `HistoryMergeSourceTests.cs` (new, real repository) — a line with two branches offers both as the
  source, and nothing to merge yet; setting a source offers merging it into every other local
  branch, never into itself; merging into the current branch merges and keeps the source; merging
  into a branch that is not checked out checks it out first; clearing; the source goes with its
  branch, stays while it is there, and is forgotten with the repository; a remote branch can be a
  source and never a destination; the checked-out branch cannot be checked out, deleted or merged into
  itself; the badges are branch view models and plain badges
- `HistoryPageTests.cs` — a real line's menu opens with exactly its entries, and every branch badge
  carries a menu
- `BranchesPageTests.cs`, `MergeOperationTests.cs`, `TagsAndCheckoutTests.cs` — the checkout and
  merge tests moved from the row's removed commands to the branch's

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the badge strip holds two kinds of badge | `Badges`: a `HistoryBranchViewModel` per branch, a `RefBadgeItem` for everything else, each with its own data template | A tag with an empty menu would still take the right-click from the line's menu |
| How the menus follow the source | The page tells every loaded row when the source changes, and the row tells its branches | The headers name the source; without the notification a menu opened after a change would still name the old one |
| The line menu | Data built on demand, rebuilt by the view on right-click | Its length depends on the line's branches and on the source; the host's name, too, is known only after the rows are built |
| "Merge X into the current branch" | Kept, on the badge | It was the row menu's, and it is the most common merge there is |
| The source's badge | An accent ring | The chip says what the source is; the ring says where it is |

## Deviations & follow-ups

- **Process slip:** this dev's changes were made on the run branch's working tree before its dev
  branch was cut. They were still uncommitted, so the dev branch was cut before the commit and nothing
  landed on the run branch except the merge. The plan's status went from `TODO` straight to `DONE` in
  the same commit, with no `IN PROGRESS` commit of its own.
- One fix cycle: a test tagged a commit outside the application without refreshing the context before
  reloading the history.
- The line's menu lists every item, even when its command refuses; a refused item is greyed out by
  its command's can-execute rather than hidden.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 1882 passed, 0 failed (12 new; 5 moved to the new
  API).
