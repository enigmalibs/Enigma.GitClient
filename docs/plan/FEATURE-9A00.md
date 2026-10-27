# FEATURE-9A00 — Squash a range of commits onto a new branch

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-27-feature-9a00

## Objective

From the History page, replace the commits after a **base** commit up to a **tip** commit (included)
with one new commit — parent = the base, files = the tip's, byte for byte (`git diff <tip> <new>` is
empty) — on a **new branch**. Nothing that exists is rewritten or moved: the original branch, its
commits, its tags and its upstream stay exactly as they were. The user removes the originals later,
with the existing Delete and Rename branch actions.

Two steps, as the merge source does: right-click the base → `Set as squash base`; right-click the tip
→ `Squash N commits since <base short sha> into a new branch…` → a dialog → confirm.

Motivating shape (read-only, never modified): `C:\Dev\PP\1170`, `5c6eb02..9f644b8` on
`feature/2026-10-26-architecture-docs` — 20 commits, 10 phase commits and their 10 `--no-ff` merges,
every merge bringing in a phase commit from inside the range. Wanted: `5c6eb02`, then one commit.

## Context & constraints

- Rebase is permanently forbidden (`Core/Git/ForbiddenGitOperations.cs`). The squash uses neither
  rebase nor cherry-pick:
  1. `git commit-tree <tip>^{tree} -p <base> -F <file>` writes a commit object and nothing else — no
     ref, no index, no work tree;
  2. `IBranchService.CreateAsync(name, startPoint: <new sha>, checkout)` creates the branch on it (it
     validates the name and refuses an existing branch). The branch is born on the squash commit; a
     failure before it leaves only an unreferenced object, which git collects.
