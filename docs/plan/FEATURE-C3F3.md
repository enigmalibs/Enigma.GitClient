# FEATURE-C3F3 — Remove worktrees, mark their branches

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below

## Objective

Finish the worktree story FEATURE-19A5 started (it only creates them):

- **remove** a worktree from the application — its folder and git's record of it — optionally deleting
  its branch in the same step;
- **mark** every branch checked out in another working tree with Phosphor's `Tree` icon, on the
  branches dialog's rows and on the history's ref pills, and say in the repository strip when the open
  repository is itself a worktree;
- **refuse up front**, in plain words, the deletes and checkouts git would refuse with
  `cannot delete branch 'x' used by worktree at …`;
- a **Worktrees tool dialog** listing every worktree of the repository;
- **open** a worktree in a new window, its folder in the file manager, or a terminal in it.

### Background (what the user asked)

The user created a worktree on `testwt`, then could not delete `testwt` from the main repository:
`error: cannot delete branch 'testwt' used by worktree at …`. Answers given during the interview:

- A worktree is a second working folder of the same repository, recorded by git under
  `.git/worktrees/<id>`. A branch can be checked out in **one** working tree at a time, which is why
  git refuses to delete (or check out, or force-move) a branch another working tree has checked out.
- `git worktree remove <folder>` deletes the folder and git's record and **keeps the branch**; it is run
  from the main repository or any other worktree of it — not from inside the one being removed. A
  folder deleted by hand stays recorded until `git worktree prune` (or a `remove`, which accepts a
  missing folder).

## Context & constraints

- **Core today:** `src/Enigma.GitClient.Core/Worktrees/` holds `WorktreePaths` and
  `IWorktreeService.AddAsync` (`git worktree add -b`). Nothing lists or removes.
- **git floor is 2.20** (`GitVersion.Minimum`): read worktrees with `git worktree list --porcelain`
  (2.7+), **not** `for-each-ref %(worktreepath)` (2.23+). The `prunable` line and `locked <reason>` exist
  from 2.31 only, so a missing folder is also detected with `Directory.Exists`. No `-z` (2.36+). git
  writes Windows paths with `/`: normalise with `Path.GetFullPath`.
- **State:** `IRepositoryContext` (`RepositoryContext.RefreshAsync`) reads `Refs` and `Head` through
  `IRefReader.GetStateAsync`; pages rebuild from it on every refresh.
- **Branch writes:** `IBranchOperations` / `BranchOperations`:
  - `DeleteAsync` → `DescribeAsync` gives each branch a skip reason (`CheckedOutReason` for the
    checked-out one) or its merged state (`IsMergedAsync`, `GetUnmergedCommitsAsync`,
    `CountUnmergedCommitsAsync`, `-d` vs `-D`); one branch alone that is skipped is reported as a warning
    ("Could not delete …"); a batch reports skipped ones in its summary.
  - `CheckoutAsync(name, isRemote)` is called by the branches dialog rows, the history badge menu and
    `BranchDropOperations` (which checks the drop target out first when it is not current).
  - `ICheckoutOperations.CheckoutAsync(revision, label, detach)` checks anything else out.
- **House flow for a write:** confirmation (`ConfirmDestructiveAsync`) or a form through
  `IContentDialogService`; then `IRepositoryContext.RunExclusiveAsync`; then one sentence in the info
  bar. `GitOperationRefusedException` is a warning; `GitCommandException` an error with git's first
  stderr line.
- **Branch marks today:**
  - branches dialog: `BranchRowViewModel` + `Views/Pages/BranchesPageView.axaml`, a badge strip
    (*checked out*, *hidden* — icon and word —, ahead, behind, *local only*), each with a tooltip and
    `AutomationProperties.Name`;
  - history: `RefBadgeItem(Kind, Name, IsCurrent, Upstream)` (`CommitRowViewModel.cs`), the `RefBadge`
    control (`IsCurrentProperty` shows a `Check`, FEATURE-0743), its template in `Themes/Controls.axaml`,
    and `RefBadgeMetrics`, which sizes the Refs column — `HistoryPageTests` reads the numbers back off a
    realised badge so the two cannot drift;
  - history badge menu: `HistoryBranchViewModel` / `HistoryBranchCommands` in `HistoryPageView.axaml`.
- **Tool dialogs:** `ToolDialog` {Branches, Tags, Remotes} mapped in `ToolDialogService`, opened by the
  history toolbar buttons `OpenBranches`/`OpenTags`/`OpenRemotes` (`HistoryPageViewModel.OpenToolAsync`).
- **Reaching outside:** `IInstanceLauncher.Launch(path)` (`RecordingInstanceLauncher` in tests),
  `ISystemInterop.OpenPathAsync` / `OpenTerminalAsync`.
