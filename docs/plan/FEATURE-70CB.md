# FEATURE-70CB — Delete a branch and its remote at once

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-70cb-delete-local-and-remote`
**Run:** feature/2026-11-05-history-badges-release

## Objective

A branch badge that stands for a local branch and its upstream (the two on the same commit, one
badge) offers, besides *Delete "x"…* and *Delete "origin/x"…*, a third item:
*Delete "x" and "origin/x"…*. It asks once, naming both branches and what is at risk, then deletes
both and reloads the history.

## Context & constraints

- **The badge menu** (`HistoryPageView.axaml`, the `HistoryBranchViewModel` template) has
  *Delete "<name>"…* (`Commands.Delete`, parameter the branch) and, when `HasRemote`,
  *Delete "<remote>"…* (`Commands.Delete`, parameter `Remote`). `DeleteRemoteHeader` builds the latter.
- **`IBranchOperations.DeleteAsync(IReadOnlyList<BranchToDelete>)`** already exists: one question
  for the batch (unmerged commits named, the checked-out branch and a branch checked out in another
  worktree left out with the reason, the remote warning), then deletes going on past a failure, reads
  the repository again once and reports once. The Branches page's multi-select delete uses it.
- `HistoryBranchCommands` takes optional commands last, defaulting to `null` (`Copy`,
  `CreateWorktree`, `Worktrees`, `Rename`), hidden when absent.
- A local branch and its upstream on different commits are two badges: neither has `HasRemote`.

## Steps

1. `HistoryBranchViewModel.cs`:
   - `HistoryBranchCommands` gains an optional last parameter
     `AsyncRelayCommand<HistoryBranchViewModel>? DeleteWithRemote = null`, documented like its
     neighbours.
   - `HistoryBranchViewModel` gains `CanDeleteWithRemote => HasRemote && Commands.DeleteWithRemote is
     not null` and `DeleteWithRemoteHeader => $"Delete \"{Name}\" and \"{Remote?.Name}\"…"`.
2. `HistoryPageViewModel.cs`: pass
   `new AsyncRelayCommand<HistoryBranchViewModel>(OnDeleteBranchWithRemoteAsync, branch => branch is { CanDeleteWithRemote: true, IsCurrent: false })`.
   `OnDeleteBranchWithRemoteAsync` calls
   `_branchOperations.DeleteAsync([new(branch.Name, false), new(branch.Remote!.Name, true)])` and
   reloads when it returns `true`.
3. `HistoryPageView.axaml`: a `MenuItem` right after *Delete "<remote>"…*, `Header` bound to
   `DeleteWithRemoteHeader`, `IsVisible` to `CanDeleteWithRemote`, command `Commands.DeleteWithRemote`
   with the branch as parameter, glyph `Trash`.
4. Tests (`tests/Enigma.GitClient.Desktop.UnitTests/HistoryDeleteBothTests.cs`, headless, real git
   with a bare remote, as `HistoryGroupedBadgeTests`):
   - a joined badge shows the three delete items in order, the new one bound to
     `Commands.DeleteWithRemote` with the branch as parameter, glyph `Trash`;
   - a local-only badge and a remote-only badge do not show it;
   - confirming deletes both (`git branch --list topic` and `git ls-remote --heads origin topic` are
     empty) and the reloaded history no longer draws the badge; one question was asked;
   - cancelling deletes nothing;
   - on the checked-out branch's joined badge the item is disabled.
5. Documentation sweep (§7).

## Acceptance criteria

- A joined badge offers *Delete "x" and "origin/x"…* after the two single deletes; no other badge
  does.
- One confirmation; confirming deletes both, cancelling neither; the history reloads.
- Disabled on the checked-out branch.
- The build has zero warnings; the whole suite is green against the baseline.

## Out of scope

- The item on a local badge whose upstream is on another commit.
- A "delete both" item on the Branches page.
- Changing `IBranchOperations`.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which badges offer it | Joined badges (`HasRemote`) only | That is the badge with both single deletes, which the request names | Every local branch with an upstream: a badge that does not draw the remote would delete a branch it does not show |
| How it asks | The existing batch delete, one question | It already names the commits at risk and warns about the remote; the Branches page asks the same | Two questions in a row; a new dialog |
| Wording | `Delete "x" and "origin/x"…` | The request's wording; matches the two single items | `Delete both…` |
| Checked-out branch | Shown, disabled | git refuses deleting the checked-out branch; the batch would delete only the remote, which is not what the item says | Hidden: the reader would not know why it is missing; enabled: a half delete |
| Glyph, place | `Trash`, right after `Delete "origin/x"…` | A delete group, local before remote before both | `CloudX` |