- Checked against git 2.52 on 2026-10-27, in a scratch repository:
  - `checkout -b <name> <sha>` blocked by local changes exits non-zero and creates **no** branch;
  - `rev-list --ancestry-path base..tip` lists exactly the commits that descend from the base and
    lead to the tip. A merge parent that is neither on that path nor the base is a branch brought in
    from outside the range (a `develop` forked before the base was flagged; 1170's phase merges are not);
  - `commit-tree` output: `git diff <tip> <new>` is empty.
- The History page reads in date order (`CommitLogOrdering.Date`): a commit is always listed above its
  parents. Once the base is loaded, every commit between it and a tip on the tip's first-parent line
  is loaded too, so the menu can measure the range from the loaded lines without asking git.
- Engines live in `Core/<Concept>/<Concept>Service.cs`, driving git through `IGitCommandFactory` /
  `IGitProcessRunner` (`RevertService` is the template). The user-facing half lives in
  `Desktop/Services/*Operations.cs` (`RevertOperations`: refusals, `IRepositoryContext.RunExclusiveAsync`,
  the info bar, logging). The dialog follows `CreateBranchDialogViewModel` (name judged by
  `RefNameValidator` and the taken names on every keystroke, `ValidationChanged`), shown the way
  `BranchOperations` shows it (primary button enabled from `IsValid`).
- The merge source is the two-step precedent: `HistoryPageViewModel.MergeSource`, its toolbar chip
  `MergeSourceChip`, `CommitRowViewModel.NotifyMergeSourceChanged`.
- A commit message travels through a temporary UTF-8 file without BOM, normalised with
  `CommitService.Normalise` (the `CommitService` precedent).
- Minimum git is 2.20: no `--end-of-options`. A revision starting with `-` is refused.
- House MVVM: no source generators — `field` + `SetProperty`, explicit `…Command` properties, private
  handlers.
- Desktop tests are written and compiled, **not run**, on this Windows machine; the Core suites run.

## Decisions

| Question | Chosen | Why | Rejected |
|---|---|---|---|
| Which tip | Any commit line (not the uncommitted line, not a stash) | commit-tree builds off-branch: nothing above the tip is touched, the new branch ends at the squash commit | Local branch tips only |
| Which base | A strict ancestor on the tip's first-parent line | The meaning stays obvious ("everything the branch did since X"); exact from the loaded lines | Any ancestor |
| Removing the originals | The existing Delete, then Rename, branch actions | v1 stays strictly non-destructive | A "Replace" action |
| Author and date | The effective identity, now (commit-tree's default) | The squash is a new commit, made today | The tip's author and date |
| Name of the action | Squash | git's own word | Compress, Collapse |
| Default branch name | `<branch>-squashed` when a local branch points at the tip (the checked-out one first, else the first badge), otherwise `squash/<tip short sha>`; `-2`, `-3`… appended while taken | Renaming back means dropping a suffix | `squash/<branch>`; an empty field |
| "Switch to the new branch" default | Ticked when HEAD's commit is the tip, unticked otherwise | Same tree: the default switch never changes a file | Ticked when the tip's branch is checked out; always unticked |
| Switch blocked by local changes | Nothing is created; the info bar names the files | "Nothing half-done"; `checkout -b` refuses before it creates the branch | Create the branch, report the switch |
| Default message | Empty title line, blank line, then `- <subject>` per non-merge commit of the range, oldest first; Confirm disabled while the title is blank | Merge subjects are noise; git's own squash message lists the commits | Plain subjects; git's full squash log |
| Base marker | A glyph on the base line, and a `Squash base: <short sha>` chip with ✕ next to the merge source chip | A choice that outlives its menu is visible and has a way out | Line marker only |
| Base lifetime | Cleared by a successful squash, by ✕ or `Clear the squash base`, when another repository opens, and when a reload no longer lists its commit; kept across refresh, fetch and branch switch | A base serves one squash; a commit outlives HEAD moving | Cleared by hand only |
| After a squash | The history reloads and reveals the squash commit (`RevealAsync`) | The result is what the user came to check | Keeping the selection |
| A range that changes no file | Allowed, with a warning: the commit is empty | Rare, still a valid result | Refusing |
| Breakdown | Two phases: the engine, then the Desktop | The reset (FEATURE-1A7E) and revert (FEATURE-659E) precedent | Three phases |

### Assumptions (planning defaults)

- **N** is the ancestry-path count — commits that descend from the base and lead to the tip, tip
  included — in the menu and the dialog alike. Commits a merge brought in from outside are counted
  separately, in the warning. 1170: N = 20.
- Refused as for the reset: every multi-step operation in progress (merge, revert, cherry-pick…), not
  only a merge (`ResetOperations.DescribeOperationInProgress`).
- A detached HEAD is fine: nothing about the squash needs HEAD on a branch.
- `commit-tree` runs no hooks (no `pre-commit`, no `commit-msg`); nothing re-creates them.
- The dialog is the confirmation; no second question.
- A base beyond the first page that a plain reload no longer lists is cleared (the lifetime rule).

## PHASE01 — The squash engine

**Branch:** `feature/feature-9a00-phase01-squash-engine`
**Status:** TODO

### Steps

1. `Core/Squash/SquashService.cs`:
   - `SquashPreview(BaseSha, TipSha, CommitCount, MergeCount, Subjects, OutsideMerges,
     OutsideCommitCount, ChangesNothing)`: `CommitCount` and `MergeCount` over the ancestry path;
     `Subjects` the non-merge subjects, oldest first; `OutsideMerges` a `SquashOutsideMerge(Sha,
     Subject)` per merge on the path with a parent neither on the path nor the base;
     `OutsideCommitCount` = `rev-list --count base..tip` − `CommitCount`; `ChangesNothing` when
     `base^{tree}` equals `tip^{tree}`. One `log --ancestry-path` with a NUL-separated
     sha / parents / subject format (as `CommitLogParser` does) gives the path, the merges, the
     subjects and the outside merges.
   - `SquashRequest(BaseSha, TipSha, BranchName, Message, Checkout)`;
     `SquashOutcome(CommitSha, BranchName, CheckedOut)`.
   - `ISquashService.PreviewAsync(RepositoryHandle, string baseSha, string tipSha, CancellationToken)`;
     `ISquashService.SquashAsync(RepositoryHandle, SquashRequest, CancellationToken)`.
   - Guards, each a `GitOperationRefusedException` carrying a sentence, all before anything is
     written: the base is the tip; the base is not on the tip's first-parent line
     (`rev-list --first-parent --parents base..tip`: the last line's first parent is the base; an
     empty list means not an ancestor); a blank title (first line); an invalid branch name
     (`RefNameValidator.ValidateBranch`); a branch of that name exists (`IBranchService.ExistsAsync`).
   - `BuildCommitTreeArguments(tip, base, messageFile)` (public static): `commit-tree`,
     `<tip>^{tree}`, `-p`, `<base>`, `-F`, the file. A blank or dash-led revision throws
     `ArgumentException`, as `RevertService.BuildArguments` does.
   - `ComposeDefaultMessage(IReadOnlyList<string> subjects)` (public static): `"\n\n- s1\n- s2…"`;
     no subjects → an empty string.
   - `SquashAsync`: guards → the message to a temporary file (UTF-8 without BOM,
     `CommitService.Normalise`, deleted in `finally`) → `commit-tree` → `IBranchService.CreateAsync(name,
     newSha, checkout)`. A checkout blocked by local changes (`GitCommandException` whose output says
     "would be overwritten") is rethrown as a `GitOperationRefusedException` naming the files git
     listed and saying that nothing was created.
   - It runs `rev-list`, `log`, `rev-parse`, `commit-tree` and the branch service's own commands —
     never rebase, never cherry-pick.
2. `Core/Squash/SquashRange.cs` — the menu's measure, from loaded commits, without git:
   `SquashRange.Measure(IReadOnlyList<GitCommit> newestFirst, string baseSha, string tipSha)` returns
   `SquashRangeMeasure(CommitCount)`, or `null` when the base is the tip, is not in the list, or is
   not on the tip's first-parent line within it. The count is the preview's (ancestry path): the
   descendants of the base, found by walking the list upward from the base, intersected with the
   ancestors of the tip.
3. Register `ISquashService` in `AddGitClientCore`.
4. Tests:
   - Unit (`Core.UnitTests/Squash/SquashArgumentsTests.cs`, `SquashRangeTests.cs`): the commit-tree
     vector; a dash-led or blank revision refused; `ComposeDefaultMessage`'s shape and the empty case;
     `Measure` on a 1170-shaped graph (count, first-parent rule), a base on a merged side branch
     (`null`), an outside merge (its commits not counted), the base as the tip (`null`), a base not in
     the list (`null`).
   - Integration (`Core.IntegrationTests/Squash/SquashServiceTests.cs`, real git):
     - a 1170-shaped miniature (a base, three phases each on a branch merged `--no-ff`): the preview
       says 6 commits, 3 merges, 3 subjects oldest first, no outside merge; the squash commit's only
       parent is the base, `git diff <tip> <new>` is empty, the new branch is the base plus one commit;
     - every ref that existed (`for-each-ref`), the upstream configuration, HEAD, the index and the
       work tree are unchanged when not switching;
     - switching with HEAD at the tip and uncommitted changes (modified and untracked): HEAD on the
       new branch, the changes kept;
     - switching with HEAD elsewhere and a conflicting local change: refused, no branch, HEAD unchanged;
     - a `develop` forked before the base and merged in: in `OutsideMerges`, `OutsideCommitCount` right;
     - a tip in the middle of a branch: the new branch ends at the squash commit, the original untouched;
     - a change then its revert: `ChangesNothing`, the squash commit has the base's tree;
     - a base on a side branch, the base as the tip, a base that is no ancestor, a taken branch name,
       a blank title: refused, no ref changed;
     - a non-ASCII message recorded byte for byte; the author is the configured identity.

### Acceptance criteria

- `ISquashService` resolves from the container.
- A squash records exactly one commit (parent = the base, tree = the tip's) and one new branch on it.
  No existing ref, nor the index, nor the work tree changes — unless the switch was asked for, and then
  HEAD moves to the new branch and nothing else changes.
- Every refusal happens before anything is written; a blocked switch leaves no branch.
- Build clean with zero warnings; the Core unit and integration suites green.

## PHASE02 — Squash base, menu and dialog

**Branch:** `feature/feature-9a00-phase02-squash-menu-dialog`
**Status:** TODO

### Steps

1. `Desktop/Services/SquashOperations.cs`: `ISquashOperations.SquashAsync(SquashCandidate)` returning the
   new commit's sha, or `null` when nothing was created; a singleton in `AddGitClientApp`.
   `SquashCandidate` carries the base and the tip (sha, short sha, subject), the local branches on the
   tip, and whether HEAD's commit is the tip.
   - Refuses (warning, git not asked) when no repository is open or a multi-step operation is in
     progress (`ResetOperations.DescribeOperationInProgress`).
   - Reads `ISquashService.PreviewAsync` (a refusal → a warning), builds the dialog, shows it; Cancel
     changes nothing.
   - On confirm: `RunExclusiveAsync` → `SquashAsync`. Success → a success bar:
     `<short sha> on "<new branch>" replaces the N commits since <base short sha>`, then
     `"<original>" is untouched and still holds them; deleting it later warns about unmerged commits —
     that is expected.` (without an original branch: `The original commits are untouched.`). A refusal
     → a warning with its sentence; a `GitCommandException` → an error with git's first line, logged.
2. `Desktop/ViewModels/Dialogs/SquashDialogViewModel.cs`, `Desktop/Views/Dialogs/SquashDialogView.axaml(.cs)`:
   - read-only: the base (short sha, subject), the tip (short sha, subject, its branches),
     `N commits (M merges)`;
   - `BranchName`, defaulted per *Decisions*, judged as `CreateBranchDialogViewModel` judges a name;
   - `SwitchToNewBranch`, defaulted per *Decisions*;
   - `Message`, multi-line, pre-filled with `SquashService.ComposeDefaultMessage`, the caret on the
     empty title line; a blank first line makes the dialog invalid;
   - warnings: per outside merge, `<short sha> <subject>` brings in commits from outside the range
     (K in all): the new branch loses that link, so merging that branch again later may conflict
     again; the range changes no file → the commit is empty; no local branch ends at the tip →
     anything above it is not on the new branch;
   - a closing line: nothing that exists changes, the original keeps its commits;
   - `ValidationMessage`, `HasValidationMessage`, `IsValid`, `ValidationChanged`; buttons `Squash`
     (enabled from `IsValid`) and `Cancel`.
3. History page (`HistoryPageViewModel`, `CommitRowViewModel`, `HistoryPageView.axaml`):
   - `SquashBase` (sha, short sha), `HasSquashBase`, `SquashBaseSummary` (`Squash base: <short sha>`),
     `ClearSquashBaseCommand`; setting it re-announces every row's `IsSquashBase` and menu, as the
     merge source does;
   - `SquashBaseChip` beside `MergeSourceChip`, same markup; its ✕ has the tooltip and automation name
     "Clear the squash base";
   - a glyph on the base line, before the subject, tooltip "Squash base", bound to `IsSquashBase`;
   - `HistorySquashCommands(SetBase, ClearBase, Squash, CurrentBase, Measure)` on `HistoryRowCommands`;
     `Measure` runs `SquashRange.Measure` over the loaded rows' commits; `Squash` takes a
     `HistorySquashRequest(Row, Base)` carrying the base the menu showed, as `HistoryResetRequest` does;
   - a new menu group after the reset/revert group, on commit lines (listed greyed on the uncommitted
     line, never on a stash line): no base → `Set as squash base`; on the base line →
     `Clear the squash base`; elsewhere → `Set as squash base`,
     `Squash N commits since <base short sha> into a new branch…` (greyed, as
     `Squash since <base short sha> into a new branch…`, when `Measure` is `null`),
     `Clear the squash base`. Glyphs from `PhosphorIcon`;
   - the lifetime per *Decisions*: cleared after a successful squash, on repository change, after a
     reload that no longer lists its commit;
   - after a successful squash: `ReloadAsync`, then `RevealAsync(<new sha>)`.
4. Tests (`Desktop.UnitTests/HistorySquashTests.cs`, `SquashDialogTests.cs`, real repository — compiled,
   not run on Windows):
   - the menu per state (no base, on the base, elsewhere, uncommitted line, stash line); the header's
     N on a 1170-shaped history; greyed when the base is on a side branch;
   - the marker and the chip follow the base; ✕ clears it; a repository change clears it;
   - the dialog's defaults: the name (`-squashed`, `squash/<sha>`, the `-2` suffix), the switch rule,
     the message; validation (blank title, taken name, invalid name); each warning;
   - confirming creates the branch, reveals the squash commit and clears the base; Cancel changes
     nothing; refused, nothing written, while a merge is in progress.
5. Documentation sweep: README's feature list mentions the squash.

### Acceptance criteria

- `Set as squash base` marks the line and shows the chip; on a commit above it on its first-parent
  line, `Squash N commits since <base> into a new branch…` opens the dialog with the right N.
- Confirming creates the squashed branch, never changes an existing ref, and reveals the squash
  commit; the bar says the original is untouched.
- Nothing is written during a multi-step operation, with an invalid or taken name, or a blank title.
- Build clean with zero warnings; the Core suites green; the Desktop tests compiled (not run here).

## Out of scope

- A "Replace `<original>` with `<squashed>`" action (moving the original branch onto the squash commit).
- Squashing in place on the current branch; picking or reordering commits within the range.
- Squashing from a branch badge's menu; pushing the new branch.
- Running commit hooks on the squash commit.