- **Repository strip:** the top border of `MainWindow.axaml` (folder icon, name, `RepositoryPath` tooltip).
- **Glyph:** `PhosphorIcon.Tree`. `TreeStructure` already means "show the files as a tree".
- Core has no `InternalsVisibleTo`: what Core tests call is `public`.
- **Tests on this machine:** the Core unit and integration suites run; Desktop tests are written and
  **compiled, not run** (standing instruction) — run them on Linux before tagging.

### Shared definitions

- **Checked out elsewhere:** a worktree from the list whose branch is the branch in question and whose
  folder is not the open repository's `WorkTreePath`. It includes the **main** working tree when the open
  repository is a linked worktree. The open window's own branch keeps its *checked out* badge, never the
  tree.
- **Linked worktree:** any entry but the main one. Only a linked worktree can be removed — never the main
  working tree, never the one this window has open.

## PHASE01 — List, probe and remove in Core

**Status:** TODO
**Branch:** `feature/feature-c3f3-phase01-worktree-list-remove`

### Steps

1. `Worktrees/GitWorktree.cs` — public sealed record: `Path` (full, OS separators), `HeadSha` (empty for
   a bare entry), `Branch` (short name; `null` when detached or bare), `IsMain`, `IsBare`, `IsDetached`,
   `IsLocked`, `LockReason` (empty when none given), `IsMissing`.
2. `Worktrees/WorktreeListParser.cs` — public static `Parse(string porcelain)`: records separated by blank
   lines; `worktree <path>`, `HEAD <sha>`, `branch refs/heads/<name>`, `detached`, `bare`,
   `locked [reason]`, `prunable [reason]`; the first record is the main working tree; unknown lines are
   ignored; CRLF tolerated. The parser is pure: `IsMissing` comes from `prunable`.
3. `IWorktreeService` grows:
   - `ListAsync(repository, ct)` — `git worktree list --porcelain`; `IsMissing` also set when the folder
     does not exist (git before 2.31 prints no `prunable`).
   - `CountChangesAsync(GitWorktree worktree, ct)` — `git status --porcelain --ignore-submodules=none`
     run in the worktree's folder: the files `worktree remove` refuses on (ignored files do not count,
     and are deleted with the folder). `0` for a missing folder.
   - `RemoveAsync(repository, worktreePath, force, ct)` — lists again, then refuses
     (`GitOperationRefusedException`) a folder that is not a worktree of the repository, the main working
     tree, the open repository's own working tree, and a locked worktree (naming its reason and
     `git worktree unlock`); runs `git worktree remove [--force] -- <full path>`. The branch is left
     alone. `BuildRemoveArguments(path, force)` public static, refusing a path that is not fully
     qualified.
4. Unit tests (`tests/Enigma.GitClient.Core.UnitTests/Worktrees/`): the parser — main only; main and
   linked; detached; bare; locked with and without a reason; prunable; `/` paths on Windows; trailing
   blank lines and CRLF — and the argument vectors with their refusals.
