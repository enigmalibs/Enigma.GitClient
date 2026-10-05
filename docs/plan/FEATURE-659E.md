# FEATURE-659E — Revert a commit from the history

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-05-revert-tree-basedir-release

## Objective

Beside the two reset items, every history line's menu offers **`Revert this commit…`**: `git revert`
records a new commit on the branch HEAD is on that undoes what the line's commit changed, and the
commit itself stays in the history.

## Context & constraints

- The line's menu is data: `CommitRowViewModel.MenuEntries` builds `HistoryMenuEntry` items from the
  commands in `HistoryRowCommands`, which `HistoryPageViewModel` wires. The reset items (FEATURE-1A7E)
  sit in a group of their own after "Check out this commit (detaches HEAD)", offered only when HEAD is on
  a branch (`ResetBranch`).
- Engines live in `Core/<Concept>/<Concept>Service.cs`, driving git through `IGitCommandFactory` /
  `IGitProcessRunner`; the user-facing half lives in `Desktop/Services/*Operations.cs` (refusals,
  question, `IRepositoryContext.RunExclusiveAsync`, the info bar). `ResetService` / `ResetOperations` are
  the template; `MergeService.Classify` is the precedent for reading git's wording when an exit code is
  not enough.
- `RepositoryOperation.Revert` already exists (`REVERT_HEAD`), and the main window's banner names it —
  but nothing in the app can finish or abandon a revert: the conflict page drives `merge --abort` and the
  merge commit only.
- git's behaviour (checked against git 2.56): a conflicting revert exits 1 and leaves `REVERT_HEAD`; a
  revert whose changes are already undone exits 1 with "nothing to commit" and leaves no state; staged
  changes, or unstaged changes to a file the revert touches, make it refuse with exit 128 and leave no
  state; unstaged changes to other files do not stop it. A merge commit needs `-m <parent>`.
- The minimum git is 2.20: `--end-of-options` (2.24) is unavailable, a trailing `--` is.
- `GitCommit.IsMerge` tells a merge commit apart.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Commit at once, or only stage the undo? | Commit at once with git's own message (`--no-edit`): `Revert "<subject>"` / `This reverts commit <sha>.` | It is what `git revert` does, and the message records which commit was undone | `--no-commit` (leaves staged work and an extra step); a message dialog (gold-plating) |
| Ask first? | Yes: `Revert <short sha>?`, naming the commit's subject and the branch the new commit lands on; `Revert` / `Cancel`, `Revert` the default | It writes a commit on the branch; nothing is lost, so the safe answer is not the default | No question (a misclick commits); Cancel as the default (nothing destructive to guard) |
| A merge commit | Offered, reverted against its first parent (`--mainline 1`); the question says so | "Undo this merge into my branch" is what reverting a merge means almost every time | Greyed out; asking which parent |
| A revert that conflicts | Abandoned at once (`git revert --abort`), the repository exactly as before; a warning names the files (at most `CheckoutOperations.MaximumListedFiles`) | The app cannot finish or abandon a revert in progress, and one left behind blocks the reset and the banner offers no way out | Leaving it in progress; extending the conflict page to reverts (a follow-up) |
| What it changed is already undone | Nothing committed; an information bar says so | git commits nothing; the reader should know why no commit appeared | An empty commit (`--allow-empty`) |
| Uncommitted work in the way | git's refusal, as a sentence: commit it or stash it, then revert | git leaves nothing behind; unrelated unstaged edits do not stop git, so refusing up front would be stricter than git | Refusing whenever the tree is dirty |
| Detached or unborn HEAD | Not offered, as the reset items are not; the operation refuses it too | A revert commit on a detached HEAD is lost at the next checkout | Offering it there |
| A multi-step operation in progress | Refused with a sentence, the reset's wording | A revert in the middle of a merge leaves a state nobody can read | Letting git decide |
| Which lines | Every commit line; listed but greyed out on the uncommitted line, as the other commit items are; stash lines keep their own three actions | The menu keeps its shape; a stash is not a commit anyone reverts | Hiding it on the uncommitted line |
| Title and place | `Revert this commit…`, in the reset group right after the two reset items | "In addition to git reset"; the ellipsis because it asks first | Its own group; naming the branch in the title (the question names it) |
| HEAD moves between opening the menu and clicking | The question names the branch HEAD is on when it is asked; the operation refuses when HEAD is detached by then | The reader confirms against what is true at the moment of the write | Carrying the menu's branch, as the reset does (its title names no branch) |
| Engine | `Core/Revert/RevertService.cs`: `IRevertService.RevertAsync(repository, revision, mainline, token)` returning a `RevertOutcome` (`Reverted`, `NothingToRevert`, `Conflicted`, `Failed`), registered in `AddGitClientCore` | One service per git concept, like reset, tags, stashes; an outcome rather than an exception because a conflicting revert is a result, not a crash | A method on `IResetService` (a revert moves no branch backwards) |
| Breakdown | Two phases: the engine, then the menu | The reset's precedent; each is one reviewable commit | One dev |

