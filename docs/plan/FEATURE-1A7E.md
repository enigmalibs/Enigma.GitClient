# FEATURE-1A7E — Reset the branch to a commit

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-23-history-branch-reset

## Objective

From the history, the reader can move the branch they are on to any commit. Every line's menu offers
two items naming the current branch:

- `Reset "<branch>" to this commit - Soft (keep all changes)` — `git reset --soft`: the branch moves,
  and everything the commits after it changed stays, staged, ready to be committed again.
- `Reset "<branch>" to this commit - Hard (discard all changes)` — `git reset --hard`: the branch
  moves, and the index and the work tree become that commit's, after a confirmation naming what is
  lost.

## Context & constraints

- The line's menu is data: `CommitRowViewModel.MenuEntries` builds `HistoryMenuEntry` items from the
  commands in `HistoryRowCommands`, which `HistoryPageViewModel` wires. The view rebuilds the menu as
  it opens (`RefreshMenu`), so a command's can-execute is evaluated fresh each time.
- User-facing writes live in `App/Services/*Operations` (confirm, `RunExclusiveAsync`, report in the
  info bar); the git engines live in `Core/*Service`. There is no reset engine yet.
- `ForbiddenGitOperations` forbids rebase only; reset is allowed.
- The minimum git is 2.20, so `--end-of-options` (2.24) is not available; a trailing `--` is.
- `ChangedFile.IsUntracked` tells untracked files apart in the uncommitted list, which
  `IDiffService.GetChangedFilesAsync(…, DiffTarget.Uncommitted(), …)` returns.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| The exact titles | `Reset "main" to this commit - Soft (keep all changes)` and `Reset "main" to this commit - Hard (discard all changes)` — the user's wording and hyphen, the branch in quotes | Every branch name in this menu is quoted (`Set "x" as merge source`, `Merge "s" into "d"`); FEATURE-3507 read `<branch>` the same way | The name unquoted (inconsistent with its neighbours); an em dash (not what was asked) |