5. Integration tests (`tests/Enigma.GitClient.Core.IntegrationTests/Worktrees/`):
   - after `AddAsync`, the list has the main working tree first and the new one with its branch and sha;
   - a clean worktree is removed: folder gone, out of the list, **branch still there**, and
     `IBranchService.DeleteAsync` then succeeds;
   - a worktree with a modified and an untracked file: `CountChangesAsync` > 0, remove without force
     fails (git's error), with force succeeds;
   - a folder deleted by hand: `IsMissing`, and remove clears the record;
   - the main working tree, the open working tree and a locked worktree are refused, nothing removed.

### Acceptance criteria

- `ListAsync` returns every worktree, main first, with its branch, detached, locked and missing state, on
  git 2.20 and later.
- `RemoveAsync` deletes the folder and git's record and keeps the branch; every refusal happens before
  git runs and names the reason.
- Build clean with zero warnings; Core unit and integration suites green.

## PHASE02 — Worktrees in the state, tree marks

**Status:** TODO
**Branch:** `feature/feature-c3f3-phase02-worktree-marks`

### Steps

1. `IRepositoryContext.Worktrees` (`IReadOnlyList<GitWorktree>`, empty while closed), read with the refs
   on every refresh. A failed list is logged and leaves the list empty — it never fails the refresh.
   Helpers: `WorktreeOf(string branch)` (the worktree that has it checked out elsewhere, or `null`),
   `IsLinkedWorktree`, `MainWorktreePath`.
2. Branches dialog row (`BranchRowViewModel`): `WorktreePath`, `IsInOtherWorktree`, `WorktreeTip` —
   "Checked out in the worktree at <path>", "Checked out in the main working tree at <path>" for the
   main one, with " (its folder is missing)" when it is. A badge right after *checked out*: the `Tree`
   icon (`pill faint`) and the word "worktree", tooltip and `AutomationProperties.Name` from the tip.
   Local rows only.
3. History pill: `RefBadgeItem.IsWorktree`, set from the context's list when the rows are built;
   `RefBadge.IsWorktreeProperty`; the template draws a 13 px `Tree` after the `Check` and before the kind
   icon; `RefBadgeMetrics.MeasureBadge` counts it and `Measure` passes it; the realised-width test covers a
   badge with the tree.
4. Repository strip: when the open repository is a linked worktree, a `Tree` icon after the folder name,
   tooltip "A worktree of <main path>".
5. Desktop tests (compiled): the badge on a branch checked out in another worktree — including the main
   one seen from a linked worktree — and on no other row, the current branch included; the pill's tree
   and its measured width; the strip's icon only for a linked worktree.

### Acceptance criteria

- The tree marks exactly the branches checked out elsewhere, on both surfaces, and its tooltip names the
  folder.
- The Refs column is still seeded to fit its badges.
- Build clean; Core suites green; Desktop tests compile.

## PHASE03 — Guards on delete and checkout

**Status:** TODO
**Branch:** `feature/feature-c3f3-phase03-worktree-guards`

### Steps

1. `BranchOperations.DescribeAsync`: a local branch checked out elsewhere gets the skip reason
   `WorktreeReason` — "it is checked out in the worktree at <path>". Alone: a warning *Could not delete
   "x"* — "\"x\" is checked out in the worktree at <path>. Remove the worktree first." In a batch: skipped
   with that reason in the summary; the others are deleted.
2. `BranchOperations.CheckoutAsync(name, isRemote)`, before git runs: a local branch checked out
   elsewhere — or a remote branch whose local counterpart is — raises *Branch in a worktree*: "\"x\" is
   checked out in the worktree at <path>. Open it in a new window?", primary *Open*, close *Cancel*.
   *Open* calls `IInstanceLauncher.Launch(path)` (a failed start says so). A missing folder: the message
   alone, no *Open*. Returns `false` either way. This covers the dialog's rows, the history badges and a
   drop-merge that checks its target out first.
3. `CheckoutOperations.CheckoutAsync(revision, …)`: the same guard when the revision names a local branch
   checked out elsewhere.
4. Desktop tests (compiled): a lone delete is refused with the sentence and runs nothing; a batch skips it
   and deletes the rest; a checkout is refused with the offer, *Open* launches on the folder, *Cancel*
   does not; a drop-merge onto such a target is refused; a missing folder gets the message only.

### Acceptance criteria

- No delete or checkout started from the application reaches git's "used by worktree" error.
- The refusals name the folder; the checkout's offers to open it.
- Build clean; Core suites green; Desktop tests compile.

## PHASE04 — Remove and open from branch menus

**Status:** TODO
**Branch:** `feature/feature-c3f3-phase04-worktree-remove-flow`

### Steps

1. `IWorktreeOperations` grows:
   - `RemoveAsync(GitWorktree worktree)`:
     - refuses, as Core does, the main working tree, the open one and a locked one;
     - probes `CountChangesAsync`; for a worktree on a branch, whether the branch is merged and, if not,
       its unmerged commits (as the delete flow reads them, at most `MaximumListedCommits` subjects);
     - a confirmation of its own (`RemoveWorktreeDialogViewModel` + `RemoveWorktreeDialogView`):
       title *Remove worktree*; "Remove the worktree at <path>? Its folder is deleted."; with changes, in
       the warning colour: "N uncommitted change(s) in it will be lost."; always: "Close any window
       working in it first."; the checkbox *Also delete the branch "x"* — **ticked when the branch is
       merged, unticked when it is not**, the commits it holds listed under it ("holds N commits the
       current branch does not; deleting it loses them"); no checkbox for a detached worktree; for a
       missing folder: "Its folder is already gone; this removes git's record of it.";
       destructive primary *Remove*, or *Remove anyway* when there are changes;
     - then, under `RunExclusiveAsync`: `IWorktreeService.RemoveAsync(force: changes > 0)`, then, when
       ticked, the branch delete (`-d` merged, `-D` unmerged) through `IBranchService`;
     - the info bar: *Worktree removed* — "<path> is gone" (+ " and \"x\" is deleted"). A branch delete
       that fails after the removal is a warning naming the branch; the removal is still reported.
     - Returns whether the worktree was removed.
   - `OpenAsync(worktree)` (`IInstanceLauncher`), `OpenFolderAsync(worktree)`
     (`ISystemInterop.OpenPathAsync`), `OpenTerminalAsync(worktree)` — each says so when it fails.
2. Menus, for a local branch checked out elsewhere — the branches dialog row menu and the history badge
   menu, the same four items in the same order: *Open worktree in a new window*, *Open worktree folder*,
   *Open a terminal in the worktree*, *Remove worktree…*. *Remove worktree…* is absent for the main
   working tree; the three *Open* items are greyed for a missing folder.
3. A removal reloads the history as a branch deletion does.
4. Register what is new; extend `CompositionRootTests`.
5. Desktop tests (compiled): the dialog's checkbox default (merged → ticked, unmerged → unticked with the
   commits), the changes warning and *Remove anyway*, the "close any window" line, no checkbox when
   detached; confirmed → removed with or without the branch, force only with changes; cancelled → nothing;
   branch delete failing after removal → warning; each menu offers the items for such a branch only.

### Acceptance criteria

- From the main repository's window, *Remove worktree…* on `testwt` with the box ticked removes the
  folder and the branch; unticked, the branch stays and can be deleted afterwards.
- A worktree with changes is removed only after the warning names them.
- Build clean; Core suites green; Desktop tests compile.

## PHASE05 — The Worktrees tool dialog

**Status:** TODO
**Branch:** `feature/feature-c3f3-phase05-worktrees-dialog`

### Steps

1. `ToolDialog.Worktrees`, `WorktreesPageViewModel` + `WorktreesPageView`, mapped in `ToolDialogService`;
   a history toolbar button `OpenWorktrees` (`Tree` icon, tip *Worktrees*) right after *Branches*, with
   `OpenWorktreesCommand`.
2. Rows from `IRepositoryContext.Worktrees`, main first, then by path: the folder's name (tooltip: the
   full path), the branch or "detached at <short sha>", and badges *main*, *this window*, *locked*
   (tooltip: its reason), *folder missing*. Rebuilt on refresh.
