# FEATURE-19A5 — Create a worktree from a branch or line

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-15-worktrees-toolbar

## Objective

Let the user create a git worktree — a second working directory of the same repository, on a new
branch — from a branch or a history line's context menu. A content dialog asks for the branch to
create and the working directory. The directory defaults to a folder **next to** the repository,
never inside it, named `<repository folder>.worktree.<branch>`.

## Context & constraints

- **No worktree support today.** `RepositoryLocator` already opens a linked worktree (its git
  directory under `<main>/.git/worktrees/`), covered by
  `RepositoryLocatorTests.DiscoverAsync_ResolvesTheGitDirectoryOfALinkedWorktree`. The file-system
  watcher already watches a linked worktree where its references live.
- **House flow for a write:** a form in `IContentDialogService` with live validation (the primary
  button follows `IsValid` through a `ValidationChanged` event), then `IRepositoryContext.RunExclusiveAsync`
  (exclusive write lock, refresh after), then one sentence in the info bar. `GitOperationRefusedException`
  is a warning, `GitCommandException` an error showing git's first stderr line (`BranchOperations`).
- **Menus:**
  - a history line — `CommitRowViewModel.MenuEntries`, data-built, with *Create branch here…*
    (greyed on the uncommitted line, absent on stash lines) — commands in `HistoryRowCommands`;
  - a branch on a history line — the `HistoryBranchViewModel` badge menu in `HistoryPageView.axaml`,
    commands in `HistoryBranchCommands`;
  - a branch row of the Branches page — `BranchesPageView.axaml`, commands owned by
    `BranchesPageViewModel` and exposed through `BranchRowViewModel`.
- **Starting another instance on a folder:** `IInstanceLauncher.Launch(path)`; its test double
  `RecordingInstanceLauncher` records the paths.
- **Folder picker:** `IFolderDialogService` (`RecordingFolderDialogService` in tests), as the
  create-repository dialog's *Browse* uses it.
- Core has no `InternalsVisibleTo`: anything Core unit tests call is `public`.
- Desktop tests are written and compiled, **not run** on this Windows machine; Core suites are run.

## PHASE01 — A worktree service

**Status:** DONE — see `docs/done/FEATURE-19A5-PHASE01.md`
**Branch:** `feature/feature-19a5-phase01-worktree-service`

### Steps

1. `src/Enigma.GitClient.Core/Worktrees/WorktreePaths.cs` — a public static class:
   - `DefaultFor(string workTreePath, string branch)`: the sibling folder
     `<parent>/<folder name>.worktree.<branch>`, where every `/`, `\`, `<`, `>`, `:`, `"`, `|`, `?`, `*`
     and control character of the branch becomes `-`. A repository at a file-system root has no
     "next to": the result is empty.
   - `Validate(string workTreePath, string path)`: why a folder cannot take a worktree, empty when it
     can — empty path; not absolute; the repository's own work tree or a folder inside it; an
     existing file; an existing directory that is not empty. An empty existing directory is fine
     (git accepts it).
2. `src/Enigma.GitClient.Core/Worktrees/WorktreeService.cs` — `IWorktreeService.AddAsync(repository,
   path, branch, startPoint, cancellationToken)`:
   - refuses (`GitOperationRefusedException`) an invalid branch name (`RefNameValidator`), an
     existing branch (`IBranchService.ExistsAsync`), and a folder `WorktreePaths.Validate` rejects;
   - runs `git worktree add -b <branch> <full path> <startPoint>` — `BuildAddArguments`, public
     static, refusing a blank start point or one that begins with `-`.
3. Register `IWorktreeService` in Core's DI.
4. Unit tests (`tests/Enigma.GitClient.Core.UnitTests/Worktrees/`): the default folder (sibling,
   never inside, `/` and Windows-forbidden characters replaced, root → empty); every rule of
   `Validate`; the argument vector and its refusals.
5. Integration tests (`tests/Enigma.GitClient.Core.IntegrationTests/Worktrees/`): a worktree is
   created at the default folder with the new branch checked out at the start point (a sha and a
   branch name), the main repository's HEAD does not move, and the branch is in its refs; an existing
   branch, a folder inside the repository and a non-empty folder are refused and nothing is created.

### Acceptance criteria

- `git worktree add -b` creates the worktree at the given folder, on a new branch at the start point,
  leaving the open repository's HEAD alone.
- The default folder sits next to the repository, never inside, and is one folder whatever the
  branch name holds.
- Every refusal happens before git runs and names the reason.
- Build clean; Core unit and integration suites green.

## PHASE02 — The worktree dialog

**Status:** DONE — see `docs/done/FEATURE-19A5-PHASE02.md`
**Branch:** `feature/feature-19a5-phase02-worktree-dialog`

### Steps

1. `ViewModels/Dialogs/CreateWorktreeDialogViewModel.cs`:
   - `Name` (the branch to create), `Path` (the working directory), `StartPointLabel` (read-only),
     `OpenAfterCreate` (default `true`), `BrowseCommand`;
   - `Path` follows `Name` (`WorktreePaths.DefaultFor`) until the user edits it; *Browse* picks the
     folder it goes in and keeps the folder's own name;
   - `ValidationMessage` / `HasValidationMessage` / `IsValid` / `ValidationChanged`, house shape:
     branch name (`RefNameValidator`, not an existing local branch), then the folder
     (`WorktreePaths.Validate`).