| Where in the menu | A group of its own right after "Check out this commit (detaches HEAD)": Soft, then Hard | Both move HEAD, like the checkout; the destructive one last | At the bottom (after the merge suggestions and the host link) |
| HEAD detached or unborn | The two items are not offered | There is no current branch to name or to move | A disabled "Reset HEAD…" (a title the user did not ask for) |
| The uncommitted row and the HEAD row | Listed but greyed out on the uncommitted row (no commit), as the other commit items are; on the HEAD row Soft is greyed out (a no-op) and Hard stays enabled (it discards the uncommitted work — a real use) | The menu's rule: every item is listed, its command's can-execute greys it out | Hiding them (the menu changes shape line to line) |
| Confirmation | Soft: none. Hard: always, naming the tracked files whose changes are lost (at most `CheckoutOperations.MaximumListedFiles`), Cancel as the default button | Soft loses nothing — the work stays staged and the commits stay in the reflog; Hard throws uncommitted work away for good, and a misclick must not do it | Confirming both (a question with nothing at stake); confirming Hard only when the tree is dirty (a misclick still moves the branch) |
| Untracked files on Hard | Left alone, as `git reset --hard` does; the confirmation says so | "Reset hard" is git's reset; removing untracked files is `git clean`, another operation | Adding a clean |
| HEAD moves between opening the menu and clicking | The operation takes the branch the menu named and refuses, with a sentence, when HEAD is no longer on it | Never reset a branch the reader did not see named | Resetting whatever is current |
| A merge, cherry-pick, revert or rebase in progress | Both refused with a sentence: finish or abandon it first | A reset in the middle of a multi-step operation leaves a state nobody can read; the conflict page can abandon a merge | Letting git decide (Soft refuses, Hard silently abandons the merge) |
| The engine | `Core/Reset/ResetService.cs`: `IResetService.ResetAsync(repository, revision, ResetMode, token)`, `ResetMode { Soft, Hard }`, registered in `AddGitClientCore` | One service per git concept, like tags, checkout, stashes | A method on `ICheckoutService` or `IBranchService` (neither is about moving a branch's tip with the index) |
| `ResetMode` values | `Soft` and `Hard` only | What was asked; `Mixed` is one enum member away when someone needs it | Shipping an unused `Mixed` |
| Argument safety | A revision starting with `-` is refused; the arguments are `reset --soft|--hard <revision> --`; the history passes the row's full sha | Nothing can be read as an option, on every supported git | `--end-of-options` (git 2.24+) |
| The user-facing half | `App/Services/ResetOperations.cs`: `IResetOperations.ResetAsync(sha, shortSha, branch, mode)`, a singleton, injected into `HistoryPageViewModel` | Same shape as `ICheckoutOperations`; keeps the page from carrying the dialog and the reporting | Folding it into `ICheckoutOperations` (a reset is not a checkout) |
| After a reset | The history reloads; the info bar says where the branch is and what happened to the changes | Same as the sibling operations; a soft reset's result is otherwise only visible as the uncommitted row | No report |
| A branch that was already pushed | No warning; out of scope | The app has no force-push; a warning with no action is noise. Recorded as a follow-up | Refusing to reset a pushed branch |

## PHASE01 — The reset engine

**Branch:** `feature/feature-1a7e-phase01-reset-engine`
**Status:** TODO

### Steps

1. `Core/Reset/ResetService.cs`: `ResetMode { Soft, Hard }`, `IResetService.ResetAsync(RepositoryHandle,
   string revision, ResetMode mode, CancellationToken)`, and `ResetService` driving git through
   `IGitCommandFactory` / `IGitProcessRunner` (`throwOnError: true`).
2. `ResetService.BuildArguments(revision, mode)` (public static, testable): `reset`, `--soft`/`--hard`,
   the revision, `--`. A blank revision throws `ArgumentException`; a revision starting with `-` throws
   `GitOperationRefusedException`; an undefined mode throws `ArgumentOutOfRangeException`.
3. Register `IResetService` in `AddGitClientCore`.
4. Tests:
   - Unit (`Core.UnitTests/Reset/ResetArgumentsTests.cs`): both modes' vectors end in `--`; a
     dash-led revision is refused; a blank one throws.
   - Integration (`Core.IntegrationTests/Reset/ResetServiceTests.cs`): soft moves the branch and keeps
     the later commits' changes staged; hard moves the branch, discards tracked changes and keeps an
     untracked file; HEAD stays attached to the branch in both; resetting to a commit that is not an
     ancestor moves the branch there.

### Acceptance criteria

- `IResetService` resolves from the container.
- A soft reset moves the current branch to the commit and leaves the index and the work tree as they
  were (the changes since show as staged).
- A hard reset moves the current branch to the commit and makes the index and the tracked files that
  commit's; untracked files are left alone.
- Nothing starting with `-` reaches git as the revision.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Soft and hard reset in the line menu

**Branch:** `feature/feature-1a7e-phase02-reset-menu`
**Status:** TODO

### Steps

1. `App/Services/ResetOperations.cs`: `IResetOperations.ResetAsync(string sha, string shortSha,
   string branch, ResetMode mode)` returning `true` when the branch moved.
   - Refuses (warning in the info bar, git never asked) when no repository is open, HEAD is detached
     or unborn, HEAD is no longer on `branch`, or a multi-step operation is in progress.
   - Hard: a confirmation — `Discard all changes?` — saying where the branch will point, listing the
     tracked files whose uncommitted changes are thrown away (or that there are none), saying that
     untracked files are kept; `Reset and discard` / `Cancel`, Cancel the default.
   - Runs through `IRepositoryContext.RunExclusiveAsync`; reports success (`"main" reset to abc1234`
     with a sentence per mode), a refusal as a warning, a git failure as an error with git's first line
     (logged).
   - Registered as a singleton in `AddGitClientApp`.
2. `HistoryRowCommands` gains `ResetSoft` and `ResetHard` (`AsyncRelayCommand<CommitRowViewModel>`) and
   `CurrentBranch` (`Func<string?>`, the branch HEAD is on, `null` when detached or unborn).
   `CommitRowViewModel` gains `ResetSoftHeader` / `ResetHardHeader`, and `MenuEntries` lists the two
   items after the checkout when there is a current branch.
3. `HistoryPageViewModel`: takes `IResetOperations`; `ResetSoft` can execute on a row with a commit that
   is not HEAD, `ResetHard` on any row with a commit; both reload the history after a reset.
4. Tests (`App.UnitTests/HistoryResetTests.cs`, real repository):
   - the line menu names the current branch in both items, right after the checkout;
   - neither is offered when HEAD is detached;
   - both are greyed out on the uncommitted row; Soft is greyed out on the HEAD row, Hard is not;
   - Soft moves the branch, keeps the changes staged, asks nothing, and the history reloads;
   - Hard asks; Cancel changes nothing; confirming discards the tracked changes and keeps an untracked
     file; the dialog lists the file that is lost;
   - a reset is refused, without touching the repository, when HEAD is no longer on the branch the menu
     named, and while a merge is in progress.
5. Documentation sweep: README's feature list and RELEASENOTES' "The graph" mention the reset.

### Acceptance criteria

- On any line, with HEAD on a branch, the menu offers `Reset "<branch>" to this commit - Soft (keep all
  changes)` and `Reset "<branch>" to this commit - Hard (discard all changes)`.
- Soft moves the branch and keeps every change; Hard asks first, then moves the branch and discards the
  uncommitted changes to tracked files.
- Nothing is reset on a detached HEAD, during a multi-step operation, or when HEAD has left the named
  branch.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Mixed reset, and resetting a branch that is not checked out.
- Removing untracked files (`git clean`).
- Warning about, or force-pushing, a branch whose reset diverges it from its upstream.
- A reflog / "undo reset" view.