3. Row menu — and double-click for *Open in a new window* — with the four actions through
   `IWorktreeOperations`. *Remove…* greyed, with a tip saying why: main "The main working tree cannot be
   removed", this window "Remove it from another window of this repository", locked "Locked: <reason> —
   unlock it with git worktree unlock". The *Open* items greyed for a missing folder.
4. Empty state when only the main working tree exists: "No other worktrees. Create one from a branch's or
   a commit's menu."
5. Desktop tests (compiled): the rows and their badges; the greyed items and their tips; each action
   reaches `IWorktreeOperations`; the toolbar button opens the dialog.
6. Documentation sweep: the README's feature list (remove, list and marks).

### Acceptance criteria

- The Worktrees dialog lists every worktree with its state and offers open, folder, terminal and remove
  where they apply, saying why where they do not.
- Build clean; Core suites green; Desktop tests compile.

## Out of scope

- Lock / unlock, move, repair; a separate *Prune* (removing a worktree whose folder is missing does it).
- Creating a worktree from the Worktrees dialog; worktrees on the start window's repository list.
- Another window noticing its folder was removed (the confirmation warns instead).
- Removing several worktrees at once.

## Decisions

| Question | Chosen | Source |
|---|---|---|
| Where removal is offered | Branch menus **and** a Worktrees list | User |
| Where the list lives | A 4th tool dialog, toolbar button after *Branches* | User |
| The branch on removal | Checkbox, ticked by default; **unticked when the branch is unmerged**, commits listed | User (recommendations) |
| Worktree with changes | Warn naming the count, then `--force` (*Remove anyway*) | User (recommendation) |
| Deleting a worktree's branch | Refused up front, naming the folder; skipped in batches | User (recommendation) |
| Checking out a worktree's branch | Refused up front, offering to open the worktree in a new window | User (recommendation) |
| Other actions | Open in a new window, open folder, open terminal | User (recommendations) |
| Where the tree shows | Branches dialog rows and history pills | User (recommendation) |
| The row's form | A *worktree* badge (icon + word), tooltip names the folder | User (recommendation) |
| Glyph | Phosphor `Tree` | User (recommendation) |
| Which branches | Checked out in any other working tree, main included | User (recommendation) |
| Removing the window's own worktree | Not offered; from another window | User (recommendation) |
| Open in another window | A warning line in the confirmation | User (recommendation) |
| Repository strip | Tree + "A worktree of <main path>" for a linked worktree | User (recommendation) |
| Reading worktrees | `git worktree list --porcelain`, no `-z`; `Directory.Exists` for a missing folder | Default (git 2.20 floor) |
| Locked worktree | Refused, naming `git worktree unlock` | Default |
| Missing folder | Removable (clears git's record); no Prune button | Default |
| List failure | Logged, empty list, refresh carries on | Default |
| Phases | Core → state and marks → guards → remove flow and menus → dialog | Default (one reviewable commit each) |