2. `Views/Dialogs/CreateWorktreeDialogView.axaml` (+ code-behind): branch name, *Starts from*, the
   folder with *Browse*, the checkbox *Open it in a new window*, the validation line — the
   create-branch and create-repository dialogs' look.
3. `Services/WorktreeOperations.cs` — `IWorktreeOperations.CreateAsync(startPoint, startPointLabel)`:
   the form (title *Create a worktree*, primary *Create*), then `IWorktreeService.AddAsync` under
   `RunExclusiveAsync`; on success the info bar says *Worktree created* — "<branch>" is checked out in
   "<path>" — and, when asked, `IInstanceLauncher.Launch(path)` opens it (a failed start says so).
   Refusals and git failures are reported as `BranchOperations` reports them. Returns whether a
   worktree was created.
4. Register `IWorktreeOperations`; add it to `CompositionRootTests`.
5. Desktop tests (compiled): the default folder follows the name and stops once edited; *Browse*;
   each validation message; a confirmed dialog creates the worktree and starts an instance on it;
   unchecked, nothing is started; cancelled, nothing is created.

### Acceptance criteria

- The dialog asks for the branch and the folder, proposes `<repository>.worktree.<branch>` next to
  the repository, and cannot be confirmed while either is unusable, saying why.
- Confirming creates the worktree and, by default, opens it in a new window.
- Build clean; Core suites green; Desktop tests compile.

## PHASE03 — Worktree items in the menus

**Status:** TODO
**Branch:** `feature/feature-19a5-phase03-worktree-menus`

### Steps

1. History line: *Create worktree here…* after *Create branch here…* (`HistoryRowCommands.CreateWorktreeHere`,
   enabled on a line with a commit; the row's sha and short sha as start point and label). Not on
   stash lines.
2. History branch badge: *Create worktree from "<branch>"…* (`HistoryBranchCommands.CreateWorktree`),
   local and remote branches, the branch's name as start point.
3. Branches page row: *Create worktree…* (single selection, as *Rename…*), the branch's name as start
   point.
4. A created worktree reloads the history as a created branch does.
5. Desktop tests (compiled): each surface offers the item and passes its start point; the line's item
   is greyed on the uncommitted line and absent on a stash line.
6. Documentation sweep: the README's feature list.

### Acceptance criteria

- A history line, a branch badge in the history and a Branches page row each offer the item, which
  opens PHASE02's dialog starting from that commit or branch.
- Build clean; Core suites green; Desktop tests compile.

## Out of scope

- A worktree on an **existing** branch, a detached worktree, `--orphan`, locking.
- Listing, removing, pruning or moving worktrees; showing which branch is checked out in another
  worktree.
- Adding the worktree to the start window's repository list (opening it in a new window does what an
  opening does).
- Basing the default folder on the main working tree when the open repository is itself a linked
  worktree.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| New branch or existing one | Always a new branch, from the clicked branch or commit | The draft asks for "the branch to create"; one shape for both surfaces | Checking out the clicked branch in the worktree; offering both |
| Where the item appears | History lines (commits, not stashes), history branch badges (local and remote), Branches page rows | "A branch or a line": both branch surfaces must offer the same operation, as `BranchOperations` insists for every branch action | History only |
| The uncommitted line | Listed, greyed — as *Create branch here…* | The menu keeps its shape; there is no commit to start from | Hiding it |
| A branch name with `/` in the folder name | `/`, `\` and Windows-forbidden characters become `-` | One folder next to the repository, valid on both platforms | Nested folders; stripping the characters |
| Editing the folder | Editable field + *Browse* (the folder it goes in); follows the name until edited | The house's other path fields have *Browse*; following the name keeps the default right while typing | Read-only path; a full "save as" picker |
| Folders refused | Empty, relative, the repository or inside it, a file, a non-empty directory | "Never inside": a worktree inside would show in the repository's own changes; git refuses the rest anyway, less clearly | Only the default is outside; letting git answer |
| After creation | *Open it in a new window* checkbox, on by default, through `IInstanceLauncher` | The worktree is created to be worked in; mirrors *Check the branch out once it is created* | Info bar only; always opening |
| Repository at a file-system root | No default folder; the user chooses one | There is no "next to" a root | Defaulting inside the root |
| Open repository is a linked worktree | Default named after the open folder | Literal reading of the draft; a rare case | Reading the main working tree |
| Upstream from a remote start point | git's default (`branch.autoSetupMerge`) | `worktree add -b x path origin/x` already tracks `origin/x` | Setting it explicitly |
| Where the rules live | Core `Worktrees` (`WorktreePaths`, `IWorktreeService`); Desktop `IWorktreeOperations` | The rules run in the Core suites, which run here; one flow shared by three menus | Everything in the dialog ViewModel; widening `IBranchOperations` |
| Phases | Core service → dialog and flow → menus | Three small reviewable commits, each building on the last | One dev |