## PHASE01 — The revert engine

**Branch:** `feature/feature-659e-phase01-revert-engine`
**Status:** TODO

### Steps

1. `Core/Revert/RevertService.cs`:
   - `RevertResultKind { Reverted, NothingToRevert, Conflicted, Failed }` and
     `RevertOutcome(Kind, ConflictedPaths, Message, Detail)`, with the new commit's sha when reverted;
   - `IRevertService.RevertAsync(RepositoryHandle, string revision, int? mainline, CancellationToken)`;
   - `RevertService.BuildArguments(revision, mainline)` (public static): `revert`, `--no-edit`,
     `--mainline <n>` when given, the revision, `--`. A blank revision throws `ArgumentException`, one
     starting with `-` throws `ArgumentException`, a mainline below 1 throws
     `ArgumentOutOfRangeException`;
   - `RevertService.Classify(exitCode, standardOutput, standardError)` (public static), reading git's
     wording as `MergeService.Classify` does;
   - on a conflict: read the conflicted paths from the status, then `revert --abort`, and return
     `Conflicted` with the paths; on any other failure that left `REVERT_HEAD` behind, abort as well;
   - after a revert, `rev-parse HEAD` for the new commit.
2. Register `IRevertService` in `AddGitClientCore`.
3. Tests:
   - Unit (`Core.UnitTests/Revert/RevertArgumentsTests.cs`): the vector with and without a mainline
     ends in `--`; a dash-led or blank revision is refused; a mainline of 0 is refused; `Classify` reads
     the conflict, nothing-to-commit, local-changes and other wordings.
   - Integration (`Core.IntegrationTests/Revert/RevertServiceTests.cs`, real git): a revert records a
     commit undoing the change, with git's message, HEAD still on the branch; a merge reverted with
     mainline 1 undoes what it brought in; a conflicting revert reports the file and leaves no
     `REVERT_HEAD`, the branch and the work tree as before; reverting what is already undone reports
     `NothingToRevert` and commits nothing; staged work in the way reports `Failed` with git's reason and
     leaves no state; an unstaged change to another file does not stop it and survives it.

### Acceptance criteria

- `IRevertService` resolves from the container.
- A revert records one new commit on the current branch that undoes the given commit; a merge is
  reverted against its first parent.
- A conflicting or failed revert never leaves the repository in the reverting state.
- Nothing starting with `-` reaches git as the revision.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Revert in the line menu

**Branch:** `feature/feature-659e-phase02-revert-menu`
**Status:** TODO

### Steps

1. `Desktop/Services/RevertOperations.cs`: `IRevertOperations.RevertAsync(string sha, string shortSha,
   string subject, bool isMerge)` returning `true` when a commit was recorded; a singleton registered in
   `AddGitClientApp`.
   - Refuses (warning, git never asked) when no repository is open, HEAD is detached or unborn, or a
     multi-step operation is in progress.
   - Asks `Revert <short sha>?` — the subject, the branch the new commit lands on, that the commit stays
     in the history, and for a merge that it is undone against the branch it was merged into —
     `Revert` / `Cancel`.
   - Runs through `IRepositoryContext.RunExclusiveAsync`; reports `Reverted` as a success (the new
     commit's short sha), `NothingToRevert` as information, `Conflicted` as a warning naming the files and
     saying nothing changed, `Failed` as a warning with the reason; a git error as an error with git's
     first line (logged).
2. `HistoryRowCommands` gains `Revert` (`AsyncRelayCommand<CommitRowViewModel>`); `CommitRowViewModel`
   gains `RevertHeader` and lists the item after the reset items when HEAD is on a branch.
3. `HistoryPageViewModel` takes `IRevertOperations`; `Revert` can execute on a row with a commit; the
   history reloads after a revert that recorded a commit.
4. Tests (`Desktop.UnitTests/HistoryRevertTests.cs`, real repository):
   - the line menu offers `Revert this commit…` right after the reset items, with a glyph;
   - it is not offered on a detached HEAD, and greyed out on the uncommitted line;
   - confirming records the revert commit and the history reloads with it on top; Cancel changes
     nothing; the question names the branch;
   - a merge line's revert undoes what the merge brought in;
   - a conflicting revert changes nothing and says which file conflicted;
   - a revert is refused, without touching the repository, while a merge is in progress.
5. Documentation sweep: README's feature list mentions the revert.

### Acceptance criteria

- On any commit line, with HEAD on a branch, the menu offers `Revert this commit…` after the reset
  items; confirming records a commit undoing that commit on the current branch.
- Nothing is reverted on a detached HEAD or during a multi-step operation; a conflict leaves the
  repository as it was and says why.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Resolving a revert's conflicts in the app (the conflict page handles merges only) — a follow-up.
- Reverting several commits at once, or a range.
- Editing the revert's message before it is committed; reverting without committing.
- Choosing a merge's other parent as the mainline.
