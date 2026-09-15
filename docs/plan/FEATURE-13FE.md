# FEATURE-13FE — Working directory & commits

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-13fe-<phase>-<slug>` (one per phase)
**Run:** feature/2026-09-15-enigma-git-client

## Objective

The half of a git client the draft did not spell out but which it cannot ship without: see what has
changed in the working tree, stage and unstage it, discard it, and commit — with the same diff viewer
used for history.

## Context & constraints

- Status is read with `git status --porcelain=v2 -z --branch --untracked-files=all
  --ignore-submodules=none`, which is the only stable machine-readable status format and is the reason
  for the git ≥ 2.20 floor.
- Discard is destructive and always confirmed, naming every file.
- The panel refreshes on demand and when the window regains focus; a filesystem watcher is recorded as
  a follow-up rather than added now (it needs debouncing and ignore-file awareness to be trustworthy).

## PHASE01 — Status, staging & commit engine

**Status:** DONE — see `docs/done/FEATURE-13FE-PHASE01.md`

**Steps**

1. `WorkingTreeStatus` model: branch, upstream, ahead/behind, and `ChangedFile` entries carrying both
   the index and the work-tree status, rename source, submodule state, and `IsConflicted`.
2. `IStatusService.GetStatusAsync(ct)` — full `--porcelain=v2 -z` parser including `1`/`2`/`u`/`?`/`!`
   record kinds, rename records with their `\0`-separated original path, and the `# branch.*` headers.
3. `IStagingService`: `StageAsync(paths)`, `StageAllAsync()`, `UnstageAsync(paths)`,
   `UnstageAllAsync()`, `DiscardAsync(paths)` (tracked → `checkout --`, untracked → delete),
   `RemoveAsync`, `IgnoreAsync(path)` appending to `.gitignore`.
4. `ICommitService`: `CommitAsync(CommitRequest)` with message, amend, allow-empty, sign-off, and an
   explicit author override; the message is passed via a temp file (`-F`) so multi-line and non-ASCII
   messages survive on both platforms; `GetLastCommitMessageAsync` for amend prefill.
5. `IGitIgnoreService.IsIgnoredAsync(path)` via `git check-ignore -v`.

**Acceptance criteria**

- Unit tests: the `--porcelain=v2` parser over captured payloads covering modified-staged,
  modified-unstaged, both, added, deleted, renamed (with the `\0` original path), copied, untracked,
  ignored, unmerged (all conflict states `DD AU UD UA DU AA UU`), a submodule with a dirty work tree,
  a path containing a space and a non-ASCII path.
- Integration tests: stage/unstage single files and everything; discard restores a tracked file and
  deletes an untracked one; commit produces the expected message, author and parent; amend rewrites
  the tip; allow-empty works; a multi-line non-ASCII message round-trips exactly.
- Committing with nothing staged is refused with a clear message (unless allow-empty).

## PHASE02 — Changes page & commit UI

**Status:** DONE — see `docs/done/FEATURE-13FE-PHASE02.md`

**Steps**

1. `ChangesPageViewModel` — staged and unstaged collections (each reusing
   `ChangedFilesPanelViewModel`, so the list/tree toggle works here too), the commit message editor,
   amend and sign-off toggles, and the commit command with its `CanExecute` rules.
2. Layout: unstaged (top) and staged (bottom) panels with stage/unstage per file, per selection and
   for everything; a diff pane on the right showing the selected file's unstaged or staged diff,
   driven by the FEATURE-7D1B viewer.
3. Commit box: multi-line editor, a 50/72 subject/body guide, character counter, Ctrl+Enter to commit,
   and the resulting commit surfaced in the InfoBar with its short hash.
4. Discard actions with a confirmation dialog listing the affected files; "Discard all" additionally
   requires the repository name to be typed, matching what other clients do for an irreversible bulk
   action.
5. The uncommitted-changes pseudo-row in the history graph navigates here.
6. Empty state ("working tree clean") with the current branch and ahead/behind.

**Acceptance criteria**

- Unit tests with faked services: staging moves files between the two collections and refreshes;
  commit is disabled with an empty message or nothing staged, enabled for amend on a clean tree;
  discard is gated on confirmation; the list/tree toggle is shared with the history panel's setting.
- Integration test: a full stage → commit → verify-in-log cycle through the ViewModels.
- Zero `AVLN` warnings.

## Out of scope

- Hunk- and line-level staging (recorded as a follow-up; file-level staging ships here).
- A filesystem watcher (follow-up).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Status format | `--porcelain=v2 -z` | The only format git guarantees to be machine-readable and stable, and the only one that reports rename sources unambiguously | `--porcelain=v1` (no rename detail, ambiguous quoting); parsing human output (breaks on locale) |
| Commit message transport | A temp file with `-F` | `-m` through argv hits platform length limits and mangles some encodings; a file is exact on both OSes | `-m` with the raw string; stdin (harder to cancel cleanly) |
| Staging granularity | File-level now, hunk-level as a follow-up | File staging covers the common case and keeps this dev reviewable; hunk staging needs a patch editor, which is its own feature | shipping hunk staging here (doubles the dev); no staging at all (unusable) |
| Refresh trigger | Manual + window-activated | Predictable and cheap; a watcher without debouncing and ignore-awareness produces more noise than value | an unconditional `FileSystemWatcher` |
| Discard-all guard | Type the repository name | The only genuinely unrecoverable bulk action in the app | a plain Yes/No |
