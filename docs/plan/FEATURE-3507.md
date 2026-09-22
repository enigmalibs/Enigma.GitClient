# FEATURE-3507 — Branch actions in the history

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-22-home-window-merges-refresh

## Objective

The history becomes the place where branches are worked with. From the lines that carry branches, the
user can pick a branch as the **merge source** ("Set `<branch>` as merge source") and then merge it into
another branch ("Merge `<source>` into `<destination>`"). They can also **pull**, **push** and
**delete** a branch. A line with several branches offers the actions for each of them.

## Context & constraints

- Today the row's menu acts on *one* branch (`CommitRowViewModel.BranchName`: the first local branch,
  else the first remote one), offering check out, "merge into the current branch" and delete.
- The badges (`RefBadgeItem`: kind, name, is-current) are drawn per row in their own column and carry
  no menu.
- `IBranchDropOperations.DropAsync` is "merge A into B" (checks B out when it is not current, then
  merges), with `BranchDropOperations.CanDrop` as the policy.
- `ISyncOperations.PullAsync` pulls into the current branch, and `PushAsync` pushes the current one.
  `SyncService` builds `PushRequest` with any `Branch`, but it cannot update a local branch that is not
  checked out.
- Rebase is permanently forbidden (`ForbiddenGitOperations`); a refspec fetch is not a rebase.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Line menu or badge menu | **Both, with a split of duties.** Each branch badge gets its own menu with everything for that branch (check out, set as merge source, merge the source into it, pull, push, delete). The line menu keeps the commit actions (show changes, create branch/tag here, check out this commit, open on host) and also lists the merge suggestions for **every** branch on the line | A badge menu is unambiguous on a line with several branches. The line menu still offers the merges exactly as the prompt describes them, and still carries the commit actions | Line menu only (a list of every action × every branch); badge menu only (drops the described line-menu flow) |
| The old single-branch items on the line menu | Removed (check out / merge into current / delete "first branch") — they live on the badges now | They acted on a branch the reader did not choose | Keeping them beside the new ones (two ways to say the same thing, one of them ambiguous) |
| Where the merge source lives | `HistoryPageViewModel.MergeSource` (name + is-remote); a toolbar chip shows "Merge source: X" with a ✕ | The state is invisible otherwise, and the reader needs a way out | A modal "pick the destination" mode |
| How long the source lasts | Until it is cleared, replaced, or its branch no longer exists; a merge does not clear it | One source can be merged into several branches, and the chip makes the state visible | Clearing it after each merge |
| Which branches can be a destination | Local branches other than the source (the drop policy) | A remote branch is changed by pushing | Offering and then refusing |
| Pulling a branch that is not checked out | `git fetch <remote> <upstream-branch>:<local-branch>`, which only ever fast-forwards, as a new `ISyncService.FastForwardBranchAsync` | Never moves HEAD behind the reader's back, never merges into a branch nobody is looking at | Checking it out and pulling |
| Pulling the current branch | The existing pull (merge or fast-forward-only, per the setting) | Unchanged behaviour | — |
| Pushing | Any local branch, via the existing `PushRequest` (setting the upstream when there is none) | `PushRequest.Branch` already takes any branch | Current branch only |
| Pull/push on a remote badge | Not offered; a remote badge offers check out (creates the tracking branch), set as merge source, and delete (the remote branch, with the existing confirmation) | There is nothing local to pull into or push from | — |

## PHASE01 — Merge source and merge into

**Branch:** `feature/feature-3507-phase01-merge-source`
**Status:** DONE — see `docs/done/FEATURE-3507-PHASE01.md`

### Steps

1. `HistoryPageViewModel`: `MergeSource` state, `SetMergeSourceCommand`, `ClearMergeSourceCommand`, and
   `MergeIntoCommand` / `FastForwardIntoCommand` taking a destination, carried out through
   `IBranchDropOperations` and followed by a reload when the repository changed. The source is dropped
   when a refresh no longer finds its branch.
2. A badge-level ViewModel (the badge plus the page's branch commands and headers) so a badge menu can
   bind plainly — `RefBadgeItem` stays the drawing model.
3. `CommitRowViewModel`: per-branch merge suggestions for the line menu — for every branch on the line,
   "Set X as merge source", and when a source is set, "Merge S into X" for every valid destination —
   built as data so the menu is an `ItemsSource` and not hand-written items.
4. `HistoryPageView`: the badge menu (branch actions); the line menu keeps the commit actions and gets
   the merge suggestions; the toolbar chip shows the source with its ✕.
5. Tests: set a source from a badge and from the line; the line menu of a line with two branches offers
   both; "Merge S into D" calls the drop operations with the right request; a remote branch is never
   offered as a destination; the source is never offered as its own destination; the source goes when
   its branch does; clearing the chip clears it.

### Acceptance criteria

- A branch on any line can be set as the merge source, from its badge or from the line's menu.
- With a source set, every other local branch on a line offers "Merge `<source>` into `<branch>`".
- A line carrying several branches offers the suggestions for each of them.
- The current source is visible and can be cleared.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Pull, push and delete per branch

**Branch:** `feature/feature-3507-phase02-branch-sync`
**Status:** TODO

### Steps

1. Core: `ISyncService.FastForwardBranchAsync(handle, remote, remoteBranch, localBranch, progress, token)`
   running `git fetch <remote> <remoteBranch>:<localBranch>`; `SyncService.BuildFastForwardArguments`.
2. `ISyncOperations.PullBranchAsync(branch)` — the current branch through the existing pull, any other
   through the fast-forward, refusing a branch with no upstream with a sentence saying so — and
   `PushBranchAsync(branch)` for any local branch.
3. The badge menu gets Pull, Push and Delete for local branches (Delete for remote ones through the
   existing remote-branch deletion), and Check out for both.
4. Tests: the argument builder; an integration test in which a non-current branch is fast-forwarded
   from a bare remote and a diverged one is refused; the badge menu's items are enabled for the right
   kinds; the operations are called with the right branch.

### Acceptance criteria

- A local branch in the history can be pulled, pushed and deleted from its badge, current or not.
- Pulling a branch that is not checked out never moves HEAD and never creates a merge.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Tag actions on the tag badges.
- Force-pushing.
- Dragging badges in the history (deliberately removed by FEATURE-3030).
